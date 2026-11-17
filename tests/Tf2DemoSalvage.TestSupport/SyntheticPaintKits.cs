using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.TestSupport;

/// <summary>
/// A `proto_defs.vpd` holding one paint kit (B754), framed as the game ships it ([type][count] then [size][protobuf]) with
/// the field numbers of tf_proto_def_messages.proto: a kit whose recipe is one `combine_multiply` of one `texture_lookup`.
/// </summary>
public static class SyntheticPaintKits
{
    /// <summary>A file with kit <paramref name="kit"/> listing item <paramref name="item"/>.</summary>
    /// <param name="kit">The kit's `paintkit_proto_def_index`.</param>
    /// <param name="item">The item definition index the kit lists, with `can_apply_paintkit` set.</param>
    /// <param name="material">The entry's `material_override`, or null for none.</param>
    /// <param name="texture">The lookup's texture.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] File(uint kit, int item, string? material, string texture)
    {
        // `OperationNode.stage` (1) holding `OperationStage.texture_lookup` (1) whose `texture` (1) is a literal (9).
        byte[] lookup = Message(Field(1, Message(Field(9, Encoding.UTF8.GetBytes(texture)))));
        byte[] textureNode = Message(Field(1, Message(Field(1, lookup))));

        // `combine_multiply` is `OperationStage` field 4; its `operation_node` is 11.
        byte[] combineNode = Message(Field(1, Message(Field(4, Message(Field(11, textureNode))))));
        byte[] operation = Message(Field(1, Header(1)), Field(2, combineNode));
        byte[] itemDefinition = Message(Field(1, Header(2)), Varint(2, (uint)item), Field(4, Message()));

        // The kit item's `data` (5): `can_apply_paintkit` (2), `material_override` (3).
        byte[] data = Message(Varint(2, 1), material is null ? [] : Field(3, Encoding.UTF8.GetBytes(material)));
        byte[] definition = Message(
            Field(1, Header(kit)),
            Field(3, DefId(7, 1)),
            Field(5, Message(Field(1, DefId(8, 2)), Field(5, data))));

        return [.. Block(7, operation), .. Block(8, itemDefinition), .. Block(9, definition)];
    }

    private static byte[] Header(uint defindex) => Varint(1, defindex);

    private static byte[] DefId(uint type, uint defindex) => Message(Varint(1, defindex), Varint(2, type));

    private static byte[] Message(params byte[][] fields)
    {
        List<byte> bytes = [];

        foreach (byte[] field in fields)
        {
            bytes.AddRange(field);
        }

        return [.. bytes];
    }

    private static byte[] Field(int number, byte[] payload) => [(byte)((number << 3) | 2), .. Encode((uint)payload.Length), .. payload];

    private static byte[] Varint(int number, uint value) => [(byte)(number << 3), .. Encode(value)];

    private static byte[] Encode(uint value)
    {
        List<byte> bytes = [];

        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return [.. bytes];
    }

    private static byte[] Block(int type, byte[] definition) =>
        [.. BitConverter.GetBytes(type), .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(definition.Length), .. definition];
}
