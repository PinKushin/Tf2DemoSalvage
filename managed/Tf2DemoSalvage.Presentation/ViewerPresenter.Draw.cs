using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <content>
/// What the owner of a load decides about the DRAW: whether the world needs uploading and what follows an upload, the
/// moment's view, the leaf outline, the screen overlay (D214). The device calls stay the window's; each member here
/// is the decision that stood beside one [no engine counterpart: the engine's renderer and its window are one program].
/// </content>
public sealed partial class ViewerPresenter
{
    // `_texturesUploaded` was here until 2026-08-26. It is `WorldPresenter.TexturesAreCurrent`,
    // beside the code that reads it (B188, D90).
    //
    // **It has to exist at all because `HasWorldTextures` answers a different question.** The device
    // knows whether textures are RESIDENT, which stays true across a map change — they are simply
    // the wrong ones. Only something that knows about levels can say "resident AND for this one".

    /// <summary>The world upload, and whether this level's textures are the resident ones.</summary>
    private readonly WorldPresenter _world;

    /// <summary>The leaf outline for <c>mat_leafvis</c>, and the warning it may write.</summary>
    /// <remarks>
    /// **Was `_reportedNoLeafBox`, a bool in this window** (B188, D90), whose only job was to stop a
    /// per-frame warning becoming a per-frame warning. The latch, the log and the three reasons it
    /// chooses between now sit with the outline they describe — the same arrangement `MomentScene`
    /// already uses for "no player appearance".
    /// </remarks>
    private readonly LeafBoxes _leafBoxes;

    /// <summary>Something a window knows moved the view: the world's projection is stale.</summary>
    public void InvalidateWorld() => _world.Invalidate();

    /// <summary>Whether the world asks to be projected again.</summary>
    public bool WorldNeedsProjecting => _world.NeedsProjecting;

    /// <summary>The world is about to be projected: it no longer asks.</summary>
    public void WorldProjected() => _world.Projected();

    /// <summary>Projects the map through a camera fitted to its own bounds.</summary>
    /// <remarks>
    /// Fitted to the MAP rather than to the players. A camera that reframes itself around wherever
    /// the players happen to be turns every scrub into a jump - the world should sit still while
    /// the players move within it.
    /// </remarks>
    /// <param name="surfaceColours">Whether the surface-colour view is on.</param>
    /// <param name="size">The viewport's size in pixels.</param>
    /// <param name="fromTheRead">Whether this is the map read's own projection (`ILoadView.MapRead`), which runs inside the barrier.</param>
    public void ProjectMap(bool surfaceColours, (int Width, int Height) size, bool fromTheRead = false)
    {
        // **Only the read itself projects while a map reads** (B779 reviews 27-28). The window's `OpenDevice` reaches here
        // on the UI thread with no barrier check, and projecting then runs the upload beside the worker's own on the same
        // map (its texture lists swapped under the upload) and lets go of model vertices the worker is appending to. It
        // marks the world instead, and the first frame after the load projects it. The read's own projection says so
        // (`fromTheRead`, review 28 item 1) rather than being guessed from its thread, which an await or an inlined task
        // could change.
        if (_readingMap && !fromTheRead)
        {
            _world.Invalidate();
            return;
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_loaded is not { } map || map.Outline.IsEmpty)
        {
            return;
        }

        // **The segment projection and the flat fill are gone, not skipped.** The first projected all
        // 60,764 of the BSP's edge segments for an overhead view that was removed; the second was
        // dead by construction, built FROM the map as a fallback for having no map. Together they
        // measured `project 615.8 ms` in a 679 ms frame, re-run on every camera change, for
        // triangles nothing would draw.
        //
        // **The early return that replaced them was a real bug, caught before it shipped.** Returning
        // here skips everything below, and everything below is the TEXTURED WORLD UPLOAD. Dropping
        // the fill must not drop the world.
        // **What is left here is the camera, the viewport and the status bar.** Deciding whether the
        // world needs uploading is a question about a level and about what already reached the GPU;
        // performing the upload needs a device, which arrives through `IWorldUpload` — the same seam
        // `IModelUpload` already uses, and the same reason: the decision becomes testable with a
        // fake, and the code that decides stops being the code that talks to Direct3D.
        //
        // **A `TopDownCamera` was passed alongside the matrix until 2026-08-26** (D98). It was
        // threaded down through `BuildWorld` into `MapWorld.Build`, which never read it — a leftover
        // of the top-down culling that broke the free camera once already. The matrix is what the
        // world is actually pointed with.
        WorldUpload result = _world.Project(
            map,
            _device,
            _camera.ViewMatrix(),
            surfaceColours,
            size,
            _loggers);

        FollowUpload(result, map);
    }

    /// <summary>What follows the world's upload: its problem said, the models' copies let go, a lost world read again.</summary>
    /// <param name="result">What the upload answered.</param>
    /// <param name="map">The map it uploaded.</param>
    internal void FollowUpload(WorldUpload result, LoadedMap map)
    {
        // A lost world says "Reloading the map…" only when it reloads now: with no demo nothing reloads (review 27 item 5),
        // and with a load in flight the reload waits and the load's own status ("Opening…") stays (review 28 item 3).
        bool reloadWaits = result.NeedsReload && (_demo is null || LoadInFlight);

        if (result.Problem is { } problem && !reloadWaits)
        {
            _view.Status = problem;
        }

        // **The world is up and the map's copies are gone, so the entity models' can go too** (B407). Armed only now, because a
        // failure before this point clears the renderer's model buffers and must be able to re-upload from memory.
        if (result.Uploaded && map.Assets is { Released: true })
        {
            _models.ReleaseUploaded();
        }

        // The textures and props were released after their first upload and a failure cleared them (B407): read the map again.
        if (result.NeedsReload && _demo is { } open)
        {
            // **Not while any load is in flight** (B779 reviews 26-28): decoding, fetching, queued, reading or applying.
            // `ProjectMap` already does nothing but mark the world while a worker reads, so this is reached then only by the
            // read's own projection, whose assets are fresh. Otherwise — a load still decoding, one queued and not yet
            // raised, a superseded load's gap — a reload here would read the old demo's map only for the next load to
            // clear it. So mark the world and wait, saying nothing (review 28 item 3): the first frame with no load in
            // flight projects, the upload answers that the world is lost, and this reads the map again **synchronously, on
            // the UI thread** — a full map read, 13 to 18 seconds on a large map (B146), with the window not pumping. That
            // is the cost of recovering a lost device; the read in flight does not recover it, because it uploaded to the
            // device that was lost (review 27 item 3).
            if (reloadWaits)
            {
                _log.LogInformation("{Message}", $"world textures lost while a load is in flight; reading {open.MapName} again after it");
                _world.Invalidate();
                return;
            }

            _log.LogWarning("{Message}", $"world textures lost after their pixels were released; reading {open.MapName} again");
            LoadMap(open.MapName);
        }
    }

    /// <summary>The leaf outline to draw over the world, when that overlay is switched on.</summary>
    /// <returns>World-space segments, or nothing when the mode is off or there is no leaf.</returns>
    /// <remarks>
    /// **What is left here is the TOGGLE, which is view state, and the eye, which the camera owns.**
    /// The tree walk is <see cref="LeafVis"/>'s and the transform is the GPU's (D95) — this method
    /// no longer takes a matrix at all, because nothing on this side of the seam projects anything.
    ///
    /// The origin is the one the free camera flies from, so the box is the leaf the VIEWER is in
    /// rather than the one the recording happens to be looking from.
    ///
    /// **It reads `_loaded` rather than a field of its own, and that is the fix for a regression
    /// this method carried for a day** (B196). A separate `_level` survived the move that created
    /// <see cref="LoadedMap"/> and its assignment did not, so it held null for ever and this drew
    /// nothing on every map. One field, or it drifts.
    /// </remarks>
    /// <param name="on">Whether `mat_leafvis` is switched on, which is the window's to know.</param>
    public IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> LeafBoxLines(bool on)
    {
        if (!on)
        {
            return [];
        }

        // **The warning and its once-per-map latch went with the outline** (B188, D90). Saying which
        // of the three silences this is, and saying it once, are both `LeafBoxes`' business — the
        // window's part is which camera to measure from.
        return _leafBoxes.Lines(_loaded, _camera.LeafEye);
    }

    /// <summary>A new render frame: the moment shown is posed again this frame, at the HUD's real time (B744).</summary>
    public void NextFrame() => _moments.NextFrame(_viewerWindow?.VguiSeconds());

    /// <summary>Builds the scene at a moment through the camera now on, with no replay before it.</summary>
    /// <param name="tick">The moment to show, which may fall between ticks.</param>
    /// <param name="playing">Whether the transport is playing: paused draws an uninterpolated pose (B399).</param>
    public void Show(double tick, bool playing)
    {
        // **`EnsureWeaponRoles()` was called here until 2026-08-26** (B188, D90). It was the last
        // non-view work in the frame path: one line reaching for `_timeline` and `_game` on every
        // frame, to keep `MomentScene.Appearance` current. `MomentPresenter` asks
        // `PlayerAppearances` now, still before the sampling and still outside both timers — the
        // first call reads weapon scripts out of the archives at an ICE decryption each, and
        // charging that to `sampling` or to `posing` would misname the same spike either way.
        _moments.Show(
            tick,
            new MomentView(
                _window().Tick,
                _camera.FirstPerson,
                _camera.FollowedEntity(),
                _camera.FirstPerson ? _camera.FirstPersonCamera() : null,

                // `fovViewmodel`: the setting moved by however far the view is from `default_fov` (view.cpp:725).
                _camera.ViewFovNow().RenderedViewmodel(_settings.ViewmodelFieldOfView, _camera.Aspect),

                // **`r_drawviewmodel`, which the viewer never read until B166.** It travels beside
                // the field of view because they are the same kind of thing: a setting the watcher
                // owns, applied to the moment being drawn rather than stored in the scene.
                _settings.DrawViewmodel,

                // **Paused draws a different pose, not a frozen one** (B399). The engine clears
                // `s_bInterpolate` while paused, so the transport's own state decides whether the
                // sampler interpolates at all.
                playing,

                // The watcher's `cl_flipviewmodels` (B515).
                _settings.FlipViewModels,

                // The watcher's `tf_use_min_viewmodels` (B525).
                _settings.UseMinViewmodels,

                // The watcher's `r_lightaverage` and `r_lightinterp` (B676).
                _settings.LightAverage,
                _settings.LightInterp,
                _settings.WorldLights,

                // The watcher's `r_ambientboost` (B672).
                _settings.AmbientBoost,

                // `gpGlobals->realtime`, the HUD's own clock, which the jiggle bones step on (B615).
                Window.VguiSeconds()));
    }

    /// <summary>The recorder's screen overlay, resolved against the map's loaded materials (`CViewRender::PerformScreenOverlay`).</summary>
    public ScreenOverlayMaterial? ScreenOverlay =>
        _moment.ScreenOverlay is { } overlayName &&
        _loaded?.Assets?.ScreenOverlays.TryGetValue(overlayName, out ScreenOverlayMaterial? screenOverlay) == true
            ? screenOverlay
            : null;
}
