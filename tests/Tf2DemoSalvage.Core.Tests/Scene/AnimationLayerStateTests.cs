using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>An entity's <c>m_AnimOverlay</c> layers, read from its path-shaped state keys.</summary>
/// <remarks>
/// **Written to pin behaviour before B407's rewrite** of the reader, which allocated a string per property per call — 98 GB of
/// them across one f12 load. The fixture mirrors the real vector shape (`_ST_` / `lengthproxy` / numbered elements, all
/// referencing one element table), because the reader rides on that shape.
/// </remarks>
public sealed class AnimationLayerStateTests
{
    private const int ClassId = 0;
    private const int EntityIndex = 1;
    private const int Serial = 3;

    [Test]
    public void AnimationLayers_TwoLayersOutOfOrder_ComeBackSortedByOrder()
    {
        EntityState state = Apply(
            length: 2,
            (0, Sequence: 7, Cycle: 0.25f, Weight: 1f, Order: 1),
            (1, Sequence: 9, Cycle: 0.5f, Weight: 0.75f, Order: 0));

        IReadOnlyList<SceneAnimationLayer> layers = state.AnimationLayers();

        layers.Count.ShouldBe(2);
        layers[0].ShouldBe(new SceneAnimationLayer(0, 9, 0.5f, 0.75f));
        layers[1].ShouldBe(new SceneAnimationLayer(1, 7, 0.25f, 1f));
    }

    [Test]
    public void AnimationLayers_AnElementPastTheLength_IsAStaleSlot()
    {
        EntityState state = Apply(
            length: 1,
            (0, Sequence: 7, Cycle: 0.25f, Weight: 1f, Order: 0),
            (1, Sequence: 9, Cycle: 0.5f, Weight: 0.75f, Order: 1));

        state.AnimationLayers().ShouldHaveSingleItem().Sequence.ShouldBe(7);
    }

    [Test]
    public void AnimationLayers_AnOrderAtMaxOverlays_IsNoLayer()
    {
        // `if (m_AnimOverlay[i].m_nOrder < MAX_OVERLAYS)` — fifteen marks an unused slot.
        EntityState state = Apply(
            length: 2,
            (0, Sequence: 7, Cycle: 0f, Weight: 1f, Order: 15),
            (1, Sequence: 9, Cycle: 0f, Weight: 1f, Order: 2));

        state.AnimationLayers().ShouldHaveSingleItem().Order.ShouldBe(2);
    }

    /// <summary>Applies one Enter carrying the given layers, and the vector's length.</summary>
    private static EntityState Apply(
        int length, params (int Element, int Sequence, float Cycle, float Weight, int Order)[] layers)
    {
        EntityDecoder decoder = Decoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);
        List<DecodedProperty> properties = [At(flat, "lengthprop15", 0, PropertyValue.FromInt(length))];

        foreach ((int element, int sequence, float cycle, float weight, int order) in layers)
        {
            properties.Add(At(flat, "m_nSequence", element, PropertyValue.FromInt(sequence)));
            properties.Add(At(flat, "m_flCycle", element, PropertyValue.FromFloat(cycle)));
            properties.Add(At(flat, "m_flWeight", element, PropertyValue.FromFloat(weight)));
            properties.Add(At(flat, "m_nOrder", element, PropertyValue.FromInt(order)));
        }

        properties.Sort((left, right) => left.Index.CompareTo(right.Index));

        EntityStateTable table = new(decoder);
        table.Apply(new DecodedEntity(EntityIndex, ClassId, Serial, EntityUpdateType.Enter, properties));
        table.TryGet(EntityIndex, out EntityState? state).ShouldBeTrue();

        return state;
    }

    /// <summary>The Nth occurrence of a property name — the element, since one vector holds every occurrence.</summary>
    private static DecodedProperty At(IReadOnlyList<FlatProperty> flat, string name, int element, PropertyValue value)
    {
        int seen = 0;

        for (int index = 0; index < flat.Count; index++)
        {
            if (string.Equals(flat[index].Property.Name, name, StringComparison.Ordinal) && seen++ == element)
            {
                return new DecodedProperty(index, flat[index], value);
            }
        }

        throw new InvalidOperationException($"the fixture schema has no occurrence {element} of {name}");
    }

    private static EntityDecoder Decoder()
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_Animationlayer", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_nSequence", 1, string.Empty, 0f, 0f, 11, 0),
                    new SendProperty(SendPropType.Float, "m_flCycle", 1, string.Empty, 0f, 1f, 11, 0),
                    new SendProperty(SendPropType.Float, "m_flWeight", 1, string.Empty, 0f, 1f, 11, 0),
                    new SendProperty(SendPropType.Int, "m_nOrder", 1, string.Empty, 0f, 0f, 5, 0),
                ]),
                new SendTable("_LPT_m_AnimOverlay_15", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "lengthprop15", 1, string.Empty, 0f, 0f, 4, 0),
                ]),
                new SendTable("_ST_m_AnimOverlay_15", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.DataTable, "lengthproxy", 1, "_LPT_m_AnimOverlay_15", 0f, 0f, 0, 0),
                    new SendProperty(SendPropType.DataTable, "000", 1, "DT_Animationlayer", 0f, 0f, 0, 0),
                    new SendProperty(SendPropType.DataTable, "001", 1, "DT_Animationlayer", 0f, 0f, 0, 0),
                ]),
                new SendTable("DT_OverlayVars", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.DataTable, "m_AnimOverlay", 1, "_ST_m_AnimOverlay_15", 0f, 0f, 0, 0),
                ]),
                new SendTable("DT_Thing", NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                    new SendProperty(SendPropType.DataTable, "overlay_vars", 1, "DT_OverlayVars", 0f, 0f, 0, 0),
                ]),
            ],
            [new ServerClass(ClassId, "CThing", "DT_Thing")]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }
}
