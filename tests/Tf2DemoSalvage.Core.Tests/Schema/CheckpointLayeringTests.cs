using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// A per-entity checkpoint layers over the class baseline rather than replacing it.
/// </summary>
/// <remarks>
/// **This is a regression this project shipped, and it lasted about an hour** (B248). The fix for a
/// weapon's carry state (B245) made an `Enter` FORGET its accumulated properties and rebuild from
/// whatever baseline applies — which is what `CL_CopyNewEntity` does — and `EffectiveProperties`
/// then chose the entity's own checkpoint *instead of* the class baseline.
///
/// The engine chooses, and is right to: its checkpoints are complete packed entities, so anything a
/// class baseline could add is already in them. **Ours are built from the properties a snapshot
/// happened to carry, which is a subset.** So a partial checkpoint shadowed a complete class
/// baseline and everything only the baseline knew was dropped.
///
/// What it cost, measured on `tf2-2026-pub-pov-clean`: `CBaseDoor`'s class baseline declares
/// `m_nRenderMode = 10`, `kRenderNone`. Entity 532 last stated that on the wire at tick 6440 and
/// holds it for the rest of the recording. With the checkpoint shadowing it, the door came back as
/// `kRenderNormal` — so `cp_fulgur`'s invisible spawn doors began drawing as solid brushwork.
/// Prop count at tick 14000 went 559 → 549 and the composition shifted underneath that number,
/// which is why a bare count is a poor instrument for this.
///
/// **Layering is a superset of the engine's behaviour, not a departure from it.** Where a checkpoint
/// is complete the two are identical, because every baseline value is already present to be
/// overwritten. It stops being necessary the day our checkpoints hold full entity state.
/// </remarks>
public sealed class CheckpointLayeringTests
{
    /// <summary>The one class these fixtures network.</summary>
    private const int ClassId = 0;

    /// <summary>The entity slot under test.</summary>
    private const int EntityIndex = 3;

    [Test]
    public void EffectiveProperties_APartialCheckpoint_KeepsWhatOnlyTheClassBaselineKnows()
    {
        EntityDecoder decoder = Decoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);

        // The class baseline knows the render mode and nothing else — exactly `CBaseDoor`'s shape,
        // which declares `m_nRenderMode = 10` and is the only place that value comes from once the
        // entity stops restating it.
        decoder.SetBaseline(
            ClassId,
            EntityDecoder.EncodeProperties(
                [new DecodedProperty(0, flat[0], PropertyValue.FromInt(10))]));

        // Snapshot one checkpoints the entity while it is stating only its PARENT. That is the
        // partial checkpoint: it knows the parent and has never heard of the render mode.
        Decode(decoder, slot: false, updateBaseline: true,
            Entity(EntityUpdateType.Enter, new DecodedProperty(1, flat[1], PropertyValue.FromInt(1587610))));

        // Snapshot two names the slot that was just written and re-enters carrying nothing at all,
        // which is what a real re-entry looks like: measured on `cp_fulgur`, a spawn door re-enters
        // with zero properties.
        DecodedEntity returning = Decode(decoder, slot: true, updateBaseline: false,
            Entity(EntityUpdateType.Enter));

        Dictionary<int, long> effective = decoder.EffectiveProperties(returning)
            .ToDictionary(property => property.Index, property => property.Value.AsInt);

        effective.ContainsKey(0).ShouldBeTrue(
            "the checkpoint never knew the render mode, so the class baseline is what has it");
        effective[0].ShouldBe(10);

        effective[1].ShouldBe(1587610, "and the checkpoint's own value survives beside it");
    }

    [Test]
    public void EffectiveProperties_ACheckpointThatKnowsBetter_OverridesTheClassBaseline()
    {
        // **The control, and it is what stops layering becoming "the baseline always wins".** Where
        // both know a property, the CHECKPOINT is the newer statement and must win — otherwise a
        // re-entering entity would be dragged back to one representative entity's state, which is
        // the very failure B231 measured when the class baseline was used alone.
        EntityDecoder decoder = Decoder();
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(ClassId);

        decoder.SetBaseline(
            ClassId,
            EntityDecoder.EncodeProperties(
                [new DecodedProperty(0, flat[0], PropertyValue.FromInt(10))]));

        Decode(decoder, slot: false, updateBaseline: true,
            Entity(EntityUpdateType.Enter, new DecodedProperty(0, flat[0], PropertyValue.FromInt(3))));

        DecodedEntity returning = Decode(decoder, slot: true, updateBaseline: false,
            Entity(EntityUpdateType.Enter));

        decoder.EffectiveProperties(returning)
            .Single(property => property.Index == 0)
            .Value.AsInt
            .ShouldBe(3, "the checkpoint is the later statement about this entity");
    }

    /// <summary>Runs one snapshot through the decoder and hands back the entity it described.</summary>
    private static DecodedEntity Decode(
        EntityDecoder decoder, bool slot, bool updateBaseline, DecodedEntity entity)
    {
        byte[] body = decoder.EncodeEntities([entity], [], isDelta: true, 0, out int bits);

        PacketEntitiesMessage header = new(
            MaxEntries: 64,
            IsDelta: true,
            DeltaFromTick: 0,
            BaselineIndex: slot,
            UpdatedEntries: 1,
            LengthBits: bits,
            UpdateBaseline: updateBaseline,
            Body: body);

        return decoder.Decode(body, header, bits).Single();
    }

    private static DecodedEntity Entity(EntityUpdateType type, params DecodedProperty[] properties) =>
        new(EntityIndex, ClassId, 7, type, properties);

    /// <summary>A decoder over one class with two plain integer properties.</summary>
    private static EntityDecoder Decoder()
    {
        DemoSchema schema = new(
            [
                new SendTable("DT_Thing", NeedsDecoder: true,
                [
                    new SendProperty(
                        SendPropType.Int, "m_nRenderMode", 1, string.Empty, 0f, 0f, 8, 0),
                    new SendProperty(
                        SendPropType.Int, "moveparent", 1, string.Empty, 0f, 0f, 21, 0),
                ]),
            ],
            [new ServerClass(ClassId, "CThing", "DT_Thing")]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }
}
