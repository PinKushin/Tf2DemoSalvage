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

    private static readonly (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] Four =
    [
        (10, 1, [], 0.2f, 0),
        (20, 1, [], 0.4f, 0),
        (30, 1, [], 0.6f, 1),
        (40, 1, [], 0.8f, 1),
    ];

    private static DemoTimeline Timeline(params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames) =>
        DemoTimeline.Build(SyntheticAnimatingProp.Demo(
            [string.Empty, "models/props_gameplay/door.mdl"], animationLag: 3, frames));

    /// <summary>Plays from 10 a tick at a time to <paramref name="last"/>, posing where <paramref name="posed"/> says; every cycle.</summary>
    private static List<float> Cycles(
        DemoTimeline timeline, BoneSetups? setups, System.Func<int, bool> posed, int last = 40)
    {
        List<SceneProp> props = [];
        List<float> cycles = [];

        for (int tick = 10; tick <= last; tick++)
        {
            timeline.PropsAt(tick, props, setups: setups);
            cycles.Add(props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle);

            if (setups is not null && posed(tick))
            {
                timeline.NoteBonesSetUp(setups, Prop, tick);
            }
        }

        return cycles;
    }

    /// <summary>
    /// **Unposed across the update at 40, the engine's history starts there**: the last setup (25) stored parity 0, the
    /// update at 40 states 1, so it resets — three entries at 40 holding 0.8. The target, 40 less the delay, precedes them,
    /// so the walk holds the oldest entry it passed: 0.8. Posed every frame, 40's parity equals the setup's (1, stored at 39),
    /// no reset, and the pair runs from the reset at 30 (0.6) to 0.8 stamped 37.
    /// </summary>
    [Test]
    public void PropsAt_UnposedAcrossAParityItsLastSetupDidNotSee_HoldsTheReset()
    {
        float unposed = Cycles(Timeline(Four), new BoneSetups(), tick => tick <= 25)[^1];
        float posed = Cycles(Timeline(Four), new BoneSetups(), _ => true)[^1];

        unposed.ShouldBe(0.8f);
        posed.ShouldBe(0.6f + (0.2f * (40 - Delay - 30) / 7f), 1e-5f, "the control: posed every frame, the reset at 30 is the floor");
    }

    /// <summary>
    /// **Posed every frame, a view sees what no view sees, at every tick** — including the window just after a sequence
    /// starts, where a further update at 32 arrives while the target is still before 30: every update is followed by a
    /// setup, so only the parity change resets, which the timeline already stores.
    /// </summary>
    [Test]
    public void PropsAt_PosedEveryFrame_IsTheAnswerWithNoViewAtEveryTick()
    {
        (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames =
            [(10, 1, [], 0.2f, 0), (20, 1, [], 0.4f, 0), (30, 1, [], 0.6f, 1), (32, 1, [], 0.65f, 1), (40, 1, [], 0.8f, 1)];

        Cycles(Timeline(frames), new BoneSetups(), _ => true).ShouldBe(Cycles(Timeline(frames), null, _ => true));
    }

    /// <summary>
    /// **Never posed, every update resets** — the constructor's −1 differs from any parity (`c_baseanimating.cpp:716`) — so
    /// at 40 it holds 0.8, as unposed from 25 does.
    /// </summary>
    [Test]
    public void PropsAt_NeverPosed_HoldsTheLatestReset()
    {
        Cycles(Timeline(Four), new BoneSetups(), _ => false)[^1].ShouldBe(0.8f);
    }

    /// <summary>
    /// **The reset a view did not see is kept after it is posed again**: unposed from 26 to 44 across the update at 40,
    /// posed from 45, then 1.0 arrives at 50 stamped 47. The engine's list is the reset at 40 (0.8) and 47, so at 52 the
    /// pair runs linearly from 40. Posed every frame, the pair is 37 to 47 with the reset at 30 before it — a spline, and
    /// a different value.
    /// </summary>
    [Test]
    public void PropsAt_PosedAgainAfterAnUnseenReset_KeepsItsHistoryFromThere()
    {
        (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames =
            [.. Four, (50, 1, [], 1.0f, 1)];

        float kept = Cycles(Timeline(frames), new BoneSetups(), tick => tick <= 25 || tick >= 45, last: 52)[^1];
        float posed = Cycles(Timeline(frames), new BoneSetups(), _ => true, last: 52)[^1];

        kept.ShouldBe(0.8f + (0.2f * (52 - Delay - 40) / 7f), 1e-5f);
        System.Math.Abs(kept - posed).ShouldBeGreaterThan(1e-3f, "the control: posed every frame blends from 37");
    }

    /// <summary>
    /// **A seek back keeps the setups** (*our choice*, B878): posed every frame to 40, back to 20, then to 40 with nothing
    /// posed draws what posing every frame drew — the setups recorded after 20 name no update after them until 40 is passed
    /// again, and the one at 39 saw the parity 40 states.
    /// </summary>
    [Test]
    public void PropsAt_AfterASeekBack_KeepsTheSetupsAsIfPlayedThrough()
    {
        DemoTimeline timeline = Timeline(Four);
        BoneSetups setups = new();
        List<SceneProp> props = [];

        float posed = Cycles(timeline, setups, _ => true)[^1];
        timeline.PropsAt(20, props, setups: setups);
        timeline.PropsAt(40, props, setups: setups);

        props.Single(prop => prop.EntityIndex == SyntheticAnimatingProp.EntityIndex).Pose.Cycle.ShouldBe(posed);
    }

    /// <summary>
    /// **The reset is at the newest update, whether or not it latched the cycle** (B878 review 1, Fable): the parity test
    /// runs in every update's `PostDataUpdate`. A setup at 105 saw parity 0; 1 arrives at 110 with a new animation time, then
    /// an update at 120 moves only the origin. Asked at 125 the reset is at 120, the newest update — which has no cycle entry.
    /// </summary>
    [Test]
    public void UnseenResetAt_AnOriginOnlyUpdateAfterAParityChange_IsTheReset()
    {
        ScenePropTrack track = new(entityIndex: 3, "models/props_gameplay/door.mdl");
        track.Add(100, new ScenePose { NewSequenceParity = 0 }, appliedAt: 100, animationAppliedAt: 97);
        track.Add(110, new ScenePose { NewSequenceParity = 1, Cycle = 0.5f }, appliedAt: 110, animationAppliedAt: 107);
        track.Add(120, new ScenePose { NewSequenceParity = 1, Cycle = 0.5f, X = 8f }, appliedAt: 120, animationAppliedAt: 107);

        track.UnseenResetAt(125, new BoneSetup(105, 0, null)).ShouldBe(120);
        track.UnseenResetAt(125, new BoneSetup(120, 1, null)).ShouldBeNull("the control: a setup at 120 saw parity 1");
    }
}
