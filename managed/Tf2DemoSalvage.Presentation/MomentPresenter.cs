using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a window knows about a moment that the timeline does not.</summary>
/// <param name="CurrentTick">Where the transport says playback is.</param>
/// <param name="FirstPerson">Whether the view is through a player's eyes.</param>
/// <param name="Followed">The entity being spectated, or null.</param>
/// <param name="Eye">That player's camera, or null when nobody's eyes are available.</param>
/// <param name="ViewmodelFieldOfView">The user's viewmodel field of view, in degrees.</param>
/// <param name="DrawViewmodel">
/// Whether the weapon in hand is drawn — Valve's <c>r_drawviewmodel</c> (B166). A setting like the
/// field of view beside it, defaulted true because Valve ships it at <c>"1"</c>.
/// </param>
/// <param name="Playing">
/// Whether playback is running rather than paused — <c>engine-&gt;IsPaused()</c>, which clears
/// <c>s_bInterpolate</c> for every entity at once (<c>c_baseentity.cpp:3226</c>) so a paused client
/// draws last-received positions with no <c>cl_interp</c> delay (B399).
/// </param>
/// <remarks>
/// **Five values, and every one of them is genuinely the window's.** The camera mode is a UI state,
/// the transport tick is a control's position, the eye needs the viewport's aspect, and the field of
/// view is a setting. Everything else a moment needs — who is where, at what tick rate — belongs to
/// the recording and is read from <see cref="IMomentSource"/>.
/// </remarks>
/// <param name="FlipViewModels">The watcher's own <c>cl_flipviewmodels</c> (B515).</param>
/// <param name="UseMinViewmodels">The watcher's own <c>tf_use_min_viewmodels</c> (B525).</param>
/// <param name="LightAverage">The watcher's own <c>r_lightaverage</c> (B676).</param>
/// <param name="LightInterp">The watcher's own <c>r_lightinterp</c> (B676).</param>
/// <param name="WorldLights">The watcher's own <c>r_worldlights</c> (B676).</param>
/// <param name="AmbientBoost">The watcher's own <c>r_ambientboost</c> and its two thresholds (B672); null is their defaults.</param>
/// <param name="RealTime">`gpGlobals->realtime`, the window's wall clock, which jiggle bones step on (B615); null is none.</param>
public readonly record struct MomentView(
    int CurrentTick,
    bool FirstPerson,
    int? Followed,
    FreeCamera? Eye,
    float ViewmodelFieldOfView,
    bool DrawViewmodel = true,

    // **Whether playback is RUNNING, which decides whether anything is interpolated at all** —
    // `engine->IsPaused()` clears `s_bInterpolate` for every entity at once
    // (`c_baseentity.cpp:3226`), and a paused client then draws last-received positions (B399).
    // Genuinely the window's, like the transport tick beside it: the recording cannot know whether
    // someone has pressed pause.
    bool Playing = true,

    // The watcher's `cl_flipviewmodels` (B515), a setting like `r_drawviewmodel`.
    bool FlipViewModels = false,

    // The watcher's `tf_use_min_viewmodels` (B525), the same kind of thing.
    bool UseMinViewmodels = false,

    // The watcher's `r_lightaverage` and `r_lightinterp` (B676), the same kind of thing.
    bool LightAverage = true,
    float LightInterp = 5f,
    int WorldLights = 4,

    // The watcher's `r_ambientboost` and its two thresholds (B672); null is their defaults.
    AmbientBoostSettings? AmbientBoost = null,

    // `gpGlobals->realtime`: the window's wall clock, which the jiggle bones step on (B615); null is none.
    double? RealTime = null);

/// <summary>Samples a moment from the demo and hands it to the scene.</summary>
/// <remarks>
/// **This was `MainForm.ShowMoment` and the two buffers it filled** (B188, D90). The owner's
/// question is what found it — *"does the view need to hold them to pass them on?"* — and the answer
/// was no: the window held a `DemoTimeline`, a `List&lt;ScenePlayer&gt;` and a `List&lt;SceneProp&gt;`
/// only because the sampling happened there.
///
/// **It had no tests and could not have had any.** `DemoTimeline`'s constructor is private and
/// `Build` takes the bytes of a real file, so anything sampling one directly needs a demo shipped
/// into the test project and, here, a window to hold it. `IMomentSource` is what changes that.
///
/// **One path from "which moment" to "what is drawn".** Scrubbing and playing both come through
/// here, so the two cannot disagree about what a tick looks like — which they did once, when
/// playback and the scrub bar each built the scene their own way.
/// </remarks>
public sealed class MomentPresenter
{
    private readonly MomentScene _moment;
    private readonly FrameLedger _ledger;
    private readonly ILogger _render;

    /// <summary>Averages the rebuild cost, since the threshold report cannot see this rate.</summary>
    private readonly MomentCostLog _cost = new();

    /// <summary>What <see cref="Show"/> measured, waiting for <see cref="PoseNow"/> to complete it.</summary>
    private MomentPhases _built;

    /// <summary>The moment <see cref="_built"/> belongs to, or null before the first build.</summary>
    private MomentInfo? _builtFor;

    /// <summary>What the last <c>Show</c> told the moment, for a test.</summary>
    internal MomentInfo? BuiltFor => _builtFor;

    /// <summary>Sampling ticks for the build being held, reported with the pose.</summary>
    private long _sampled;

    /// <summary>What PlayersAt cost, of the sampling total, for B258.</summary>
    private long _playerTicks;

    /// <summary>Whether the held build has already been posed, so a repaint does not pose it again.</summary>
    private bool _posed;

    // **`_rebuilt` was here until B385**, guarding the first frame against an empty interpolation set
    // so that a cold start interpolated everything. There is no set to be empty any more: the source
    // answers `ShouldInterpolate` from the recording, and a freshly created entity answers it the same
    // way on its first frame as on its thousandth. The first-frame special case went with the field.

    /// <summary>The players at a moment, refilled each time rather than reallocated.</summary>
    private readonly List<ScenePlayer> _players = [];

    /// <summary>
    /// The players the last frame was built from — interpolated while playing, last received while paused — for an
    /// effect that follows one, which must stand where he was drawn (B523). Refilled by <see cref="Show"/>.
    /// </summary>
    public IReadOnlyList<ScenePlayer> Players => _players;

    /// <summary>The whole tick the last frame showed; null before any frame.</summary>
    private int? _shownTick;

    /// <summary>
    /// <see cref="Players"/> when the last frame showed this tick, else null — for a trace that must meet a player
    /// where his hitboxes were posed, and a replay over other ticks that must not be handed this frame's (B526).
    /// </summary>
    /// <param name="tick">The whole tick asked about.</param>
    /// <returns>The frame's players, or null when the frame was of another tick or there has been none.</returns>
    public IReadOnlyList<ScenePlayer>? PlayersShownAt(int tick) => _shownTick == tick ? _players : null;

    /// <summary>The props at a moment, refilled each time.</summary>
    private readonly List<SceneProp> _props = [];

    /// <summary>Wires a presenter to the scene it fills.</summary>
    /// <param name="moment">The scene being assembled.</param>
    /// <param name="ledger">Where the per-second frame counters live.</param>
    /// <param name="render">The render log, for slow-step reports.</param>
    /// <exception cref="ArgumentNullException">A collaborator is null.</exception>
    public MomentPresenter(MomentScene moment, FrameLedger ledger, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(moment);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(render);

        _moment = moment;
        _ledger = ledger;
        _render = render;
    }

    /// <summary>The demo being shown, or null when none is open.</summary>
    /// <remarks>
    /// **Set when a demo opens, beside `SpectatorView.Eyes` and `MomentScene.Viewmodels`** — the
    /// two sources this one joins. `DemoSystems.Open` sets all three, so a demo that fails to decode
    /// clears every one of them rather than leaving one behind pointing at the previous recording.
    /// </remarks>
    /// <remarks>
    /// **And it hands the scene the side-effect-free player query** a seek needs to recover when each activity began
    /// (D233); a demo opened without it would key every roll on the landing tick.
    /// </remarks>
    public IMomentSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            _moment.EarlierPlayer = value is null ? null : value.ActivityPlayerAt;
            _framedAt = null;
        }
    }

    private IMomentSource? _source;

    /// <summary>The tick the last frame was shown at, or null before the first of this source (B860).</summary>
    private double? _framedAt;

    /// <summary>The tick this frame's <c>FRAME_START</c> had read through (B860).</summary>
    private double _frameRead;

    /// <summary>Where the players' appearance comes from, or null when nothing can build one.</summary>
    /// <remarks>
    /// **This was `MainForm.EnsureWeaponRoles`** (B188, D90): one line in the window, called every
    /// frame, reaching for the timeline and the game install. It is a property rather than a
    /// constructor argument because the appearance depends on two things with different lifetimes —
    /// a demo, and an install located later — so there is no moment at which a finished one could be
    /// handed over.
    /// </remarks>
    public IAppearanceSource? Appearances { get; set; }

    /// <summary>The tick interval used by the last <see cref="Show"/>, for tests.</summary>
    /// <remarks>
    /// **Exposed because the alternative was worse.** The interval reaches `MomentScene` inside a
    /// `MomentInfo` that nothing hands back, so asserting it would otherwise need a fake scene —
    /// a second implementation of a large class, to observe one float.
    /// </remarks>
    public float LastInterval { get; private set; }

    /// <summary>What the last <see cref="Show"/> spent sampling, and its build's phases, in stopwatch ticks.</summary>
    public (long Sampled, MomentPhases Built) LastBuild => (_sampled, _built);

    /// <summary>The most recent rebuild-cost line, or null before one has been produced.</summary>
    /// <remarks>For <c>--measure</c>. Read and cleared by the caller so a line is printed once.</remarks>
    public string? LastCost { get; set; }

    /// <summary>
    /// While true, a moment is built from the players alone — no world prop, projectile, building or worn item. For a
    /// seek's model-decal replay, which needs only the struck player's bones: his body comes from the player, and his
    /// sequence from the timeline, so nothing it poses depends on a prop.
    /// </summary>
    public bool PlayersOnly { get; set; }

    /// <summary>Shows the moment at a tick.</summary>
    /// <param name="tick">The moment, which may fall between ticks.</param>
    /// <param name="view">What the window knows and the recording does not.</param>
    /// <remarks>
    /// **Silent with no source**, because the render loop calls this without asking whether a demo
    /// is open — a resize or a repaint arrives before anything is loaded.
    /// </remarks>
    public void Show(double tick, in MomentView view)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'source'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (Source is not { } source)
        {
            return;
        }

        // **Before the sampling and outside both timers, which is where it was and where it
        // belongs.** It is free after the first call, but the FIRST reads the weapon scripts out of
        // the archives and each one costs an ICE decryption — so counting it as sampling reports one
        // enormous `sampling` spike for work that is not sampling, and counting it as posing moves
        // the same lie one column along.
        //
        // **The return value IS the wiring.** The version of this that wrote into
        // `MomentScene.Appearance` as a side effect is the one that shipped B193 — every weapon
        // suffix answering null and every player animating in the generic primary pose, with the
        // suite green.
        if (Appearances is { } appearances)
        {
            _moment.Appearance = appearances.Ensure(_moment.Appearance);
        }

        // **Timed because three untimed steps once hid 129 ms of a 133 ms pose** (B191). The column
        // moved out of the window with the sampling it measures.
        long sampledAt = Stopwatch.GetTimestamp();

        // **`FRAME_START` comes before the frame's packets are read** (B860): `CEngine::Frame` notifies the client before
        // the host frame's `CL_ReadPackets`, so a delay change read this frame is interpolated under from the next. While
        // playing forward the frame start had read what the last frame showed. A second build at the same tick is the
        // same frame, with the same frame start.
        // A paused frame has read everything to its tick — a scrub while paused is a skip its own frames finish — and
        // interpolates nothing (B399); so playing on from it starts there (B860 review 3, Fable). Anything else is a jump — a
        // rewind, the first frame of a demo opened part-way — which the engine reads as a skip, a hundred packets a frame:
        // negative infinity hands that to the source (review 2, Sonnet).
        if (!view.Playing)
        {
            _frameRead = tick;
            _framedAt = tick;
        }
        else if (_framedAt is not { } shown || shown < tick || shown > tick)
        {
            _frameRead = _framedAt is { } previous && previous < tick ? previous : double.NegativeInfinity;
            _framedAt = tick;
        }

        source.FrameStart(_frameRead, tick);
        source.PlayersAt(tick, _players, view.Playing);
        _shownTick = (int)Math.Floor(tick);

        // **Split because the whole of B258 rests on which half this is**, and it was about to be
        // assumed. `sample` is 2.0 ms of a 5.2 ms rebuild, and the plan — interpolate only what was
        // visible last frame, as `ShouldInterpolate` does — pays off against six hundred PROPS and
        // barely at all against two dozen players, who are nearly all visible anyway.
        long playersAt = Stopwatch.GetTimestamp();

        // **The view entity, and nothing else** (B385). This passed `MomentScene.PosedEntities` —
        // the entities that reached `SetupBones` last frame — on the belief that `IsVisible()`
        // reports the previous render. It does not: it is `m_hRender != INVALID_CLIENT_RENDER_HANDLE`
        // (`c_baseentity.h:691`), which `UpdateVisibility` sets from `ShouldDraw() && !IsDormant()`
        // and which no cull, no frame and no skeleton takes part in. The set was therefore wrong for
        // three whole populations at once — brush entities, which have no bones and so were never in
        // it, so no door in any map was ever interpolated; everything the frustum rejected; and, on
        // any frame with a viewmodel, the entire world, because that pass calls `Instances` again and
        // it clears the set.
        //
        // The source answers the other three clauses from the recording. Only
        // `render->GetViewEntity()` needs the window, because only the window knows who is followed.
        // **Paused is a different sample, not the same one frozen** (B399): the engine clears
        // `s_bInterpolate` while paused (`c_baseentity.cpp:3226`), so every entity draws its last
        // received position with no `cl_interp` delay. A `--shot` capture is a paused frame.
        if (PlayersOnly)
        {
            _props.Clear();
        }
        else
        {
            source.PropsAt(tick, _props, view.Followed, view.Playing);
        }

        long sampleTicks = Stopwatch.GetTimestamp() - sampledAt;

        _playerTicks = playersAt - sampledAt;

        _ledger.Sampled(sampleTicks);

        LastInterval = source.IntervalPerTick;

        MomentInfo info = new(
            tick,
            view.CurrentTick,
            view.FirstPerson,
            view.Followed,
            view.Eye,
            LastInterval,
            view.ViewmodelFieldOfView,
            view.DrawViewmodel,

            // From the recording, not from the window: the round is something the demo knows
            // and the viewer cannot derive.
            source.RoundStateAt(tick),

            // The same, and for the same reason — who recorded it decides whose vision filters
            // the scene is drawn through (B354).
            source.Recorder,
            source.HolidayVisionFlagsAt(tick),
            source.ServerTimeAt(tick),
            source.HalloweenScenarioAt(tick),
            view.FlipViewModels,
            view.UseMinViewmodels,
            view.Playing,
            view.LightAverage,
            view.LightInterp,
            view.WorldLights,
            view.AmbientBoost,
            source.DormantPlayersAt(tick),
            view.RealTime,
            EntityDormantAt(source, tick))
        {
            // Over the same source and tick as `EntityDormant` (B69 review 6), made once.
            DormantSince = _dormantSince ??= entity => _dormantOf.Source.DormantSinceAt(entity, _dormantOf.Tick),
        };

        // **Selection only. The pose is `Pose`, and it runs after the camera** (B255).
        _built = _moment.Build(_players, _props, info);
        _builtFor = info;
        _sampled = sampleTicks;
        _posed = false;
        _reposing = false;
    }

    /// <summary>The source and tick <see cref="_entityDormant"/> answers for, this build's.</summary>
    private (IMomentSource Source, double Tick) _dormantOf;

    /// <summary>Any entity's dormancy at <see cref="_dormantOf"/>, made once (B69 review: no closure per <c>Show</c>).</summary>
    private Func<int, bool>? _entityDormant;

    /// <summary>Any entity's dormant spell's start at <see cref="_dormantOf"/>, made once, as <see cref="_entityDormant"/>.</summary>
    private Func<int, int?>? _dormantSince;

    /// <summary>
    /// The moment's <c>IsDormant()</c> for any entity, asked of the source at the tick shown. One delegate over the build's
    /// source and tick: a build replaces the last, so the moment holding the delegate is always the one it answers for.
    /// </summary>
    private Func<int, bool> EntityDormantAt(IMomentSource source, double tick)
    {
        _dormantOf = (source, tick);

        return _entityDormant ??= entity => _dormantOf.Source.IsDormantAt(entity, _dormantOf.Tick);
    }

    /// <summary>A new render frame: the moment held from an earlier frame is posed again, at this frame's real time (B744).</summary>
    /// <param name="realTime">`gpGlobals->realtime` now; null is none.</param>
    /// <remarks>
    /// **The engine rebuilds every skeleton every frame, paused or not.** `OnRenderStart` runs `InvalidateBoneCaches` each
    /// render frame (`cdll_client_int.cpp:2163`), so `SetupBones` sees `m_iMostRecentModelBoneCounter != g_iModelBoneCounter`
    /// and builds again (`c_baseanimating.cpp:2874-2911`). Nothing on that path tests for a pause. A paused frame's
    /// animation stands still because `curtime` does, but the jiggle bones step on `gpGlobals->realtime`
    /// (`c_baseanimating.cpp:1586`) and settle. Posing a held build once per build, as this did, froze them mid-swing.
    ///
    /// No build ran for a re-pose, so it is not reported as one: the cost line's mean is over rebuilds (B243), and a pose
    /// alone is not a rebuild. A playing frame's `Show` replaces the held build before the pose, which drops the re-pose
    /// unread. A real time that cannot be read keeps the last one, so the jiggle clock does not fall to demo seconds.
    /// </remarks>
    public void NextFrame(double? realTime)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'info' unassigned (CS0165), and Safe Mode then
        // drops every mutation in this method — B410.
        if (_builtFor is not { } info || !_posed)
        {
            return;
        }

        _repose = realTime ?? info.RealTime;
        _reposing = true;
        _posed = false;
    }

    /// <summary>Whether the next <see cref="PoseNow"/> re-poses the held build rather than posing a new one (B744).</summary>
    private bool _reposing;

    /// <summary>The real time a re-pose runs at.</summary>
    private double? _repose;

    /// <summary>Poses the moment already built, once the view for this frame exists.</summary>
    /// <param name="frustum">The view being drawn, for the cull that precedes the pose (B254).</param>
    /// <param name="views">The views' leaf lists the scene collates over before posing (B262).</param>
    /// <remarks>
    /// **Called after `PlaceCamera` and not from `Show`**, because the engine computes the view
    /// before it decides what is visible and long before it sets up any bones — `SetUpView`, then
    /// `BuildWorldLists`, then `BuildRenderablesList`, then the draw that reaches `SetupBones`
    /// (B255). Posing inside `Show` meant the newest frustum available was the previous frame's.
    ///
    /// **Silent when there is nothing built**, which is every frame before a demo is open and every
    /// frame a resize arrives on — the same reason <see cref="Show"/> is.
    ///
    /// **Guarded against posing twice for one build**, because the frame loop is not the only caller
    /// of a repaint: a paused viewer draws repeatedly off one moment, and posing again per draw is
    /// exactly the per-frame cost this change exists to remove.
    /// </remarks>
    /// <param name="eye">
    /// <c>CurrentViewOrigin()</c> — where this frame is drawn from, in EVERY camera mode (B365).
    /// It arrives here rather than in <see cref="MomentInfo"/> because the view does not exist when
    /// the moment is built, and it comes from the device so that it is the same origin the frustum
    /// beside it was built from.
    /// </param>
    /// <param name="screen">The main view for the static props' screen fades (B432), from the same device.</param>
    /// <param name="monitor">Whether a monitor can draw this frame, so the first-person player is worth posing (B511).</param>
    public void PoseNow(
        ViewFrustum frustum = default,
        RenderableViews views = default,
        (float X, float Y, float Z)? eye = null,
        ScreenFadeView? screen = null,
        bool monitor = true)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'info'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (_builtFor is not { } info || _posed)
        {
            return;
        }

        _posed = true;

        // A re-pose of the held build (B744): the frame's real time, and no rebuild to report.
        if (_reposing)
        {
            _reposing = false;
            info = info with { RealTime = _repose };
            _builtFor = info;
            _ledger.Posed(_moment.Pose(info, frustum, views, eye, screen, monitor).Pose);

            return;
        }

        MomentPhases posing = _moment.Pose(info, frustum, views, eye, screen, monitor);

        // **One line per rebuild, not two.** The two halves are measured apart and read together;
        // reporting each on its own would put `advance`'s parts in separate lines that a reader has
        // to add up, and the residual columns would stop meaning anything.
        MomentPhases phases = _built with
        {
            Total = _built.Total + posing.Total,
            Pose = posing.Pose,
            Weapons = posing.Weapons,
            Viewmodel = posing.Viewmodel,
            ViewmodelCounters = posing.ViewmodelCounters,
            ViewmodelSplit = posing.ViewmodelSplit,
            Counters = posing.Counters,

            // **From the POSE's record, because only the pose has seen the frustum.** Taking these
            // from `_built` reported the selection count as though it were the survivor count and
            // the survivor count as zero — a line that read "posed 600 of 0 selected" and was
            // believed for one measurement. `docs/memory/instrument-bugs-outnumber-decoder-bugs.md#log-the-event-not-a-sample-of-it`'s
            // sibling rule: report the value the code USED, carried to it, never the one a
            // neighbouring record happens to hold.
            Drawn = posing.Drawn,
            LightAveraged = posing.LightAveraged,
            Selected = posing.Selected,
            Hidden = posing.Hidden,

            // **And `Unjudgeable`, which this list omitted while the comment above described
            // exactly why it must not.** `Build` never names it, so `_built` carried the record's
            // default of zero and the printed column read `0 unjudgeable` on every frame however
            // many props had no usable bounds — the counter existing, the pose carrying it
            // correctly, and the one line that reads it silently dropping it. The same defect the
            // three fields above were fixed for, one field short of finished.
            Unjudgeable = posing.Unjudgeable,
            Undrawn = posing.Undrawn,

            // B594's census: the first run of it read all zeros, world sun included, because this list omitted it.
            UndrawnCensus = posing.UndrawnCensus,
        };

        _ledger.Posed(phases.Pose);

        StallReport.Moment(phases, _sampled, _playerTicks, _render);

        // **Every rebuild, averaged — the line above fires only past 30 ms and this runs at 8.**
        // `advance` is seventy per cent of the frame at the rate this actually plays, and until this
        // existed nothing said which part of it.
        if (_cost.Report(phases, _sampled, _playerTicks) is { } mean)
        {
            _render.LogInformation("{Message}", mean);

            // **Kept for `--measure`, which prints to stdout because the log is buffered.** The
            // rebuild breakdown is the more useful half of a measurement and was missing from it.
            LastCost = mean;
        }
    }
}
