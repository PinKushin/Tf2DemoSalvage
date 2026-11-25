using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `GetClientInterpAmount()` by tick (B849): `cdll_bounded_cvars.cpp:126` computes it when asked, clamping the watcher's
/// `cl_interp_ratio` by the server's `sv_client_min_interp_ratio` and `_max_` as they stand, so a server that raises the
/// minimum mid-recording delays every entity further from that tick on.
/// </summary>
public sealed class InterpolationScheduleTests
{
    private const double Interval = 0.015d;

    /// <summary>The owner's config: one update behind, 0 / 1 / 66.</summary>
    private static readonly ClientInterp Owner = new(0f, 1f, 66f);

    private static ServerConVars Set(params (string Name, string Value)[] values)
    {
        ServerConVars server = new();
        server.Apply(new SetConVarMessage([.. values.Select(static value => new KeyValuePair<string, string>(value.Name, value.Value))]));
        return server.Snapshot();
    }

    /// <summary>
    /// A server that raises `sv_client_min_interp_ratio` to 4 at tick 100: before it the owner's ratio of 1 holds (1/66 s,
    /// 1 + 1 = 2 ticks), after it the ratio is clamped up to 4 (4/66 s, 4 + 1 = 5 ticks).
    /// </summary>
    [Test]
    public void TicksAt_BeforeAndAfterTheServerRaisedTheMinimumRatio_FollowsEachTick()
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, new ServerConVars(), [(100, Set(("sv_client_min_interp_ratio", "4")))], Interval);

        (schedule.TicksAt(99.5), schedule.TicksAt(100)).ShouldBe((2, 5));
    }

    /// <summary>A change that leaves the amount alone adds no step, so the schedule names no change for a reader to wake on.</summary>
    [Test]
    public void NextChangeAfter_ASetThatLeavesTheAmount_IsNone()
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, new ServerConVars(), [(100, Set(("sv_gravity", "400")))], Interval);

        schedule.NextChangeAfter(0).ShouldBeNull();
    }

    /// <summary>
    /// **The first tick a delayed target reaches a changetime** (B849): at a delay of 2 until tick 100 and 5 after, a
    /// changetime of 90 is reached at 92; one of 97 would be at 99 under the first delay, which still holds there; one of
    /// 99 would be at 101 under the first but the delay is 5 from 100, so it is reached at 104.
    /// </summary>
    [TestCase(90, 92)]
    [TestCase(97, 99)]
    [TestCase(99, 104)]
    public void Reaches_AChangeTimeEitherSideOfADelayChange_IsTheFirstTickTheTargetPassesIt(int changeTime, double expected)
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, new ServerConVars(), [(100, Set(("sv_client_min_interp_ratio", "4")))], Interval);

        schedule.Reaches(changeTime, 0).ShouldBe(expected);
    }

    /// <summary>
    /// **A rising delay can drop the target back below a changetime it had passed** (B848 review 2, Fable, Sonnet): at 99
    /// the target is 97, at 100 it is 95, so a changetime of 97 is reached again at 102; one of 90 the target stays past
    /// (95 at 100), so asked after 100 it is never reached again.
    /// </summary>
    [TestCase(97, 102d)]
    [TestCase(90, null)]
    public void Reaches_AfterARiseThatDropsTheTargetBack_IsTheReachAfterTheAskedTick(int changeTime, double? expected)
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, new ServerConVars(), [(100, Set(("sv_client_min_interp_ratio", "4")))], Interval);

        schedule.Reaches(changeTime, 100).ShouldBe(expected);
    }

    /// <summary>
    /// **Two changes on one tick: the tick runs under the later** (B848 review 2, Sonnet): 4 then 6 at tick 100 is 7 ticks
    /// there, not 5.
    /// </summary>
    [Test]
    public void TicksAt_TwoChangesOnOneTick_IsTheLaters()
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner,
            new ServerConVars(),
            [(100, Set(("sv_client_min_interp_ratio", "4"))), (100, Set(("sv_client_min_interp_ratio", "6")))],
            Interval);

        schedule.TicksAt(100).ShouldBe(7);
    }

    /// <summary>
    /// **A face's time on an exact step reads the step** (B848 review 2, Sonnet): 1.5 s at 0.015 s a tick is tick 100 by
    /// arithmetic, which the division leaves a hair short of; the delay there is the raised one, 5 ticks, 0.075 s.
    /// </summary>
    [Test]
    public void SecondsAtTime_TheTimeOfAStep_IsTheStepsDelay()
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, new ServerConVars(), [(100, Set(("sv_client_min_interp_ratio", "4")))], Interval);

        schedule.SecondsAtTime(100 * Interval).ShouldBe(5 * Interval, 1e-12);
    }

    /// <summary>
    /// **A falling delay** (B848 review 2, Codex): the minimum ratio of 4 from the start (5 ticks) put back to Valve's at
    /// tick 100 (2 ticks). A changetime of 90 is reached at 95; one of 97 would be at 102 under the first delay, but from 100
    /// the target is 98, so 100; one of 99 is reached at 101.
    /// </summary>
    [TestCase(90, 95)]
    [TestCase(97, 100)]
    [TestCase(99, 101)]
    public void Reaches_AChangeTimeEitherSideOfAFallingDelay_IsTheFirstTickTheTargetPassesIt(int changeTime, double expected)
    {
        InterpolationSchedule schedule = InterpolationSchedule.From(
            Owner, Set(("sv_client_min_interp_ratio", "4")), [(100, new ServerConVars())], Interval);

        (schedule.TicksAt(99), schedule.TicksAt(100), schedule.Reaches(changeTime, 0)).ShouldBe((5, 2, (double?)expected));
    }
}
