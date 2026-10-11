using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A moment of a match stated by a test — these players, these scenes, these sounds — wired to the scene as the
/// presenter wires a recording (D213).
/// </summary>
/// <remarks>
/// **What a real demo was being opened for, without the demo.** The face and cloak tests need one player in one state
/// and the two or three list entries that drive it; a thirty-minute match supplied those at a minute's decode each.
/// Here the test writes them down — from `moment-dump`, with the recording and tick named beside them — and everything
/// AFTER the timeline is production's: <see cref="PlayerAppearances"/> resolving the scenes from the install's
/// <c>scenes.image</c> and the sentences from its sound caches, <see cref="MomentScene"/> building and posing.
///
/// **What this cannot say** is that the timeline produces those entries from the wire. That is the decode's own
/// suites' claim (`Core.Tests`, on <c>SyntheticDemo</c>) and the corpus suite's.
/// </remarks>
internal static class SyntheticMatch
{
    /// <summary>Every specimen here was recorded at 66.67 ticks a second.</summary>
    public const float IntervalPerTick = 0.015f;

    /// <summary>A timeline holding these players in one frame, and the lists a performance is read from.</summary>
    public static DemoTimeline Timeline(
        IReadOnlyList<ScenePlayer> players,
        List<SceneChoreography>? scenes = null,
        List<SceneSound>? sounds = null,
        IReadOnlyList<SceneRagdoll>? corpses = null) =>
        DemoTimeline.ForPerformance([new TimelineFrame(0, players)], scenes, sounds, corpses, IntervalPerTick);

    /// <summary>The appearance the presenter builds for a recording, from the shared install.</summary>
    public static GameAppearance Appearance(DemoTimeline timeline) =>
        (GameAppearance)new PlayerAppearances(NullLogger.Instance) { Timeline = timeline, Game = SharedGame.Content }
            .Ensure(NoAppearance.Instance);

    /// <summary>A scene over a test's own model set, with the install's weapons and the given appearance.</summary>
    public static MomentScene Scene(EntityModelSet models, IPlayerAppearance appearance) =>
        new(models, new ViewmodelScene(), NullLogger.Instance)
        {
            Weapons = SharedGame.Content.Weapons,
            Appearance = appearance,
        };

    /// <summary>A third-person moment at a tick, as the viewer describes one to the scene.</summary>
    /// <param name="timeline">The timeline the moment is of.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="recorder">The recording's own player, for a specimen taken from a point-of-view demo; else the timeline's.</param>
    /// <param name="serverTime">
    /// The server's clock at that tick where the recording's differs from tick times interval — a server that had been
    /// up before the recording began — which is what a networked time such as a cloak's end is measured against.
    /// </param>
    public static MomentInfo Info(DemoTimeline timeline, int tick, int? recorder = null, float? serverTime = null) =>
        new(
            Tick: tick,
            CurrentTick: tick,
            FirstPerson: false,
            Followed: -1,
            EyeCamera: null,
            IntervalPerTick: timeline.IntervalPerTick,
            ViewmodelFieldOfView: 54f,
            Recorder: recorder ?? timeline.RecorderEntityIndex)
        {
            ServerTime = serverTime ?? new TimelineMoments(timeline).ServerTimeAt(tick),
        };
}
