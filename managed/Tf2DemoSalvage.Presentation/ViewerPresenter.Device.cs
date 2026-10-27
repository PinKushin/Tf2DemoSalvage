using System;
using System.Collections.Generic;
using System.Diagnostics;

using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <content>
/// The device, handed over once, and the frame's calls on it (D214): the view's setup beside the camera, the pose's
/// cull inputs, the overlay, the draw. Moved from the window with their order and their arguments as they were; the
/// window still creates the device, sizes it and disposes it, and still makes the calls that are a device's alone
/// (a debug toggle, a capture, the exclusive mode). The engine's order is `CViewRender::SetUpView`, then
/// `BuildWorldLists` and `BuildRenderablesList`, then the draw (`viewrender.cpp`).
/// </content>
public sealed partial class ViewerPresenter
{
    /// <summary>The device as it was handed over: what the entries that upload to it take.</summary>
    private Device3D? _device;

    /// <summary>The camera the last frame was set up with, for <see cref="Pick"/>.</summary>
    private FreeCamera? _lastViewing;

    /// <summary>The rate line's name for each of the device's <see cref="IViewerDevice.SetCameraParts"/>, built once.</summary>
    private readonly Dictionary<string, string> _setCameraNames = new(StringComparer.Ordinal);

    /// <summary>Takes the device, gives it every setting a new device takes, and tells the scene where packed geometry goes.</summary>
    /// <param name="device">The device, just created.</param>
    /// <param name="specularOff">Unticks the menu's Reflections item, which is what switches `mat_specular` off.</param>
    /// <param name="setFullbright">Sets `mat_fullbright` through the menu's own method, so the check marks follow.</param>
    public void AttachDevice(Device3D device, Action specularOff, Action<int> setFullbright)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;
        _settingsOwner.ApplyTo(device, specularOff, setFullbright);

        // **Where packed geometry goes, and forgetting it draws NOTHING** (B193). Without this
        // the scene packs every model, poses it, transforms it correctly and submits it against
        // a vertex buffer the renderer never received — B148's symptom exactly, and silent.
        // Assigned here rather than at construction because the device does not exist until the
        // viewport has a handle.
        _moment.Upload = device;
    }

    /// <summary>The window is about to dispose the device: the scene uploads nowhere, and no frame call reaches it.</summary>
    public void DetachDevice()
    {
        _moment.Upload = null;
        _device = null;
    }

    /// <summary>Every camera upload's pieces so far, in stopwatch ticks, timed where they ran (B595).</summary>
    public CameraTotals CameraSpent { get; private set; }

    /// <summary>Sends the current view to the device, without rebuilding anything.</summary>
    /// <param name="flyTicks">How long the flight that asked for this took, for the stall report; zero from the frame.</param>
    /// <param name="exposureSeconds">Game time the exposure walks by: the demo's frame time, or a tick's worth for a pending capture (B520).</param>
    /// <param name="surfaceColours">Whether the surface-colour view is on.</param>
    public void UploadCamera(long flyTicks, float exposureSeconds, bool surfaceColours)
    {
        if (_device is null || !_device.HasWorld)
        {
            return;
        }

        UploadCameraOver(_device, _device, flyTicks, exposureSeconds, surfaceColours);
    }

    /// <summary><see cref="UploadCamera"/> over a device a test can read.</summary>
    internal void UploadCameraOver(IViewerDevice device, IFrameDevice drawing, long flyTicks, float exposureSeconds, bool surfaceColours)
    {
        long viewAt = Stopwatch.GetTimestamp();

        // **The camera itself rather than its matrix**, so the projection and the cull volume are
        // derived from one thing. `SetCamera(float[])` still exists for the viewmodel pass, which
        // has a camera of its own and no business culling against the world's.
        // **The one per-frame view setup, so this is what advances the chase camera's recovery** —
        // `CViewRender::SetUpView` computes the view once and everything downstream reads it.
        FreeCamera viewing = _camera.ViewCameraNow(Window.Frame().DemoFrameSeconds);

        _lastViewing = viewing;

        long deviceAt = Stopwatch.GetTimestamp();

        // **The fog in force at this tick, from the demo** (B139) — `EnableWorldFog` runs in every
        // view setup, so this is set beside the camera rather than once per map.
        TickFeeds feeds = _camera.FeedsNow();

        device.WorldFog = feeds.WorldFog;
        device.SkyFog = feeds.SkyFog;
        device.MonitorCamera = feeds.MonitorCamera;
        device.Tonemap = feeds.Tonemap;

        // The exposure walks by game frame time and resets when the followed player changes (B514).
        device.ExposureSeconds = exposureSeconds;
        device.ObserverTarget = _camera.FollowedEntity();

        device.SetCamera(viewing, surfaceColours);

        // **Posed before the particles read a single attachment** (D221, B539): `view->OnRenderStart()` places the
        // player and the view models (cdll_client_int.cpp:2184) before `ParticleMgr()->Simulate` (`:2234`) reads
        // them. Stepped first, every followed point read the LAST frame's pose, and the first frame after a seek read
        // a viewmodel posed for no camera at all. `ProjectWorld`'s own call then finds the moment posed and returns.
        // **The pose begins with the wait for the frame in flight**, timed on its own (B581 rung 2, finding 98 §9 step 5):
        // `SetCamera` above wrote only its own view half, so it overlapped the draw; the pose rewrites what the draw reads.
        long waitAt = Stopwatch.GetTimestamp();
        long posingAt = PoseOver(device);

        long particlesAt = Stopwatch.GetTimestamp();

        _effects.DrawOver(ViewFrameNow, viewing, drawing);

        // One reading of the clock for the particles' end, carried to both reports (B243).
        long particledAt = Stopwatch.GetTimestamp();

        if (_effects.Parts is { } parts)
        {
            parts.Add("camera view", deviceAt - viewAt);
            // `posingAt - deviceAt` also covers the six `device.*` feed writes before `SetCamera`, so it exceeds the seven parts' sum by microseconds.
            parts.Add("camera setcamera", waitAt - deviceAt);
            parts.Add("camera wait", posingAt - waitAt);

            // The device's own split of that, as it timed it (B243). Indexed, because a foreach over the interface
            // allocates an enumerator on every measured frame.
            IReadOnlyList<(string Part, long Ticks)> split = device.SetCameraParts;

            for (int at = 0; at < split.Count; at++)
            {
                (string part, long ticks) = split[at];

                if (!_setCameraNames.TryGetValue(part, out string? name))
                {
                    name = "camera setcamera " + part;
                    _setCameraNames.Add(part, name);
                }

                parts.Add(name, ticks);
            }

            parts.Add("camera pose", particlesAt - posingAt);
            parts.Add("camera particles", particledAt - particlesAt);
        }

        // Running totals, so a stalled frame can be told its own share by differencing two frame boundaries (B595).
        CameraSpent = new CameraTotals(
            CameraSpent.View + (deviceAt - viewAt),
            CameraSpent.SetCamera + (waitAt - deviceAt),
            CameraSpent.Wait + (posingAt - waitAt),
            CameraSpent.Pose + (particlesAt - posingAt),
            CameraSpent.Particles + (particledAt - particlesAt));

        // **Each piece named, because the `camera` column alone held 102 and 410 ms stalls** on f12
        // in seconds with no collection, and it is four unrelated pieces of work.
        StallReport.Camera(
            flyTicks,
            deviceAt - viewAt,
            particlesAt - deviceAt,
            particledAt - particlesAt,
            _renderLog);
    }

    /// <summary>Poses this frame's moment against the device's own frustum and visible set, or against nothing before a device.</summary>
    /// <remarks>
    /// **The frustum is the device's own**, the same one the world cull and the draw use, rather
    /// than a second one built from the camera here
    /// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#one-camera-or-the-cull-lies`).
    /// A span cannot be null-coalesced, so the device is tested once and both halves come from
    /// the same one — which is also what stops a frustum and a visible set from different frames
    /// reaching the cull together.
    /// </remarks>
    public void PoseNow()
    {
        if (_device is { } device)
        {
            _ = PoseOver(device);
        }
        else
        {
            _moments.PoseNow();
        }
    }

    /// <summary>Told each time <see cref="PoseOver"/> has posed the moment, for a test that checks where the pose falls.</summary>
    internal Action? Posed { get; set; }

    /// <summary><see cref="PoseNow"/> over a device a test can read.</summary>
    /// <returns>The timestamp read when the wait for the frame in flight ended and the pose began, carried to the
    /// `camera wait` and `camera pose` parts (B243).</returns>
    /// <remarks>**The one wait before the pose, for both callers** (finding 98 §9 step 5): the camera stage, and
    /// `ProjectWorld`'s pose on a frame whose camera upload returned early.</remarks>
    internal long PoseOver(IViewerDevice device)
    {
        device.FinishDrawBeforePose();

        // `ThreadedBoneSetup` reads `cl_threaded_bone_setup` each frame (c_baseanimating.cpp:2779), before any bone is built.
        _models.ThreadedBoneSetup = _settings.ThreadedBoneSetup;

        long posingAt = Stopwatch.GetTimestamp();

        _moments.PoseNow(device.Frustum, device.Views, device.Eye, device.ScreenView, device.DrawsMonitor);
        Posed?.Invoke();

        return posingAt;
    }

    /// <summary>Hands this frame's scene to the device.</summary>
    /// <param name="red">The background's red.</param>
    /// <param name="green">The background's green.</param>
    /// <param name="blue">The background's blue.</param>
    /// <param name="leafVis">Whether `mat_leafvis` is on.</param>
    /// <param name="overlay">The VGUI <see cref="BuildOverlay"/> built for this frame.</param>
    public void DrawScene(float red, float green, float blue, bool leafVis, FrameOverlay overlay)
    {
        if (_device is null)
        {
            return;
        }

        DrawSceneOver(_device, red, green, blue, leafVis, overlay);
    }

    /// <summary><see cref="DrawScene"/> over a device a test can read.</summary>
    internal void DrawSceneOver(IViewerDevice device, float red, float green, float blue, bool leafVis, FrameOverlay overlay)
    {
        // The recorder's screen overlay, resolved against the map's loaded materials (`CViewRender::PerformScreenOverlay`).
        device.ScreenOverlay = ScreenOverlay;

        // The followed player, whom a monitor view force-draws though the main view does not (B511).
        device.MonitorOnlyModels = _moment.UndrawnInstances;

        // The local player's FOV distance factor, which divides the detail fade when he zooms (B580).
        device.DetailFovFactor = _camera.DistanceFactorNow();

        device.DrawScene(
            red,
            green,
            blue,
            // **The line channel carries mat_leafvis, in WORLD units, drawn depth-tested** (D95).
            // It used to be the BSP's own edge segments projected for the overhead view; that view
            // is gone (D49) and `mat_wireframe` replaced the outline, so the channel now does what
            // the engine's debug lines do — absolute coordinates, transformed on the GPU, occluded
            // by the geometry the box describes.
            //
            // The projected outline is gone entirely (B151 closed by deletion rather than by the
            // split it proposed): nothing read it, and 615 ms of a 679 ms frame was spent building
            // it. `MapOutline` still supplies the play-area bounds, which is the half that was
            // actually wanted.
            LeafBoxLines(leafVis),
            _moment.Instances,
            _moment.ViewmodelInstances,
            _moment.ViewmodelCamera?.ToMatrix(),
            overlay.List);
    }

    /// <summary>Whether an install is open.</summary>
    public bool HasInstall => _game is not null;

    /// <summary>Where a second of frames went, counted and reported once a second; the window tells it of each frame.</summary>
    public FrameReporter Frames { get; }
}
