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
public readonly record struct BspStaticProp(
    string Model, float X, float Y, float Z, float Pitch, float Yaw, float Roll, float Scale,
    int Skin = 0);

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

    private const int DirectoryEntryBytes = 16;

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
    /// **Thirty-two rather than thirty-one, because of padding.** The members before it are
    /// <c>m_PropType</c>, <c>m_FirstLeaf</c> and <c>m_LeafCount</c> (three <c>unsigned short</c>,
    /// ending at 30) then <c>m_Solid</c>, one byte. The next member is an <c>int</c>, which the
    /// compiler aligns to four — so byte 31 is padding and the skin begins at 32.
    ///
    /// Derived independently by <c>StaticPropConformanceTests</c> from the declaration itself, so
    /// this constant is checked rather than asserted.
    ///
    /// **That sentence was false until 2026-08-21**: the test derived 32 from the SDK and compared
    /// it against the literal 32, never against this field. It is internal now so the comparison it
    /// claims can actually happen — which is the whole conformance sweep in one example.
    /// </remarks>
    internal const int SkinOffset = 32;

    /// <summary>Reads every static prop a map places.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The placements, empty when the map has none.</returns>
    /// <exception cref="InvalidDataException">The game lump is malformed.</exception>
    public static IReadOnlyList<BspStaticProp> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);
        BspLump game = header.Lump(BspLumpIndex.GameLump);

        if (game.Length == 0)
        {
            return [];
        }

        if (!TryFindPayload(file, game, out ReadOnlyMemory<byte> payload, out int version))
        {
            return [];
        }

        return ReadPayload(payload.Span, version);
    }

    /// <summary>Locates the static-prop sub-lump inside the game lump and decompresses it.</summary>
    private static bool TryFindPayload(
        ReadOnlyMemory<byte> file, BspLump game, out ReadOnlyMemory<byte> payload, out int version)
    {
        payload = ReadOnlyMemory<byte>.Empty;
        version = 0;

        ReadOnlySpan<byte> directory = file.Slice(game.Offset, game.Length).Span;

        if (directory.Length < sizeof(int))
        {
            throw new InvalidDataException("The game lump is too short to hold its own count.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(directory);

        if (count < 0 || sizeof(int) + ((long)count * DirectoryEntryBytes) > directory.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"The game lump declares {count:N0} sub-lumps, which do not fit in its " +
                $"{directory.Length:N0} bytes."));
        }

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = directory.Slice(
                sizeof(int) + (index * DirectoryEntryBytes), DirectoryEntryBytes);

            if (BinaryPrimitives.ReadInt32LittleEndian(entry) != StaticPropId)
            {
                continue;
            }

            version = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);

            // **The stored length is the decompressed one when the entry is compressed**, so the
            // bytes actually present run to the next entry's offset. Taking the rest of the file
            // would work for the last entry and overrun into the next lump for any other.
            int packedEnd = NextOffset(directory, count, index, file.Length);

            if (offset < 0 || packedEnd <= offset || packedEnd > file.Length)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The static prop lump lies at {offset:N0} to {packedEnd:N0} of a " +
                    $"{file.Length:N0}-byte file."));
            }

            // Through the same reader as every other lump, which recognises the LZMA header by its
            // magic rather than by the flag - one place that knows how a compressed lump looks.
            payload = BspLumpData.Read(file, new BspLump(offset, packedEnd - offset, version));

            if (payload.Length > length && length > 0)
            {
                payload = payload[..length];
            }

            return true;
        }

        return false;
    }

    /// <summary>Where the sub-lump after this one begins, or the end of the file.</summary>
    private static int NextOffset(
        ReadOnlySpan<byte> directory, int count, int index, int fileLength)
    {
        int next = fileLength;

        for (int other = 0; other < count; other++)
        {
            if (other == index)
            {
                continue;
            }

            int offset = BinaryPrimitives.ReadInt32LittleEndian(
                directory.Slice(sizeof(int) + (other * DirectoryEntryBytes) + 8, sizeof(int)));

            int mine = BinaryPrimitives.ReadInt32LittleEndian(
                directory.Slice(sizeof(int) + (index * DirectoryEntryBytes) + 8, sizeof(int)));

            if (offset > mine && offset < next)
            {
                next = offset;
            }
        }

        return next;
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
                ReadSkin(prop, stride)));
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
