using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The paint kit definitions of `proto_defs.vpd`, as `GetProtoScriptObjDefManager()` holds them for
/// `DEF_TYPE_PAINTKIT_DEFINITION` (tf_proto_script_obj_def.cpp:545-554, :626-722): each kit's `loc_desctoken` by its
/// header's `defindex`.
/// </summary>
/// <remarks>
/// <para>
/// **The file is blocks, each a type and a count, then per definition a size and that many bytes of protobuf**
/// (`BInitDefinitions` :144-161, `BParseFromBuffer` :201-216); `GetInt()` is a little-endian 32-bit integer. Only the
/// kits are kept: every other type is stepped over by its sizes, as each is framed the same way.
/// </para>
/// <para>
/// **The signature beside it (`proto_defs.vpd.sig`, :108-136) is not checked.** The engine refuses a file whose signature
/// fails; this reads the user's own installed file, which the game has already accepted.
/// </para>
/// <para>
/// **Decode is total**: a size or field that runs past the end ends the read with the definitions that were whole, where
/// the engine's `SCHEMA_INIT_CHECK` would fail the load.
/// </para>
/// </remarks>
public sealed class PaintKitDefinitions
{
    /// <summary>`g_pszProtoDefFile` (tf_proto_script_obj_def.cpp:31).</summary>
    public const string FilePath = "scripts/protodefs/proto_defs.vpd";

    /// <summary>`DEF_TYPE_PAINTKIT_DEFINITION` (tf_proto_def_messages.proto:142-157).</summary>
    private const int TypePaintKitDefinition = 9;

    private const int WireVarint = 0;
    private const int WireFixed64 = 1;
    private const int WireLengthDelimited = 2;
    private const int WireFixed32 = 5;

    private readonly Dictionary<uint, string> _tokens;

    private PaintKitDefinitions(Dictionary<uint, string> tokens) => _tokens = tokens;

    /// <summary>How many paint kit definitions were read.</summary>
    public int Count => _tokens.Count;

    /// <summary>
    /// `GetDefinition( ProtoDefID_t( DEF_TYPE_PAINTKIT_DEFINITION, index ) )->GetDescriptionToken()` (econ_paintkit.h:61):
    /// the kit's `loc_desctoken`, "" when it has none, or null when there is no such kit.
    /// </summary>
    /// <param name="defindex">The kit's `paintkit_proto_def_index`.</param>
    /// <returns>The token, or null.</returns>
    public string? DescriptionToken(uint defindex) => _tokens.GetValueOrDefault(defindex);

    /// <summary>The kits of a `proto_defs.vpd`.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <returns>The definitions that were whole.</returns>
    public static PaintKitDefinitions Read(ReadOnlySpan<byte> file)
    {
        Dictionary<uint, string> tokens = [];
        int at = 0;

        while (file.Length - at >= 8)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(file[at..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]);
            at += 8;

            for (int definition = 0; definition < count; definition++)
            {
                if (file.Length - at < 4)
                {
                    return new PaintKitDefinitions(tokens);
                }

                int size = BinaryPrimitives.ReadInt32LittleEndian(file[at..]);
                at += 4;

                if (size < 0 || size > file.Length - at)
                {
                    return new PaintKitDefinitions(tokens);
                }

                if (type == TypePaintKitDefinition && Kit(file.Slice(at, size)) is (uint defindex, string token))
                {
                    // `InsertObjectIntoMap` (:275-291): a second def at an index replaces the first.
                    tokens[defindex] = token;
                }

                at += size;
            }
        }

        return new PaintKitDefinitions(tokens);
    }

    /// <summary>A `CMsgPaintKit_Definition`'s header `defindex` (fields 1.1) and `loc_desctoken` (field 2), or null when cut short.</summary>
    private static (uint Defindex, string Token)? Kit(ReadOnlySpan<byte> message)
    {
        uint defindex = 0;
        string token = string.Empty;
        int at = 0;

        while (at < message.Length)
        {
            if (!Field(message, ref at, out int number, out int wire, out ulong value, out int payloadAt))
            {
                return null;
            }

            if (number == 1 && wire == WireLengthDelimited)
            {
                defindex = HeaderDefindex(message.Slice(payloadAt, (int)value)) ?? defindex;
            }
            else if (number == 2 && wire == WireLengthDelimited)
            {
                token = Encoding.UTF8.GetString(message.Slice(payloadAt, (int)value));
            }
        }

        return (defindex, token);
    }

    /// <summary>`CMsgProtoDefHeader.defindex`, field 1, or null when absent; what was read before a cut stands.</summary>
    private static uint? HeaderDefindex(ReadOnlySpan<byte> header)
    {
        uint? defindex = null;
        int at = 0;

        while (at < header.Length && Field(header, ref at, out int number, out int wire, out ulong value, out _))
        {
            if (number == 1 && wire == WireVarint)
            {
                defindex = unchecked((uint)value);
            }
        }

        return defindex;
    }

    /// <summary>
    /// One field: its number and wire type, and in <paramref name="value"/> a varint's value or a length-delimited field's
    /// length, whose payload starts at <paramref name="payloadAt"/>. False when cut short.
    /// </summary>
    private static bool Field(ReadOnlySpan<byte> message, ref int at, out int number, out int wire, out ulong value, out int payloadAt)
    {
        number = 0;
        wire = 0;
        value = 0;
        payloadAt = 0;

        if (Varint(message, ref at) is not { } tag)
        {
            return false;
        }

        number = (int)(tag >> 3);
        wire = (int)(tag & 7);

        switch (wire)
        {
            case WireVarint:
                if (Varint(message, ref at) is not { } read)
                {
                    return false;
                }

                value = read;
                return true;

            case WireFixed64:
            case WireFixed32:
                int width = wire == WireFixed64 ? 8 : 4;

                if (message.Length - at < width)
                {
                    return false;
                }

                at += width;
                return true;

            case WireLengthDelimited:
                if (Varint(message, ref at) is not { } length || length > (ulong)(message.Length - at))
                {
                    return false;
                }

                value = length;
                payloadAt = at;
                at += (int)length;
                return true;

            default:
                // Groups (3, 4) and the reserved types do not appear in these messages.
                return false;
        }
    }

    /// <summary>A base-128 varint of at most ten bytes, or null when cut short.</summary>
    private static ulong? Varint(ReadOnlySpan<byte> bytes, ref int at)
    {
        ulong value = 0;

        for (int shift = 0; shift < 70 && at < bytes.Length; shift += 7)
        {
            byte next = bytes[at++];
            value |= (ulong)(next & 0x7F) << shift;

            if ((next & 0x80) == 0)
            {
                return value;
            }
        }

        return null;
    }
}
