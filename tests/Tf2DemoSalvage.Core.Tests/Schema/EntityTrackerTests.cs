using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Accumulating entity state across snapshots.
/// </summary>
/// <remarks>
/// A snapshot carries only what *changed*, so a decoded snapshot answers "what moved this tick"
/// and cannot answer "where was this player at this tick". Everything past a trace needs the
/// second question answered — a 2D viewer is exactly a query for every player's position at an
/// arbitrary tick — and that requires carrying values forward.
///
/// The property that matters most here is the one a single-entity, single-property test cannot
/// see: an update to one entity must leave every other entity, and every *other property of the
/// same entity*, untouched. Both are checked below with bystanders rather than inferred.
/// </remarks>
public sealed class EntityTrackerTests
{
    private static FlatProperty Property(string table, string name) =>
        new(new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 8, 0), table, null);

    private static DecodedProperty Value(string table, string name, int index, long value) =>
        new(index, Property(table, name), PropertyValue.FromInt(value));

    private static DecodedEntity Entity(
        int index, EntityUpdateType type, params DecodedProperty[] properties) =>
        new(index, ClassId: 7, SerialNumber: 0, type, properties);

    private static DecodedEntity EntityWithSerial(
        int index, int serial, params DecodedProperty[] properties) =>
        new(index, ClassId: 7, serial, EntityUpdateType.Enter, properties);

    [Fact]
    public void EnteringEntity_HasItsProperties()
    {
        EntityTracker tracker = new();

        tracker.Apply([Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 125))]);

        tracker.State(3).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(125);
    }

    [Fact]
    public void DeltaUpdate_CarriesForwardWhatItDidNotMention()
    {
        // The whole point. A delta names only what changed, so a tracker that replaced state
        // instead of merging it would lose every property the tick happened not to touch.
        EntityTracker tracker = new();
        tracker.Apply(
        [
            Entity(3, EntityUpdateType.Enter,
                Value("DT_A", "health", 0, 125),
                Value("DT_A", "ammo", 1, 32)),
        ]);

        tracker.Apply([Entity(3, EntityUpdateType.Delta, Value("DT_A", "health", 0, 90))]);

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();
        state["DT_A.health"].AsInt.ShouldBe(90);

        // The bystander property. Without it, "merged" and "replaced with only health" are
        // indistinguishable.
        state["DT_A.ammo"].AsInt.ShouldBe(32);
    }

    [Fact]
    public void UpdatingOneEntity_LeavesOthersAlone()
    {
        // The bystander entity. With one entity in the table, "changed the target" and
        // "changed everything" produce identical evidence.
        EntityTracker tracker = new();
        tracker.Apply(
        [
            Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 125)),
            Entity(4, EntityUpdateType.Enter, Value("DT_A", "health", 0, 200)),
        ]);

        tracker.Apply([Entity(3, EntityUpdateType.Delta, Value("DT_A", "health", 0, 90))]);

        tracker.State(3).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(90);
        tracker.State(4).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(200);
    }

    [Fact]
    public void DeletedEntity_IsForgotten()
    {
        EntityTracker tracker = new();
        tracker.Apply(
        [
            Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 125)),
            Entity(4, EntityUpdateType.Enter, Value("DT_A", "health", 0, 200)),
        ]);

        tracker.Apply([Entity(3, EntityUpdateType.Delete)]);

        tracker.State(3).ShouldBeNull();
        tracker.State(4).ShouldNotBeNull();          // bystander survives the deletion
        tracker.ActiveEntities.ShouldBe([4]);
    }

    [Fact]
    public void LeavingEntity_KeepsItsStateBecauseItMayReturn()
    {
        // Leave means "no longer in the PVS", not "destroyed" - the entity is still alive on
        // the server and will resume delta updates against the state it left with. Forgetting
        // it would make the next delta reference properties the tracker no longer has.
        EntityTracker tracker = new();
        tracker.Apply([Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 125))]);

        tracker.Apply([Entity(3, EntityUpdateType.Leave)]);

        tracker.State(3).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(125);

        // But it is not currently visible, which is a different question from whether it exists.
        tracker.ActiveEntities.ShouldNotContain(3);
    }

    [Fact]
    public void ReEnteringAfterLeaving_BecomesVisibleAgain()
    {
        EntityTracker tracker = new();
        tracker.Apply([Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 125))]);
        tracker.Apply([Entity(3, EntityUpdateType.Leave)]);

        tracker.Apply([Entity(3, EntityUpdateType.Delta, Value("DT_A", "health", 0, 90))]);

        tracker.ActiveEntities.ShouldContain(3);
        tracker.State(3).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(90);
    }

    [Fact]
    public void SameNameInDifferentTables_AreSeparateProperties()
    {
        // DT_TFLocalPlayerExclusive.m_vecOrigin and DT_TFNonLocalPlayerExclusive.m_vecOrigin
        // both exist and hold different values in every real demo, so keying state on the bare
        // property name would silently collapse them into one.
        EntityTracker tracker = new();

        tracker.Apply(
        [
            Entity(3, EntityUpdateType.Enter,
                Value("DT_Local", "m_vecOrigin", 0, 10),
                Value("DT_NonLocal", "m_vecOrigin", 1, 20)),
        ]);

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();
        state["DT_Local.m_vecOrigin"].AsInt.ShouldBe(10);
        state["DT_NonLocal.m_vecOrigin"].AsInt.ShouldBe(20);
    }

    [Fact]
    public void UnknownEntity_HasNoState()
    {
        new EntityTracker().State(99).ShouldBeNull();
    }

    [Fact]
    public void Apply_NullEntities_Throws()
    {
        Should.Throw<System.ArgumentNullException>(() => new EntityTracker().Apply(null!));
    }

    [Fact]
    public void SlotReusedByADifferentEntity_DoesNotInheritTheOldOne()
    {
        // Entity slots are recycled, and the serial number is the only thing distinguishing the
        // new occupant from the old. Merging into the old state would leave a dead player's
        // properties on a live one - values that are real, plausible, and wrong, which is the
        // failure mode this project keeps meeting.
        EntityTracker tracker = new();
        tracker.Apply(
        [
            EntityWithSerial(3, 1, Value("DT_A", "health", 0, 125), Value("DT_A", "ammo", 1, 32)),
        ]);

        tracker.Apply([EntityWithSerial(3, 2, Value("DT_A", "health", 0, 50))]);

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();
        state["DT_A.health"].AsInt.ShouldBe(50);
        state.ContainsKey("DT_A.ammo").ShouldBeFalse();
    }

    [Fact]
    public void SameSlotAndSerialEnteringAgain_KeepsWhatItHad()
    {
        // The control for the test above: without it, "cleared on a serial change" and
        // "cleared on every Enter" produce the same evidence.
        EntityTracker tracker = new();
        tracker.Apply(
        [
            EntityWithSerial(3, 1, Value("DT_A", "health", 0, 125), Value("DT_A", "ammo", 1, 32)),
        ]);

        tracker.Apply([EntityWithSerial(3, 1, Value("DT_A", "health", 0, 50))]);

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();
        state["DT_A.health"].AsInt.ShouldBe(50);
        state["DT_A.ammo"].AsInt.ShouldBe(32);
    }


    [Fact]
    public void EnteringEntity_StartsFromItsClassBaseline()
    {
        // A snapshot sends an entering entity as a delta against its class's *baseline*, not
        // against nothing, so properties still at their default never appear on the wire.
        // Without baselines the tracker simply does not know them - the gap documented on
        // EntityTracker since it was written.
        //
        // The measurement is a property the enter update never mentioned. Asserting only on the
        // one it did mention could not tell "baseline applied" from "baseline ignored".
        EntityTracker tracker = new();
        Dictionary<int, IReadOnlyList<DecodedProperty>> baselines = new()
        {
            [7] =
            [
                Value("DT_A", "health", 0, 100),
                Value("DT_A", "ammo", 1, 32),
                Value("DT_A", "team", 2, 2),
            ],
        };

        tracker.Apply(
            [Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 55))],
            classId => baselines.GetValueOrDefault(classId));

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();

        // The update wins where they overlap - it is the newer value.
        state["DT_A.health"].AsInt.ShouldBe(55);

        // And the baseline supplies what the update left out. This is the whole point.
        state["DT_A.ammo"].AsInt.ShouldBe(32);
        state["DT_A.team"].AsInt.ShouldBe(2);
    }

    [Fact]
    public void DeltaUpdate_DoesNotReapplyTheBaseline()
    {
        // A baseline seeds an entity when it enters, not on every update. Re-applying it on a
        // delta would silently resurrect defaults over values the match had already changed -
        // a player's health springing back to 100 because a later tick touched only their
        // position.
        EntityTracker tracker = new();
        IReadOnlyList<DecodedProperty> baseline = [Value("DT_A", "health", 0, 100)];

        tracker.Apply(
            [Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 55))],
            _ => baseline);
        tracker.Apply(
            [Entity(3, EntityUpdateType.Delta, Value("DT_A", "ammo", 1, 7))],
            _ => baseline);

        IReadOnlyDictionary<string, PropertyValue> state = tracker.State(3).ShouldNotBeNull();
        state["DT_A.health"].AsInt.ShouldBe(55);
        state["DT_A.ammo"].AsInt.ShouldBe(7);
    }

    [Fact]
    public void WithoutABaselineSource_BehaviourIsUnchanged()
    {
        // The control. Baselines are optional, and a caller that has none must get exactly the
        // old behaviour rather than an empty state or a throw.
        EntityTracker tracker = new();

        tracker.Apply([Entity(3, EntityUpdateType.Enter, Value("DT_A", "health", 0, 55))]);

        tracker.State(3).ShouldNotBeNull()["DT_A.health"].AsInt.ShouldBe(55);
    }

}
