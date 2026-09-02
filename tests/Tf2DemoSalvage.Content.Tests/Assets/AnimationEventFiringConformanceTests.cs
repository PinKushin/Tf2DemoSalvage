using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>C_BaseAnimating::DoAnimationEvents</c> — which events a cycle crossed since the last frame.
/// </summary>
/// <remarks>
/// **<c>c_baseanimating.cpp:3550</c>, and every rule here is one of its branches.** The events
/// themselves were already read off the sequence; nothing fired them, so a taunt played silently
/// and no foot ever landed (B275).
///
/// The engine keeps the state on the entity — <c>m_flPrevEventCycle</c> and
/// <c>m_nEventSequence</c> — and this returns it instead, so the traversal is a function of its
/// inputs and can be asked a question without an entity to hang it on.
///
/// **The four rules that are easy to get wrong and each have a case below:**
///
/// - A sequence change RESTARTS the walk from cycle 0 with the previous cycle at −0.01, which is
///   how an event authored at cycle 0 fires at all: <c>"back up to get 0'th frame animations"</c>.
/// - An unchanged cycle is a STALL and fires nothing, however many events sit before it.
/// - A cycle that went backwards is a LOOP only when it fell by more than half; anything smaller is
///   the animation being nudged back, and the engine refuses to replay that slice.
/// - On a loop the TAIL fires first — everything after the old cycle — and only then the head, so
///   an event at 0.9 is heard before one at 0.1 rather than a frame later.
/// </remarks>
public sealed class AnimationEventFiringConformanceTests
{
    /// <summary>A client event, since a server one is filtered out before any of this.</summary>
    private static StudioEvent At(float cycle, string options = "") =>
        new(cycle, StudioEvent.OldSystemClientId + 4, 0, options);

    [Test]
    public void Fired_ACycleCrossingAnEvent_FiresIt()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.5f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.4f),
            cycle: 0.6f,
            resetEvents: false,
            into: fired);

        fired.Count.ShouldBe(1);
        fired[0].Cycle.ShouldBe(0.5f);
    }

    [Test]
    public void Fired_ACycleShortOfAnEvent_FiresNothing()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.5f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.4f),
            cycle: 0.45f,
            resetEvents: false,
            into: fired);

        fired.ShouldBeEmpty();
    }

    /// <remarks>
    /// **The boundary is <c>&gt; prev</c> and <c>&lt;= now</c>**, so an event exactly on the new
    /// cycle fires and one exactly on the old does not. Asserted because the alternative pairing is
    /// just as plausible to write and fires everything twice at a frame boundary.
    /// </remarks>
    [Test]
    public void Fired_AnEventExactlyOnEachBound_FiresOnlyTheLater()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.4f, "old"), At(0.6f, "new")],
            sequence: 3,
            state: new AnimationEventState(3, 0.4f),
            cycle: 0.6f,
            resetEvents: false,
            into: fired);

        fired.Select(fire => fire.Options).ShouldBe(["new"]);
    }

    /// <remarks>
    /// <c>m_flPrevEventCycle = -0.01</c> on a sequence change, with Valve's own comment: *"back up
    /// to get 0'th frame animations"*. Without it every event authored at cycle 0 — which is where
    /// a taunt's first sound sits — is skipped for ever.
    /// </remarks>
    [Test]
    public void Fired_AfterASequenceChange_FiresAnEventAtCycleZero()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0f)],
            sequence: 7,
            state: new AnimationEventState(3, 0.9f),
            cycle: 0.2f,
            resetEvents: false,
            into: fired);

        fired.Count.ShouldBe(1, "a sequence change restarts the walk at cycle zero minus a hair");
    }

    /// <remarks>
    /// **The control for the case above**: without a sequence change the same cycle 0.9 to 0.2 is a
    /// backwards step of 0.7, which IS more than half and so reads as a loop — a different branch
    /// reaching the same event, and one that would let the test above pass for the wrong reason.
    /// Here the drop is small, so it is neither a loop nor a change, and the engine refuses it.
    /// </remarks>
    [Test]
    public void Fired_ACycleNudgedBackwards_FiresNothing()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.1f), At(0.9f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.5f),
            cycle: 0.3f,
            resetEvents: false,
            into: fired);

        fired.ShouldBeEmpty("a drop under half a cycle is a hitch, not a loop");
    }

    /// <remarks>
    /// **A loop fires the TAIL first**, which is the whole of Valve's comment: *"This makes sure
    /// events that occur at the end of a sequence occur are sent before events that occur at the
    /// beginning"*. An implementation that ran one pass in cycle order would put the footstep at
    /// 0.1 ahead of the one at 0.9 that actually happened first.
    ///
    /// **And the head event at 0.1 does NOT fire here, which is surprising and is the engine's.**
    /// The loop pass ends with <c>m_flPrevEventCycle = flEventCycle - 0.001f</c>, set BEFORE the
    /// ordinary pass runs — so that pass only ever fires events in the last thousandth of a cycle.
    /// Anything between zero and there is skipped for that lap.
    ///
    /// **This test originally asserted `["tail", "head"]` and was wrong**, which is worth keeping:
    /// the expectation is the natural one and the engine does not meet it. In a live client the
    /// gap barely matters, because a frame advances the cycle by a hair and the loop lands within
    /// that thousandth — it only becomes visible when a frame covers a large slice, which for a
    /// demo viewer is exactly what a seek or a slow frame does.
    /// </remarks>
    [Test]
    public void Fired_AcrossALoop_FiresTheTailAndSkipsAHeadBelowTheBacktrack()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.1f, "head"), At(0.9f, "tail")],
            sequence: 3,
            state: new AnimationEventState(3, 0.8f),
            cycle: 0.2f,
            resetEvents: false,
            into: fired);

        fired.Select(fire => fire.Options).ShouldBe(["tail"]);
    }

    /// <remarks>
    /// **The other side of the branch above**: a head event that IS inside the thousandth the loop
    /// pass leaves behind does fire, and after the tail. Without this case the assertion above is
    /// equally satisfied by an implementation that never runs the second pass at all.
    /// </remarks>
    [Test]
    public void Fired_AcrossALoop_FiresAHeadEventInsideTheBacktrack()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.2f, "head"), At(0.9f, "tail")],
            sequence: 3,
            state: new AnimationEventState(3, 0.8f),
            cycle: 0.2f,
            resetEvents: false,
            into: fired);

        fired.Select(fire => fire.Options).ShouldBe(["tail", "head"]);
    }

    [Test]
    public void Fired_AStalledCycle_FiresNothing()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0.5f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.5f),
            cycle: 0.5f,
            resetEvents: false,
            into: fired);

        fired.ShouldBeEmpty("an unchanged cycle has crossed nothing");
    }

    /// <remarks>
    /// <c>m_nResetEventsParity</c> changing restarts the walk exactly as a sequence change does —
    /// the engine tests <c>m_nEventSequence != GetSequence() || resetEvents</c>. It is how a
    /// repeated taunt plays its sounds again without the sequence number ever changing.
    /// </remarks>
    [Test]
    public void Fired_WhenTheResetParityChanged_RestartsTheWalk()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [At(0f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.9f),
            cycle: 0.2f,
            resetEvents: true,
            into: fired);

        fired.Count.ShouldBe(1, "a reset replays the sequence from its start");
    }

    /// <remarks>
    /// The state comes back so the caller can keep it, and after an ordinary step it is simply the
    /// new cycle. Pinned because a caller that stored the wrong one re-fires everything every frame
    /// — the loudest possible failure, and the easiest to write.
    /// </remarks>
    [Test]
    public void Fired_AfterAnOrdinaryStep_ReturnsTheNewCycle()
    {
        List<StudioEvent> fired = [];

        AnimationEventState next = AnimationEventFiring.Fired(
            [At(0.5f)],
            sequence: 3,
            state: new AnimationEventState(3, 0.4f),
            cycle: 0.6f,
            resetEvents: false,
            into: fired);

        next.Sequence.ShouldBe(3);
        next.PreviousCycle.ShouldBe(0.6f);
    }

    /// <remarks>
    /// **A server event is not the client's to fire**, whatever its cycle.
    /// <c>DoAnimationEvents</c> filters on the type before looking at the cycle at all, and all
    /// eight events on `sentry3.mdl` are of this kind — they reach the client as ordinary
    /// server-sent sounds instead.
    /// </remarks>
    [Test]
    public void Fired_AServerEvent_IsNeverFired()
    {
        List<StudioEvent> fired = [];

        AnimationEventFiring.Fired(
            [new StudioEvent(0.5f, 0, StudioEvent.NewSystemType, "Rockets 1")],
            sequence: 3,
            state: new AnimationEventState(3, 0.4f),
            cycle: 0.6f,
            resetEvents: false,
            into: fired);

        fired.ShouldBeEmpty();
    }
}
