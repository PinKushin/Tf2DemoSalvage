using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How far back a checkpoint keeps what its readers look back over, computed from the settings that bound each reader
/// (D220) — never a fixed number of seconds.
/// </summary>
public sealed class WindowReachTests
{
    private const double Interval = 0.015d;

    /// <remarks>
    /// **At TF2's defaults**: tracks reach back the interpolation delay — `cl_interp` 0.1 s, (int)(0.5 + 0.1/0.015) + 1 = 8
    /// ticks — plus the 0.1 s a player's speed and heading are differenced over, 6.67 rounded up to 7: 15. The output lists
    /// reach the longest HUD feed, `hud_saytext_time` 12 s = 800 ticks.
    /// </remarks>
    [Test]
    public void For_TheDefaults_IsTheInterpDelayAndMotionAndTheChatFeed()
    {
        WindowReach reach = WindowReach.For(new WindowSettings(), new ServerConVars(), Interval);

        (reach.TrackTicks, reach.OutputTicks).ShouldBe((15, 800));
    }

    /// <remarks>
    /// **Each setting moves its own reader's reach**: `cl_interp` 0.5 s is (int)(0.5 + 33.3) + 1 = 34 ticks of delay, 41
    /// with the motion window; a kill feed of 20 s lasts 40 s for the local player (`GetExpiryTime` doubles it,
    /// hud_basedeathnotice.cpp:929), outlasting the chat's 12: 2,667 output ticks (2,666.7 rounded up).
    /// </remarks>
    [Test]
    public void For_LongerInterpAndKillFeed_ReachesFurther()
    {
        WindowSettings settings = new(new ClientInterp(Interp: 0.5f), DeathNoticeSeconds: 20d);

        WindowReach reach = WindowReach.For(settings, new ServerConVars(), Interval);

        (reach.TrackTicks, reach.OutputTicks).ShouldBe((41, 2667));
    }

    /// <remarks>
    /// **A settings change re-windows only when it reaches further**: a walk at the defaults covers the defaults and a
    /// shorter chat, and does not cover `cl_interp` 0.5.
    /// </remarks>
    [Test]
    public void Covers_ASettingThatReachesFurther_IsFalse()
    {
        byte[] demo = SyntheticPlayer.DemoOverTicks(0.015f, (10, 0f, 0f), (12, 4f, 0f), (14, 8f, 2f), (20, 30f, 4f));
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, 2, new WindowSettings())!;

        checkpoints.Covers(new WindowSettings()).ShouldBeTrue();
        checkpoints.Covers(new WindowSettings(SayTextSeconds: 6d)).ShouldBeTrue();
        checkpoints.Covers(new WindowSettings(new ClientInterp(Interp: 0.5f))).ShouldBeFalse();
    }

    /// <summary>
    /// A player moving one unit a tick from 10 to 80, then `sv_client_min_interp_ratio` and `…max…` both 10 at tick 81: the
    /// default client's interpolation becomes 10 / 20 = 0.5 s at the END, where every checkpoint before it was trimmed for
    /// 0.1 s. The gap (41 track ticks against 15) is wider than the 8 history entries a trim keeps beyond its cut, so a
    /// window from a walk that did not walk again samples the player wrongly.
    /// </summary>
    private static byte[] LateInterpBound()
    {
        byte[] moving = SyntheticPlayer.DemoOverTicks(0.015f, [.. Enumerable.Range(10, 71).Select(tick => (tick, (float)tick, 0f))]);

        return SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            [
                .. DemoCommandReader.Read(moving.AsMemory(DemoHeader.SizeBytes)).Where(command => command.Type != DemoCommandType.Stop),
                SyntheticDemo.Packet(
                    SyntheticDemo.DefaultProtocol,
                    81,
                    new SetConVarMessage(
                    [
                        new KeyValuePair<string, string>("sv_client_min_interp_ratio", "10"),
                        new KeyValuePair<string, string>("sv_client_max_interp_ratio", "10"),
                    ])),
            ]);
    }

    /// <remarks>
    /// **The END bounds force a second walk**: (int)(0.5 + 0.5/0.015) + 1 + 7 = 41 track ticks are needed, past the 15 the
    /// first walk kept; the walk returned must keep 41, and a window must answer as the whole build — the moving player
    /// is what makes a short reach visible to that comparison.
    /// </remarks>
    [Test]
    public void Walk_EndBoundsReachingFurther_WalksAgainAtTheirReach()
    {
        byte[] demo = LateInterpBound();
        DemoTimeline whole = DemoTimeline.Build(demo);

        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, 2, new WindowSettings())!;

        WindowReach.For(new WindowSettings(), whole.ServerConVars, Interval).TrackTicks.ShouldBe(41, "the control: the end bound reaches further");

        foreach (int tick in new[] { 50, 65, 79 })
        {
            TimelineFingerprint.OfAnswer(TimelineAnswers.At(checkpoints.Window(tick, tick + 1), tick)).Hash
                .ShouldBe(TimelineFingerprint.OfAnswer(TimelineAnswers.At(whole, tick)).Hash, $"at {tick}");
        }

        checkpoints.Reach.TrackTicks.ShouldBe(41);
    }

    /// <remarks>**What a second walk creates is counted once**: the same as one walk at the reach it settled on.</remarks>
    [Test]
    public void Walk_ThatWalksAgain_CountsWhatItCreatedOnce()
    {
        byte[] demo = LateInterpBound();
        Dictionary<Type, (long Objects, long Elements)> twice = [];
        Dictionary<Type, (long Objects, long Elements)> once = [];

        DemoTimeline.Checkpoints.Walk(demo, 2, new WindowSettings(), twice);
        DemoTimeline.Checkpoints.Walk(demo, 2, lookbackTicks: 800, trackLookbackTicks: 41, created: once);

        twice.ShouldNotBeEmpty("the control: something is created");
        twice.OrderBy(entry => entry.Key.FullName).ShouldBe(once.OrderBy(entry => entry.Key.FullName));
    }

    /// <remarks>
    /// **A window's FirstTick is its cut; the demo's start is a carried fact** — step 4's seek bar needs the real one.
    /// </remarks>
    [Test]
    public void FirstTick_OfAWindowLateInADemo_IsTheCutAndTheCheckpointsKeepTheStart()
    {
        byte[] demo = SyntheticPlayer.DemoOverTicks(0.015f, [.. Enumerable.Range(0, 30).Select(step => (10 + (step * 2), step * 3f, 0f))]);
        DemoTimeline whole = DemoTimeline.Build(demo);
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, 2, lookbackTicks: 4)!;

        checkpoints.Window(whole.LastTick - 2, whole.LastTick).FirstTick.ShouldBeGreaterThan(whole.FirstTick, "the control: the window is cut");
        checkpoints.FirstTick.ShouldBe(whole.FirstTick);
    }
}
