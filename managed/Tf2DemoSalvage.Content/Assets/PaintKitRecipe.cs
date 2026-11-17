using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// `CPaintKitDefinition::GetItemPaintKitDefinitionKV( item, wear )` (econ_paintkit.cpp:411-536) over the compiled
/// messages: the `wear_level_N` recipe a war-painted item hands `NewTextureCompositor` (c_tf_player.cpp:3627-3661).
/// </summary>
/// <remarks>
/// <para>
/// **The walks are `ForEachConstProtoField`'s** (tf_proto_script_obj_def.h:431-497): fields in declaration order, only those
/// set (a oneof only by its set member), a singular field's callback before its recursion and a repeated element's
/// recursion before its callback, and a false return ending only its own level. Its lambdas test the PARENT message's
/// type, so an item fires on its own first set field, and a kit listing an item definition twice is built twice, the
/// last answering.
/// </para>
/// <para>
/// **The messages' declaration order is written out below** (tf_proto_def_messages.proto:104-683), because the compiled
/// file carries field numbers and not the order the generated descriptor lists them in.
/// </para>
/// <para>
/// *Interpolated:* `GetProtoObjectDefinitionFromMsg` and `GetMsgFromDefIDMsg` are not in the SDK; a `CMsgProtoDefID`
/// is resolved here as the manager's lookup of its own `type` (field 2) and `defindex` (field 1).
/// </para>
/// </remarks>
public static class PaintKitRecipe
{
    /// <summary>The messages the walks recurse into.</summary>
    private enum Kind
    {
        None,
        Header,
        VariableDefinition,
        VarField,
        DefId,
        Variables,
        Operation,
        OperationNode,
        OperationStage,
        TextureStage,
        CombineStage,
        SelectStage,
        Sticker,
        StickerStage,
        ItemDefinition,
        ItemDefinitionWear,
        Definition,
        DefinitionItem,
        ItemData,
    }

    /// <summary>One declared field: its number, name, whether repeated, the message it holds, and its oneof group (0 for none).</summary>
    private readonly record struct Declared(int Number, string Name, bool Repeated, Kind Message, int Oneof = 0);

    private static readonly Dictionary<Kind, Declared[]> Fields = new()
    {
        [Kind.Header] =
        [
            new(1, "defindex", false, Kind.None), new(2, "name", false, Kind.None), new(3, "prefabs", true, Kind.DefId),
            new(4, "tags", true, Kind.None), new(5, "prefab_only", false, Kind.None), new(6, "variables", true, Kind.VariableDefinition),
        ],
        [Kind.VariableDefinition] = [new(1, "name", false, Kind.None), new(2, "inherit", false, Kind.None), new(3, "value", false, Kind.None)],
        [Kind.VarField] =
        [
            new(1, "variable", false, Kind.None), new(2, "float", false, Kind.None, 1), new(3, "double", false, Kind.None, 1),
            new(4, "uint32", false, Kind.None, 1), new(5, "uint64", false, Kind.None, 1), new(6, "sint32", false, Kind.None, 1),
            new(7, "sint64", false, Kind.None, 1), new(8, "bool", false, Kind.None, 1), new(9, "string", false, Kind.None, 1),
        ],
        [Kind.DefId] =
        [
            new(1, "defindex", false, Kind.None), new(2, "type", false, Kind.None),
            new(3, "instance_def_type_quest_map_node", false, Kind.None, 1), new(5, "instance_def_type_quest_theme", false, Kind.None, 1),
            new(6, "instance_def_type_quest_map_region", false, Kind.None, 1), new(7, "instance_def_type_quest", false, Kind.None, 1),
            new(8, "instance_def_type_quest_objective", false, Kind.None, 1),
            new(9, "instance_def_type_paintkit_variables", false, Kind.Variables, 1),
            new(10, "instance_def_type_paintkit_operation", false, Kind.Operation, 1),
            new(11, "instance_def_type_paintkit_item_definition", false, Kind.ItemDefinition, 1),
            new(12, "instance_def_type_paintkit_definition", false, Kind.Definition, 1),
            new(13, "instance_def_type_header_only", false, Kind.None, 1),
        ],
        [Kind.Variables] = [new(1, "header", false, Kind.Header)],
        [Kind.Operation] = [new(1, "header", false, Kind.Header), new(2, "operation_node", true, Kind.OperationNode)],
        [Kind.OperationNode] = [new(1, "stage", false, Kind.OperationStage, 1), new(2, "operation_template", false, Kind.DefId, 1)],
        [Kind.OperationStage] =
        [
            new(1, "texture_lookup", false, Kind.TextureStage, 1), new(2, "combine_add", false, Kind.CombineStage, 1),
            new(3, "combine_lerp", false, Kind.CombineStage, 1), new(4, "combine_multiply", false, Kind.CombineStage, 1),
            new(5, "select", false, Kind.SelectStage, 1), new(6, "apply_sticker", false, Kind.StickerStage, 1),
        ],
        [Kind.TextureStage] = VarFields(
            "texture", "texture_red", "texture_blue", "adjust_black", "adjust_offset", "adjust_gamma", "rotation", "translate_u",
            "translate_v", "scale_uv", "flip_u", "flip_v"),
        [Kind.CombineStage] =
        [
            .. VarFields("adjust_black", "adjust_offset", "adjust_gamma", "rotation", "translate_u", "translate_v", "scale_uv", "flip_u", "flip_v"),
            new(11, "operation_node", true, Kind.OperationNode),
        ],
        [Kind.SelectStage] = [new(1, "groups", false, Kind.VarField), new(2, "select", true, Kind.VarField)],
        [Kind.Sticker] = VarFields("base", "weight", "spec"),
        [Kind.StickerStage] =
        [
            new(1, "sticker", true, Kind.Sticker), new(2, "dest_tl", false, Kind.VarField), new(3, "dest_tr", false, Kind.VarField),
            new(4, "dest_bl", false, Kind.VarField), new(5, "adjust_black", false, Kind.VarField),
            new(6, "adjust_offset", false, Kind.VarField), new(7, "adjust_gamma", false, Kind.VarField),
            new(9, "operation_node", true, Kind.OperationNode),
        ],
        [Kind.ItemDefinition] =
        [
            new(1, "header", false, Kind.Header), new(2, "item_definition_index", false, Kind.None),
            new(3, "variable_template", false, Kind.DefId), new(4, "definition", true, Kind.ItemDefinitionWear),
        ],
        [Kind.ItemDefinitionWear] = [new(1, "operation_template", false, Kind.DefId), new(2, "variable", true, Kind.VarField)],
        [Kind.Definition] = DefinitionFields(),
        [Kind.DefinitionItem] = [new(1, "item_definition_template", false, Kind.DefId), new(5, "data", false, Kind.ItemData)],
        [Kind.ItemData] =
        [
            new(2, "can_apply_paintkit", false, Kind.None), new(3, "material_override", false, Kind.None),
            new(4, "variable", true, Kind.VarField),
        ],
    };

    private static Declared[] VarFields(params string[] names)
    {
        Declared[] fields = new Declared[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            fields[i] = new Declared(i + 1, names[i], false, Kind.VarField);
        }

        return fields;
    }

    /// <summary>`CMsgPaintKit_Definition` (.proto:608-683): header, token, operation template, team flag, `item`, then 6-50.</summary>
    private static Declared[] DefinitionFields()
    {
        List<Declared> fields =
        [
            new(1, "header", false, Kind.Header), new(2, "loc_desctoken", false, Kind.None), new(3, "operation_template", false, Kind.DefId),
            new(4, "has_team_textures", false, Kind.None), new(5, "item", true, Kind.DefinitionItem),
        ];

        for (int number = 6; number <= 50; number++)
        {
            fields.Add(new Declared(number, "item_" + number.ToString(CultureInfo.InvariantCulture), false, Kind.DefinitionItem));
        }

        return [.. fields];
    }

    /// <summary>The kinds `HandleOperationToKVMsg` treats as stages whose var fields become keys (econ_paintkit.cpp:754-759).</summary>
    private static bool IsStageBody(Kind kind) =>
        kind is Kind.TextureStage or Kind.CombineStage or Kind.SelectStage or Kind.StickerStage or Kind.Sticker;

    /// <summary>A variable's value and whether a later source may override it (`m_bCanOverride`).</summary>
    private sealed class Variable(string value, bool canOverride)
    {
        public string Value { get; set; } = value;

        public bool CanOverride { get; } = canOverride;
    }

    /// <summary>
    /// The recipe for <paramref name="itemDefinition"/> painted with kit <paramref name="kit"/> at wear category
    /// <paramref name="wear"/> (1 to 5), or null where the kit lists no such item.
    /// </summary>
    /// <param name="definitions">The compiled paint kit messages.</param>
    /// <param name="kit">The item's `paintkit_proto_def_index`.</param>
    /// <param name="itemDefinition">`GetRemappedItemDefIndex()` of the painted item.</param>
    /// <param name="wear">`EconWear_ToIntCategory( flWear )`.</param>
    /// <returns>The `wear_level_N` node, or null.</returns>
    public static PaintKitNode? Build(PaintKitDefinitions definitions, uint kit, int itemDefinition, int wear)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Definition(PaintKitDefinitions.TypeDefinition, kit) is not { } kitMessage)
        {
            return null;
        }

        PaintKitNode? recipe = null;

        foreach (ProtoMessage item in KitItems(kitMessage))
        {
            // `has_item_definition_template()` (:893) and the template's own index (:898-900).
            if (item.Message(1) is not { } template
                || Resolve(definitions, template) is not { } itemDefinitionMessage
                || itemDefinitionMessage.Varint(2) is not { } index || (int)index != itemDefinition)
            {
                continue;
            }

            recipe = ItemRecipe(definitions, kitMessage, item, itemDefinitionMessage, wear);
        }

        return recipe;
    }

    /// <summary>
    /// `CPaintKitDefinition::GetMaterialOverride( iDefIndex )` (econ_paintkit.cpp:72-85): the `material_override` of the
    /// kit's FIRST item entry for <paramref name="itemDefinition"/> that sets `can_apply_paintkit`, or null where there is
    /// none or it is empty (`GenerateSupportedItems`, :87-149).
    /// </summary>
    /// <param name="definitions">The compiled paint kit messages.</param>
    /// <param name="kit">The item's `paintkit_proto_def_index`.</param>
    /// <param name="itemDefinition">`pItem->GetItemDefIndex()`: the item's own index, not the remapped one (econ_item_interface.cpp:511).</param>
    /// <returns>The VMT path the painted model is drawn with, or null.</returns>
    public static string? MaterialOverride(PaintKitDefinitions definitions, uint kit, int itemDefinition)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        if (definitions.Definition(PaintKitDefinitions.TypeDefinition, kit) is not { } kitMessage)
        {
            return null;
        }

        foreach (ProtoMessage item in KitItems(kitMessage))
        {
            // `has_item_definition_template()` (:102) and `data().can_apply_paintkit()` (:119); the first added wins (:128-134).
            if (item.Message(1) is { } template
                && item.Message(5) is { } data && data.Varint(2) is not (null or 0ul)
                && Resolve(definitions, template)?.Varint(2) is { } index && (int)index == itemDefinition)
            {
                return data.Text(3) is { Length: > 0 } material ? material : null;
            }
        }

        return null;
    }

    /// <summary>The kit's items in `ForEachConstProtoField`'s order: `item`'s elements, then fields 6 to 50 that are set.</summary>
    private static List<ProtoMessage> KitItems(ProtoMessage kit)
    {
        List<ProtoMessage> items = [.. kit.Messages(5)];

        for (int number = 6; number <= 50; number++)
        {
            if (kit.Message(number) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    /// <summary>The body of the matching item (:902-985).</summary>
    private static PaintKitNode ItemRecipe(PaintKitDefinitions definitions, ProtoMessage kit, ProtoMessage item, ProtoMessage itemDefinition, int wear)
    {
        ProtoMessage baseHeader = kit.Message(1) ?? EmptyMessage;
        ProtoMessage? operation = kit.Message(3) is { } kitTemplate ? Resolve(definitions, kitTemplate) : null;

        // "check if we should override operation msg" (:912-926): the wear's entry, clamped to the item's list.
        IReadOnlyList<ProtoMessage> wears = itemDefinition.Messages(4);
        ProtoMessage wearEntry = wears.Count > 0 ? wears[Math.Clamp(wear - 1, 0, wears.Count - 1)] : EmptyMessage;

        if (wearEntry.Message(1) is { } wearTemplate && Resolve(definitions, wearTemplate) is { } overrideOperation)
        {
            operation = overrideOperation;
            baseHeader = overrideOperation.Message(1) ?? EmptyMessage;
        }

        // `itemVarDict`, a `CUtlDict`: names case-insensitive, iterated in insertion order (:928-934).
        Dictionary<string, Variable> variables = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (ProtoMessage variable in baseHeader.Messages(6))
        {
            string name = variable.Text(1) ?? string.Empty;

            if (!variables.ContainsKey(name))
            {
                order.Add(name);
            }

            variables[name] = new Variable(variable.Text(3) ?? string.Empty, variable.Varint(2) is not 0ul);
        }

        // The item's data, the item definition's header, then the wear entry (:970-974).
        Override(variables, VarFieldsIn(definitions, item.Message(5), Kind.ItemData));
        Override(variables, VariableDefinitionsIn(itemDefinition.Message(1)));
        Override(variables, VarFieldsIn(definitions, wearEntry, Kind.ItemDefinitionWear));

        // `CreatePaintKitDefinitionKV` (:847-863).
        PaintKitNode recipe = new("wear_level_" + wear.ToString(CultureInfo.InvariantCulture));

        foreach (string name in order)
        {
            recipe.SetString("$" + name, variables[name].Value);
        }

        if (operation is not null)
        {
            OperationToKV(definitions, operation, Kind.Operation, recipe);
        }

        return recipe;
    }

    private static readonly ProtoMessage EmptyMessage =
        ProtoMessage.Parse(ReadOnlyMemory<byte>.Empty) ?? throw new InvalidOperationException("an empty message always parses");

    /// <summary>`lambdaUpdateVarField` / `lambdaUpdateVarDef`: an overridable variable takes a differing value (:936-968).</summary>
    private static void Override(Dictionary<string, Variable> variables, IEnumerable<(string Name, string Value)> values)
    {
        foreach ((string name, string value) in values)
        {
            if (variables.TryGetValue(name, out Variable? variable) && variable.CanOverride && !string.Equals(variable.Value, value, StringComparison.Ordinal))
            {
                variable.Value = value;
            }
        }
    }

    /// <summary>Every `CMsgVarField` a recursive walk of <paramref name="message"/> reaches, in its visiting order.</summary>
    private static List<(string Name, string Value)> VarFieldsIn(PaintKitDefinitions definitions, ProtoMessage? message, Kind kind)
    {
        List<(string Name, string Value)> found = [];

        if (message is not null)
        {
            Walk(definitions, message, kind, (parent, parentKind) =>
            {
                if (parentKind == Kind.VarField)
                {
                    found.Add((parent.Text(1) ?? string.Empty, parent.Text(9) ?? string.Empty));
                }
            });
        }

        return found;
    }

    /// <summary>Every `CMsgVariableDefinition` a recursive walk of a header reaches.</summary>
    private static List<(string Name, string Value)> VariableDefinitionsIn(ProtoMessage? header)
    {
        List<(string Name, string Value)> found = [];

        if (header is not null)
        {
            foreach (ProtoMessage variable in header.Messages(6))
            {
                found.Add((variable.Text(1) ?? string.Empty, variable.Text(3) ?? string.Empty));
            }
        }

        return found;
    }

    /// <summary>
    /// `ForEachConstProtoField( msg, op, bRecurse = true, bShowUnset = false )`, the callback told only the parent message
    /// and its kind, once per visited field.
    /// </summary>
    private static void Walk(PaintKitDefinitions definitions, ProtoMessage message, Kind kind, Action<ProtoMessage, Kind> visit)
    {
        foreach (Declared field in Fields.GetValueOrDefault(kind) ?? [])
        {
            if (!message.Has(field.Number))
            {
                continue;
            }

            if (field.Repeated)
            {
                // Each element's recursion, then its callback; a repeated scalar has one callback per element.
                if (field.Message == Kind.None)
                {
                    foreach (ProtoField element in message.Fields)
                    {
                        if (element.Number == field.Number)
                        {
                            visit(message, kind);
                        }
                    }

                    continue;
                }

                foreach (ProtoMessage element in message.Messages(field.Number))
                {
                    Walk(definitions, element, field.Message, visit);
                    visit(message, kind);
                }

                continue;
            }

            visit(message, kind);

            if (field.Message != Kind.None && message.Message(field.Number) is { } child)
            {
                Walk(definitions, child, field.Message, visit);
            }
        }
    }

    /// <summary>
    /// `OperationToKV` (econ_paintkit.cpp:794-845) with `HandleOperationToKVMsg` (:731-792): a `CMsgProtoDefID` anywhere
    /// inlines the definition it names; an operation stage's set member becomes a subkey named for its field; a stage's
    /// var field becomes a value, `$[variable]` when it names one.
    /// </summary>
    private static void OperationToKV(PaintKitDefinitions definitions, ProtoMessage message, Kind kind, PaintKitNode output)
    {
        foreach (Declared field in Fields.GetValueOrDefault(kind) ?? [])
        {
            // A scalar field's `HandleOperationToKVMsg` changes nothing: no stage body holds one.
            if (field.Message == Kind.None || !message.Has(field.Number))
            {
                continue;
            }

            IReadOnlyList<ProtoMessage> values = field.Repeated ? message.Messages(field.Number) : SingleOrNone(message.Message(field.Number));

            foreach (ProtoMessage value in values)
            {
                PaintKitNode next = Handle(definitions, kind, field, value, output);
                OperationToKV(definitions, value, field.Message, next);
            }
        }
    }

    private static IReadOnlyList<ProtoMessage> SingleOrNone(ProtoMessage? message) => message is null ? [] : [message];

    private static PaintKitNode Handle(PaintKitDefinitions definitions, Kind parent, Declared field, ProtoMessage value, PaintKitNode output)
    {
        if (field.Message == Kind.DefId)
        {
            if (Resolve(definitions, value, out Kind resolvedKind) is { } resolved)
            {
                OperationToKV(definitions, resolved, resolvedKind, output);
            }

            return output;
        }

        if (parent == Kind.OperationStage)
        {
            PaintKitNode stage = new(field.Name);
            output.Add(stage);
            return stage;
        }

        if (IsStageBody(parent))
        {
            if (field.Message == Kind.VarField)
            {
                PaintKitNode key = new(field.Name)
                {
                    Value = value.Has(1) ? "$[" + (value.Text(1) ?? string.Empty) + "]" : value.Text(9) ?? string.Empty,
                };
                output.Add(key);
                return key;
            }

            if (field.Message == Kind.Sticker)
            {
                PaintKitNode sticker = new(field.Name);
                output.Add(sticker);
                return sticker;
            }
        }

        return output;
    }

    /// <summary>A `CMsgProtoDefID`'s definition: its `type` (field 2) and `defindex` (field 1) in the manager.</summary>
    private static ProtoMessage? Resolve(PaintKitDefinitions definitions, ProtoMessage id) => Resolve(definitions, id, out _);

    private static ProtoMessage? Resolve(PaintKitDefinitions definitions, ProtoMessage id, out Kind kind)
    {
        int type = id.Varint(2) is { } typed ? (int)typed : -1;
        uint defindex = id.Varint(1) is { } index ? unchecked((uint)index) : 0u;

        kind = type switch
        {
            PaintKitDefinitions.TypeVariables => Kind.Variables,
            PaintKitDefinitions.TypeOperation => Kind.Operation,
            PaintKitDefinitions.TypeItemDefinition => Kind.ItemDefinition,
            PaintKitDefinitions.TypeDefinition => Kind.Definition,
            _ => Kind.None,
        };

        return kind == Kind.None ? null : definitions.Definition(type, defindex);
    }
}
