using System;
using System.Buffers.Binary;

using static Tf2DemoSalvage.Content.Assets.StudioLayout;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// A model's skin families, which is how one model paints two teams.
/// </summary>
/// <remarks>
/// **A team colour is a different material, not a tint.** A TF2 player model carries two skin
/// families — 0 is RED and 1 is BLU — and the game picks between them with
/// <c>m_nSkin = ( team == TF_TEAM_RED ) ? 0 : 1</c> (<c>tf_player_shared.cpp:4849</c>). Drawing
/// everything with family zero paints both teams red, which is exactly what it looks like.
///
/// The table is <c>numskinref</c> entries wide and <c>numskinfamilies</c> tall, of shorts: the
/// material a mesh uses in family <c>f</c> is <c>skinref[f * numskinref + mesh.MaterialIndex]</c>.
/// Offsets 220, 224 and 228 in <c>studiohdr_t</c>, counted from the published field order and
/// anchored on <c>numbodyparts</c> at 232, which this project verified against real files.
/// </remarks>
public static class StudioSkins
{

    /// <summary>Most entries a skin table may hold, as a guard against a malformed header.</summary>
    private const int MaximumEntries = StudioReaderLimits.SkinTableEntries;

    /// <summary>How many materials a skin family names.</summary>
    /// <param name="file">The model's bytes.</param>
    /// <returns>The width of the table.</returns>
    public static int References(ReadOnlyMemory<byte> file) => Count(file, HeaderSkinReferenceCountOffset);

    /// <summary>How many skins the model has.</summary>
    /// <param name="file">The model's bytes.</param>
    /// <returns>The height of the table; one for a model with no team colours.</returns>
    public static int Families(ReadOnlyMemory<byte> file) =>
        Math.Max(1, Count(file, HeaderSkinFamilyCountOffset));

    /// <summary>Reads the skin table.</summary>
    /// <param name="file">The model's bytes.</param>
    /// <returns>Family-major material references, or empty when the model has no table.</returns>
    public static short[] Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        if (bytes.Length < HeaderSkinTableOffset + 4)
        {
            return [];
        }

        int references = Count(file, HeaderSkinReferenceCountOffset);
        int families = Count(file, HeaderSkinFamilyCountOffset);
        int at = BinaryPrimitives.ReadInt32LittleEndian(bytes[HeaderSkinTableOffset..]);

        long entries = (long)references * families;

        if (references <= 0 || families <= 0 || entries > MaximumEntries)
        {
            return [];
        }

        if (at < 0 || at + (entries * sizeof(short)) > bytes.Length)
        {
            return [];
        }

        short[] table = new short[entries];

        for (int index = 0; index < entries; index++)
        {
            table[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes[(at + (index * 2))..]);
        }

        return table;
    }

    /// <summary>Which texture a mesh paints with, at a given skin.</summary>
    /// <param name="table">The skin table, as <see cref="Read"/> returns it.</param>
    /// <param name="references">How many materials a family names, from the header.</param>
    /// <param name="families">How many families the model has, from the header.</param>
    /// <param name="skin">The family asked for — a placement's <c>m_Skin</c>, or an entity's.</param>
    /// <param name="reference">The mesh's own <c>mstudiomesh_t::material</c>.</param>
    /// <returns>An index into the model's textures, or −1 when the reference cannot be answered.</returns>
    /// <remarks>
    /// **Valve's own comment names the shape** —
    /// <c>utils/motionmapper/motionmapper.h:134</c>:
    ///
    /// <code>
    ///   EXTERN  int g_skinref[256][MAXSTUDIOSKINS]; // [skin][skinref], returns texture index
    /// </code>
    ///
    /// So a mesh's <c>material</c> field is a SKINREF and not a texture index, and the skin picks
    /// the row that turns it into one. Family zero is a row like any other.
    ///
    /// **This exists as its own function because privileging family zero is invisible on nearly
    /// every model (B229).** Almost all props have one family, where the row is the identity and
    /// every reading agrees. Where they differ, the difference is total: `cp_fulgur` places
    /// `props_aquatic/pipe_256.mdl` at skins 1 and 12 of 15 and packs only those two textures, so a
    /// reader that resolves family zero first gets nothing and paints every pipe on the map in the
    /// missing-material chequer.
    ///
    /// An out-of-range SKIN falls back to family zero, which is <c>props_shared.cpp:1079</c>'s
    /// answer for the same situation. An out-of-range REFERENCE is refused, because there is no row
    /// that answers it and guessing paints a mesh with another mesh's texture — silently wrong is
    /// worse than magenta.
    /// </remarks>
    public static int TextureFor(
        short[] table, int references, int families, int skin, int reference)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (reference < 0)
        {
            return -1;
        }

        // **A model with no table is ordinary, not malformed.** Most props carry one family and
        // many carry no table at all, and for those the mesh's reference already IS the texture
        // index — the identity is the answer.
        if (table.Length == 0 || references <= 0 || families <= 0)
        {
            return reference;
        }

        if (reference >= references)
        {
            return -1;
        }

        // `props_shared.cpp:1079`. A placement naming a family the model does not have is input
        // this project does not control (D32).
        int family = skin >= 0 && skin < families ? skin : 0;
        int at = (family * references) + reference;

        // The header's counts and the table's length are separate facts in an untrusted file, so a
        // short table falls back rather than reading past its end.
        return at >= 0 && at < table.Length ? table[at] : reference;
    }

    private static int Count(ReadOnlyMemory<byte> file, int offset)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < offset + 4
            ? 0
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]));
    }
}
