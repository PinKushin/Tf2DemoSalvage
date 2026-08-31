using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where the players and props at a moment come from.</summary>
/// <remarks>
/// **The third of these, and deliberately the same shape as the other two.** `IEyeSource` supplies
/// the first-person camera and `IViewmodelSource` the held weapon; both are implemented over a
/// `DemoTimeline` and set when a demo opens. This one supplies the scene's contents.
///
/// **It exists because a `DemoTimeline` cannot be constructed in a test.** Its constructor is
/// private and `Build` takes the bytes of a real file, so anything that samples one directly can
/// only be exercised by shipping a demo into the test project — which is why the sampling sat
/// untested inside `MainForm.ShowMoment` for as long as it did.
///
/// **Buffers are passed in rather than returned**, matching `DemoTimeline`'s own signatures: a
/// moment is rebuilt every frame while playing, and returning fresh lists would allocate two per
/// frame for the garbage collector to find again.
/// </remarks>
public interface IMomentSource
{
    /// <summary>Seconds per tick, as the recording server ran.</summary>
    /// <remarks>
    /// **A server setting rather than a constant.** A box left at its default runs 33 where a
    /// configured one runs 66, and interpolating at the wrong rate reads as a slow or fast server
    /// rather than as a defect.
    /// </remarks>
    public float IntervalPerTick { get; }

    /// <summary>Fills a buffer with the players at a moment, fraction included.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="into">The buffer to fill; cleared first.</param>
    public void PlayersAt(double tick, ICollection<ScenePlayer> into);

    /// <summary>Fills a buffer with the props at a moment, fraction included.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="into">The buffer to fill; cleared first.</param>
    public void PropsAt(double tick, ICollection<SceneProp> into);

    /// <summary>The round the game rules were in, or null when the demo does not say.</summary>
    /// <param name="tick">The moment being shown.</param>
    /// <returns><c>m_iRoundState</c>; <c>GR_STATE_TEAM_WIN</c> is 5.</returns>
    /// <remarks>
    /// **Asked because two drawing rules need it and neither can derive it.** A spawn's team wall
    /// is drawn to nobody once the round is won (<c>c_func_respawnroom.cpp:47</c>). Null is "the
    /// demo did not say" rather than a state — every pre-2009 era specimen carries no game rules
    /// entity at all.
    /// </remarks>
    public int? RoundStateAt(double tick);
}

/// <summary>A demo's timeline, as a moment source.</summary>
/// <param name="timeline">The decoded demo.</param>
/// <remarks>
/// **The production implementation, and it is a pass-through by design** — the same shape as
/// `TimelineEyes` and `TimelineViewmodels`. Everything interesting is in `DemoTimeline`; this only
/// names which of its many questions the scene asks.
/// </remarks>
public sealed class TimelineMoments(DemoTimeline timeline) : IMomentSource
{
    /// <inheritdoc />
    public float IntervalPerTick => timeline.IntervalPerTick;

    /// <inheritdoc />
    public void PlayersAt(double tick, ICollection<ScenePlayer> into) =>
        timeline.PlayersAt(tick, into);

    /// <inheritdoc />
    public void PropsAt(double tick, ICollection<SceneProp> into) =>
        timeline.PropsAt(tick, into);

    /// <inheritdoc />
    public int? RoundStateAt(double tick) => timeline.RoundStateAt(tick);
}
