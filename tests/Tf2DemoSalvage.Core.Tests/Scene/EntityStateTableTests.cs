using System.Collections.Generic;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Tests the accumulation of entity state across delta snapshots.
/// </summary>
/// <remarks>
/// **A snapshot carries only what changed**, so the current state of the world is nowhere in the
/// file — it exists only as the sum of every update so far. That makes this the first component
/// where a bug is invisible in the trace: the trace prints each delta correctly and the
/// accumulated view is still wrong.
/// </remarks>
public sealed class EntityStateTableTests
{
    private const string PlayerClass = "CTFPlayer";

    [Test]
    public void ADeltaKeepsPropertiesEarlierSnapshotsSet()
    {
        // The whole point of the accumulator. A player who stops moving stops sending an origin,
        // and a table that forgot it would place them at the world origin - a real position, in
        // the middle of the map, indistinguishable from a decoding success.
        EntityStateTable table = new();

        table.Apply(Entity(1, EntityUpdateType.Enter,
            Property("DT_BaseEntity", "m_iTeamNum", PropertyValue.FromInt(3)),
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))));

        table.Apply(Entity(1, EntityUpdateType.Delta,
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(70))));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();

        // The changed one moved.
        state.Integer("DT_BasePlayer.m_iHealth").ShouldBe(70);

        // The untouched one survived, which the delta alone does not say.
        state.Integer("DT_BaseEntity.m_iTeamNum").ShouldBe(3);
    }

    [Test]
    public void LeavingTheVisibleSetIsNotBeingDestroyed()
    {
        // `Leave` and `Delete` are different messages and mean different things: an entity that
        // leaves the potentially-visible set still exists and will come back with a DELTA, not a
        // fresh ENTER. Discarding its properties there loses everything that delta does not
        // resend - which for a player who walked behind a wall is most of them.
        //
        // A viewer still must not draw it, so visibility is tracked rather than the state being
        // thrown away. Those are two different questions and this is the test that keeps them
        // separate.
        EntityStateTable table = new();
        table.Apply(Entity(4, EntityUpdateType.Enter,
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))));

        table.TryGet(4, out EntityState? entered).ShouldBeTrue();
        entered.IsVisible.ShouldBeTrue();

        table.Apply(Entity(4, EntityUpdateType.Leave));

        table.TryGet(4, out EntityState? left).ShouldBeTrue();
        left.IsVisible.ShouldBeFalse();
        left.Integer("DT_BasePlayer.m_iHealth").ShouldBe(125);

        // And it comes back without re-sending anything.
        table.Apply(Entity(4, EntityUpdateType.Enter));
        table.TryGet(4, out EntityState? returned).ShouldBeTrue();
        returned.IsVisible.ShouldBeTrue();
        returned.Integer("DT_BasePlayer.m_iHealth").ShouldBe(125);
    }

    [Test]
    public void ADeletedEntityIsGone()
    {
        EntityStateTable table = new();
        table.Apply(Entity(5, EntityUpdateType.Enter,
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))));

        table.Apply(Entity(5, EntityUpdateType.Delete));

        table.TryGet(5, out _).ShouldBeFalse();
    }

    [Test]
    public void AReusedSlotDoesNotInheritTheLastOccupantsProperties()
    {
        // Entity indices are recycled when a player disconnects, and the serial number is what
        // distinguishes the new occupant. Without this the new entity inherits the old one's
        // health, team and position and then partially overwrites them - which produces a player
        // who is on the wrong team until they happen to send a team update.
        EntityStateTable table = new();

        table.Apply(new DecodedEntity(
            7, ClassId: 212, SerialNumber: 100, EntityUpdateType.Enter,
            [Property("DT_BaseEntity", "m_iTeamNum", PropertyValue.FromInt(2)),
             Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))]));

        table.Apply(new DecodedEntity(
            7, ClassId: 212, SerialNumber: 101, EntityUpdateType.Enter,
            [Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(150))]));

        table.TryGet(7, out EntityState? state).ShouldBeTrue();
        state.Integer("DT_BasePlayer.m_iHealth").ShouldBe(150);

        // The control, and the actual assertion: the previous occupant's team must not survive.
        state.Integer("DT_BaseEntity.m_iTeamNum").ShouldBeNull();
    }

    [Test]
    public void OriginResolvesFromWhicheverExclusiveTableCarriedIt()
    {
        // The trap this class exists for. TF2 sends a player's position through
        // DT_TFLocalPlayerExclusive for the recording client and
        // DT_TFNonLocalPlayerExclusive for everyone else, and a reader that knows only one of
        // them silently loses either the recorder or the whole rest of the server.
        //
        // Both cases are asserted here rather than one, because "reads the local table" and
        // "reads both tables" agree on any test that only ever supplies the local one.
        EntityStateTable local = new();
        local.Apply(Entity(1, EntityUpdateType.Enter,
            Property("DT_TFLocalPlayerExclusive", "m_vecOrigin",
                PropertyValue.FromVectorXY(-480f, -4512f)),
            Property("DT_TFLocalPlayerExclusive", "m_vecOrigin[2]",
                PropertyValue.FromFloat(192.031f))));

        local.TryGet(1, out EntityState? recorder).ShouldBeTrue();
        recorder.Origin().ShouldBe((-480f, -4512f, 192.031f));

        EntityStateTable other = new();
        other.Apply(Entity(2, EntityUpdateType.Enter,
            Property("DT_TFNonLocalPlayerExclusive", "m_vecOrigin",
                PropertyValue.FromVectorXY(128f, 256f)),
            Property("DT_TFNonLocalPlayerExclusive", "m_vecOrigin[2]",
                PropertyValue.FromFloat(64f))));

        other.TryGet(2, out EntityState? teammate).ShouldBeTrue();
        teammate.Origin().ShouldBe((128f, 256f, 64f));
    }

    [Test]
    public void TheLaunchEraSendsOriginAsOneVectorRatherThanSplitInTwo()
    {
        // An era change, found by this accumulator producing zero positioned players on the 2007
        // demo while every later demo worked. At protocol 11 m_vecOrigin is a full three-component
        // vector; by 2013 Valve had split it into a two-component XY plus a separate scalar
        // m_vecOrigin[2], so that Z can delta independently of the horizontal position.
        //
        // A reader that knows only the modern shape finds no origin at all on a launch-era demo -
        // not a wrong position, no position - which is why this was invisible until players were
        // counted rather than spot-checked.
        EntityStateTable table = new();
        table.Apply(Entity(1, EntityUpdateType.Enter,
            Property("DT_TFLocalPlayerExclusive", "m_vecOrigin",
                PropertyValue.FromVector(-1343.862f, -6527.691f, -287.969f))));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();
        state.Origin().ShouldBe((-1343.862f, -6527.691f, -287.969f));
    }

    [Test]
    public void ANonPlayerEntityPositionsFromTheBaseEntityTable()
    {
        // Players are the special case, not the rule. Everything else - projectiles, buildings,
        // ammo packs - sends its position through DT_BaseEntity, and a viewer that drew only
        // players would be ignoring most of what moves.
        EntityStateTable table = new();
        table.Apply(Entity(40, EntityUpdateType.Enter,
            Property("DT_BaseEntity", "m_vecOrigin",
                PropertyValue.FromVector(-992f, -5537.5f, -358.438f))));

        table.TryGet(40, out EntityState? state).ShouldBeTrue();
        state.Origin().ShouldBe((-992f, -5537.5f, -358.438f));
    }

    [Test]
    public void AnEntityWithNoOriginHasNoPositionRatherThanTheWorldOrigin()
    {
        // Absence has to be representable. (0,0,0) is a real place on every map, so returning it
        // for "not known" puts unpositioned entities in the middle of the world and calls it
        // data.
        EntityStateTable table = new();
        table.Apply(Entity(9, EntityUpdateType.Enter,
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))));

        table.TryGet(9, out EntityState? state).ShouldBeTrue();
        state.Origin().ShouldBeNull();
    }

    [Test]
    public void TheClassNameIsCarriedSoCallersNeedNotHoldTheSchema()
    {
        EntityStateTable table = new();
        table.SetClassName(212, PlayerClass);
        table.Apply(Entity(1, EntityUpdateType.Enter,
            Property("DT_BasePlayer", "m_iHealth", PropertyValue.FromInt(125))));

        table.TryGet(1, out EntityState? state).ShouldBeTrue();
        state.ClassName.ShouldBe(PlayerClass);

        // An unmapped class id is reported as unknown rather than guessed at or thrown on: a
        // demo whose datatables were not read still has usable entity indices.
        table.Apply(Entity(2, EntityUpdateType.Enter, classId: 999));
        table.TryGet(2, out EntityState? unknown).ShouldBeTrue();
        unknown.ClassName.ShouldBeNull();
    }

    private static DecodedEntity Entity(
        int index, EntityUpdateType update, params DecodedProperty[] properties) =>
        new(index, ClassId: 212, SerialNumber: 1, update, properties);

    private static DecodedEntity Entity(int index, EntityUpdateType update, int classId) =>
        new(index, classId, SerialNumber: 1, update, []);

    private static DecodedProperty Property(string table, string name, PropertyValue value) =>
        new(0, new FlatProperty(
                new SendProperty(SendPropType.Int, name, 0, string.Empty, 0f, 0f, 32, 0),
                table,
                null),
            value);
}
