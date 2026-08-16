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
    private const int MaximumEntries = 65536;

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

    private static int Count(ReadOnlyMemory<byte> file, int offset)
    {
        ReadOnlySpan<byte> bytes = file.Span;

        return bytes.Length < offset + 4
            ? 0
            : Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]));
    }
}
