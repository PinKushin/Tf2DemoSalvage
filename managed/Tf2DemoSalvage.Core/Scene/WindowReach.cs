using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The viewer's settings that bound how far back a window's readers look (D220).</summary>
/// <param name="Client">The viewer's interpolation ConVars (<c>cl_interp</c>, <c>cl_interp_ratio</c>, <c>cl_updaterate</c>).</param>
/// <param name="DeathNoticeSeconds"><c>hud_deathnotice_time</c>, default 6 (tf/hud_basedeathnotice.cpp:31).</param>
/// <param name="SayTextSeconds"><c>hud_saytext_time</c>, default 12 (hud_basechat.cpp:37).</param>
public sealed record WindowSettings(ClientInterp? Client = null, double DeathNoticeSeconds = 6d, double SayTextSeconds = 12d)
{
    /// <summary>The interpolation settings, defaulted.</summary>
    public ClientInterp Interp => Client ?? new ClientInterp();

    /// <summary>How long the HUD's longest feed line lasts: what a seek replays, and the output lists' reach.</summary>
    /// <remarks>
    /// A death notice lasts <c>hud_deathnotice_time</c>, DOUBLED when the local player is involved
    /// (<c>DeathNoticeItem::GetExpiryTime</c>, tf/hud_basedeathnotice.cpp:923-931); a chat line
    /// <c>hud_saytext_time</c>.
    /// </remarks>
    public double FeedSeconds => Math.Max(DeathNoticeSeconds * 2d, SayTextSeconds);
}

/// <summary>
/// How far back before a checkpoint its output must reach, in ticks: ONE function of the current settings over every
/// reader a window serves (D220), never a fixed number of seconds.
/// </summary>
/// <param name="TrackTicks">What track histories and keyframes must keep: the interpolation delay.</param>
/// <param name="OutputTicks">What the tick-ordered output lists must keep: the longest HUD feed that looks back.</param>
/// <remarks>
/// **Each reader, and what bounds it.** A window answers through <see cref="TimelineAnswers"/>' questions plus the HUD
/// feeds that read its lists:
///
/// - **Track pose** (<c>At</c>, <c>Held</c>): samples at <c>tick − delay</c>, the delay from <c>GetClientInterpAmount</c>
///   (cdll_bounded_cvars.cpp:126) under the server's ratio bounds — so <c>cl_interp</c>, <c>cl_interp_ratio</c>,
///   <c>cl_updaterate</c>, <c>sv_client_min/max_interp_ratio</c>. The spline's older samples are a COUNT of entries,
///   which <c>ScenePropTrack.TrimBefore</c> keeps beyond the cut whatever the reach.
/// - **A player's speed and heading** (<c>SpeedAt</c>, <c>HeadingAt</c>): the track pose a further
///   <see cref="DemoTimeline.MotionWindowSeconds"/> back — fixed, this project's own differencing window. Missed in the
///   first version and found by the windows probe on rgl-pug (<c>Speed</c> differed at 2 of 120 seeks).
/// - **Kill feed and chat** (<c>HudEventFeed</c>, replaying <c>GameEvents</c> and <c>UserMessages</c> after a seek):
///   <see cref="WindowSettings.FeedSeconds"/> — <c>hud_deathnotice_time</c> × 2 and <c>hud_saytext_time</c>.
/// - **Frames, by the same feed**: <c>HudEventFeed.Resolve</c> reads the players and rules at each replayed event's own
///   tick, so frames are cut at the OUTPUT reach, not the track's.
/// - **Fixed, reach 0**: every other at-or-before list (soundscape, fog, director, recorded view, ConVar history,
///   viewmodels by stream) — the trim keeps the element in force at the cut, which is all an at-or-before reader reads.
/// - **Unbounded, carried in the checkpoint**: sounds, kept whole (a loop started long ago still plays,
///   `docs/memory/a-loop-is-state-not-an-event.md`); and a track's birth tick and pose, kept through any trim.
///
/// **Corpses are not a reader of the window.** <c>C_TFRagdoll::ClientThink</c> re-arms the fade at
/// <c>cl_ragdoll_fade_time × 0.33</c> on every think the corpse is visible (c_tf_player.cpp:1532-1546), so a corpse in
/// view never fades — no setting bounds it — and its state belongs in a checkpoint, not a reach. The ragdoll ENTITY is a
/// track, kept while live with its birth; the simulated corpse is the viewer's (<c>RagdollFade</c>) and is a follow-up.
/// </remarks>
public sealed record WindowReach(int TrackTicks, int OutputTicks)
{
    /// <summary>The reach the readers need under these settings and these server bounds.</summary>
    /// <param name="settings">The viewer's settings.</param>
    /// <param name="server">The server's ConVars, which bound the interpolation.</param>
    /// <param name="intervalPerTick">Seconds per tick.</param>
    /// <returns>The reach.</returns>
    public static WindowReach For(WindowSettings settings, ServerConVars server, double intervalPerTick)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(server);

        double interval = intervalPerTick > 0d ? intervalPerTick : ScenePropTrack.Tf2TickInterval;
        double feed = settings.FeedSeconds;

        // SpeedAt and HeadingAt sample the track a fixed 0.1 s before the tick, converted at 0.015 s per tick as they
        // convert it, whatever the demo's own interval.
        return new WindowReach(
            ScenePropTrack.DelayTicksFor(interval, settings.Interp.Amount(server))
                + (int)Math.Ceiling(DemoTimeline.MotionWindowSeconds / 0.015d),
            (int)Math.Ceiling(feed / interval));
    }

    /// <summary>Whether this reach keeps at least everything <paramref name="other"/> needs.</summary>
    /// <param name="other">The reach asked about.</param>
    /// <returns>True when nothing need be walked again.</returns>
    public bool Covers(WindowReach other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return TrackTicks >= other.TrackTicks && OutputTicks >= other.OutputTicks;
    }
}
