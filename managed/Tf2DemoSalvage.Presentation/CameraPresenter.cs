using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a window knows that a camera needs: the transport's tick, the tick last shown, and its own shape.</summary>
/// <param name="Tick">The transport's whole tick.</param>
/// <param name="ShownTick">The moment last shown, which may fall between ticks.</param>
/// <param name="Width">The viewport's width in pixels; zero before it has one.</param>
/// <param name="Height">The viewport's height in pixels; zero before it has one.</param>
/// <param name="FullScreenExclusive">Whether the window is full screen in the exclusive mode (`view.cpp:1077`'s "not windowed").</param>
public readonly record struct CameraWindow(int Tick, double ShownTick, int Width, int Height, bool FullScreenExclusive);

/// <summary>What the demo says of a tick that the device is set with beside the camera: the fog, the monitor's camera, the tonemap.</summary>
/// <param name="WorldFog">The fog in force (B139): `EnableWorldFog` runs in every view setup.</param>
/// <param name="SkyFog">The 3D sky's fog.</param>
/// <param name="MonitorCamera">The `point_camera` a monitor shows, or null.</param>
/// <param name="Tonemap">The tonemap controller's state.</param>
public readonly record struct TickFeeds(SceneFog? WorldFog, SceneFog? SkyFog, ScenePointCamera? MonitorCamera, SceneTonemap Tonemap);

/// <summary>
/// Owns which camera the viewport is drawn through — the requested mode, the spectator, the free camera — and every
/// decision about entering, leaving, cycling and moving one (D90, D92, D214).
/// </summary>
/// <remarks>
/// **Moved out of the window whole.** The engine computes a view on the PLAYER and dispatches on observer mode —
/// `C_BasePlayer::CalcView` (`c_baseplayer.h:112`) to `CalcObserverView` to `CalcInEyeCamView`, `CalcChaseCamView` or
/// `CalcRoamingView` — and the mode beside the target is `C_HLTVCamera`'s (`hltvcamera.cpp`). None of it is a window's
/// there either; what a window has is its size, its tick and its input, which arrive as <see cref="CameraWindow"/> and
/// as arguments: a wheel notch, a drag in pixels, an action already resolved through the bindings.
///
/// **A point-of-view demo takes no manual camera** (D128, D188): <see cref="RefusedByTheDemo"/> is asked before every
/// change of mode, here, so no caller can forget it.
/// </remarks>
public sealed class CameraPresenter
{
    private readonly ICameraView _view;
    private readonly SpectatorView _spectator;
    private readonly ILogger _log;
    private readonly ILogger _renderLog;
    private readonly ILogger _spectateLog;
    private readonly Func<CameraWindow> _window;
    private readonly Func<DemoTimeline?> _timelineNow;
    private readonly Func<LoadedMap?> _loadedNow;
    private readonly Func<ViewerSettings> _settingsNow;

    /// <summary>The open demo's timeline, as its owner holds it now.</summary>
    private DemoTimeline? _timeline => _timelineNow();

    /// <summary>The open map, as its owner holds it now.</summary>
    private LoadedMap? _loaded => _loadedNow();

    /// <summary>The window's settings as they are now.</summary>
    private ViewerSettings _settings => _settingsNow();

    /// <summary>Where the free camera is and how it is placed.</summary>
    private readonly FreeCameraController _freeCamera;

    /// <summary>Which camera the viewport is drawn through.</summary>
    /// <remarks>
    /// **Free by default, because after D98 there is no other kind.** The orthographic top-down
    /// camera is gone; what remains is the free camera and the first-person one, and a demo opens
    /// into the free camera.
    ///
    /// **A stale doc block sat above this one until 2026-08-26**, describing a deleted `_freeLook`
    /// bool — *"Off by default, because the top-down view is what this viewer is for"*. Two
    /// <c>&lt;summary&gt;</c> tags on one field, of which the compiler says nothing, and the first
    /// asserted the exact opposite of what the code now does. Residue of the ortho removal itself:
    /// the field went, its documentation did not.
    /// </remarks>
    private CameraMode _cameraMode = CameraMode.Free;

    /// <summary>Composes the camera's parts. Nothing is placed until a camera is asked for.</summary>
    /// <param name="view">The window: its status line, its invalidation, its held keys, its headless exit.</param>
    /// <param name="spectator">Whose eyes the view uses.</param>
    /// <param name="loggers">The viewer's loggers, by area.</param>
    /// <param name="window">The window's tick and shape, asked for when a camera is.</param>
    /// <param name="timeline">The open demo's timeline as it is when asked.</param>
    /// <param name="map">The open map as it is when asked.</param>
    /// <param name="settings">The window's settings as they are when asked.</param>
    public CameraPresenter(
        ICameraView view,
        SpectatorView spectator,
        ILoggerFactory loggers,
        Func<CameraWindow> window,
        Func<DemoTimeline?> timeline,
        Func<LoadedMap?> map,
        Func<ViewerSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(spectator);
        ArgumentNullException.ThrowIfNull(loggers);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(settings);

        _view = view;
        _spectator = spectator;
        _window = window;
        _timelineNow = timeline;
        _loadedNow = map;
        _settingsNow = settings;
        _log = loggers.CreateLogger("viewer");
        _renderLog = loggers.CreateLogger("render");
        _spectateLog = loggers.CreateLogger("spectate");
        _freeCamera = new FreeCameraController(_renderLog);
    }

    /// <summary>The demo's feeds at the transport's tick, for the device, read where the window read them: after the camera.</summary>
    public TickFeeds FeedsNow() =>
        new(
            _timeline?.FogAt(_window().Tick),
            _timeline?.SkyFogAt(_window().Tick),
            _timeline?.PointCameras.At(_window().Tick),
            _timeline?.Tonemap.At(_window().Tick) ?? default);

    /// <summary>The mode the user asked for, which death does not change; see <see cref="EffectiveMode"/>.</summary>
    public CameraMode Mode => _cameraMode;

    /// <summary>Whether first person is the mode asked for, which a dead target does not change.</summary>
    public bool AskedFirstPerson => _cameraMode == CameraMode.FirstPerson;

    /// <summary>Whether third person is the mode asked for.</summary>
    public bool AskedThirdPerson => _cameraMode == CameraMode.ThirdPerson;

    /// <summary>The first-person action: enters a player's eyes outright, rather than cycling to them.</summary>
    /// <returns>Always true: the key was the camera's.</returns>
    public bool EnterFirstPerson() => EnterCamera(CameraMode.FirstPerson);

    /// <summary>The third-person action: enters the chase camera outright.</summary>
    /// <returns>Always true: the key was the camera's.</returns>
    public bool EnterThirdPerson() => EnterCamera(CameraMode.ThirdPerson);

    /// <summary>The free camera's controller: where it is and how it is placed.</summary>
    public FreeCameraController Free => _freeCamera;

    /// <summary>Whether the free camera is the one asked for.</summary>
    public bool FreeLook => _freeLook;

    /// <summary>The mode actually drawn through, which death can change.</summary>
    public CameraMode EffectiveMode => _effectiveMode;

    /// <summary>Whether the viewport is drawn through a player's eyes.</summary>
    public bool FirstPerson => _firstPerson;

    /// <summary>
    /// Shorthand for the free camera, so the flight and drag handlers read as they did.
    /// </summary>
    /// <remarks>
    /// **Kept deliberately rather than replaced everywhere.** A dozen sites ask "is the free camera
    /// on" to decide whether a drag turns the view or pans the map, and rewriting each to compare
    /// against an enum would be a dozen chances to write the comparison backwards for no gain. The
    /// mode is the state; this is a reading of it.
    /// </remarks>
    private bool _freeLook => _cameraMode == CameraMode.Free;

    /// <summary>The mode actually drawn through, which death can change.</summary>
    /// <remarks>
    /// **`_cameraMode` is what the user ASKED for; this is what the engine allows.** A dead target
    /// cannot be watched from inside his head, so `C_HLTVCamera::CalcInEyeCamView` hands to the
    /// chase camera — see <c>SpectatorView.Effective</c> for the citation.
    ///
    /// **Everything that asks "are we in first person" must ask THIS**, because the answer carries
    /// more than the camera: the viewmodel is drawn only in first person
    /// (<c>ShouldDrawViewModel</c>), and the followed player is hidden only in first person. Reading
    /// the requested mode instead would draw a weapon over a chase view and hide the very player
    /// the chase camera is pointed at.
    /// </remarks>
    private CameraMode _effectiveMode =>
        _spectator.Effective(_window().Tick, _cameraMode);

    /// <summary>Whether the viewport is drawn through a player's eyes.</summary>
    private bool _firstPerson => _effectiveMode == CameraMode.FirstPerson;

    /// <summary>Whether the camera is pointed at a player at all, from inside or behind.</summary>
    /// <remarks>
    /// **The engine's observer-mode gate, which is a range and not an equality.** `spec_next` and
    /// `spec_prev` both run under `if ( GetObserverMode() &gt; OBS_MODE_FIXED )`
    /// (<c>player.cpp:6643</c> and <c>:6661</c>), and `shareddefs.h:493` orders the modes so that
    /// `OBS_MODE_IN_EYE` and `OBS_MODE_CHASE` are the two directly above `OBS_MODE_FIXED`. So
    /// cycling targets is a property of following somebody, not of being inside their head.
    ///
    /// **Distinct from <see cref="_firstPerson"/> and it must stay distinct.** That one decides the
    /// viewmodel and whether the followed player is hidden, which really are first-person-only
    /// (<c>ShouldDrawViewModel</c>). Widening it would draw a weapon over the chase view; this is a
    /// second question with a wider answer, asked by the things that care who is followed.
    ///
    /// **`OBS_MODE_ROAMING` is also above the gate and the free camera is deliberately excluded**
    /// (D135). A roaming spectator in TF2 looks with raw mouse movement, so its `+attack` is free;
    /// here the left button is the look-around drag, and a click that both turned the camera and
    /// jumped to another player would fight itself. Stated as a decision rather than left as an
    /// oversight — see <c>Click_TheCycleTargetButton_InTheFreeCamera_DoesNotCycle</c>, which pins it.
    /// </remarks>
    private bool _followingAPlayer =>
        _effectiveMode is CameraMode.FirstPerson or CameraMode.ThirdPerson;

    /// <summary>Where the free camera is, once it has been placed.</summary>
    /// <remarks>
    /// **A position, not an orbit.** Orbiting a point was the first version and it could not do the
    /// one thing the view was added for: a side-on look at a player. The orbit centre sat at the
    /// middle of the map's height range, so levelling the camera looked out well above everybody's
    /// heads, and there was no way to bring it down — the owner's "not low enough for actual side
    /// on, and the camera doesn't move".
    ///
    /// So the camera flies. The orbit maths is still what PLACES it on entry, which keeps whatever
    /// the map view was centred on in the middle of the first frame.
    /// </remarks>
    private (float X, float Y, float Z)? _freeOrigin
    {
        get => _freeCamera.Origin;
        set => _freeCamera.Origin = value;
    }

    /// <summary>Where `mat_leafvis` measures from: the free camera's own origin, so the box is the leaf the VIEWER is in.</summary>
    public (float X, float Y, float Z) LeafEye => _freeOrigin ?? FreeLookCamera().Origin;

    /// <summary>The free camera, orbiting whatever the top-down view is centred on.</summary>
    /// <remarks>
    /// **Orbits the same point the map view is looking at**, so toggling between them does not
    /// move the subject — drag the map to a player, switch, and that player is still in the middle.
    ///
    /// The height it orbits is the middle of the map's own vertical range rather than the ground:
    /// a focus at floor level puts half the picture below the world, and the range is already known
    /// because the depth projection needs it.
    /// </remarks>
    /// <summary>The view matrix for whichever camera is active.</summary>
    /// <returns>Sixteen floats for the camera constant buffer.</returns>
    /// <remarks>
    /// **One chooser rather than a conditional at each draw site.** There are two places that set
    /// the camera — the world draw and the resize path — and they were a copied ternary apart.
    /// Adding a third mode to a copied ternary is how the viewer's two drawing paths drifted until
    /// one of them stopped showing decals.
    ///
    /// First person falls back rather than failing: a demo can lose its subject mid-playback — the
    /// recorded view runs out before the first packet, and a spectated player can leave — and a
    /// black screen would read as a rendering fault rather than as the end of the material.
    /// </remarks>
    /// <remarks>
    /// **Zero seconds, because projecting the map is not a frame.** It happens on load, on resize
    /// and when the world asks to be re-projected, so advancing the chase camera's wall recovery
    /// from here would ease it by an amount that depends on how often the map was invalidated.
    /// </remarks>
    public float[] ViewMatrix() => ViewCameraNow(0d).ToMatrix();

    /// <summary>The camera this frame is seen through, whichever mode is on.</summary>
    /// <returns>The first-person camera when there is one and the mode asks for it, else the free one.</returns>
    /// <remarks>
    /// **One answer to "which camera", because two things now derive from it** — the projection and
    /// the view frustum the draw culls against. A frustum built from the free camera while the
    /// picture is drawn through a player's eyes would cull the geometry the viewer is looking at.
    /// </remarks>
    /// <remarks>
    /// **Only the active mode's camera is built**, as `C_BasePlayer::CalcView` dispatches to one of `CalcInEyeCamView`,
    /// `CalcChaseCamView` or `CalcRoamingView`. All three were built every frame, and the chase camera's world sweep then cost
    /// 16 ms a frame in the free camera: measured paused on `z1800` after the playback UI test, `camera` 1.3 → 16.3 ms,
    /// which made every automation call after it slow. Building it eagerly also advanced its wall recovery
    /// (`m_flLastDistance`) on frames that were not chasing.
    /// </remarks>
    public FreeCamera ViewCameraNow(double seconds) =>
        ViewCamera.Active(
            _effectiveMode,
            _effectiveMode == CameraMode.FirstPerson ? FirstPersonCamera() : null,
            _effectiveMode == CameraMode.ThirdPerson ? ChaseCamera(seconds) : null,
            FreeLookCamera());

    /// <summary>The camera for the third-person view, or <c>null</c> when there is no target.</summary>
    /// <remarks>
    /// Sibling of <see cref="FirstPersonCamera"/>, and it follows the same target for the reason
    /// recorded on <c>SpectatorView.Chase</c>: `C_HLTVCamera` keeps one target with a mode beside
    /// it, so two independent choices would let the modes drift apart.
    /// </remarks>
    /// <summary>The camera for the third-person view, or <c>null</c> when there is no target.</summary>
    /// <param name="seconds">
    /// Demo time since the last frame, which advances the wall clip's recovery. Zero from anything
    /// that is not the per-frame view setup, so only the frame advances it.
    /// </param>
    /// <remarks>
    /// Sibling of <see cref="FirstPersonCamera"/>, and it follows the same target for the reason
    /// recorded on <c>SpectatorView.Chase</c>: `C_HLTVCamera` keeps one target with a mode beside
    /// it, so two independent choices would let the modes drift apart.
    /// </remarks>
    private FreeCamera? ChaseCamera(double seconds) =>
        _spectator.Chase(_window().ShownTick, Aspect, seconds)?.WithFieldOfView(ViewFovNow().Rendered(Aspect, FovRestricted()));

    /// <summary>Whether the fov's widening stops at 1.85:1 this frame — `view.cpp:1077-1078`, `ViewFov.RestrictsAspect`.</summary>
    /// <remarks>
    /// `sv_restrict_aspect_ratio_fov` is the SERVER's, so it is the demo's: 1 unless a `net_SetConVar` said otherwise
    /// (*interpolated*: the default, which the engine declares and the SDK does not publish). The viewer is a window
    /// unless it is full screen in the exclusive mode.
    /// </remarks>
    private bool FovRestricted()
    {
        string? sent = _timeline?.ServerConVarsAt(_window().Tick).Value(ViewFov.RestrictAspectConVar);

        return ViewFov.RestrictsAspect(
            int.TryParse(sent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 1,
            windowed: !(_window().FullScreenExclusive),
            _timeline?.MaxClients ?? 0);
    }

    /// <summary>The view's field of view this frame — <see cref="SpectatorView.Fov"/>, which every camera takes.</summary>
    /// <remarks>
    /// `curtime` is the SERVER's — `m_flFOVTime` is stamped on its clock — so the tick is the last `net_Tick`.
    /// **Interpolated:** the tick's own time, where the engine's render clock sits between ticks.
    /// </remarks>
    public ViewFov ViewFovNow() => _spectator.Fov(_window().Tick, _effectiveMode, _settings.DemoFovOverride, CurTimeNow());

    /// <summary>The local player's `GetFOVDistanceAdjustFactor()` this frame — <see cref="SpectatorView.DistanceFactor"/>.</summary>
    public float DistanceFactorNow() => _spectator.DistanceFactor(_window().Tick, _settings.DemoFovOverride, CurTimeNow());

    private float CurTimeNow()
    {
        int tick = _window().Tick;
        float interval = _timeline is { IntervalPerTick: > 0f } timeline ? timeline.IntervalPerTick : (float)ScenePropTrack.Tf2TickInterval;

        return (_timeline?.ServerTickAt(tick) ?? tick) * interval;
    }

    /// <summary>Enters or leaves the first-person view, saying why when it cannot be entered.</summary>
    /// <returns>Whether the key was handled.</returns>
    /// <remarks>
    /// **Refusing has to be visible.** A key that silently does nothing reads as a broken key, and
    /// the reason it can refuse is a real property of the demo rather than a failure — a recording
    /// with nobody in it has no eyes to borrow.
    /// </remarks>
    /// <summary>Enters a camera mode outright, rather than cycling to it.</summary>
    /// <param name="mode">The mode wanted.</param>
    /// <returns>Always true: the key was ours whether or not the mode could be entered.</returns>
    /// <remarks>
    /// **Both player modes need a target, and acquiring one is what can fail** — a demo with nobody
    /// worth following has no eye and nothing to chase. `SpectatorView.Enter` already answers that
    /// question and says why when the answer is no, so this reuses it rather than asking again.
    ///
    /// **Failing to enter leaves the mode alone rather than dropping to Free.** The user asked for a
    /// view they cannot have; taking away the one they were in as well would be a second surprise.
    /// </remarks>
    public bool EnterCamera(CameraMode mode)
    {
        // **Before anything else, because a refused mode must change nothing** (D128). The key was
        // still ours — returning true stops it falling through to whatever else Space means.
        if (RefusedByTheDemo(mode))
        {
            return true;
        }

        CameraMode leftFrom = _cameraMode;

        if (mode == CameraMode.Free)
        {
            _cameraMode = CameraMode.Free;
            _view.CameraChanged();

            // **Leaving has to say so, and this line was missing.** Every other mode change is
            // logged, so a silent one is invisible to anything reading the log — including the UI
            // suite, which waits for it to know the camera came back. The cycle reached Free
            // correctly and appeared to skip straight past it, which reads as a broken cycle rather
            // than as a missing sentence.
            //
            // Named for the mode being LEFT, since "first person off" is untrue when the camera was
            // chasing; the shared tail is what a reader waits on.
            _renderLog.LogInformation(
                "{Message}",
                leftFrom == CameraMode.ThirdPerson
                    ? "third person off, back to the free camera"
                    : "first person off, back to the free camera");

            return true;
        }

        FirstPersonEntry entry = _spectator.Enter(_window().Tick, Aspect);

        if (!entry.Entered)
        {
            _renderLog.LogWarning("{Message}", entry.Message);
            _view.Say(entry.Status);

            return true;
        }

        _cameraMode = mode;
        _view.CameraChanged();

        // **Third person says so in its own words.** `Enter`'s message is about acquiring a target,
        // which both player modes share; without a line naming the MODE, a log cannot distinguish
        // watching through a player's eyes from watching over his shoulder — and those look very
        // different on screen while producing identical target messages.
        _renderLog.LogInformation(
            "{Message}",
            mode == CameraMode.ThirdPerson
                ? $"third person on, chasing {entry.Message}"
                : entry.Message);

        return true;
    }

    /// <summary>Moves to the next camera mode, which is what TF2's "Switch Camera Mode" does.</summary>
    /// <remarks>
    /// **Three modes, because the engine has three and cycles them.** A TF2 spectator's mode runs
    /// through `OBS_MODE_IN_EYE`, `OBS_MODE_CHASE` and `OBS_MODE_ROAMING` (`shareddefs.h:490`), and
    /// `C_HLTVCamera::ToggleChaseAsFirstPerson` (`hltvcamera.cpp:843`) flips between the first two
    /// directly. This viewer had only two, so third person was unreachable from the key TF2 puts
    /// the cycle on — the commands `firstperson` and `thirdperson` were the only way in.
    ///
    /// Free comes last so the cycle ends where a viewer with no target can always sit, which is the
    /// same reason D98 makes it the fallback.
    /// </remarks>
    public bool CycleCameraMode() =>
        EnterCamera(_cameraMode switch
        {
            CameraMode.Free => CameraMode.FirstPerson,
            CameraMode.FirstPerson => CameraMode.ThirdPerson,
            _ => CameraMode.Free,
        });

    /// <summary>Refuses a camera this demo cannot answer, saying why — the D128 lock.</summary>
    /// <param name="wanted">The mode about to be entered.</param>
    /// <returns>True when the mode was refused and reported; the caller should change nothing.</returns>
    /// <remarks>
    /// **The rule is <see cref="SpectatorView.Refuses"/>; this is only the reporting.** A
    /// point-of-view recording is locked to the recorder's view because entities outside his
    /// visibility were never transmitted — the refusal names that, in the log and the status bar,
    /// because a key that silently does nothing reads as a broken key.
    ///
    /// **The headless instrument is exempt, and deliberately.** `TF2VIEW_CAMERA` places a camera
    /// for a capture or a measurement; refusing it would blind this project's own instruments on
    /// every point-of-view specimen. An explicitly placed camera is a measurement, not playback —
    /// the lock is about what a VIEWER offers, and the environment variable is not offered, it is
    /// typed.
    /// </remarks>
    private bool RefusedByTheDemo(CameraMode wanted)
    {
        if (Environment.GetEnvironmentVariable(FreeCameraController.CameraVariable)
            is { Length: > 0 })
        {
            return false;
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_spectator.Refuses(wanted) is not { } refusal)
        {
            return false;
        }

        _renderLog.LogWarning("{Message}", refusal.Message);
        _view.Say(refusal.Status);

        return true;
    }

    public bool ToggleFirstPerson()
    {
        // **The REQUESTED mode, not the effective one.** A dead target is drawn in third person, so
        // `_firstPerson` reads false while the user is still in the first-person mode — asking that
        // here would make this key try to ENTER a mode the user is already in, and leaving would
        // become impossible for the whole of a death.
        if (_cameraMode == CameraMode.FirstPerson)
        {
            // Leaving first person is entering the free camera, so the D128 lock applies to the
            // exit exactly as it does to the cycle: on a point-of-view recording there is nowhere
            // to leave TO.
            if (RefusedByTheDemo(CameraMode.Free))
            {
                return true;
            }

            _cameraMode = CameraMode.Free;
            _view.CameraChanged();
            _renderLog.LogInformation("{Message}", "first person off, back to the free camera");
            return true;
        }

        FirstPersonEntry entry = _spectator.Enter(_window().Tick, Aspect);

        if (!entry.Entered)
        {
            _renderLog.LogWarning("{Message}", entry.Message);
            _view.Say(entry.Status);
            return true;
        }

        _cameraMode = CameraMode.FirstPerson;
        _view.CameraChanged();

        _renderLog.LogInformation("{Message}", entry.Message);

        return true;
    }

    /// <summary>Whose eyes the first-person camera is in, or <c>null</c> when it is not in any.</summary>
    /// <remarks>
    /// **The same choice the camera makes, asked separately** — the camera needs a position and
    /// the renderer needs an entity to hide, and deriving one from the other would let them
    /// disagree. On a point-of-view demo it is the recorder; on a SourceTV demo it is whoever is
    /// being spectated.
    /// </remarks>
    public int? FollowedEntity() => _spectator.Followed(_window().Tick);

    /// <summary>The viewport's width over its height, which is all a camera needs from a window.</summary>
    /// <remarks>16:9 before the control has a size, so a camera built during construction is sane.</remarks>
    public float Aspect =>
        _window().Height > 0
            ? _window().Width / (float)_window().Height
            : 16f / 9f;

    /// <summary>The camera for the first-person view, or <c>null</c> when there is none.</summary>
    /// <remarks>
    /// **The field of view is the view's, as in the free camera**: `CalcInEyeCamView` and `CalcChaseCamView` both end with
    /// `fov = GetFOV()` (c_baseplayer.cpp:1609, :1751). Both cameras were built at the compiled-in 90 and ignored the setting.
    /// </remarks>
    public FreeCamera? FirstPersonCamera() =>
        _spectator.Eye(_window().ShownTick, Aspect)?.WithFieldOfView(ViewFovNow().Rendered(Aspect, FovRestricted()));

    /// <summary>The free camera, placed by the controller if nothing has placed it yet.</summary>
    /// <remarks>
    /// **All that is left here is the aspect ratio** (D90). Placing a camera, parsing a placement
    /// and framing a map are presenter work and moved to <see cref="FreeCameraController"/>; the
    /// viewport's width over its height is the only part that needs a window, and it is one float.
    /// </remarks>
    public FreeCamera FreeLookCamera()
    {
        // **Read from the settings every time, not captured once.** A config can be reloaded while
        // the viewer runs, and a field of view latched at construction would ignore it — which is
        // the shape of no-op this project keeps catching: the setting exists, the config is read,
        // and nothing downstream asks.
        // The controller holds the player's fov, clamped as `demo_fov_override` is; the widening is `CViewRender::Render`'s,
        // applied to the finished view, so it goes on after (B518 — set through the clamp, 106° came back as 90°).
        ViewFov fov = ViewFovNow();
        _freeCamera.FieldOfView = fov.World;

        return _freeCamera.Camera(
            Math.Max(1, _window().Width) / (float)Math.Max(1, _window().Height),
            _loaded?.Outline,
            _loaded?.HeightRange is { } range ? range.Highest : 0f).WithFieldOfView(fov.Rendered(Aspect, FovRestricted()));
    }

    /// <summary>Follows the next or previous player.</summary>
    /// <remarks>
    /// **Only while spectating, which is the gate the game applies too.** `spec_next` does nothing
    /// unless `GetObserverMode() > OBS_MODE_FIXED`, and in the free camera the left button is
    /// already the look-around drag — cycling on it would fight the gesture.
    ///
    /// **A cycle that finds nobody leaves the camera where it is**, following
    /// `if ( target ) SetObserverTarget( target );`. The first seconds of a competitive match really
    /// are SourceTV alone, and a click then must not blank the view.
    ///
    /// **This read `_firstPerson` until B289, which excluded the chase view** — the one mode where
    /// choosing a subject matters most, since you can see who you are following. The gate is a
    /// range in the engine and was an equality here, so third person accepted the click and threw
    /// it away. Found by the owner using it, not by any test: every unit test of the search passed,
    /// because the search was never the part that was wrong.
    /// </remarks>
    public void CycleTarget(bool reverse)
    {
        // **Following somebody is the window's condition, not the spectator's.** Which player comes
        // next is a fact about the roster whether or not anyone is watching; that this key does
        // nothing in the free camera is a decision about this UI, recorded on `_followingAPlayer`.
        if (!_followingAPlayer)
        {
            return;
        }

        SpectatorSwitch switched = _spectator.Cycle(_window().Tick, reverse);

        _spectateLog.LogDebug("{Message}", switched.Message);

        if (!switched.Switched)
        {
            return;
        }

        _view.CameraChanged();
    }

    /// <summary>Who to follow and whether the director chooses: the launch option and the watcher's `spec_autodirector`.</summary>
    /// <param name="spectating">The entity named on the command line, or null.</param>
    /// <param name="autoDirector">The watcher's `spec_autodirector` (B535).</param>
    public void Spectate(int? spectating, bool autoDirector)
    {
        _spectator.Spectating = spectating;
        _spectator.AutoDirector = autoDirector;
    }

    /// <summary>Whose eyes the camera is in at a tick other than the transport's: the HUD's, during a seek.</summary>
    /// <param name="tick">The tick asked about.</param>
    public int? Followed(int tick) => _spectator.Followed(tick);

    /// <summary>A demo is becoming the open one: the camera for its kind, `--spectate`, the server's flight speeds.</summary>
    /// <param name="spectateWho">`--spectate`'s text — a name, a user id or an entity index — or null.</param>
    /// <remarks>Called by the load, after it holds the demo and before the demo's systems open.</remarks>
    public void DemoOpening(string? spectateWho)
    {
        // **A point-of-view demo HAS one camera, so it opens on it** (D153). The recorder's own
        // view is in the file — `democmdinfo_t` on every packet — and it is what that player saw
        // alive and dead alike, because a POV recording keeps rolling through the deathcam and
        // through whoever they spectated afterwards. Opening such a demo on a free camera offers a
        // choice the recording does not contain.
        //
        // **A SourceTV recording has no such view** and keeps the free camera, because there the
        // choice is real: eighteen players and no recorder.
        //
        // **The headless instrument is exempt here too** (B378): `TF2VIEW_CAMERA` is a placed camera, as at every other
        // lock site (`RefusedByTheDemo`, `DemoOpened`). Without this a POV capture placed the free camera, logged it, and
        // photographed the recorder's eyes.
        if (_timeline?.HasRecordedView == true &&
            Environment.GetEnvironmentVariable(FreeCameraController.CameraVariable) is not { Length: > 0 })
        {
            _cameraMode = CameraMode.FirstPerson;
        }

        // **A name is resolved HERE because this is where the roster exists.** `--spectate` accepts
        // a player name, a user id or an entity index, and only the demo can say which — so the
        // text is carried from the command line and turned into an entity once the file is read.
        if (spectateWho is { Length: > 0 } who && _timeline is { } loaded)
        {
            if (loaded.EntityForPlayer(who) is { } chosen)
            {
                _spectator.Spectating = chosen;

                // **Naming somebody is asking to WATCH them**, which is the option's whole purpose:
                // *"allow the input of a player name or id to first person cam a specific player on
                // boot"*. Setting the target and leaving the camera overhead answers half of it and
                // shows the map from above, which is what it did.
                _cameraMode = CameraMode.FirstPerson;
            }
            else
            {
                // Not silent: spectating somebody else because a name was mistyped is a recording
                // of the wrong person, which is worse than refusing.
                _log.LogWarning(
                    "{Message}", $"--spectate {who} matches nobody in this demo; choosing normally");

                // **A capture of the wrong subject is not a capture** (2026-10-11): headless, this ends the run —
                // stderr, exit 1 — as a demo that will not open does. A person's window keeps the warning.
                _view.FailHeadless($"--spectate {who} matches nobody in this demo (`roster <demo>` lists who is in it)");
            }
        }

        // **The free camera flies at the SERVER's speeds** (D106). `sv_maxspeed * sv_specspeed` is
        // what TF2's roaming spectator clamps to, both are replicated, and a mod that changes
        // movement sends the new values in this demo. One assignment, here, because this is where
        // the timeline arrives — and a camera whose speed is decided anywhere else is the shape
        // B193 collected six of.
        // A demo whose timeline failed to build keeps Valve's defaults, which is what the engine
        // would use for a server that changed nothing — and the alternative, carrying the PREVIOUS
        // demo's server, would be silently wrong rather than merely vanilla.
        _freeCamera.SetServer(_timeline?.ServerConVars ?? FreeFlightPath.Shipped);
    }

    /// <summary>The demo's systems are open: a demo that refuses the free camera enters its recorder's eyes.</summary>
    public void DemoOpened()
    {
        // **A point-of-view recording opens through the recorder's eyes** (D128). TF2's playback
        // of a POV demo is the recorded view and nothing else, so the free camera this viewer
        // normally starts in does not exist for the demo kind — entering first person at load is
        // not a preference, it is the only camera the file can answer. Through `EnterCamera` so
        // the entry is logged and the target acquired exactly as a keypress would. The headless
        // instrument (`TF2VIEW_CAMERA`) keeps the free camera, the same exemption every lock site
        // honours.
        if (_spectator.Refuses(CameraMode.Free) is { }
            && _cameraMode != CameraMode.FirstPerson
            && Environment.GetEnvironmentVariable(FreeCameraController.CameraVariable)
                is not { Length: > 0 })
        {
            _ = EnterCamera(CameraMode.FirstPerson);
        }
    }

    /// <summary>A wheel notch: in the free camera it flies forward or back; with no map drawn it does nothing.</summary>
    /// <param name="forward">Whether the wheel turned away from the user.</param>
    public void Wheel(bool forward)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_loaded is not { } shown || shown.Outline.IsEmpty)
        {
            return;
        }

        // In the free view the wheel moves the camera in and out instead of magnifying a flat map.
        // The near limit is a little over a player's height, so a model can be filled the frame
        // with without the near plane cutting into it.
        // In the free view the wheel flies forward and back, which is what a wheel does in every
        // editor and is far quicker than tapping W across a map.
        if (_freeLook)
        {
            // **The whole branch is one call now** (B204, B206). It was a hand-inlined copy of
            // `AngleVectors`' forward vector — the fourth in this repository — multiplied by a
            // travel distance spelled `FlySpeed * 4f`. How far a notch flies and along which vector
            // are both camera rules; that the WHEEL is what asks is the part this window owns.
            _freeCamera.Dolly(forward, FreeLookCamera().Origin);

            _view.CameraChanged();
        }
    }

    /// <summary>A drag, in pixels since the last one: it turns the free camera, when a map is drawn.</summary>
    /// <param name="deltaX">Pixels right.</param>
    /// <param name="deltaY">Pixels down.</param>
    /// <returns>Whether the camera turned; the window then moves its drag origin and re-projects.</returns>
    public bool Drag(float deltaX, float deltaY)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_loaded is not { } shown || shown.Outline.IsEmpty)
        {
            return false;
        }

        // **A drag turns the camera.** Pitch is clamped by the camera itself, at the same 89 degrees
        // the engine clamps a player to, because the basis is degenerate looking exactly along the
        // world's up axis.
        //
        // **The `if (_freeLook)` around this went with the pan branch on 2026-08-26** (D98). The
        // other arm slid the orthographic map by converting pixels to world units through
        // `WorldUnitsPerPixel`; with one camera left there is no second thing for a drag to mean.
        _freeCamera.Drag(deltaX, deltaY);

        return true;
    }

    /// <summary>The reset-camera action: back to the overhead placement, unless the demo refuses the free camera.</summary>
    /// <returns>Always true: the key was the camera's.</returns>
    public bool ResetCamera()
    {
        // The reset lands in the free camera, so the D128 lock applies here too: on a
        // point-of-view recording "show me the whole map" asks for a map that was never
        // transmitted.
        if (RefusedByTheDemo(CameraMode.Free))
        {
            return true;
        }

        // **F now RESETS the camera to the overhead placement rather than switching mode.**
        // It used to toggle between the map view and the free camera; with the orthographic
        // camera gone (D49) there is no second mode to switch to, and the overhead view is a
        // placement of this one. So the key keeps its meaning — "show me the whole map again" —
        // and drops the mode it used to carry.
        _cameraMode = CameraMode.Free;
        _freeOrigin = null;

        // A key still recorded as held would move the camera the instant it is re-placed.
        _view.ReleaseHeldKeys();

        _view.CameraChanged();

        // **Says what it did, not which mode it is in.** The old line reported "free camera on"
        // or "free camera off, back to the map view", and both are now false: there is one
        // camera and this key does not switch anything. A log that names the wrong quantity
        // misdirects with authority (`docs/memory/logs-are-the-debugger.md#a-log-must-name-what-it-measured`).
        // **Information rather than Debug since 2026-08-26.** It is a thing the user did and the
        // picture jumps because of it, which is the same class as "wrote shot-…" beside it — and
        // at Debug the app discarded it, so nothing outside a debugger could ever tell whether
        // the key arrived (`docs/memory/logs-are-the-debugger.md#a-log-level-regression-is-invisible-to-unit-tests`).
        // Found because a UI test asserted on this line and watched a line that is never written.
        _renderLog.LogInformation("{Message}", "camera reset to the overhead placement");

        return true;
    }

    /// <summary>Flies the free camera by one frame of input; in any other mode the input is not read.</summary>
    /// <param name="intent">Reads this frame's flight input, which consumes its impulse bits: asked at most once.</param>
    /// <param name="seconds">How long the last frame took.</param>
    /// <returns>Whether the camera moved.</returns>
    public bool Fly(Func<FlightInput> intent, double seconds)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!_freeLook)
        {
            return false;
        }

        // **`Intent` is read exactly once per frame, and that is a requirement rather than a
        // convenience.** Reading a button consumes its impulse bits — Valve's `CInput::KeyState`
        // ends with `key->state &= 1` — so a second read in the same frame reports a plain held
        // button and loses the partial credit a tapped key earns. This is the only call site.
        //
        // **The old "nothing is held, so skip" guard is gone with it.** A key pressed and released
        // between two frames is up by the time anyone looks, so that guard would have thrown the
        // tap away; `Fly` returning false still costs nothing when genuinely idle.
        return _freeCamera.Fly(intent(), seconds, FreeLookCamera().Origin);
    }

    /// <summary>This frame's <c>cl_showpos</c> subject, gathered from the camera and the demo.</summary>
    /// <returns>The readout; hidden when the convar is off.</returns>
    /// <remarks>
    /// **Gathering only — every rule about what is SHOWN lives in <see cref="PositionReadout"/>**
    /// (D55). This picks up the four values the engine's own `Paint` picks up and hands them over;
    /// which of them reaches the screen is the readout's decision, not a window's.
    ///
    /// **The angles for mode 2 are the player's BODY angles**, which is what
    /// <c>GetAbsAngles()</c> returns for a player: pitch and roll are zero and the yaw is the
    /// entity's. The eyes are a separate question the engine answers with <c>EyeAngles()</c>, and
    /// <c>cl_showpos</c> does not ask it — so `EyePitch` is deliberately not substituted here even
    /// though this project decodes it.
    ///
    /// **`vel` is a DIVERGENCE and it is forced by the data.** Valve reports
    /// <c>GetLocalVelocity().Length()</c>, a 3D velocity the server sends; `m_vecVelocity` lives in
    /// <c>DT_LocalPlayerExclusive</c>, so a SourceTV recording carries nobody's and a POV recording
    /// carries only the recorder's. `ScenePlayer.Speed` is differenced from position over a tenth
    /// of a second and is HORIZONTAL — see `DemoTimeline.SpeedAt` — so this line is that number,
    /// which is the same quantity the animation system already runs on. It is close for a player
    /// running and reads low for one falling.
    /// </remarks>
    public PositionReadout ReadPosition()
    {
        int mode = _settings.ShowPosition;

        if (mode <= PositionReadout.Hidden)
        {
            // **Returned early rather than built and discarded**, because the camera below is not
            // free: `ViewCameraNow` resolves the spectator target and the free camera's placement.
            // A viewer that never switches this on should pay nothing for it.
            return default;
        }

        FreeCamera camera = ViewCameraNow(0d);
        ScenePlayer? watched = _spectator.Viewed(_window().Tick);

        return new PositionReadout(
            mode,
            camera.Origin,
            camera.Angles,
            watched is { } player ? (player.X, player.Y, player.Z) : default,
            watched is { } angles ? (0f, angles.Yaw, 0f) : default,
            watched?.Speed ?? 0f);
    }
}
