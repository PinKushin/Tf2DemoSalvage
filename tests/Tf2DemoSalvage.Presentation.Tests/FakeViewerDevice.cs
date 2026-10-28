using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A device that writes down, in order, every setting it is given and every call made on it (D214).</summary>
/// <remarks>
/// **One list for both**, because what a moved frame could get wrong is the ORDER: the fog after the camera, the
/// overlay after the draw. A property set is "Name value"; a call is its name.
/// </remarks>
internal sealed class FakeViewerDevice : IViewerDevice
{
    private readonly List<string> _set = [];

    /// <summary>Every set and call, in the order made.</summary>
    public IReadOnlyList<string> Set => _set;

    /// <summary>Adds a line of the test's own to the same list, for a callback whose place in the order matters.</summary>
    public void Mark(string what) => _set.Add(what);

    /// <summary>What the last <see cref="DrawScene"/> was given.</summary>
    public (float Red, float Green, float Blue, int Lines, IReadOnlyList<ModelInstance>? Models, float[]? ViewmodelCamera, VguiDrawList? Vgui) Drawn
    {
        get;
        private set;
    }

    /// <summary>The camera the last <see cref="SetCamera"/> was given, and its surface-colour flag.</summary>
    public (FreeCamera? Camera, bool SurfaceColours) Camera { get; private set; }

    public bool VerticalSync { get => false; set => Note(nameof(VerticalSync), value); }

    public int QueueMode { get => 0; set => Note(nameof(QueueMode), value); }

    public bool Phong { get => false; set => Note(nameof(Phong), value); }

    public bool DrawMonitors { get => false; set => Note(nameof(DrawMonitors), value); }

    public bool DisableBloom { get => false; set => Note(nameof(DisableBloom), value); }

    public float ForceToneMapScale { get => 0f; set => Note(nameof(ForceToneMapScale), value); }

    public bool DrawHud { get => false; set => Note(nameof(DrawHud), value); }

    public int Draw3dSky { get => 0; set => Note(nameof(Draw3dSky), value); }

    public float DetailDistance { get => 0f; set => Note(nameof(DetailDistance), value); }

    public float DetailFadeWidth { get => 0f; set => Note(nameof(DetailFadeWidth), value); }

    public float DetailFovFactor { get; set; } = float.NaN;

    public bool HasWorld { get; set; } = true;

    public SceneFog? WorldFog { get => null; set => Note(nameof(WorldFog), value is null ? "none" : "set"); }

    public SceneFog? SkyFog { get => null; set => Note(nameof(SkyFog), value is null ? "none" : "set"); }

    public ScenePointCamera? MonitorCamera { get => null; set => Note(nameof(MonitorCamera), value is null ? "none" : "set"); }

    public SceneTonemap Tonemap { get => default; set => Note(nameof(Tonemap), value == default ? "default" : "set"); }

    public float ExposureSeconds { get => 0f; set => Note(nameof(ExposureSeconds), value); }

    public int? ObserverTarget { get => null; set => Note(nameof(ObserverTarget), value?.ToString(CultureInfo.InvariantCulture) ?? "nobody"); }

    public IReadOnlyList<(string Part, long Ticks)> SetCameraParts { get; set; } = [];

    public ViewFrustum Frustum => default;

    public RenderableViews Views => default;

    public (float X, float Y, float Z)? Eye => null;

    public ScreenFadeView? ScreenView => null;

    /// <summary>True by default, the behaviour before the device was asked: the undrawn player is posed.</summary>
    public bool DrawsMonitor
    {
        get
        {
            _set.Add(string.Create(CultureInfo.InvariantCulture, $"DrawsMonitor read {_drawsMonitor}"));
            return _drawsMonitor;
        }
        set => _drawsMonitor = value;
    }

    private bool _drawsMonitor = true;

    public ScreenOverlayMaterial? ScreenOverlay { get => null; set => Note(nameof(ScreenOverlay), value is null ? "none" : "set"); }

    public IReadOnlyList<ModelInstance> MonitorOnlyModels { get => []; set => Note(nameof(MonitorOnlyModels), value.Count); }

    /// <summary>A render queue whose in-flight state <see cref="SetCamera"/> writes down, or null for none (B581 rung 2).</summary>
    public Render.QueuedRenderThread? Queue { get; init; }

    public void SetCamera(FreeCamera camera, bool surfaceColours = false)
    {
        _set.Add(Queue is null ? nameof(SetCamera) : string.Create(CultureInfo.InvariantCulture, $"{nameof(SetCamera)} in flight {Queue.InFlight}"));
        Camera = (camera, surfaceColours);
    }

    /// <summary>Drains <see cref="Queue"/>, as the device does, and writes the call down.</summary>
    public void FinishDrawBeforeEffects()
    {
        Queue?.FinishPreviousDraw();
        _set.Add(nameof(FinishDrawBeforeEffects));
    }

    /// <summary>Hand-offs so far, written down when read; a test sets it to stand for frames handed.</summary>
    public long HandOffs
    {
        get
        {
            _set.Add(string.Create(CultureInfo.InvariantCulture, $"HandOffs read {_handOffs}"));
            return _handOffs;
        }
        set => _handOffs = value;
    }

    private long _handOffs;

    public void DrawScene(
        float red,
        float green,
        float blue,
        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> mapLines,
        IReadOnlyList<ModelInstance>? models,
        IReadOnlyList<ModelInstance>? viewmodels,
        float[]? viewmodelCamera,
        VguiDrawList? vgui)
    {
        _set.Add(nameof(DrawScene));
        Drawn = (red, green, blue, mapLines.Count, models, viewmodelCamera, vgui);
    }

    private void Note(string name, object value) => _set.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {value}"));
}
