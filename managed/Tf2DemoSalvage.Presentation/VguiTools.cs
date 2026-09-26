using System;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The engine's tools panel — `PANEL_TOOLS`, where `CFPSPanel` lives (`vgui_int.cpp:209`) — drawn through VGUI.</summary>
/// <remarks>
/// A full-screen root with no background, under the platform scheme `resource/SourceScheme.res`, which is where
/// `DefaultFixedOutline` is declared. Each frame: sample the meter, pass the tick, `SolveTraverse`, `PaintTraverse` into
/// the host's draw list. A new screen size loads the scheme again, as the engine does on a resolution change.
/// **The tick** is `ivgui()->AddTickSignal( panel, 250 )`: `OnTick` runs once 250 ms have passed since the last.
/// </remarks>
public sealed class VguiTools
{
    private const string SchemePath = "resource/SourceScheme.res";
    private const double TickSeconds = 0.25;

    private readonly VguiSurfaceHost _host;
    private readonly VguiPanel _root = new(null, "ToolsPanel") { PaintBackgroundEnabled = false };
    private VguiContext? _context;
    private double _nextTick = double.NaN;

    /// <summary>Makes the tools root and the frame-rate panel on it.</summary>
    /// <param name="host">The surface the tools panel shares with the other roots.</param>
    public VguiTools(VguiSurfaceHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Fps = new FpsPanel(_root);
    }

    /// <summary>`CFPSPanel`.</summary>
    public FpsPanel Fps { get; }

    /// <summary>The meter, which owns the smoothing and the watermarks.</summary>
    public FpsMeter Meter { get; } = new();

    /// <summary>The most recent reading, drawn or not — what <see cref="FrameRateLog"/> reports.</summary>
    public FpsReading? LastReading { get; private set; }

    /// <summary>Lays out and paints this frame into the host's list, begun already.</summary>
    /// <param name="realtime">Seconds since the viewer started, for the tick.</param>
    /// <param name="frameSeconds">How long the previous frame took.</param>
    /// <param name="mode">`cl_showfps`.</param>
    /// <param name="position">`cl_showpos` and what it reads.</param>
    /// <param name="mapName">The open map without its extension, or null.</param>
    public void Frame(double realtime, double frameSeconds, int mode, PositionReadout position, string? mapName)
    {
        Meter.Mode = mode;
        LastReading = Meter.Sample(frameSeconds);

        if (_context is null || !ReferenceEquals(_context.Surface, _host.List))
        {
            _context = _host.LoadScheme(SchemePath);

            // A resolution change reloads every panel's scheme — and `CFPSPanel::ApplySchemeSettings` calls `ComputeSize`.
            _root.InvalidateLayout(reloadScheme: true);
        }

        VguiContext context = _context;

        if ((_root.Wide, _root.Tall) != (_host.Wide, _host.Tall))
        {
            (_root.Wide, _root.Tall) = (_host.Wide, _host.Tall);

            // `CFPSPanel::OnScreenSizeChanged` (vgui_fpspanel.cpp:103): measured again whether shown or not.
            Fps.ComputeSize();
        }

        Fps.Mode = mode;
        Fps.FrameSeconds = frameSeconds;
        Fps.Position = position;
        Fps.Reading = LastReading;

        // `V_GetFileName( engine->GetLevelName() )` keeps the extension.
        Fps.MapName = mapName is { Length: > 0 } named ? named + ".bsp" : "no map";

        if (double.IsNaN(_nextTick))
        {
            _nextTick = realtime + TickSeconds;
        }
        else if (realtime >= _nextTick)
        {
            _nextTick = realtime + TickSeconds;
            Fps.OnTick();
        }

        VguiLayout.SolveTraverse(_root, context);
        _root.PaintTraverse(_host.List, context);
    }
}
