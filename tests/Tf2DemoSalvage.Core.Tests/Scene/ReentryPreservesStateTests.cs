using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entity that leaves the visible set and comes back is the SAME entity.
/// </summary>
/// <remarks>
/// **Found from the owner's description, which named the shape exactly**: *"the things are showing
/// up at tick 0, but immedietly dissapearing when you hit play"*. A value that is present when an
/// entity enters and gone shortly after is not a value that never arrived — and two earlier
/// readings had concluded the second.
///
/// Measured on `cp_fulgur`, watching one spawn-door prop across the whole recording:
///
/// <code>
///   tick  9781: Enter, serial 91, 11 properties, moveparent 1587610
///   tick  9860: Leave
///   tick 14180: Enter, serial 91,  0 properties, moveparent not in this update
///   tick 14635: Leave
///   tick 15059: Enter, serial 91,  0 properties
/// </code>
///
/// **Same serial, zero properties.** `EntityStateTable.Apply` gets that right — a matching serial
/// keeps the existing state rather than building a new one — and then hands the update to
/// <c>EffectiveProperties</c>, which merges the class baseline for EVERY <c>Enter</c>. So the
/// accumulated state is overwritten by defaults the moment an entity re-enters the potentially
/// visible set, and a prop that has been parented since tick 9781 becomes unparented at 14180.
///
/// **The baseline belongs to CREATION, not to visibility.** `EffectiveProperties`' own remarks say
/// so — *"the engine merges the baseline in CL_CopyNewEntity before the entity exists at all"* —
/// and an entity that already exists has passed that point. An `Enter` carrying ZERO properties is
/// unreadable any other way: as "rebuild from baseline" it would mean the server discarding
/// everything it knows about a live, unchanged entity; as "this is visible again, nothing has
/// changed" it is exactly what a delta-compressed protocol should send.
///
/// **This is not a door bug.** It is every entity that leaves and re-enters the PVS — which on a
/// point-of-view recording is most of the map, repeatedly. Team, skin, parent, render mode and
/// anything else sent once are all reset to defaults each time.
/// </remarks>
public sealed class ReentryPreservesStateTests
{
    /// <summary><c>INVALID_NETWORKED_EHANDLE_VALUE</c> — 21 bits of ones, the "no parent" value.</summary>
    /// <remarks>
    /// Zero is NOT it: a handle of zero masks to slot 0, a real entity. That is the trap
    /// `EntityState.Slot` documents — masking before testing turns "no owner" into "owned by
    /// entity 2047" — and it caught this fixture on its first run.
    /// </remarks>
    private const int NoParent = (1 << 21) - 1;

    /// <summary>The class every entity here belongs to.</summary>
    private const int ClassId = 0;

    [Test]
    public void Apply_AnEntityReenteringWithTheSameSerial_KeepsWhatItAlreadyKnew()
    {
        // The measured sequence, reduced: a full enter, a leave, then an enter carrying nothing.
        //
        // **The outcome this asserts has not changed; the MECHANISM it models has** (B245). The
        // parent survives because the entity is decoded against its OWN checkpoint — the per-entity
        // baseline slot the snapshot names — and not because the reader kept what it had.
        // `CL_CopyNewEntity`, read out of `engine.dll`, prefers exactly that:
        //
        //     if ( !asDelta || (stored = LookupEntityBaseline(...)) == NULL
        //                   || stored->classId != thisClass )
        //         GetClassBaseline( classId, ... );        // fatal if missing
        //     else
        //         use stored;
        //
        // The fixture used to supply only a CLASS baseline, which is the one case where the parent
        // is genuinely lost — `CDynamicProp`'s real class baseline declares
        // `moveparent = 2097151`, the invalid-handle sentinel, which is precisely how B231's gate
        // came off its door. Modelling the checkpoint is what makes this test describe the
        // protocol rather than our old workaround.
        EntityStateTable table = new(
            new CheckpointingBaselines([Property("moveparent", NoParent)]));

        table.Apply(Enter(serial: 91, Property("moveparent", 1587610)));
        table.Apply(Leave(serial: 0));
        table.Apply(Enter(serial: 91));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        state.Attachment().ShouldBe(
            410,
            "the server checkpointed this entity WITH its parent, and that is what it re-enters against");
    }

    [Test]
    public void Apply_AnEntityReenteringAgainstOnlyItsClassBaseline_TakesTheClassBaselineValue()
    {
        // **The control, and it is the case B231 measured.** With no per-entity checkpoint — a full
        // snapshot, or an entity the server has not checkpointed for this client — the class
        // baseline is what applies, and a class baseline is one representative entity's state
        // rather than a table of defaults. For `CDynamicProp` on `cp_fulgur` that means
        // `moveparent = 2097151`: no parent.
        //
        // Without this case, "re-entry uses the checkpoint" and "re-entry keeps whatever we had"
        // predict the same observation, and the test above cannot tell them apart.
        EntityStateTable table = new(Baselines(Property("moveparent", NoParent)));

        table.Apply(Enter(serial: 91, Property("moveparent", 1587610)));
        table.Apply(Leave(serial: 0));
        table.Apply(Enter(serial: 91));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        state.Attachment().ShouldBeNull(
            "the class baseline says no parent, and with no checkpoint that is what it decodes against");
    }

    [Test]
    public void Apply_AnEntityReenteringWithADifferentSerial_TakesTheBaseline()
    {
        // **The control, and the reason the fix is not simply 'never merge on re-enter'.** A
        // different serial in the same slot is a DIFFERENT entity, and merging into the old one
        // leaves the newcomer holding whichever properties it has not happened to resend — the
        // failure `EntityStateTable` already documents as a player wearing the previous occupant's
        // team.
        EntityStateTable table = new(Baselines(Property("moveparent", NoParent)));

        table.Apply(Enter(serial: 91, Property("moveparent", 1587610)));
        table.Apply(Leave(serial: 0));
        table.Apply(Enter(serial: 92));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        state.Attachment().ShouldBeNull(
            "a new occupant of the slot starts from its class baseline, not from its predecessor");
    }

    [Test]
    public void Apply_TheFirstEnter_StillTakesTheBaseline()
    {
        // **The other control, and it is the case the baseline merge exists for** (B132). An entity
        // whose whole state IS its baseline — a fog controller entering once with fifteen
        // properties, none of them on the wire — must still receive them. A fix that stopped
        // merging on creation would trade this defect for that one.
        EntityStateTable table = new(Baselines(Property("m_nRenderMode", 10)));

        table.Apply(Enter(serial: 91));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        state.RenderMode().ShouldBe(
            10, "an entity being created has nothing but its baseline to be built from");
    }

    [Test]
    public void Apply_AnUpdateThatIsNotAnEnter_NeverTakesTheBaseline()
    {
        // A delta is a delta against what the entity already is, and merging a baseline into one
        // would undo every value the entity has been sent since it entered. Asserted because the
        // fix touches the branch that decides this.
        EntityStateTable table = new(Baselines(Property("moveparent", NoParent)));

        table.Apply(Enter(serial: 91, Property("moveparent", 1587610)));
        table.Apply(Delta(serial: 91, Property("m_nRenderMode", 3)));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        state.Attachment().ShouldBe(410);
        state.RenderMode().ShouldBe(3);
    }

    private static DecodedEntity Enter(int serial, params DecodedProperty[] properties) =>
        new(1, ClassId, serial, EntityUpdateType.Enter, properties);

    private static DecodedEntity Delta(int serial, params DecodedProperty[] properties) =>
        new(1, ClassId, serial, EntityUpdateType.Delta, properties);

    private static DecodedEntity Leave(int serial) =>
        new(1, ClassId, serial, EntityUpdateType.Leave, []);

    /// <summary>A baseline source holding one class's defaults.</summary>
    private static FixedBaselines Baselines(params DecodedProperty[] properties) =>
        new FixedBaselines(properties);

    private static DecodedProperty Property(string name, int value) =>
        new(name.GetHashCode(System.StringComparison.Ordinal) & 0xFF,
            new FlatProperty(
                new SendProperty(SendPropType.Int, name, 1, string.Empty, 0f, 0f, 32, 0),
                "DT_BaseEntity",
                null),
            PropertyValue.FromInt(value));

    /// <summary>One class baseline, for a table that needs nothing else.</summary>
    /// <summary>A baseline source that checkpoints each entity, as the server does.</summary>
    /// <remarks>
    /// **Models the per-entity baseline slots rather than only the class baseline.** The real
    /// `svc_PacketEntities` names one of two per-entity arrays and periodically asks the client to
    /// rebuild the other, so an entity re-entering the visible set is described against its own
    /// last checkpoint — which is why two properties can describe a door completely.
    ///
    /// Kept deliberately simple: one checkpoint per entity, updated from every update it carries,
    /// and the class baseline when there is none. That is enough to tell "decoded against its own
    /// checkpoint" apart from "kept what the reader had", which is the distinction the tests using
    /// it exist to make.
    /// </remarks>
    private sealed class CheckpointingBaselines(IReadOnlyList<DecodedProperty> classBaseline)
        : IEntityBaselines
    {
        private readonly Dictionary<int, Dictionary<int, DecodedProperty>> _checkpoints = [];

        public IReadOnlyList<DecodedProperty> EffectiveProperties(DecodedEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (!_checkpoints.TryGetValue(
                entity.EntityIndex, out Dictionary<int, DecodedProperty>? checkpoint))
            {
                checkpoint = [];

                foreach (DecodedProperty property in classBaseline)
                {
                    checkpoint[property.Index] = property;
                }

                _checkpoints[entity.EntityIndex] = checkpoint;
            }

            foreach (DecodedProperty property in entity.Properties)
            {
                checkpoint[property.Index] = property;
            }

            return entity.UpdateType == EntityUpdateType.Enter
                ? [.. checkpoint.Values]
                : entity.Properties;
        }
    }

    private sealed class FixedBaselines(IReadOnlyList<DecodedProperty> baseline) : IEntityBaselines
    {
        public IReadOnlyList<DecodedProperty> EffectiveProperties(DecodedEntity entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            if (entity.UpdateType != EntityUpdateType.Enter)
            {
                return entity.Properties;
            }

            Dictionary<int, DecodedProperty> merged = [];

            foreach (DecodedProperty property in baseline)
            {
                merged[property.Index] = property;
            }

            foreach (DecodedProperty property in entity.Properties)
            {
                merged[property.Index] = property;
            }

            return [.. merged.Values];
        }
    }
}
