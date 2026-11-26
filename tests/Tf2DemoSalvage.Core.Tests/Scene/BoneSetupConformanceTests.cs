using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A server-animated prop's cycle history resets on every update whose parity differs from the parity at its last bone
/// setup (B878), which is a view-time event: what the view posed decides it.
/// </summary>
/// <remarks>
/// **`m_nPrevNewSequenceParity` is written only at a bone setup** (`c_baseanimating.cpp:716`, `:1824`, `:1835`), and
/// `PostDataUpdate` resets `m_iv_flCycle` whenever the update's parity differs from it (`:4738-4747`) — three entries at
/// the arrival holding the value just latched (`interpolatedvar.h:740-751`). Bones are set up only on demand: drawing,
/// an attachment, a hitbox, a merge (`docs/RISKS.md` B878). Fixture: updates at 10, 20, 30, 40 with cycles 0.2, 0.4, 0.6,
/// 0.8, animation times 3 behind, parity 0, 0, 1, 1 — so the update at 30 starts a sequence and resets for every view.
/// </remarks>
public sealed class BoneSetupConformanceTests
{
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    private static readonly HashSet<int> Prop = [SyntheticAnimatingProp.EntityIndex];

    private static DemoTimeline Timeline() => DemoTimeline.Build(SyntheticAnimatingProp.Demo(
        [string.Empty, "models/props_gameplay/door.mdl"],
        animationLag: 3,
        (10, 1, [], 0.2f, 0),
        (20, 1, [], 0.4f, 0),
        (30, 1, [], 0.6f, 1),
        (40, 1, [], 0.8f, 1)));

    /// <summary>Plays 10 to 40 a tick at a time, posing the prop where <paramref name="posed"/> says; the cycle at 40.</summary>
    private static float CycleAt40(DemoTimeline timeline, BoneSetups? setups, System.Func<int, bool> posed)
    {
        List<SceneProp> props = [];

        for (int tick = 10; tick < 40; tick++)
        {
            timeline.PropsAt(tick, props, setups: setups);

            if (setups is not null && posed(tick))
            {
                timeline.NoteBonesSetUp(setups, Prop, tick);
            }
        }

        timeline.PropsAt(40, props, setups: setups);

        return props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle;
    }

    /// <summary>
    /// **Unposed across the update at 40, the engine's history starts there**: the last setup (25) stored parity 0, the
    /// update at 40 states 1, so it resets — three entries at 40 holding 0.8. The target, 40 less the delay, precedes every
    /// entry, so nothing brackets and the networked 0.8 holds. Posed every frame, 40's parity equals the setup's (1, stored
    /// at 39), no reset, and the pair runs from the reset at 30 (0.6) to 0.8 stamped 37.
    /// </summary>
    [Test]
    public void PropsAt_UnposedAcrossAParityItsLastSetupDidNotSee_HoldsTheLatestCycle()
    {
        float unposed = CycleAt40(Timeline(), new BoneSetups(), tick => tick <= 25);
        float posed = CycleAt40(Timeline(), new BoneSetups(), _ => true);

        unposed.ShouldBe(0.8f);
        posed.ShouldBe(0.6f + (0.2f * (40 - Delay - 30) / 7f), 1e-5f, "the control: posed every frame, the reset at 30 is the floor");
    }

    /// <summary>
    /// **Posed every frame, a view sees what no view sees**: every update is followed by a setup, so only a parity change
    /// resets — the history the timeline already builds. The two answers agree bit for bit.
    /// </summary>
    [Test]
    public void PropsAt_PosedEveryFrame_IsTheAnswerWithNoView()
    {
        CycleAt40(Timeline(), new BoneSetups(), _ => true).ShouldBe(CycleAt40(Timeline(), null, _ => true));
    }

    /// <summary>
    /// **Never posed, every update resets** — the constructor's −1 differs from any parity (`c_baseanimating.cpp:716`) — so
    /// at 40 it holds 0.8, as unposed from 25 does.
    /// </summary>
    [Test]
    public void PropsAt_NeverPosed_HoldsTheLatestCycle()
    {
        CycleAt40(Timeline(), new BoneSetups(), _ => false).ShouldBe(0.8f);
    }

    /// <summary>
    /// **A seek back forgets the setups** (*our choice*, B878): posed every frame to 40, back to 20, then forward to 40
    /// with nothing posed holds 0.8, where the same frames without the seek would carry the setups made before it.
    /// </summary>
    [Test]
    public void PropsAt_AfterASeekBack_ForgetsEverySetup()
    {
        DemoTimeline timeline = Timeline();
        BoneSetups setups = new();
        List<SceneProp> props = [];

        CycleAt40(timeline, setups, _ => true);
        timeline.PropsAt(20, props, setups: setups);
        timeline.PropsAt(40, props, setups: setups);

        props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle.ShouldBe(0.8f);
    }
}
