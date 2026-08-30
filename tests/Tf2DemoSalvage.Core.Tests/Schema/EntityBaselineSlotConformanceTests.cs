using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// An entering entity deltas against its PER-ENTITY baseline, not its class's.
/// </summary>
/// <remarks>
/// **<c>svc_PacketEntities</c> carries two fields this project decoded, round-tripped and never
/// consumed** — <c>baseline</c> (which of two baseline arrays this snapshot deltas from) and
/// <c>update_baseline</c> (whether the client should rebuild the other array from what it just
/// read). Counted on the owner's `cp_fulgur` recording:
///
/// | flags | snapshots |
/// |---|---|
/// | `baseline=0 updatebaseline=0` | 12,340 |
/// | `baseline=0 updatebaseline=1` | 1,169 |
/// | `baseline=1 updatebaseline=0` | 12,798 |
/// | `baseline=1 updatebaseline=1` | 1,171 |
///
/// **2,340 snapshots ask for a baseline update and the index alternates**, so this is not a corner
/// of the protocol — it is how most entities re-enter the potentially visible set.
///
/// **The engine's networking source is closed** — `source-sdk-2013/src/engine` ships only `audio`,
/// and the SDK carries no more than the `CLC_BaselineAck` declaration in
/// `public/inetmsghandler.h:99`. So the semantics here are cross-checked against
/// [demostf/parser](https://github.com/demostf/parser), the reference this project names for demo
/// container and entity decode, read and not ported. Its `ParserState::get_baseline`
/// (`src/demo/parser/state.rs:153`) and the `updated_base_line` block
/// (`src/demo/parser/state.rs:271`) state the rules, and each test below pins one of them:
///
/// - **Read.** Use the stored baseline in the named slot only when it exists, its class MATCHES,
///   and the snapshot is a delta. Otherwise fall back to the class baseline.
/// - **Write.** On `update_baseline`, copy the whole named array into the other one, then for each
///   ENTERING entity store the merged state — its old baseline overlaid with this update.
///
/// **What ignoring it cost, measured.** An entity is created as a delta against whichever baseline
/// applies, so a two-property `EnterPVS` describes a door completely when the door's own state is
/// already stored. Read against the CLASS baseline instead, those two properties leave the entity
/// wearing a stranger's model and a stranger's position — `cp_fulgur`'s BLU spawn door came out as
/// `resupply_locker.mdl` at `prop_locker_blu_5`'s world origin, and the spawn cabinets acquired a
/// second keyframe thousands of units away that the timeline then interpolated through.
///
/// `docs/RISKS.md:388` listed this first under **Still to read** during the B12/B13 hunt, with the
/// note that *"a baseline swap that changes how a later delta is interpreted would look exactly
/// like this"*. It was right, and it went unread for months.
///
/// Synthetic and file-free (D38): every value asserted here was put there by the test.
/// </remarks>
public sealed class EntityBaselineSlotConformanceTests
{
    /// <summary>The class the entities under test belong to.</summary>
    private const int TestClass = 0;

    /// <summary>A second class, for the mismatch control.</summary>
    private const int OtherClass = 1;

    /// <summary>Slot the entity occupies.</summary>
    private const int Slot = 5;

    [Test]
    public void Decode_AnEnterInTheSlotItWasStoredIn_UsesTheStoredBaseline()
    {
        // The measured shape: a full update stores the entity, a later sparse Enter reads it back.
        EntityDecoder decoder = Decoder(classHealth: 11);

        // Snapshot one: baseline slot 0, asking for slot 1 to be rebuilt from what it carries.
        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        // Snapshot two reads slot 1 and carries only the team, so health can only come from the
        // stored per-entity baseline.
        DecodedEntity sparse = Decode(decoder, slot: true, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            77, "the entity's own stored baseline supplies what this update did not send");
    }

    [Test]
    public void Decode_AnEnterWithNothingStored_UsesTheClassBaseline()
    {
        // **The control, and the case that was the ONLY behaviour before.** Without it, a fix that
        // read the per-entity slot and never fell back would pass the test above and lose every
        // entity the server has not yet asked the client to store.
        EntityDecoder decoder = Decoder(classHealth: 11);

        DecodedEntity sparse = Decode(decoder, slot: false, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            11, "with no stored baseline for this entity the class baseline is what remains");
    }

    [Test]
    public void Decode_AnEnterReadingTheOtherSlot_DoesNotSeeTheStoredBaseline()
    {
        // **Two slots, and this is what makes them two.** A store into slot 1 must not be visible
        // to a snapshot that names slot 0 — otherwise the pair is one array with extra bookkeeping
        // and the alternation in the recording means nothing.
        //
        // Snapshot one names slot 1, so `update_baseline` rebuilds slot 0. Snapshot two then names
        // slot 1 itself, which was never written.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: true, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        DecodedEntity sparse = Decode(decoder, slot: true, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            11, "the store went to the OTHER slot, so this one still has nothing for the entity");
    }

    [Test]
    public void Decode_AnEnterWhoseStoredBaselineIsAnotherClass_UsesTheClassBaseline()
    {
        // `Some(baseline) if baseline.server_class == class_id` — a slot the engine reissues can
        // hold a stored baseline belonging to the PREVIOUS occupant, and merging that would give
        // the newcomer a stranger's state through a different door than the one just closed.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        DecodedEntity newcomer = Decode(decoder, slot: true, updateBaseline: false, isDelta: true,
            Enter(decoder, classId: OtherClass, team: 3))[0];

        Health(decoder.EffectiveProperties(newcomer)).ShouldBe(
            11, "a stored baseline for a different class does not describe this entity");
    }

    [Test]
    public void Decode_AnEnterInAFullUpdate_IgnoresTheStoredBaseline()
    {
        // `&& is_delta` in the same condition. A full update is the server saying "forget what you
        // had", so an entity in one is described against its class baseline however much is stored.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        DecodedEntity sparse = Decode(decoder, slot: true, updateBaseline: false, isDelta: false,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            11, "a full update is not a delta, so nothing stored applies to it");
    }

    [Test]
    public void Decode_ASnapshotWithoutTheFlag_StoresNothing()
    {
        // **The control on the write path.** Without it, "stores on update_baseline" and "stores on
        // every snapshot" are the same observation, and the flag would be decorative.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: false, updateBaseline: false, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        DecodedEntity sparse = Decode(decoder, slot: true, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            11, "the snapshot did not ask for a baseline update, so nothing was stored");
    }

    [Test]
    public void Decode_ADeltaUpdateUnderTheFlag_StoresNothingForThatEntity()
    {
        // `if entity.update_type == UpdateType::Enter` — the store covers entering entities only.
        // A delta describes a change to something already on screen and is not a new baseline.
        EntityDecoder decoder = Decoder(classHealth: 11);

        // An entity must enter before it can be delta'd — the decoder cannot size the properties
        // of a slot whose class it has never been told. This entering snapshot deliberately does
        // NOT ask for a baseline update, so anything stored below came from the delta.
        Decode(decoder, slot: false, updateBaseline: false, isDelta: true,
            Enter(decoder, health: 5, team: 1));

        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Delta(decoder, health: 77, team: 2));

        DecodedEntity sparse = Decode(decoder, slot: true, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            11, "a delta update does not become anybody's baseline");
    }

    [Test]
    public void Decode_AnEntityAbsentFromTheUpdatingSnapshot_KeepsItsStoredBaseline()
    {
        // **`baseline2.copy_from(baseline1)` — the whole array is carried across before the
        // entering entities are written into it.** Without the copy, one entity's update would
        // erase every other entity's stored baseline, and since the index alternates every
        // snapshot, nothing would ever survive two of them.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        // A second update naming slot 1, carrying a DIFFERENT entity. Slot 0 is rebuilt from slot
        // 1, and entity 5 must ride across in the copy even though it is not mentioned.
        Decode(decoder, slot: true, updateBaseline: true, isDelta: true,
            Enter(decoder, entityIndex: 9, health: 3, team: 1));

        DecodedEntity sparse = Decode(decoder, slot: false, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        Health(decoder.EffectiveProperties(sparse)).ShouldBe(
            77, "the array is copied wholesale, so an unmentioned entity keeps what it had");
    }

    [Test]
    public void Decode_AStoredBaselineBeingUpdated_IsTheMergedStateNotTheUpdate()
    {
        // `updated_baseline.apply_update(&entity.props)` — the stored value is the entity's old
        // baseline OVERLAID with this update, not the update alone. Storing the update alone would
        // make every baseline as sparse as the snapshot that produced it, so a value would survive
        // exactly one alternation and then vanish.
        EntityDecoder decoder = Decoder(classHealth: 11);

        Decode(decoder, slot: false, updateBaseline: true, isDelta: true,
            Enter(decoder, health: 77, team: 2));

        // Names slot 1 — where the entity was just stored — and sends only the team, then asks for
        // slot 0 to be rebuilt. Health can only reach slot 0 by being merged in.
        Decode(decoder, slot: true, updateBaseline: true, isDelta: true,
            Enter(decoder, team: 6));

        DecodedEntity sparse = Decode(decoder, slot: false, updateBaseline: false, isDelta: true,
            Enter(decoder, team: 3))[0];

        IReadOnlyList<DecodedProperty> effective = decoder.EffectiveProperties(sparse);

        Health(effective).ShouldBe(
            77, "the merged state carries forward what this update did not resend");

        // The control on the same read: the update's own value won where it spoke, and the read
        // above would also pass if the whole store had simply been ignored.
        Team(effective).ShouldBe(3, "and the snapshot still wins wherever it spoke");
    }

    /// <summary>A decoder whose class baseline sets a distinguishable health.</summary>
    private static EntityDecoder Decoder(uint classHealth)
    {
        EntityDecoder decoder = new(Schema(), EntityDecoder.ClassIdBits(2));

        // Both classes, so the mismatch control differs only in the class the entity claims.
        BaselineBuilder.Apply(
            [
                new StringTableEntry(0, "0", ClassBaseline(classHealth)),
                new StringTableEntry(1, "1", ClassBaseline(classHealth)),
            ],
            decoder);

        return decoder;
    }

    /// <summary>Runs one snapshot through the decoder and returns what it read.</summary>
    private static IReadOnlyList<DecodedEntity> Decode(
        EntityDecoder decoder,
        bool slot,
        bool updateBaseline,
        bool isDelta,
        DecodedEntity entity)
    {
        byte[] body = decoder.EncodeEntities([entity], [], isDelta, 0, out int bits);

        PacketEntitiesMessage snapshot = new(
            MaxEntries: 64,
            IsDelta: isDelta,
            DeltaFromTick: isDelta ? 1 : null,
            BaselineIndex: slot,
            UpdatedEntries: 1,
            LengthBits: bits,
            UpdateBaseline: updateBaseline,
            Body: body);

        return decoder.Decode(body, snapshot, bits);
    }

    private static DecodedEntity Enter(
        EntityDecoder decoder,
        uint? health = null,
        uint? team = null,
        int classId = TestClass,
        int entityIndex = Slot) =>
        Entity(decoder, EntityUpdateType.Enter, classId, entityIndex, health, team);

    private static DecodedEntity Delta(
        EntityDecoder decoder, uint? health = null, uint? team = null) =>
        Entity(decoder, EntityUpdateType.Delta, TestClass, Slot, health, team);

    private static DecodedEntity Entity(
        EntityDecoder decoder,
        EntityUpdateType updateType,
        int classId,
        int entityIndex,
        uint? health,
        uint? team)
    {
        IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(classId);
        List<DecodedProperty> properties = [];

        if (health is { } value)
        {
            properties.Add(new DecodedProperty(0, flat[0], PropertyValue.FromInt((int)value)));
        }

        if (team is { } number)
        {
            properties.Add(new DecodedProperty(1, flat[1], PropertyValue.FromInt((int)number)));
        }

        return new DecodedEntity(entityIndex, classId, 3, updateType, properties);
    }

    /// <summary>The class baseline's encoded form: <c>m_iHealth</c> and nothing else.</summary>
    /// <remarks>Written the way <c>BaselineBuilderTests</c> writes one, which is the wire form.</remarks>
    private static byte[] ClassBaseline(uint health)
    {
        BitWriter writer = new();
        writer.Write(1, 1).UBitVar(0).Write(health, 10).Write(0, 1);
        return writer.Build();
    }

    private static long Health(IReadOnlyList<DecodedProperty> properties) => Value(properties, 0);

    private static long Team(IReadOnlyList<DecodedProperty> properties) => Value(properties, 1);

    private static long Value(IReadOnlyList<DecodedProperty> properties, int index) =>
        properties.Single(property => property.Index == index).Value.AsInt;

    /// <summary>Two classes over one table, so a mismatch is expressible.</summary>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_Test", NeedsDecoder: true,
            [
                new SendProperty(
                    SendPropType.Int, "m_iHealth", Unsigned, string.Empty, 0f, 0f, 10, 0),
                new SendProperty(
                    SendPropType.Int, "m_iTeamNum", Unsigned, string.Empty, 0f, 0f, 3, 0),
            ]),
        ],
        [new ServerClass(TestClass, "CTest", "DT_Test"), new ServerClass(OtherClass, "COther", "DT_Test")]);

    /// <summary><c>SPROP_UNSIGNED</c>.</summary>
    private const int Unsigned = 1;
}
