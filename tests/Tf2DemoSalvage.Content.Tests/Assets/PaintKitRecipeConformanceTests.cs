using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CPaintKitDefinition::GetItemPaintKitDefinitionKV` (econ_paintkit.cpp:411-536) and `OperationToKV` (:794-845) over
/// hand-built compiled messages (tf_proto_def_messages.proto:104-683).
/// </summary>
public sealed class PaintKitRecipeConformanceTests
{
    /// <remarks>
    /// The kit (9:1) names operation 7:2 and lists item 8:3 (item def 200). The KIT's header is the base (:904) and declares
    /// `color` = blue and `shade` = light with `inherit` false; the kit item's data sets `color` = red; the item definition's
    /// header sets `shade` = dark, refused because `shade` cannot be overridden (:944, :961). The tree is one `combine_add`
    /// holding a `texture_lookup` whose `texture` names the variable, written `$[color]` (:770), and a literal `rotation`.
    /// </remarks>
    [Test]
    public void Build_AKitItemWithVariables_MergesThemAndWritesTheStageTree()
    {
        PaintKitDefinitions definitions = PaintKitDefinitions.Read(File());

        PaintKitNode recipe = PaintKitRecipe.Build(definitions, kit: 1, itemDefinition: 200, wear: 1).ShouldNotBeNull();

        Flatten(recipe).ShouldBe(
        [
            "wear_level_1",
            "  $color = red",
            "  $shade = light",
            "  combine_add",
            "    texture_lookup",
            "      texture = $[color]",
            "      rotation = 0 360",
        ]);
    }

    [Test]
    public void Build_AnItemTheKitDoesNotList_IsNull()
    {
        PaintKitDefinitions definitions = PaintKitDefinitions.Read(File());

        (PaintKitRecipe.Build(definitions, 1, 201, 1), PaintKitRecipe.Build(definitions, 99, 200, 1)).ShouldBe(((PaintKitNode?)null, (PaintKitNode?)null));
    }

    /// <remarks>
    /// `CPaintKitDefinition::GetMaterialOverride` (econ_paintkit.cpp:72-85) over `GenerateSupportedItems` (:87-149): an
    /// item without `can_apply_paintkit` is skipped (:119), the FIRST supported entry for a definition wins (:128-134),
    /// and an empty `material_override` is no override (:140). Kit 2 lists item 200 three times — unsupported "a", then
    /// supported "b", then supported "c" — and item 201 once, supported with no material.
    /// </remarks>
    [Test]
    public void MaterialOverride_TheFirstSupportedEntry_WinsAndAnEmptyOneIsNone()
    {
        PaintKitDefinitions definitions = PaintKitDefinitions.Read(MaterialFile());

        (PaintKitRecipe.MaterialOverride(definitions, 2, 200), PaintKitRecipe.MaterialOverride(definitions, 2, 201),
                PaintKitRecipe.MaterialOverride(definitions, 2, 202), PaintKitRecipe.MaterialOverride(definitions, 9, 200))
            .ShouldBe(("b", (string?)null, (string?)null, (string?)null));
    }

    private static byte[] MaterialFile()
    {
        // `CMsgPaintKit_Definition_Item.data` (5) is `ItemData`: can_apply_paintkit (2), material_override (3).
        static byte[] Item(uint template, bool canApply, string? material) => Message(
            Field(1, DefId(8, template)),
            Field(5, Message(canApply ? Varint(2, 1) : [], material is null ? [] : Field(3, Encoding.UTF8.GetBytes(material)))));

        byte[] kit = Message(
            Field(1, Header(2)),
            Field(5, Item(3, canApply: false, "a")),
            Field(5, Item(3, canApply: true, "b")),
            Field(5, Item(3, canApply: true, "c")),
            Field(5, Item(4, canApply: true, string.Empty)));

        return
        [
            .. Block(8, Message(Field(1, Header(3)), Varint(2, 200))),
            .. Block(8, Message(Field(1, Header(4)), Varint(2, 201))),
            .. Block(9, kit),
        ];
    }

    private static List<string> Flatten(PaintKitNode node, int depth = 0, List<string>? lines = null)
    {
        lines ??= [];
        lines.Add(new string(' ', depth * 2) + node.Name + (node.Value is null ? string.Empty : " = " + node.Value));

        foreach (PaintKitNode child in node.Children)
        {
            Flatten(child, depth + 1, lines);
        }

        return lines;
    }

    private static byte[] File()
    {
        // `CMsgPaintKit_Operation_TextureStage`: texture (1), rotation (7).
        byte[] textureLookup = Message(Field(1, VarField(variable: "color")), Field(7, VarField(text: "0 360")));

        // `OperationNode.stage` (1) holding `OperationStage.texture_lookup` (1).
        byte[] textureNode = Message(Field(1, Message(Field(1, textureLookup))));

        // `CombineStage.operation_node` (11), inside `OperationStage.combine_add` (2), inside a node's `stage` (1).
        byte[] combineNode = Message(Field(1, Message(Field(2, Message(Field(11, textureNode))))));

        // `CMsgPaintKit_Operation`: header (1), operation_node (2).
        byte[] operation = Message(Field(1, Header(2)), Field(2, combineNode));

        // `CMsgPaintKit_ItemDefinition`: header (1), item_definition_index (2), one wear entry (4).
        byte[] itemDefinition = Message(Field(1, Header(3, Variable("shade", "dark"))), Varint(2, 200), Field(4, Message()));

        // `CMsgPaintKit_Definition`: header (1), operation_template (3), item (5) of template (1) and data (5).
        byte[] kit = Message(
            Field(1, Header(1, Variable("color", "blue"), Variable("shade", "light", inherit: false))),
            Field(3, DefId(7, 2)),
            Field(5, Message(Field(1, DefId(8, 3)), Field(5, Message(Field(4, VarField(variable: "color", text: "red")))))));

        return [.. Block(7, operation), .. Block(8, itemDefinition), .. Block(9, kit)];
    }

    private static byte[] Header(uint defindex, params byte[][] variables)
    {
        List<byte> header = [.. Varint(1, defindex)];

        foreach (byte[] variable in variables)
        {
            header.AddRange(Field(6, variable));
        }

        return [.. header];
    }

    private static byte[] Variable(string name, string value, bool inherit = true) =>
        Message(Field(1, Encoding.UTF8.GetBytes(name)), inherit ? [] : Varint(2, 0), Field(3, Encoding.UTF8.GetBytes(value)));

    private static byte[] VarField(string? variable = null, string? text = null) =>
        Message(
            variable is null ? [] : Field(1, Encoding.UTF8.GetBytes(variable)),
            text is null ? [] : Field(9, Encoding.UTF8.GetBytes(text)));

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
