using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The econ attributes an entity carries, kept apart by LIST and by ELEMENT.
/// </summary>
/// <remarks>
/// **The wire sends a vector of sub-tables, and this project's state key was lossy against it**
/// (B234). `SendPropUtlVectorDataTable( m_Attributes, MAX_ATTRIBUTES_PER_ITEM,
/// DT_ScriptCreatedAttribute )` generates `_ST_m_Attributes_20` = `[lengthproxy, 000..019]`, every
/// element referencing the SAME `DT_ScriptCreatedAttribute` — so all twenty elements flatten to the
/// identical `DT_ScriptCreatedAttribute.m_iAttributeDefinitionIndex` name and the last write wins.
/// Measured on `tf2-2026-pub-pov-clean`: 50,447 attribute properties on the wire, one key.
///
/// **And there are TWO lists, colliding with each other as well as themselves.**
/// `DT_ScriptCreatedItem` embeds `DT_AttributeList` twice — `m_AttributeList` and
/// `m_NetworkedDynamicAttributesForDemos` (`econ_item_view.cpp:191,193`), the second existing
/// PRECISELY for demos: `IterateAttributes` (`:523`) reads the local list first and falls back to
/// the networked list only when there is no SOC data, which in a recording is always.
///
/// **The value is 32 raw bits, not a number** — Valve's comment at `econ_item_view.cpp:62`: *"we
/// are networking the value as an int, even though it's a 'float', because really it isn't a
/// float. It's 32 raw bits."* `SENDINFO_NAME(m_flValue, m_iRawValue32)` sends it under the raw
/// name; era demos carry a genuine float under `m_flValue` instead (*"for demo compatibility
/// only"*, `:74`). One field, two spellings, and a reader keyed to one starves on the other era.
///
/// **The fixture mirrors the real table shapes** — the `_ST_` / `lengthproxy` / `_LPT_` chain and
/// numbered element tables — because the fix rides on the flattener recognising that shape, and a
/// flatter fixture would pass without exercising it.
/// </remarks>
public sealed class EconAttributeStateTests
{
    /// <summary>The one class this fixture networks.</summary>
    private const int ClassId = 0;

    /// <summary><c>is_festivized</c>'s definition index in the shipped schema.</summary>
    private const int Festivized = 2053;

    [Test]
    public void EconAttributes_TwoElementsOfOneList_BothSurvive()
    {
        // **The element collision, which is the whole of the defect.** Two attributes in one list
        // share every table name and property name; only their flattened positions differ, and the
        // state key dropped the position — so with defindex 5 and 2053 both applied, a
        // keyed-by-name store holds whichever came second.
        EntityStateTable table = Apply(Decoder(rawBits: true), rawBits: true,
            ("m_AttributeList", 0, 5, 1.1f),
            ("m_AttributeList", 1, Festivized, 1f));

        table.TryGet(EntityIndex, out EntityState? state).ShouldBeTrue();

        IReadOnlyList<EconAttributeValue> found = state.EconAttributes(EconAttributeList.Local);

        found.Count.ShouldBe(2, "two elements went in and the key must not collapse them");
        found.Single(attribute => attribute.DefinitionIndex == 5).Value.ShouldBe(1.1f, 0.0001f);
        found.Single(attribute => attribute.DefinitionIndex == Festivized).Value.ShouldBe(1f);
    }

    [Test]
    public void EconAttributes_TheTwoLists_AreKeptApart()
    {
        // **The list collision, and the engine's resolution order depends on telling them apart.**
        // `IterateAttributes` reads `m_AttributeList` FIRST and the demos list only as a fallback,
        // so an attribute present in both must resolve to the local value — which a reader that
        // merged the lists cannot express.
        EntityStateTable table = Apply(Decoder(rawBits: true), rawBits: true,
            ("m_AttributeList", 0, 5, 1.1f),
            ("m_NetworkedDynamicAttributesForDemos", 0, Festivized, 1f));

        table.TryGet(EntityIndex, out EntityState? state).ShouldBeTrue();

        state.EconAttributes(EconAttributeList.Local)
            .ShouldHaveSingleItem().DefinitionIndex.ShouldBe(5);

        state.EconAttributes(EconAttributeList.NetworkedForDemos)
            .ShouldHaveSingleItem().DefinitionIndex.ShouldBe(Festivized);
    }

    [Test]
    public void EconAttributes_AnEraFloatValue_ReadsTheSameAsRawBits()
    {
        // The era spelling: the same field as a genuine float under `m_flValue`, which is what
        // `RecvPropFloat( RECVINFO(m_flValue) ) // for demo compatibility only` decodes.
        EntityStateTable table = Apply(Decoder(rawBits: false), rawBits: false,
            ("m_AttributeList", 0, 5, 1.1f));

        table.TryGet(EntityIndex, out EntityState? state).ShouldBeTrue();

        state.EconAttributes(EconAttributeList.Local)
            .ShouldHaveSingleItem().Value.ShouldBe(1.1f, 0.0001f);
    }

    [Test]
    public void EconAttributes_ForAnEntityWithNone_AreEmpty()
    {
        // The control: an entity that never sent an attribute answers an empty list — not null,
        // and not a neighbour's values.
        EntityDecoder decoder = Decoder(rawBits: true);
        EntityStateTable table = new(decoder);

        table.Apply(new DecodedEntity(
            EntityIndex, ClassId, Serial, EntityUpdateType.Enter,
            [Scalar(decoder, "m_nModelIndex", 7)]));

        table.TryGet(EntityIndex, out EntityState? state).ShouldBeTrue();

        state.EconAttributes(EconAttributeList.Local).ShouldBeEmpty();
        state.EconAttributes(EconAttributeList.NetworkedForDemos).ShouldBeEmpty();
    }

    /// <summary>Entity slot and serial the fixtures use.</summary>
    private const int EntityIndex = 1;

    private const int Serial = 3;

    /// <summary>Applies one Enter carrying the given attributes through the real decode types.</summary>
    /// <param name="decoder">The schema-bound decoder whose flattened list supplies definitions.</param>
    /// <param name="rawBits">Whether the value travels as raw bits or as the era float.</param>
    /// <param name="attributes">List member, element ordinal, definition index, value.</param>
    private static EntityStateTable Apply(
        EntityDecoder decoder,
        bool rawBits,
        params (string List, int Element, int Definition, float Value)[] attributes)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);
        List<DecodedProperty> properties = [];

        foreach ((string list, int element, int definition, float value) in attributes)
        {
            properties.Add(At(flat, list, element, "m_iAttributeDefinitionIndex",
                PropertyValue.FromInt(definition)));

            properties.Add(rawBits
                ? At(flat, list, element, "m_iRawValue32",
                    PropertyValue.FromInt(BitConverter.SingleToInt32Bits(value)))
                : At(flat, list, element, "m_flValue", PropertyValue.FromFloat(value)));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        EntityStateTable table = new(decoder);

        table.Apply(new DecodedEntity(
            EntityIndex, ClassId, Serial, EntityUpdateType.Enter, properties));

        return table;
    }

    /// <summary>Finds a property by its position in the nested structure, not by name alone.</summary>
    /// <remarks>
    /// **Located by counting occurrences, because the names alone are the ambiguity under test.**
    /// The flattener emits each list's elements in order — the walk descends `m_AttributeList`
    /// before `m_NetworkedDynamicAttributesForDemos` because the class table references them in
    /// that order — so the Nth occurrence of a name belongs to a computable (list, element).
    /// </remarks>
    private static DecodedProperty At(
        IReadOnlyList<FlatProperty> flat, string list, int element, string name, PropertyValue value)
    {
        // Elements per list in this fixture; the demos list's occurrences come after all of the
        // local list's.
        int listBase = string.Equals(list, "m_AttributeList", StringComparison.Ordinal) ? 0 : Elements;
        int wanted = listBase + element;

        int seen = 0;

        for (int index = 0; index < flat.Count; index++)
        {
            if (!string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (seen == wanted)
            {
                return new DecodedProperty(index, flat[index], value);
            }

            seen++;
        }

        throw new InvalidOperationException(
            $"the fixture schema has no occurrence {wanted} of {name}");
    }

    /// <summary>A top-level scalar, for the control.</summary>
    private static DecodedProperty Scalar(EntityDecoder decoder, string name, int value)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);

        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(index, flat[index], PropertyValue.FromInt(value));
            }
        }

        throw new InvalidOperationException($"the fixture schema declares no {name}");
    }

    /// <summary>Elements each list declares in this fixture.</summary>
    private const int Elements = 2;

    /// <summary>A decoder over the real nested shape, modern or era-flavoured.</summary>
    private static EntityDecoder Decoder(bool rawBits)
    {
        SendProperty value = rawBits
            ? new SendProperty(SendPropType.Int, "m_iRawValue32", 1, string.Empty, 0f, 0f, 32, 0)
            : new SendProperty(SendPropType.Float, "m_flValue", 1, string.Empty, 0f, 0f, 32, 0);

        DemoSchema schema = new(
            [
                new SendTable("DT_ScriptCreatedAttribute", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.Int, "m_iAttributeDefinitionIndex", 1, string.Empty, 0f, 0f, 16, 0),
                    value,
                ]),
                new SendTable("_LPT_m_Attributes_20", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "lengthprop20", 1, string.Empty, 0f, 0f, 5, 0),
                ]),
                new SendTable("_ST_m_Attributes_20", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.DataTable, "lengthproxy", 1, "_LPT_m_Attributes_20", 0f, 0f, 0, 0),
                    new SendProperty(
                        SendPropType.DataTable, "000", 1, "DT_ScriptCreatedAttribute", 0f, 0f, 0, 0),
                    new SendProperty(
                        SendPropType.DataTable, "001", 1, "DT_ScriptCreatedAttribute", 0f, 0f, 0, 0),
                ]),
                new SendTable("DT_AttributeList", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.DataTable, "m_Attributes", 1, "_ST_m_Attributes_20", 0f, 0f, 0, 0),
                ]),
                new SendTable("DT_Thing", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                    new SendProperty(
                        SendPropType.DataTable, "m_AttributeList", 1, "DT_AttributeList", 0f, 0f, 0, 0),
                    new SendProperty(
                        SendPropType.DataTable, "m_NetworkedDynamicAttributesForDemos", 1,
                        "DT_AttributeList", 0f, 0f, 0, 0),
                ]),
            ],
            [new ServerClass(ClassId, "CThing", "DT_Thing")]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }
}
