using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The paint kit definitions of `proto_defs.vpd`, as `GetProtoScriptObjDefManager()` holds them
/// (tf_proto_script_obj_def.cpp:545-554, :626-722): every message of the four paint kit types by its header's `defindex`.
/// </summary>
/// <remarks>
/// <para>
/// **The file is blocks, each a type and a count, then per definition a size and that many bytes of protobuf**
/// (`BInitDefinitions` :144-161, `BParseFromBuffer` :201-216); `GetInt()` is a little-endian 32-bit integer. The quest
/// types are stepped over by their sizes, as each is framed the same way.
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

    /// <summary>`DEF_TYPE_PAINTKIT_VARIABLES` (tf_proto_def_messages.proto:142-157).</summary>
    public const int TypeVariables = 6;

    /// <summary>`DEF_TYPE_PAINTKIT_OPERATION`.</summary>
    public const int TypeOperation = 7;

    /// <summary>`DEF_TYPE_PAINTKIT_ITEM_DEFINITION`.</summary>
    public const int TypeItemDefinition = 8;

    /// <summary>`DEF_TYPE_PAINTKIT_DEFINITION`.</summary>
    public const int TypeDefinition = 9;

    private readonly Dictionary<(int Type, uint Defindex), ProtoMessage> _definitions;

    private PaintKitDefinitions(Dictionary<(int Type, uint Defindex), ProtoMessage> definitions) => _definitions = definitions;

    /// <summary>How many paint kit definitions (type 9) were read.</summary>
    public int Count
    {
        get
        {
            int count = 0;

            foreach ((int type, uint _) in _definitions.Keys)
            {
                count += type == TypeDefinition ? 1 : 0;
            }

            return count;
        }
    }

    /// <summary>`GetDefinition( ProtoDefID_t( type, defindex ) )->GetMsg()` (:255-273), or null.</summary>
    /// <param name="type">A `ProtoDefTypes` value, 6 to 9.</param>
    /// <param name="defindex">The definition's header `defindex`.</param>
    /// <returns>The compiled message, or null.</returns>
    public ProtoMessage? Definition(int type, uint defindex) => _definitions.GetValueOrDefault((type, defindex));

    /// <summary>
    /// `GetDefinition( ProtoDefID_t( DEF_TYPE_PAINTKIT_DEFINITION, index ) )->GetDescriptionToken()` (econ_paintkit.h:61):
    /// the kit's `loc_desctoken` (field 2), "" when it has none, or null when there is no such kit.
    /// </summary>
    /// <param name="defindex">The kit's `paintkit_proto_def_index`.</param>
    /// <returns>The token, or null.</returns>
    public string? DescriptionToken(uint defindex) =>
        Definition(TypeDefinition, defindex) is { } kit ? kit.Text(2) ?? string.Empty : null;

    /// <summary>The paint kit types of a `proto_defs.vpd`.</summary>
    /// <param name="file">The file's bytes, kept by reference.</param>
    /// <returns>The definitions that were whole.</returns>
    public static PaintKitDefinitions Read(ReadOnlyMemory<byte> file)
    {
        Dictionary<(int Type, uint Defindex), ProtoMessage> definitions = [];
        ReadOnlySpan<byte> span = file.Span;
        int at = 0;

        while (span.Length - at >= 8)
        {
            int type = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
            int count = BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]);
            at += 8;

            for (int definition = 0; definition < count; definition++)
            {
                if (span.Length - at < 4)
                {
                    return new PaintKitDefinitions(definitions);
                }

                int size = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
                at += 4;

                if (size < 0 || size > span.Length - at)
                {
                    return new PaintKitDefinitions(definitions);
                }

                if (type is >= TypeVariables and <= TypeDefinition && ProtoMessage.Parse(file.Slice(at, size)) is { } message)
                {
                    // Every paint kit message's `header` is field 1, its `defindex` field 1 within (.proto:111-140, :501-628).
                    // `InsertObjectIntoMap` (:275-291): a second def at an index replaces the first.
                    uint defindex = message.Message(1)?.Varint(1) is { } index ? unchecked((uint)index) : 0u;
                    definitions[(type, defindex)] = message;
                }

                at += size;
            }
        }

        return new PaintKitDefinitions(definitions);
    }
}
