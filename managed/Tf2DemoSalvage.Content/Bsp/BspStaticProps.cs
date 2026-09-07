using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One placed instance of a model.</summary>
/// <param name="Model">The model's path, such as <c>models/props_forest/rock_01.mdl</c>.</param>
/// <param name="X">Where it stands, east-west.</param>
/// <param name="Y">Where it stands, north-south.</param>
/// <param name="Z">Where it stands, vertically.</param>
/// <param name="Pitch">Rotation about the side axis, in degrees.</param>
/// <param name="Yaw">Rotation about the vertical axis, in degrees.</param>
/// <param name="Roll">Rotation about the forward axis, in degrees.</param>
/// <param name="Scale">Uniform scale, 1 unless the map declares otherwise.</param>
/// <param name="Skin">
/// Which skin family the model draws with, <c>StaticPropLump_t.m_Skin</c>. Zero for most props,
/// and the reason this is not optional: a model with team or state variants draws its FIRST family
/// when this is ignored, which is not an error and reads as the map's own art.
/// <c>cap_point_base.mdl</c> has three.
/// </param>
/// <param name="Solid">
/// How the prop collides, <c>StaticPropLump_t.m_Solid</c> — the mapper's own <c>solid</c> key,
/// copied straight through by <c>vbsp</c> (`utils/vbsp/staticprop.cpp:603`), so its values are the
/// <c>SolidType_t</c> enum: <c>SOLID_NONE</c> is 0 and <c>SOLID_VPHYSICS</c> is 6
/// (`public/const.h:238-244`).
///
/// **It is here because the engine's physics world contains these props and this project's did
/// not.** `PhysicsLevelInit` builds the world and then immediately calls
/// `staticpropmgr-&gt;CreateVPhysicsRepresentations( physenv, &amp;g_SolidSetup, NULL )` — two lines
/// apart, on the CLIENT (`game/client/physics.cpp:184-186`), which is the environment a ragdoll
/// lives in.
/// </param>
public readonly record struct BspStaticProp(
    string Model, float X, float Y, float Z, float Pitch, float Yaw, float Roll, float Scale,
    int Skin = 0, int Solid = 0);

/// <summary>
/// The models a map places itself: rocks, crates, fences, foliage.
/// </summary>
/// <remarks>
/// **These are why a correctly decoded map still has holes in it.** A displacement painted with
/// <c>tools/toolsinvisibledisplacement</c> is collision-only terrain the engine never draws, and
/// what a player actually sees standing there is a static prop placed on top of it — the small rock
/// at cp_process mid being the case that named this. Skipping the tool material without drawing the
/// props leaves exactly that shape of hole, which is what the fuzzy black patches were.
///
/// They live in the GAME lump (35), which is a directory of its own rather than a structure array:
///
/// <code>
///   int              lumpCount
///   dgamelump_t[]    { int id; ushort flags; ushort version; int fileofs; int filelen; }
/// </code>
///
/// with <c>id</c> reading <c>'sprp'</c> for this one. Its payload is three counted arrays back to
/// back — a dictionary of model paths, a leaf index, then the placements:
///
/// <code>
///   int dictEntries;   char name[dictEntries][128]
///   int leafEntries;   ushort leaf[leafEntries]
///   int propEntries;   StaticPropLump_t prop[propEntries]
/// </code>
///
/// **The placement structure grew over the engine's life and the version does not tell you enough.**
/// Valve added fields at versions 5, 6, 7, 10 and 11, and third-party compilers ship their own
/// variants; a table of version-to-size is a list of things to be wrong about. The remaining bytes
/// divided by the count give the stride outright, and it must divide exactly — the same arithmetic
/// that identified the compressed lumps. Every field this reader wants sits in the first 56 bytes,
/// which every version shares, so knowing the stride is enough and the version is only checked for
/// the trailing scale.
///
/// **A compressed sub-lump does not declare its packed size.** <c>filelen</c> is the DECOMPRESSED
/// size when the compression flag is set, so the packed bytes run from this entry's offset to the
/// next entry's — which is why the directory has to be read as a whole before any payload is.
/// </remarks>
public static class BspStaticProps
{

    /// <summary>'sprp', as it appears in the game lump directory.</summary>
    private const int StaticPropId = 0x73707270;

    /// <summary>Bytes per model path in the dictionary, fixed since the format's first version.</summary>
    internal const int ModelNameBytes = 128;

    /// <summary>The fields every version of the placement structure shares.</summary>
    internal const int MinimumPropStride = 56;

    /// <summary>Beyond this, the stride is not a placement structure.</summary>
    /// <remarks>
    /// A map is untrusted input (D32). The largest version Valve shipped is 76 bytes; the ceiling
    /// is generous rather than exact so a third-party compiler's variant still reads, while a
    /// count of 1 against a megabyte of payload does not become a plausible stride.
    /// </remarks>
    private const int MaximumPropStride = 256;

    /// <summary>The version that added uniform scale, as its own trailing float.</summary>
    internal const int ScaleVersion = 11;

    internal const int OriginOffset = 0;
    internal const int AnglesOffset = 12;
    internal const int PropTypeOffset = 24;

    /// <summary>Offset of <c>StaticPropLump_t.m_Skin</c>, in every declared version.</summary>
    /// <remarks>
    /// **Thirty-two rather than thirty-one, and the byte between is a FIELD.** The members before
    /// it are <c>m_PropType</c>, <c>m_FirstLeaf</c> and <c>m_LeafCount</c> (three
    /// <c>unsigned short</c>, ending at 30), then <c>m_Solid</c> and <c>m_Flags</c>, one byte each
    /// — so the skin begins at 32 with nothing padded away.
    ///
    /// **This said "byte 31 is padding" until 2026-09-07 and that was wrong**, which mattered the
    /// moment <see cref="SolidOffset"/> was needed: a reader that believes two bytes are padding
    /// does not go looking in them. `StaticPropLumpV4_t` declares both
    /// (`public/gamebspfile.h:158-160`).
    ///
    /// Derived independently by <c>StaticPropConformanceTests</c> from the declaration itself, so
    /// this constant is checked rather than asserted.
    ///
    /// **That sentence was false until 2026-08-21**: the test derived 32 from the SDK and compared
    /// it against the literal 32, never against this field. It is internal now so the comparison it
    /// claims can actually happen — which is the whole conformance sweep in one example.
    /// </remarks>
    internal const int SkinOffset = 32;

    /// <summary>Offset of <c>StaticPropLump_t.m_Solid</c>, in every declared version.</summary>
    /// <remarks>
    /// **Thirty, immediately after the three <c>unsigned short</c> that precede it** —
    /// `public/gamebspfile.h:155-159`. Every version from V4 up declares the same prefix, which is
    /// why this reader's measured stride is enough and the version is not consulted.
    /// </remarks>
    internal const int SolidOffset = 30;

    /// <summary><c>SOLID_NONE</c> — the one value that means the prop is not collided.</summary>
    /// <remarks>
    /// **A prop's <c>m_Solid</c> is whatever the mapper typed**, copied through unexamined by
    /// `vbsp` — `build.m_Solid = IntForKey( &amp;entities[i], "solid" )`
    /// (`utils/vbsp/staticprop.cpp:603`) — so the test is against this and not against a list of
    /// the values that ARE collidable. `SOLID_VPHYSICS` (6) is the usual one and `SOLID_BBOX` (2)
    /// appears too; both collide, and so does anything else a map happens to carry.
    ///
    /// **`FSOLID_NOT_SOLID` does NOT apply here.** It is an entity solid FLAG
    /// (`public/const.h:252`), tested beside a solid type by `IsSolid`, and a static prop lump
    /// carries no flags word of that kind — its `m_Flags` is the `STATIC_PROP_*` set, which is
    /// shadows and lighting. So the whole rule is this one comparison.
    /// </remarks>
    public const int SolidNone = 0;

    /// <summary>Reads every static prop a map places.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The placements, empty when the map has none.</returns>
    /// <exception cref="InvalidDataException">The game lump is malformed.</exception>
    /// <remarks>
    /// **The directory walk moved to <see cref="BspGameLumps"/> when a second sub-lump needed it**
    /// (B360). `sprp` was the only entry anyone read, so the walk lived here; `dprp` — the detail
    /// props — made it shared, and the packed-end rule is subtle enough that a second copy would
    /// have been a second chance to get it wrong.
    /// </remarks>
    public static IReadOnlyList<BspStaticProp> Read(ReadOnlyMemory<byte> file)
    {
        foreach (BspGameLumpEntry entry in BspGameLumps.Directory(file))
        {
            if (entry.Id == StaticPropId)
            {
                return ReadPayload(BspGameLumps.Payload(file, entry).Span, entry.Version);
            }
        }

        return [];
    }

    private static List<BspStaticProp> ReadPayload(ReadOnlySpan<byte> payload, int version)
    {
        int at = 0;

        string[] models = ReadDictionary(payload, ref at);

        // The leaf array is skipped rather than read: it says which visibility leaves each prop
        // touches, which matters for a renderer that culls by PVS and not for one drawing the map.
        int leaves = ReadCount(payload, ref at, "leaf");

        if ((long)leaves * sizeof(ushort) > payload.Length - at)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The static prop lump declares {leaves:N0} leaf entries, beyond its own length."));
        }

        at += leaves * sizeof(ushort);

        int props = ReadCount(payload, ref at, "prop");

        if (props == 0)
        {
            return [];
        }

        int remaining = payload.Length - at;

        // **The stride is measured, not looked up.** A version table is a list of sizes to be
        // wrong about; the bytes present divided by the count is the size the compiler actually
        // wrote, and a wrong reading does not divide exactly.
        if (remaining % props != 0)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"{remaining:N0} bytes of static prop data do not divide into {props:N0} " +
                $"placements."));
        }

        int stride = remaining / props;

        if (stride is < MinimumPropStride or > MaximumPropStride)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Static prop version {version} implies a {stride:N0}-byte placement, which is " +
                $"not one."));
        }

        List<BspStaticProp> placements = new(props);

        for (int index = 0; index < props; index++)
        {
            ReadOnlySpan<byte> prop = payload.Slice(at + (index * stride), stride);

            int type = BinaryPrimitives.ReadUInt16LittleEndian(prop[PropTypeOffset..]);

            if (type >= models.Length)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Static prop {index} names model {type} of {models.Length:N0}."));
            }

            placements.Add(new BspStaticProp(
                models[type],
                BinaryPrimitives.ReadSingleLittleEndian(prop[OriginOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(prop[(OriginOffset + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(prop[(OriginOffset + 8)..]),
                BinaryPrimitives.ReadSingleLittleEndian(prop[AnglesOffset..]),
                BinaryPrimitives.ReadSingleLittleEndian(prop[(AnglesOffset + 4)..]),
                BinaryPrimitives.ReadSingleLittleEndian(prop[(AnglesOffset + 8)..]),
                ReadScale(prop, version, stride),
                ReadSkin(prop, stride),
                prop[SolidOffset]));
        }

        return placements;
    }

    /// <summary>The skin family, where the placement is long enough to carry one.</summary>
    /// <remarks>
    /// **Guarded by the stride rather than by the version.** Every declared version has the field
    /// at the same offset, but a lump whose stride is somehow shorter would otherwise be read past
    /// its end — and this reader already accepts a stride it derives from the data rather than one
    /// it assumes. A placement too short to hold the field reports family zero, which is what the
    /// renderer did for every prop before this existed.
    ///
    /// Negative values are clamped away for the same reason <c>ReadScale</c> rejects zero: the skin
    /// indexes a table, and a compiler writing rubbish should cost the prop its variant rather than
    /// throwing out of a map that is otherwise fine.
    /// </remarks>
    private static int ReadSkin(ReadOnlySpan<byte> prop, int stride)
    {
        if (stride < SkinOffset + sizeof(int))
        {
            return 0;
        }

        int skin = BinaryPrimitives.ReadInt32LittleEndian(prop[SkinOffset..]);

        return skin > 0 ? skin : 0;
    }

    /// <summary>The uniform scale, where the map's version carries one.</summary>
    /// <remarks>
    /// Version 11 appended it as a trailing float. A prop that declares zero is read as 1: a
    /// scale of zero collapses the model to a point, and a compiler that wrote a version-11 lump
    /// without filling the field is likelier than a mapper who asked for nothing to be drawn.
    /// </remarks>
    private static float ReadScale(ReadOnlySpan<byte> prop, int version, int stride)
    {
        if (version < ScaleVersion || stride < sizeof(float))
        {
            return 1f;
        }

        float scale = BinaryPrimitives.ReadSingleLittleEndian(prop[(stride - sizeof(float))..]);

        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    private static string[] ReadDictionary(ReadOnlySpan<byte> payload, ref int at)
    {
        int count = ReadCount(payload, ref at, "dictionary");

        if ((long)count * ModelNameBytes > payload.Length - at)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The static prop lump declares {count:N0} model names, beyond its own length."));
        }

        string[] models = new string[count];

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> name = payload.Slice(at + (index * ModelNameBytes), ModelNameBytes);
            int end = name.IndexOf((byte)0);

            // UTF-8 rather than ASCII, as every string in this project is: a path is a path
            // whatever the mapper's keyboard produced, and ASCII would replace what it cannot read
            // with a question mark rather than failing.
            models[index] = Encoding.UTF8.GetString(end < 0 ? name : name[..end]);
        }

        at += count * ModelNameBytes;

        return models;
    }

    private static int ReadCount(ReadOnlySpan<byte> payload, ref int at, string what)
    {
        if (at + sizeof(int) > payload.Length)
        {
            throw new InvalidDataException(
                $"The static prop lump ends before its {what} count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(payload[at..]);

        if (count < 0)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The static prop lump declares {count:N0} {what} entries."));
        }

        at += sizeof(int);

        return count;
    }
}
