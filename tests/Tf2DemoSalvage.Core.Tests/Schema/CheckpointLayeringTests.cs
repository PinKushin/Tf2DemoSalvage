using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// A checkpoint is stored MERGED, so it never knows less than the class baseline did.
/// </summary>
/// <remarks>
/// **These began as tests for a workaround and now test the real thing** (B248, then B250). The fix
/// for a weapon's carry state (B245) made an `Enter` forget its accumulated properties and rebuild
/// from whatever baseline applies — which is what `CL_CopyNewEntity` does — and the checkpoint it
/// then chose was stored as the bare UPDATE. So a checkpoint could know less than the class
/// baseline, shadow it, and drop everything only the baseline knew.
///
/// What that cost, measured on `tf2-2026-pub-pov-clean`: `CBaseDoor`'s class baseline declares
/// `m_nRenderMode = 10`, `kRenderNone`. Entity 532 last stated it on the wire at tick 6440 and
/// holds it for the rest of the recording. With the checkpoint shadowing it the door came back
/// `kRenderNormal`, so `cp_fulgur`'s invisible spawn doors drew as solid brushwork. Prop count at
/// tick 14000 went 559 → 549 while the composition shifted underneath that number, which is why a
/// bare count is a poor instrument for this.
///
/// **The first repair layered the two, which worked and was a divergence.** The engine chooses one
/// buffer. Reading its store side settled where the real fault was:
///
/// <code>
///   RecvTable_MergeDeltas( table, fromBuf, update, newBuf );
///   SetEntityBaseline( clientState, baseline == 0, classId, entity, newBuf, len );
/// </code>
///
/// `fromBuf` is whichever baseline the entity was decoded against — **the class baseline when no
/// slot applied** — so the engine stores the merged state and its checkpoints can never know less.
/// Fixing our store the same way made layering redundant: 560 props either way at tick 14000, doors
/// `kRenderNone` either way. The divergence was removed rather than kept as insurance.
/// </remarks>
public sealed class CheckpointLayeringTests
{
    /// <summary>The one class these fixtures network.</summary>
    private const int ClassId = 0;

    /// <summary>The entity slot under test.</summary>
    private const int EntityIndex = 3;

    [Test]
    public void EffectiveProperties_AnEntityCheckpointedWithoutASlot_StillCarriesTheClassBaseline()
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

    // **A test was written here claiming a checkpoint must carry values a DELTA changed, and it was
    // wrong about the protocol** — worth recording, because the reasoning behind it is the sort that
    // sounds airtight. It said: a weapon is deployed and holstered by deltas, so a checkpoint built
    // only from entering snapshots freezes at whatever was true when the entity last came into view,
    // and the entity reverts on its next `Enter`.
    //
    // **The server does not let that happen, and the two baseline slots are the machinery by which
    // it does not.** They exist so the SERVER knows which baseline each client is holding —
    // `clc_BaselineAck` is the client saying which one it has caught up to — and an enter-PVS update
    // is a delta against THAT. If the client's checkpoint says one thing and the truth is another,
    // the difference is in the update. A silent re-entry against a stale baseline is a packet the
    // protocol never produces, and that is exactly what the deleted test hand-built.
    //
    // `EntityBaselineSlotConformanceTests` already had this right, with the citation:
    // `if entity.update_type == UpdateType::Enter` — the store covers entering entities only. The
    // attempted "fix" contradicted a conformance test, and the conformance test won.

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
