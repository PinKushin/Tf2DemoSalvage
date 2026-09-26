using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The engine's tools panel — `PANEL_TOOLS`, where `CFPSPanel` lives (`vgui_int.cpp:209`) — drawn through VGUI.</summary>
/// <remarks>
/// A full-screen root with no background, under the platform scheme `resource/SourceScheme.res`, which is where
/// `DefaultFixedOutline` is declared. Each frame: sample the meter, pass the tick, `SolveTraverse`, `PaintTraverse` into
/// a draw list the renderer draws. A new screen size reloads the scheme's fonts, as the engine does on a resolution change.
/// **The tick** is `ivgui()->AddTickSignal( panel, 250 )`: `OnTick` runs once 250 ms have passed since the last.
/// </remarks>
public sealed class VguiTools
{
    private const string SchemePath = "resource/SourceScheme.res";
    private const double TickSeconds = 0.25;

    private readonly Func<string, byte[]?> _read;
    private readonly Func<string, string?> _fullPath;
    private readonly IVguiGdi _gdi;
    private readonly string _language;
    private readonly VguiPanel _root = new(null, "ToolsPanel") { PaintBackgroundEnabled = false };
    private VguiContext? _context;
    private VguiDrawList? _list;
    private double _nextTick = double.NaN;

    /// <summary>Makes the tools root and the frame-rate panel on it.</summary>
    /// <param name="read">Reads a game file by path.</param>
    /// <param name="fullPath">A game path to the loose file on disk, for custom fonts.</param>
    /// <param name="gdi">The GDI adapter fonts are made through.</param>
    /// <param name="textureSize">A material's texture size, for the draw list.</param>
    /// <param name="language">The game's language.</param>
    public VguiTools(Func<string, byte[]?> read, Func<string, string?> fullPath, IVguiGdi gdi, Func<string, (int Wide, int Tall)> textureSize, string language = "english")
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _fullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
        _gdi = gdi ?? throw new ArgumentNullException(nameof(gdi));
        TextureSize = textureSize ?? throw new ArgumentNullException(nameof(textureSize));
        _language = language;
        Fps = new FpsPanel(_root);
    }

    /// <summary>`CFPSPanel`.</summary>
    public FpsPanel Fps { get; }

    /// <summary>The meter, which owns the smoothing and the watermarks.</summary>
    public FpsMeter Meter { get; } = new();

    /// <summary>The most recent reading, drawn or not — what <see cref="FrameRateLog"/> reports.</summary>
    public FpsReading? LastReading { get; private set; }

    private Func<string, (int Wide, int Tall)> TextureSize { get; }

    /// <summary>Lays out and paints this frame.</summary>
    /// <param name="wide">The screen's width.</param>
    /// <param name="tall">Its height.</param>
    /// <param name="realtime">Seconds since the viewer started, for the tick.</param>
    /// <param name="frameSeconds">How long the previous frame took.</param>
    /// <param name="mode">`cl_showfps`.</param>
    /// <param name="position">`cl_showpos` and what it reads.</param>
    /// <param name="mapName">The open map without its extension, or null.</param>
    /// <returns>The frame's draw list.</returns>
    public VguiDrawList Frame(int wide, int tall, double realtime, double frameSeconds, int mode, PositionReadout position, string? mapName)
    {
        Meter.Mode = mode;
        LastReading = Meter.Sample(frameSeconds);

        if (_context is null || _list is null || _context.ScreenWide != wide || _context.ScreenTall != tall)
        {
            Reload(wide, tall);
        }

        VguiContext context = _context!;
        VguiDrawList list = _list!;

        if ((_root.Wide, _root.Tall) != (wide, tall))
        {
            (_root.Wide, _root.Tall) = (wide, tall);

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
        list.Clear();
        _root.PaintTraverse(list, context);

        return list;
    }

    private void Reload(int wide, int tall)
    {
        KeyValuesTree root = _read(SchemePath) is { } bytes ? KeyValuesTree.Load(bytes, SchemePath, _read) : KeyValuesTree.Load([], SchemePath, _read);
        VguiScheme scheme = VguiScheme.Load(root);
        VguiFontManager manager = new(_gdi);
        VguiSchemeFonts fonts = VguiSchemeFonts.Load(root, manager, _fullPath, _language, tall);

        _list = new VguiDrawList(TextureSize, manager);
        _context = new VguiContext(scheme, VguiBorders.Load(root, scheme, tall), root.FindOrCreate("Fonts"), wide, tall, _language, fonts) { Surface = _list };

        // A resolution change reloads every panel's scheme — and `CFPSPanel::ApplySchemeSettings` calls `ComputeSize`.
        _root.InvalidateLayout(reloadScheme: true);
    }
}
