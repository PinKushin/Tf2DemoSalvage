using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A weapon's <c>m_iState</c> at the tick being drawn, rather than at the end of the demo.
/// </summary>
/// <remarks>
/// **Every player in a real match was holding the wrong thing, and the suite was green** (B244).
/// `ScenePropTrack` samples its POSE per tick — position, angles, sequence, body, skin, hidden —
/// and reads nine other fields off the track as scalars. Those scalars are written while the demo
/// is parsed, so by the time anything asks for a tick they hold whatever the LAST update in the
/// whole recording wrote.
///
/// For most of them that is harmless, because they cannot change while an entity lives: a weapon
/// belongs to one player, is one item, is one class. `WeaponState` is the exception, and it is the
/// one that decides whether a weapon is DRAWN at all — `C_BaseCombatWeapon::ShouldDraw`
/// (`c_basecombatweapon.cpp:399`) reduces, for another player's weapon, to
/// <c>return ( m_iState == WEAPON_IS_ACTIVE )</c>.
///
/// So a medic whose medigun was holstered at the end of the recording had it holstered for the
/// whole recording, and drew empty-handed at every tick. Measured on `tf2-2026-pub-pov-clean`:
/// entity 1137's state is 2 from tick 13417 onward, and the timeline reported 1 at tick 14000 —
/// while the player's own `m_hActiveWeapon` named that very entity. Two to six players contradicted
/// themselves that way at every tick sampled.
///
/// **The instruments could not see it and that is the interesting part.** Every unit test of
/// `WeaponVisibility` passes a `SceneProp` the test itself built, so the rule was right and its
/// input was wrong; the viewer's log reports per MODEL, deduplicated, so one medigun drawing and
/// two not looks exactly like three drawing. It took a per-player, per-entity report at one tick —
/// `docs/memory/output-level-assertion-or-it-is-not-done.md`.
/// </remarks>
public sealed class WeaponStateOverTimeTests
{
    /// <summary>How far behind the asked-for tick a pose is sampled, matching <c>cl_interp</c>.</summary>
    /// <remarks>
    /// The same constant `SceneTrackCycleTests` uses. A state change is discrete, so it takes effect
    /// at the sampled moment rather than blending — but the sampled moment is still delayed, and a
    /// test that ignored that would be asserting against a client that draws the present.
    /// </remarks>
    private const int Delay = 7;

    [Test]
    public void PropsAt_AWeaponActiveThenHolstered_ReportsActiveAtTheEarlierTick()
    {
        // **The regression, stated by value.** Active at 100, holstered at 200: at tick 150 the
        // demo has said nothing but "active", so that is what a client holds. Reading the track's
        // scalar answers 1 — the state at the END — which is what drew every medic empty-handed.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticWeapon.DemoOfStates(
                ownerEntity: 3,
                (100, EntityState.WeaponActive),
                (200, WeaponCarried)));

        WeaponAt(timeline, 150 + Delay).ShouldBe(
            EntityState.WeaponActive,
            "at tick 150 the demo has only ever said ACTIVE; the holster is fifty ticks away");
    }

    [Test]
    public void PropsAt_AWeaponActiveThenHolstered_ReportsHolsteredAtTheLaterTick()
    {
        // **The control, and it is not decoration.** The failing assertion above is satisfied by a
        // track that answers ACTIVE always — including one that simply returned the FIRST state
        // instead of the last, which is the same bug mirrored. This is the case that separates
        // "samples the tick" from "froze at the beginning".
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticWeapon.DemoOfStates(
                ownerEntity: 3,
                (100, EntityState.WeaponActive),
                (200, WeaponCarried)));

        WeaponAt(timeline, 250 + Delay).ShouldBe(
            WeaponCarried, "the holster was stated at 200 and nothing has contradicted it");
    }

    [Test]
    public void PropsAt_AWeaponHolsteredThenDrawn_ReportsEachStateAtItsOwnTick()
    {
        // **The other direction, because a weapon is drawn as well as holstered.** A fix that
        // carried only the first keyframe's state forward would pass both tests above and fail
        // this one — the switch TO active is what puts a weapon back in a player's hands.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticWeapon.DemoOfStates(
                ownerEntity: 3,
                (100, WeaponCarried),
                (200, EntityState.WeaponActive)));

        WeaponAt(timeline, 150 + Delay).ShouldBe(WeaponCarried);
        WeaponAt(timeline, 250 + Delay).ShouldBe(EntityState.WeaponActive);
    }

    [Test]
    public void PropsAt_AWeaponReenteringThePvsWithoutRestatingItsState_IsNoLongerActive()
    {
        // **`CL_CopyNewEntity` decodes an entering entity FROM ITS BASELINE, not from whatever the
        // client was holding** (B245). The engine's own assert strings say so — `engine.dll`
        // retains `CL_CopyNewEntity: GetClassBaseline(%d) failed.` beside
        // `CL_CopyExistingEntity: missing client entity %d.` and
        // `CL_PreserveExistingEntity`, three separate paths — so an enter-PVS update is a delta
        // against a baseline and everything it omits comes from there rather than from before.
        //
        // This class has no instance baseline, which is the real case: 68 of 363 classes in
        // `tf2-2026-pub-pov-clean` carry one and `CTFBonesaw` is not among them. So the baseline is
        // all defaults, and `m_iState` returns to `WEAPON_NOT_CARRIED`.
        //
        // Left as it was, the weapon stays ACTIVE for the rest of the recording and its owner draws
        // two weapons in one hand — measured on a real bonesaw across six thousand ticks and eight
        // PVS transitions.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticWeapon.DemoOfPvsCycle(ownerEntity: 3, active: EntityState.WeaponActive));

        WeaponAt(timeline, 350 + Delay).ShouldNotBe(
            EntityState.WeaponActive,
            "the re-entry is a delta against a baseline that never says ACTIVE");
    }

    [Test]
    public void PropsAt_AWeaponStillInThePvs_KeepsTheStateItWasLastGiven()
    {
        // **The control, and it is the one that keeps the fix from becoming "forget everything".**
        // A weapon that never leaves is described by ordinary deltas, and a delta omitting
        // `m_iState` means "unchanged" — `CL_CopyExistingEntity`, the other path in that same
        // switch. A reader that reset on every update would lose the state between switches and
        // draw nobody holding anything.
        DemoTimeline timeline = DemoTimeline.Build(
            SyntheticWeapon.DemoOfStates(
                ownerEntity: 3,
                (100, EntityState.WeaponActive),
                (200, EntityState.WeaponActive)));

        WeaponAt(timeline, 250 + Delay).ShouldBe(
            EntityState.WeaponActive, "no leave, no reset — a delta that omits a value means it held");
    }

    /// <summary><c>WEAPON_IS_CARRIED_BY_PLAYER</c>, <c>shareddefs.h:297</c>.</summary>
    private const int WeaponCarried = 1;

    /// <summary>The one weapon prop's state at a tick, as the scene would receive it.</summary>
    /// <remarks>
    /// Asked through `PropsAt` rather than off the track, because that is the call the viewer makes
    /// and the field is read from the prop it hands back. A test that reached into the track could
    /// pass while `PropsAt` went on copying the scalar.
    /// </remarks>
    private static int? WeaponAt(DemoTimeline timeline, double tick)
    {
        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        return props
            .Where(prop => prop.EntityIndex == SyntheticWeapon.WeaponEntityIndex)
            .Select(prop => prop.WeaponState)
            .FirstOrDefault();
    }
}
