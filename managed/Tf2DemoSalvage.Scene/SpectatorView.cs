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
    /// <summary>Whether this demo carries a recorded camera at all — the demo-KIND question.</summary>
    /// <remarks>
    /// True for a point-of-view recording, whose <c>democmdinfo_t</c> holds the camera the client
    /// computed; false for SourceTV, which leaves it zeroed. D128 hangs on this: a POV demo is
    /// PVS-limited, so every camera except the recorded one shows a world that was never
    /// transmitted.
    /// </remarks>
    public bool HasRecordedView { get; }

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
    /// <inheritdoc />
    public bool HasRecordedView => timeline.HasRecordedView;

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
/// <summary>Whether first person was entered, and what to tell the log and the user.</summary>
/// <param name="Entered">Whether there were any eyes to borrow.</param>
/// <param name="Message">What the log records, naming which of the cases applied.</param>
/// <param name="Status">What the user is told, or null when nothing was refused.</param>
public readonly record struct FirstPersonEntry(bool Entered, string Message, string? Status);

/// <summary>What came of trying to follow somebody else.</summary>
/// <param name="Switched">Whether the target actually moved.</param>
/// <param name="Message">What happened, for the spectate log.</param>
public readonly record struct SpectatorSwitch(bool Switched, string Message);

/// <summary>Why a camera mode is refused on this demo, for the log and the status line.</summary>
/// <param name="Message">The full sentence for the log, naming the reason.</param>
/// <param name="Status">The short line for the status bar.</param>
public readonly record struct CameraRefusal(string Message, string Status);

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

    /// <summary>The player whose view is being drawn, as a player rather than an index.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The player, or null when nobody is followed or they are absent from the roster.</returns>
    /// <remarks>
    /// **<see cref="Followed"/> resolved to a player, and the distinction from <see cref="Target"/>
    /// is the whole of B225.** `Target` is `SpectatorTarget.Choose` — the lowest entity index on a
    /// playing team — which is who a SourceTV recording should watch. A POINT-OF-VIEW demo carries
    /// the recorder's own camera instead, and the recorder is usually somebody else entirely.
    ///
    /// Asking `Target` about a POV demo therefore answers about the wrong person. The owner watched
    /// his own recording play through his death and the viewer stayed in first person drawing his
    /// weapon, because some other player was alive and that is who was being asked. Measured on that
    /// demo: a thirty-second run straight through the death at tick 2008 drew the viewmodel 30 times
    /// and logged one mode line.
    ///
    /// **Built on `Followed` rather than repeating its test**, which is the point — its own remarks
    /// say it is *"asked in one place so the two decisions cannot disagree"*, and a second copy of
    /// "is there a recorded view" is exactly how they would come to.
    ///
    /// Null when the followed entity is not in the roster at this tick, which is ordinary early in
    /// a recording. Callers treat that as "no opinion" rather than as a refusal.
    /// </remarks>
    public ScenePlayer? Viewed(int tick) =>
        Eyes is { } eyes ? PlayerAt(eyes, tick, Followed(tick)) : null;

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

    /// <summary>Whether the first-person view can be entered at a tick, and what to say.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <returns>The decision, with the sentences that go with it.</returns>
    /// <remarks>
    /// **This was the decision half of <c>MainForm.ToggleFirstPerson</c>** (B188, D90). Whether
    /// there are eyes to borrow, and whose they are, is a question about the demo; what remains in
    /// the window is a mode flag, an invalidate and a status bar.
    ///
    /// **It returns sentences rather than a bool because refusing has to be VISIBLE.** A key that
    /// silently does nothing reads as a broken key, and the reason it can refuse is a real property
    /// of the demo rather than a failure: a recording can lose its subject mid-playback, and one
    /// with nobody in it has no eyes at all. A caller mapping a bool back to a sentence would be
    /// re-deriving the case this already established.
    ///
    /// **The two allowed cases are named separately and that is not decoration.** A POV demo carries
    /// the recorder's own view; a SourceTV recording carries none and a player is spectated instead.
    /// Which one is in play decides whether an angle that looks wrong is our defect or the
    /// recording's, and the log is where that gets settled after the fact.
    ///
    /// **Which case applies is asked PER TICK, and the form asked it per DEMO.** `MainForm` read
    /// `DemoTimeline.HasRecordedView`, which is true when the demo carries any recorded view at all
    /// — so on a demo whose recorded views stop partway it announced "following the recording's own
    /// camera" while <see cref="Eye"/> was in fact spectating a player, because `Eye` has always
    /// decided per tick. A deliberate correction rather than a move, and small: it changes a
    /// sentence in the log, not what is drawn.
    /// </remarks>
    public FirstPersonEntry Enter(int tick, float aspect)
    {
        if (Eye(tick, aspect) is null)
        {
            return new FirstPersonEntry(
                Entered: false,
                "first person unavailable: this demo has no recorded camera and no player to " +
                "follow at this tick",
                "No first-person view here: nothing to follow at this tick.");
        }

        return new FirstPersonEntry(
            Entered: true,
            Eyes?.RecordedViewAt(tick) is not null
                ? "first person on, following the recording's own camera"
                : "first person on, spectating a player (this demo has no recorded camera)",
            Status: null);
    }

    /// <summary>Follow the next player along, or the previous one.</summary>
    /// <param name="tick">The tick whose roster to walk.</param>
    /// <param name="reverse">Whether to go backwards.</param>
    /// <returns>Whether the target moved, and what to log.</returns>
    /// <remarks>
    /// **This was `MainForm.CycleTarget`** (B188, D90). Which player comes next is a question about
    /// a roster and about who can be observed at all; the window's only stake is that something
    /// changed and the view needs rebuilding.
    ///
    /// **`Spectating` is left ALONE when there is nobody else**, rather than being cleared or
    /// reassigned to the same value. A refusal that still wrote to the property would read
    /// identically in the log and differ only on screen.
    ///
    /// **Both counts are reported, because printing only the roster misled a real investigation.**
    /// The line said "of 12" from the roster while the cycle was choosing from the OBSERVABLE set —
    /// which on a POV demo is often one, since everyone outside the recorder's PVS is not `Drawn`.
    /// Clicking then returns the same player every time, `(at + 1) % 1` being 0, while the line
    /// claimed twelve candidates. Reporting both says which kind of nothing is happening: one
    /// reachable player out of twelve is a POV demo behaving correctly.
    /// </remarks>
    public SpectatorSwitch Cycle(int tick, bool reverse)
    {
        if (Eyes is not { } eyes)
        {
            return new SpectatorSwitch(Switched: false, "no demo open");
        }

        // **A point-of-view demo follows its recorder, and there is nobody else to give** (D128).
        // The other players exist in the file only where the recorder saw them — spectating one is
        // a view the recording cannot answer. TF2's playback of a POV demo offers no target
        // switching either; the spectator commands belong to SourceTV.
        if (eyes.HasRecordedView)
        {
            return new SpectatorSwitch(
                Switched: false,
                "a point-of-view recording follows its recorder; the other players were only "
                + "transmitted where the recorder saw them, so there is nobody else to spectate "
                + "(D128)");
        }

        IReadOnlyList<ScenePlayer> players = eyes.PlayersAt(tick);

        if (SpectatorTarget.Next(players, Spectating ?? Followed(tick), reverse) is not { } next)
        {
            return new SpectatorSwitch(Switched: false, "nobody else to follow at this tick");
        }

        Spectating = next.EntityIndex;

        int reachable = SpectatorTarget.Observable(players).Count;

        return new SpectatorSwitch(
            Switched: true,
            $"following entity {next.EntityIndex} (team {next.Team}) " +
            $"of {reachable} observable ({players.Count} on the roster) " +
            $"at tick {tick}");
    }

    /// <summary>Whether this demo refuses a camera mode outright, and why.</summary>
    /// <param name="requested">The mode being asked for.</param>
    /// <returns>The refusal, or null when the mode is available on this demo.</returns>
    /// <remarks>
    /// **A point-of-view demo is locked to the recorder's view, and that is a property of the
    /// DEMO** (D128). The owner: *"we do exactly what tf2 does, because tryign to do anything else
    /// whould be creating information we dont have."* A POV recording is PVS-limited — entities
    /// outside the recorder's visibility were never transmitted — so a free camera pointed at the
    /// rest of the map does not show a room the viewer renders badly, it shows a room that was
    /// never recorded. TF2's own playback of a POV demo is the recorded view with no spectator UI
    /// at all; SourceTV keeps every mode, because an STV recording carries the whole server.
    ///
    /// First person stays allowed because it IS the recorded view — one mode, two mechanisms, per
    /// <see cref="CameraMode.FirstPerson"/> — and death inside it is already the engine's own
    /// fallback through <see cref="Effective"/>, which is the recorded death cam rather than a
    /// choice the viewer made.
    /// </remarks>
    public CameraRefusal? Refuses(CameraMode requested)
    {
        if (requested == CameraMode.FirstPerson || Eyes is not { HasRecordedView: true })
        {
            return null;
        }

        string mode = requested == CameraMode.ThirdPerson ? "third person" : "the free camera";

        return new CameraRefusal(
            $"{mode} is refused on a point-of-view recording: entities outside the recorder's "
            + "view were never transmitted, so there is no world there to show (D128)",
            "POV demo: the camera is the recorder's own.");
    }

    /// <summary>Which camera mode is actually available, which death can change.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="requested">The mode the user asked for.</param>
    /// <returns>The mode to draw through.</returns>
    /// <remarks>
    /// **A dead player cannot be watched from inside his own head, and the engine says so outright.**
    /// <c>C_HLTVCamera::CalcInEyeCamView</c> (<c>hltvcamera.cpp:307</c>):
    ///
    /// <code>
    /// if ( !pPlayer->IsAlive() )
    /// {
    ///     // if dead, show from 3rd person
    ///     CalcChaseCamView( eyeOrigin, eyeAngles, fov );
    ///     return;
    /// }
    /// </code>
    ///
    /// **Returned as a MODE rather than as a swapped camera, and that distinction is the whole
    /// fix.** Handing back the chase camera while the viewer still believed it was in first person
    /// would draw the weapon over a chase view, because
    /// <c>CViewRender::ShouldDrawViewModel</c> (<c>viewrender.cpp:974</c>) keys off
    /// <c>ShouldDrawLocalPlayer()</c> — that is, off the mode. Changing the mode carries both
    /// consequences the engine has: no viewmodel, and the followed player becomes visible.
    ///
    /// **This is what an earlier attempt got wrong** (D116). It implemented the citation above by
    /// emptying the dead player's hands while leaving the camera in his skull — half of a mechanism
    /// whose other half had nowhere to go, because there was no third-person mode to fall into. The
    /// mode existing is what lets this be one line instead of a special case.
    ///
    /// **Applied whichever way the eye would have been found**, recorded view or spectated target.
    /// A point-of-view demo's recorded view during death IS the death cam, which is third person in
    /// TF2 as well, so treating it as first person is the divergence rather than the fidelity.
    ///
    /// **A SECOND rule joins it, from a different system** (B225). The engine's own test for "is
    /// this view first person" is <c>C_BasePlayer::LocalPlayerInFirstPersonView</c>
    /// (<c>c_baseplayer.cpp:1919</c>), which allows only <c>OBS_MODE_NONE</c> and
    /// <c>OBS_MODE_IN_EYE</c> and returns false for everything else — *"Not looking at the local
    /// player, e.g. in a replay in third person mode or freelook."*
    ///
    /// **Both are needed, and neither implies the other.** The HLTV rule above is about a dead
    /// TARGET; this one is about an OBSERVING one, and a player who goes to spectator is alive by
    /// <c>m_lifeState</c> — spectating is not dying — so liveness cannot see them at all. That is
    /// the case the owner found by watching a demo play: TF2 puts a player who goes to spectator
    /// into <c>OBS_MODE_ROAMING</c>, the point-of-view recording follows whatever camera they chose,
    /// and this viewer drew their old weapon over it.
    ///
    /// Conversely the observer mode cannot replace liveness, because a recording that never sent
    /// <c>m_iObserverMode</c> reads as <c>OBS_MODE_NONE</c> — absence is the default, not an
    /// unknown — leaving liveness the only thing that can answer on such a demo.
    /// </remarks>
    public CameraMode Effective(int tick, CameraMode requested)
    {
        if (requested != CameraMode.FirstPerson)
        {
            return requested;
        }

        // **`Viewed`, not `Target`, and that swap is B225.** The mode has to be decided by the
        // person whose eyes are being used. On a point-of-view demo that is the RECORDER, and
        // `Target` is whoever `SpectatorTarget.Choose` picks — so this rule was reading the liveness
        // of a different player entirely, and stayed in first person through the recorder's death
        // because somebody else was still alive.
        //
        // **A null keeps the requested mode**, as it always has: there is nobody to ask, and a demo
        // with no roster is not a reason to force a chase camera that has nothing to chase.
        if (Viewed(tick) is not { } target)
        {
            return requested;
        }

        return target is { IsAlive: true, InFirstPersonView: true }
            ? requested
            : CameraMode.ThirdPerson;
    }

    /// <summary>How far a box may travel through the world before something solid stops it.</summary>
    /// <remarks>
    /// **Set from the loaded map; null means no world and therefore no clipping.** The same shape as
    /// <see cref="Eyes"/>: a collaborator supplied from outside so this type stays testable without
    /// a BSP, and so a viewer with no map open behaves rather than throwing.
    ///
    /// Returns the fraction of the way the sweep got, 0 to 1 — <c>BspLeafTree.Sweep</c>.
    /// </remarks>
    public Func<(float X, float Y, float Z), (float X, float Y, float Z), float, float>? World { get; set; }

    /// <summary>The chase camera, clipped against the world and eased back out.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <param name="seconds">Time since the previous frame, for the recovery.</param>
    /// <returns>The camera, or null when there is no target.</returns>
    /// <remarks>
    /// **Valve clips the chase camera and grows it back**, and the growth is stateful, which is why
    /// the distance is remembered here rather than recomputed:
    ///
    /// <code>
    ///   UTIL_TraceHull( targetOrigin1, cameraOrigin, WALL_MIN, WALL_MAX, MASK_SOLID, ... );
    ///   float dist = VectorLength( trace.endpos - targetOrigin1 );
    ///   m_flLastDistance += gpGlobals->frametime * 32.0f;
    ///   if ( dist > m_flLastDistance ) …camera at m_flLastDistance…
    ///   else { cameraOrigin = trace.endpos; m_flLastDistance = dist; }
    /// </code>
    ///
    /// The arithmetic is <see cref="ChaseCamera.Approach"/>; this supplies the trace and holds
    /// <c>m_flLastDistance</c>.
    ///
    /// **The camera is placed by re-running the placement at the shortened distance**, rather than
    /// by moving the returned camera: the angles must not change when a wall interrupts, and
    /// deriving the position twice is how a camera and its frustum come to disagree.
    /// </remarks>
    public FreeCamera? Chase(int tick, float aspect, double seconds)
    {
        // **`Viewed` for the same reason `Effective` uses it, and fixing only one would have been
        // worse than fixing neither** (B225). `Effective` falls to third person when the person
        // being watched dies; if this then chased whoever `SpectatorTarget.Choose` picks, a POV
        // demo would drop out of the recorder's eyes and land behind a stranger. That is a new
        // visible defect created by half a fix — the shape D116 is about.
        if (Viewed(tick) is not { } target)
        {
            return null;
        }

        float yaw = target.EyeYaw ?? target.Yaw;
        bool alive = target.IsAlive;
        bool ducking = Ducking(target);

        FreeCamera ideal = FreeCamera.Chase(
            (target.X, target.Y, target.Z), yaw, alive, ducking, aspect);

        if (World is not { } sweep)
        {
            return ideal;
        }

        // The point the camera looks at, which is what the engine traces FROM — the target's eyes,
        // or the height it looks over a ragdoll from.
        (float X, float Y, float Z) look =
            (target.X, target.Y, target.Z + (alive ? PlayerEye.Spectated(ducking) : PlayerEye.DeadChaseTarget));

        float reached = sweep(look, ideal.Origin, ChaseCamera.WallHalfExtent);

        float blockedAt = ChaseCamera.Distance * Math.Clamp(reached, 0f, 1f);

        // **Without a frame clock the recovery cannot run, and running it anyway would RATCHET.**
        // `Approach` takes the smaller of blocked-and-grown, so with zero elapsed time it is a plain
        // minimum: the camera would move in at the first wall and never come back out for the rest
        // of the session. Taking the clip alone is a stated approximation — no easing — where the
        // ratchet is a defect that would read as the camera slowly strangling itself.
        _chaseDistance = seconds > 0d
            ? ChaseCamera.Approach(blockedAt, _chaseDistance, (float)seconds)
            : blockedAt;

        return FreeCamera.Chase(
            (target.X, target.Y, target.Z), yaw, alive, ducking, aspect, _chaseDistance);
    }

    /// <summary>The chase camera on whoever is being watched, or null when nobody is.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="aspect">The viewport's width over its height.</param>
    /// <returns>The camera, or null when there is no target.</returns>
    /// <remarks>
    /// **Valve's <c>OBS_MODE_CHASE</c>** — see <see cref="CameraMode.ThirdPerson"/> and
    /// <see cref="ChaseCamera"/> for the citations.
    ///
    /// **It follows the same target as <see cref="Eye"/>**, deliberately: switching between first
    /// and third person watches the same player, which is what <c>C_HLTVCamera</c> does with one
    /// <c>m_iTraget1</c> and a mode beside it. Two independent target choices would let the modes
    /// drift apart, and the bug would look like the camera jumping to a different player.
    ///
    /// **A dead target is fine here** — unlike <see cref="Eye"/>, which refuses one. That asymmetry
    /// IS the engine's: `CalcInEyeCamView` bails to this, so this is where a dead target ends up
    /// rather than somewhere it must be kept out of.
    ///
    /// **No elapsed time, so the wall recovery cannot advance.** For a caller that has no frame
    /// clock the camera is clipped but never eases back out, which is a worse picture than the
    /// overload that takes one — this exists for tests and for callers that do not draw.
    /// </remarks>
    public FreeCamera? Chase(int tick, float aspect) => Chase(tick, aspect, 0d);

    /// <summary>Valve's <c>m_flLastDistance</c>: how far out the camera was allowed last frame.</summary>
    private float _chaseDistance = ChaseCamera.Distance;

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

        // **A dead spectated player is watched in third person, and this is where the engine says
        // so** — <c>C_HLTVCamera::CalcInEyeCamView</c> (<c>hltvcamera.cpp:307</c>) opens with it:
        //
        //     if ( !pPlayer->IsAlive() )
        //     {
        //         // if dead, show from 3rd person
        //         CalcChaseCamView( eyeOrigin, eyeAngles, fov );
        //         return;
        //     }
        //
        // **Returning null is how that is said here**, because the caller already falls back to the
        // free camera when there is no eye — which is our chase camera.
        //
        // **It belongs HERE and not on the viewmodel** (B222). This project first implemented it by
        // emptying the hands while keeping the first-person camera, which is a state the engine
        // never has: `C_BaseViewModel::ShouldDraw` carries no liveness term at all, only "in eye"
        // and "belongs to the target". Half the mechanism took the viewmodel off screen and left
        // the camera in the dead player's skull. The owner's rule, and it is the right one:
        // *"dont be changing shit to not match valve while trying to fix this"*.
        //
        // **Only the SPECTATED path, deliberately.** `CalcInEyeCamView` is `C_HLTVCamera`, which a
        // point-of-view demo never runs — there the recorded view IS what the player saw, deathcam
        // included, and refusing it would cut to a free camera on every death. That is an assumption
        // worth falsifying: if a POV demo should also chase on death, this check moves up.
        if (!target.IsAlive)
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
