using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a demo can say about whose eyes the view is using.</summary>
/// <remarks>
/// **An abstraction rather than the timeline itself, for the reason D54 gives**: a test that had to
/// supply a <see cref="DemoTimeline"/> would have to build one, and building one means a demo file.
/// Three members is everything the eye view asks, so a stand-in is a dozen lines.
///
/// The same shape as <see cref="IViewmodelSource"/>, deliberately — that seam already exists for the
/// same reason, and two different arrangements for one problem is how they drift.
/// </remarks>
public interface IEyeSource
{
    /// <summary>The camera the recording client computed, when the demo carries one.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The recorded view, or null for a SourceTV demo.</returns>
    public RecordedView? RecordedViewAt(int tick);

    /// <summary>Which entity did the recording, when the demo says.</summary>
    public int? RecorderEntityIndex { get; }

    /// <summary>Everyone present at a tick.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The players, which may be empty.</returns>
    public IReadOnlyList<ScenePlayer> PlayersAt(int tick);
}

/// <summary>A demo timeline, as a source of eyes.</summary>
/// <param name="timeline">The timeline.</param>
/// <remarks>The whole adapter, mirroring <see cref="TimelineViewmodels"/>.</remarks>
public sealed class TimelineEyes(DemoTimeline timeline) : IEyeSource
{
    /// <inheritdoc/>
    public RecordedView? RecordedViewAt(int tick) => timeline.RecordedViewAt(tick);

    /// <inheritdoc/>
    public int? RecorderEntityIndex => timeline.RecorderEntityIndex;

    /// <inheritdoc/>
    public IReadOnlyList<ScenePlayer> PlayersAt(int tick) => timeline.PlayersAt(tick);
}

/// <summary>Whose eyes the first-person view is using, and where they are.</summary>
/// <remarks>
/// **This was <c>MainForm.FollowedEntity</c>, <c>Spectated</c>, <c>FirstPersonCamera</c>,
/// <c>PlayerAt</c> and <c>Ducking</c>** (B188, D90). The only thing any of it wanted from a window
/// was the viewport's aspect ratio, which is one float and is now an argument.
///
/// **Valve computes a view on the PLAYER, dispatching on observer mode**: <c>C_BasePlayer::CalcView</c>
/// (<c>c_baseplayer.h:112</c>) hands off to <c>CalcObserverView</c> (<c>:455</c>), which picks
/// between <c>CalcInEyeCamView</c>, <c>CalcChaseCamView</c> and <c>CalcRoamingView</c>
/// (<c>:463</c>). None of that is in the window either.
///
/// **Two mechanisms behind one mode here, and which applies is a property of the demo.** A
/// point-of-view demo carries the camera the recording client computed, in <c>democmdinfo_t</c> —
/// used as it stands, because it already accounts for death, spectating and every observer mode.
/// Rebuilding it from the recorder's entity would be right while they lived and wrong for the rest:
/// measured, the two part company by 169 units on the 2009 demo the moment the recorder dies. A
/// SourceTV demo carries no camera, so the view is built from a player's own position and eye
/// angles, which is what the engine does when you spectate in game.
/// </remarks>
public sealed class SpectatorView
{
    private readonly ILogger _spectate;

    /// <summary>Creates a view over a demo.</summary>
    /// <param name="spectate">Where an overridden target that cannot be followed is reported.</param>
    /// <exception cref="ArgumentNullException"><paramref name="spectate"/> is null.</exception>
    public SpectatorView(ILogger spectate)
    {
        ArgumentNullException.ThrowIfNull(spectate);

        _spectate = spectate;
    }

    /// <summary>Where eyes come from, set when a demo is loaded.</summary>
    /// <remarks>Null before one is, which is every frame of a freshly opened viewer.</remarks>
    public IEyeSource? Eyes { get; set; }

    /// <summary>The entity <c>--spectate</c> named, or null to choose automatically.</summary>
    /// <remarks>Also what the target-cycling key writes, so both routes are one piece of state.</remarks>
    public int? Spectating { get; set; }

    /// <summary>Which entity the camera is following at a tick, or null.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The entity index, or null when nobody is followed.</returns>
    /// <remarks>
    /// Asked in one place so the two decisions cannot disagree — this decides which player is hidden
    /// from their own view, and a mismatch would hide the wrong body or leave the followed one
    /// standing in front of the lens.
    /// </remarks>
    public int? Followed(int tick)
    {
        if (Eyes is not { } eyes)
        {
            return null;
        }

        return eyes.RecordedViewAt(tick) is not null
            ? eyes.RecorderEntityIndex
            : Target(tick)?.EntityIndex;
    }

    /// <summary>The player being spectated at a tick, honouring an override.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The player, or null when nobody can be followed.</returns>
    /// <remarks>
    /// **One resolver, because two call sites decide different halves of the same picture** — the
    /// camera's position and which body to hide. They read this rather than
    /// <see cref="SpectatorTarget.Choose"/> directly, so an override cannot reach one and miss the
    /// other and leave a player standing in front of their own lens.
    ///
    /// The override falls back rather than failing when the named entity is not playing at this
    /// tick: a spy is dead, in the lobby, or another class for most of a match, and a viewer that
    /// went black for those stretches would be worse than one that shows somebody. It says so in the
    /// log rather than silently, because "I asked for entity 11 and got somebody else" is exactly
    /// the kind of thing that reads as a decode bug.
    /// </remarks>
    public ScenePlayer? Target(int tick)
    {
        if (Eyes is not { } eyes)
        {
            return null;
        }

        IReadOnlyList<ScenePlayer> players = eyes.PlayersAt(tick);

        if (Spectating is { } wanted)
        {
            foreach (ScenePlayer player in players)
            {
                if (player.EntityIndex == wanted)
                {
                    return player;
                }
            }

            _spectate.LogWarning(
                "{Message}",
                $"--spectate {wanted} is not playing at tick {tick}; following the default");
        }

        return SpectatorTarget.Choose(players);
    }

    /// <summary>The camera for the first-person view, or null when there is none.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <returns>The camera, or null when nobody's eyes are available.</returns>
    public FreeCamera? Eye(int tick, float aspect)
    {
        if (Eyes is not { } eyes)
        {
            return null;
        }

        if (eyes.RecordedViewAt(tick) is { } recorded)
        {
            // Only the eye height is added, because the recorded origin is the feet.
            ScenePlayer? recorder = PlayerAt(eyes, tick, eyes.RecorderEntityIndex);

            return FreeCamera.AtEye(recorded, recorder?.PlayerClass ?? 0, Ducking(recorder), aspect);
        }

        // No recorded camera: spectate somebody who is actually playing. Taking the first player in
        // the list took the SourceTV camera instead — see SpectatorTarget, and docs/findings/29 for
        // the three identical captures that found it.
        if (Target(tick) is not { } target)
        {
            return null;
        }

        // **The heights differ between the two paths and that is Valve's doing** rather than an
        // approximation; see `PlayerEye`.
        return FreeCamera.SpectatingEye(
            (target.X, target.Y, target.Z),
            target.EyePitch ?? 0f,
            target.EyeYaw ?? target.Yaw,
            Ducking(target),
            aspect);
    }

    /// <summary>One player at a tick, by entity index.</summary>
    /// <remarks>
    /// <see cref="ScenePlayer"/> is a record STRUCT, so <c>FirstOrDefault</c> hands back a zeroed
    /// player rather than null and an <c>is null</c> check never fires — which would put the camera
    /// at the world origin with class zero rather than reporting that nobody was found.
    /// </remarks>
    private static ScenePlayer? PlayerAt(IEyeSource eyes, int tick, int? entityIndex)
    {
        if (entityIndex is not { } index)
        {
            return null;
        }

        foreach (ScenePlayer player in eyes.PlayersAt(tick))
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>Whether a player is crouched, which lowers the eye by more than a foot.</summary>
    /// <remarks>
    /// <c>FL_DUCKING</c> on <c>m_fFlags</c>. A player whose flags the recording never stated is
    /// treated as standing, which is what they usually are — the same default the animation state
    /// machine takes.
    /// </remarks>
    private static bool Ducking(ScenePlayer? player) =>
        player?.Flags is { } flags && (flags & PlayerActivityState.Ducking) != 0;
}
