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
}
