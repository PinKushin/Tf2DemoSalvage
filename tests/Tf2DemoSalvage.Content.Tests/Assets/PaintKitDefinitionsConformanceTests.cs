using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CProtoBufScriptObjectDefinitionManager::BInitDefinitions` (tf_proto_script_obj_def.cpp:626-722) over `proto_defs.vpd`:
/// blocks of `GetInt()` type and count, each definition a `GetInt()` size and that many bytes of protobuf (:392-428), read
/// here for `DEF_TYPE_PAINTKIT_DEFINITION` (9, tf_proto_def_messages.proto:142-157) alone.
/// </summary>
public sealed class PaintKitDefinitionsConformanceTests
{
    private const int TypePaintKitVariables = 6;
    private const int TypePaintKitDefinition = 9;

    [Test]
    public void Read_TwoPaintKitDefinitions_KeepsEachDescriptionTokenByItsHeaderDefindex()
    {
        // `CMsgPaintKit_Definition` (.proto:609-628): `header` is field 1, whose `defindex` is field 1 (:111-140), and
        // `loc_desctoken` is field 2. 300 is a two-byte varint, so a one-byte reading of it would give 44.
        byte[] file = Block(TypePaintKitDefinition, Definition(300, "9_300_field { field_number: 2 }"), Definition(14, "#Kit14"));

        PaintKitDefinitions kits = PaintKitDefinitions.Read(file);

        (kits.Count, kits.DescriptionToken(300), kits.DescriptionToken(14), kits.DescriptionToken(44))
            .ShouldBe((2, "9_300_field { field_number: 2 }", "#Kit14", (string?)null));
    }

    [Test]
    public void Read_ABlockOfAnotherType_IsSkippedByItsSizes()
    {
        // Every type is framed the same way; a variables block before the kits must be stepped over by its sizes, its
        // bytes never read as a kit. Its header carries defindex 7, which must not appear.
        byte[] file = [.. Block(TypePaintKitVariables, Definition(7, "#NotAKit")), .. Block(TypePaintKitDefinition, Definition(8, "#Kit8"))];

        PaintKitDefinitions kits = PaintKitDefinitions.Read(file);

        (kits.Count, kits.DescriptionToken(7), kits.DescriptionToken(8)).ShouldBe((1, (string?)null, "#Kit8"));
    }

    [Test]
    public void Definition_AnItemDefinitionAndAKitAtOneIndex_AreKeptApartByType()
    {
        // `m_arDefinitionsMaps[ type ]` (:265-266): one map per type, so an item definition (8) and a kit (9) may share an
        // index. The quest types (0-5) are not kept.
        byte[] file = [.. Block(8, Definition(5, "#ItemDef")), .. Block(9, Definition(5, "#Kit")), .. Block(4, Definition(5, "#Quest"))];

        PaintKitDefinitions kits = PaintKitDefinitions.Read(file);

        (kits.Definition(8, 5)?.Text(2), kits.Definition(9, 5)?.Text(2), kits.Definition(4, 5)).ShouldBe(("#ItemDef", "#Kit", (ProtoMessage?)null));
    }

    [Test]
    public void Read_FieldsTheReaderDoesNotName_AreSkippedByTheirWireType()
    {
        // `ParsePartialFromArray` (:209) keeps what it knows and steps over the rest: a varint (`has_team_textures`, field 4),
        // a fixed 32-bit and a fixed 64-bit field, and a nested message (`paintkit_tool`, field 6), all before the token.
        List<byte> message = [.. Header(21)];
        message.AddRange([0x20, 0x01]);
        message.AddRange([(5 << 3) | 5, 1, 2, 3, 4]);
        message.AddRange([(9 << 3) | 1, 1, 2, 3, 4, 5, 6, 7, 8]);
        message.AddRange(LengthDelimited(6, [0x08, 0x05]));
        message.AddRange(LengthDelimited(2, Encoding.UTF8.GetBytes("#Kit21")));

        PaintKitDefinitions.Read(Block(TypePaintKitDefinition, [.. message])).DescriptionToken(21).ShouldBe("#Kit21");
    }

    [Test]
    public void Read_AFileCutShortInsideADefinition_KeepsTheDefinitionsBeforeIt()
    {
        // Decode is total: a size that runs past the end stops the read with what was whole, rather than throwing.
        byte[] whole = Block(TypePaintKitDefinition, Definition(1, "#One"), Definition(2, "#Two"));
        byte[] cut = whole[..^3];

        PaintKitDefinitions kits = PaintKitDefinitions.Read(cut);

        (kits.Count, kits.DescriptionToken(1)).ShouldBe((1, "#One"));
    }

    [Test]
    public void Read_ADefinitionWithNoToken_IsKnownWithAnEmptyToken()
    {
        // `loc_desctoken().c_str()` of an unset optional string is "" (econ_paintkit.h:61): the definition exists, so
        // `GetDefinition` answers, and its name is `FindSafe( "" )`.
        PaintKitDefinitions kits = PaintKitDefinitions.Read(Block(TypePaintKitDefinition, [.. Header(5)]));

        (kits.Count, kits.DescriptionToken(5)).ShouldBe((1, string.Empty));
    }

    private static byte[] Block(int type, params byte[][] definitions)
    {
        List<byte> bytes = [.. BitConverter.GetBytes(type), .. BitConverter.GetBytes(definitions.Length)];

        foreach (byte[] definition in definitions)
        {
            bytes.AddRange(BitConverter.GetBytes(definition.Length));
            bytes.AddRange(definition);
        }

        return [.. bytes];
    }

    private static byte[] Definition(uint defindex, string token) =>
        [.. Header(defindex), .. LengthDelimited(2, Encoding.UTF8.GetBytes(token))];

    /// <summary>Field 1, a `CMsgProtoDefHeader` holding `defindex` (field 1) and a `name` (field 2) the reader ignores.</summary>
    private static byte[] Header(uint defindex) =>
        LengthDelimited(1, [0x08, .. Varint(defindex), .. LengthDelimited(2, Encoding.UTF8.GetBytes("editor name"))]);

    private static byte[] LengthDelimited(int field, byte[] payload) => [(byte)((field << 3) | 2), .. Varint((uint)payload.Length), .. payload];

    private static byte[] Varint(uint value)
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
}
