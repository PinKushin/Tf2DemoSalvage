using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A paused client draws each entity's LAST RECEIVED position, with no interpolation delay (B399).
/// </summary>
/// <remarks>
/// **The engine turns interpolation off while paused, and the flag is global.**
/// <c>C_BaseEntity::InterpolateServerEntities</c> (<c>client/c_baseentity.cpp:3219</c>):
///
/// <code>
///   s_bInterpolate = cl_interpolate.GetBool();
///
///   // Don't interpolate during timedemo playback
///   if ( engine-&gt;IsPlayingTimeDemo() || engine-&gt;IsPaused() )
///   {
///       s_bInterpolate = false;
///   }
/// </code>
///
/// <c>IsInterpolationEnabled()</c> returns exactly that flag (<c>c_baseentity.h:2156</c>), and
/// <c>BaseInterpolatePart1</c> takes its early exit on it (<c>c_baseentity.cpp:2845</c>):
///
/// <code>
///   if ( IsFollowingEntity() || !IsInterpolationEnabled() )
///   {
///       // Assume current origin ( no interpolation )
///       MoveToLastReceivedPosition();
///       return INTERPOLATE_STOP;
///   }
/// </code>
///
/// **So paused is not "the same pose, frozen" — it is a DIFFERENT pose from the one playback shows**,
/// eight ticks nearer the present. That is why this is asserted rather than assumed: the two
/// samplings differ by the whole interpolation window, which is how B397's rocket-jumping soldier
/// came to be drawn still airborne where the paused real client had already landed him.
///
/// <c>ScenePropTrack.Held</c> is `MoveToLastReceivedPosition`'s answer here — the last stated pose,
/// undelayed since B370 — so the conformance question is only whether the paused caller asks it.
/// </remarks>
public sealed class PausedSamplingConformanceTests
{
    private const float Interval = 1f / 66.67f;

    /// <summary>The render delay, read from production rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <summary>A rocket moving 20 units a tick, so a tick of delay is visible as 20 units.</summary>
    private static DemoTimeline Moving() => DemoTimeline.Build(SyntheticRocket.DemoOverTicks(
        Interval,
        (100, -2000f, -2452f, 699f),
        (110, -1800f, -2452f, 699f),
        (120, -1600f, -2452f, 699f)));

    [Test]
    public void PropsAt_WhenPaused_IsTheLastStatedPositionRatherThanTheDelayedBlend()
    {
        List<SceneProp> shown = [];

        Moving().PropsAt(120.0, shown, viewEntity: null, interpolating: false);

        // The keyframe at or before 120, with no delay subtracted and nothing blended: what the
        // last update assigned to `m_vecOrigin`, which is what `MoveToLastReceivedPosition` leaves.
        shown.ShouldHaveSingleItem().Pose.X.ShouldBe(-1600f, 0.5f);
    }

    [Test]
    public void PropsAt_PausedAndPlaying_DisagreeByTheWholeInterpolationWindow()
    {
        // **The control.** If a paused frame happened to agree with a playing one, the test above
        // could pass against a sampler that ignored the flag entirely — and the divergence B399
        // records is precisely that the two answers are a window apart.
        List<SceneProp> playing = [];
        List<SceneProp> paused = [];

        DemoTimeline timeline = Moving();
        timeline.PropsAt(120.0, playing);
        timeline.PropsAt(120.0, paused, viewEntity: null, interpolating: false);

        // 20 units a tick over the 110-to-120 span, so the gap is the delay itself.
        float expected = 20f * Delay;

        (paused.ShouldHaveSingleItem().Pose.X - playing.ShouldHaveSingleItem().Pose.X)
            .ShouldBe(expected, 1f);
    }

    [Test]
    public void PropsAt_PausedAfterPlayingAtTheSameTick_DoesNotServeThePlayingSample()
    {
        // **The cache is the trap.** Sampling is incremental — a track keeps its last built prop
        // and is only re-derived when a wake says its answer moved — so a pause that changes only
        // the sampling RULE and not the tick must still invalidate what was built, or the viewer
        // keeps drawing the interpolated pose it built one frame earlier.
        DemoTimeline timeline = Moving();

        List<SceneProp> shown = [];
        timeline.PropsAt(120.0, shown);
        timeline.PropsAt(120.0, shown, viewEntity: null, interpolating: false);

        shown.ShouldHaveSingleItem().Pose.X.ShouldBe(-1600f, 0.5f);
    }

    /// <summary>A player crossing 100 units per ten-tick span, so a delay is visible as distance.</summary>
    private static DemoTimeline MovingPlayer() => DemoTimeline.Build(SyntheticPlayer.DemoOverTicks(
        Interval, (100, 0f, 0f), (110, 100f, 0f), (120, 200f, 0f)));

    [Test]
    public void PlayersAt_WhenPaused_IsTheLastStatedPositionRatherThanTheDelayedBlend()
    {
        // **A player is interpolated by the same machinery as a rocket** — `m_vecOrigin` is
        // registered on `C_BaseEntity` (`c_baseentity.cpp:905`) — and `s_bInterpolate` is one
        // global flag, so nothing about a player exempts it from the paused rule.
        //
        // **This asserted only that the call answered, and a sabotage proved it could not fail.**
        // Restoring `track.At(tick)` for the player path reddened nothing, which is the exact shape
        // `docs/memory/most-of-a-decoder-is-untested.md` warns about: the props were covered and
        // the players were not, in the same fix.
        List<ScenePlayer> shown = [];

        MovingPlayer().PlayersAt(120.0, shown, interpolating: false);

        shown.ShouldHaveSingleItem().X.ShouldBe(200f, 0.5f);
    }

    [Test]
    public void PlayersAt_PausedAndPlaying_DisagreeByTheWholeInterpolationWindow()
    {
        // The control, as for the props: the paused answer must differ from the playing one by the
        // delay, or the assertion above would pass against a sampler that ignored the flag.
        List<ScenePlayer> playing = [];
        List<ScenePlayer> paused = [];

        DemoTimeline timeline = MovingPlayer();
        timeline.PlayersAt(120.0, playing);
        timeline.PlayersAt(120.0, paused, interpolating: false);

        // 10 units a tick across the 110-to-120 span, so the gap is the delay itself.
        (paused.ShouldHaveSingleItem().X - playing.ShouldHaveSingleItem().X)
            .ShouldBe(10f * Delay, 1f);
    }
}
