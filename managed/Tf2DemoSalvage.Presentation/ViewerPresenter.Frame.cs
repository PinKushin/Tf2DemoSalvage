using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Text;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a window knows of the frame it is drawing: the transport, the tick it showed, its size, its clock.</summary>
/// <param name="Tick">The transport's whole tick.</param>
/// <param name="ShownTick">The moment last shown, which may fall between ticks.</param>
/// <param name="Playing">Whether the transport is playing.</param>
/// <param name="ViewportWidth">The viewport's width in pixels.</param>
/// <param name="ViewportHeight">The viewport's height in pixels.</param>
/// <param name="FrameSeconds">How long the last frame took.</param>
/// <param name="DemoFrameSeconds">Demo time the last frame covered: zero while paused.</param>
public readonly record struct WindowFrame(
    int Tick, double ShownTick, bool Playing, int ViewportWidth, int ViewportHeight, float FrameSeconds, double DemoFrameSeconds);

/// <summary>What the presenter asks of the window at its own moment — attached once, read when needed (D214).</summary>
/// <param name="Frame">The window's frame facts as they are now.</param>
/// <param name="ShowOnly">Shows a tick with no replay before it: the window notes the tick, then calls <see cref="ViewerPresenter.Show"/>.</param>
/// <param name="Bindings">Which key performs which action, for the HUD's key labels.</param>
/// <param name="ClientConVars">The watcher's own ConVar values, or null where unset.</param>
/// <param name="VguiSeconds">The HUD's real-time clock.</param>
/// <param name="ScoresHeld">Whether `+showscores` is held.</param>
/// <param name="LastFrameSeconds">How long the last frame took, for `cl_showfps`.</param>
/// <remarks>
/// **Delegates and not values, as <see cref="HudWindow"/> is and for its reason**: the old code read each at its own
/// moment — the bindings when a HUD frame is built, the clock when the tools panel is framed, the frame facts after a
/// tick was shown.
/// </remarks>
public sealed record ViewerWindow(
    Func<WindowFrame> Frame,
    Action<double> ShowOnly,
    Func<KeyBindings> Bindings,
    Func<string, string?> ClientConVars,
    Func<double> VguiSeconds,
    Func<bool> ScoresHeld,
    Func<double> LastFrameSeconds);

/// <content>
/// The frame records, filled here: the window says what a window knows (<see cref="WindowFrame"/>) and this adds the
/// demo, the map, the install, the camera's answers and the settings it already holds (D214) [no engine counterpart:
/// the engine's view setup reads globals].
/// </content>
public sealed partial class ViewerPresenter
{
    private ViewerWindow? _viewerWindow;

    /// <summary>The window's questions, once attached. Asking before is a wiring fault, said plainly.</summary>
    private ViewerWindow Window =>
        _viewerWindow ?? throw new InvalidOperationException("the window was not attached to the presenter before a frame was asked for");

    /// <summary>Attaches the window's questions and its font rasteriser, and gives the HUD what it asks of both.</summary>
    /// <typeparam name="TGdi">The rasteriser's type: the window's project is the one that has GDI (D84).</typeparam>
    /// <param name="window">The window's questions.</param>
    /// <param name="gdi">The font rasteriser.</param>
    public void AttachWindow<TGdi>(ViewerWindow window, TGdi gdi)
        where TGdi : IVguiGdi
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(gdi);

        _viewerWindow = window;
        _effects.AttachHud(new HudWindow(
            gdi, () => _game, () => _demo?.MapName, HudWindowAt, _camera.ReadPosition, window.VguiSeconds, _camera.FirstPersonCamera));
    }

    /// <summary>Whether a demo is open.</summary>
    public bool HasDemo => _demo is not null;

    /// <summary>The open demo's path, or null.</summary>
    public string? DemoPath => _demo?.Path;

    /// <summary>The map the open demo names, or null.</summary>
    public string? MapName => _demo?.MapName;

    /// <summary>The camera the last frame was drawn from, as handed to the device; null before one had a world (D226).</summary>
    public FreeCamera? LastViewing => _lastViewing;

    /// <summary>Whether the open demo has a timeline to show.</summary>
    public bool HasTimeline => _timeline is not null;

    /// <summary>What this presenter knows of the frame the window is drawing, for the presenters that draw it.</summary>
    private ViewFrame ViewFrameNow => FrameOf(Window.Frame());

    private ViewFrame FrameOf(in WindowFrame window) =>
        new(
            _timeline,
            _loaded,
            _game,
            window.Tick,
            window.ShownTick,
            window.Playing,
            _camera.FirstPerson,
            _camera.FollowedEntity(),
            window.ViewportWidth,
            window.ViewportHeight,
            window.FrameSeconds,
            window.DemoFrameSeconds,
            _settings.DoPixelVisibility,
            _settings.DrawViewmodel,
            _settings.HealTargetMarker,
            _settings.Decals,
            _settings.MaxModelDecals,
            _settings.ThreadedParticles);

    /// <summary>What the HUD reads at a tick, its projection, and the frame — asked for by the HUD.</summary>
    private (HudView View, float[] WorldToScreen, ViewFrame Frame) HudWindowAt(int hudTick) =>
        (new HudView(
            _timeline,
            hudTick,
            null,
            null,
            Window.Bindings(),
            Window.ClientConVars,
            (float)Window.VguiSeconds(),
            _camera.ViewFovNow().World,
            _camera.EffectiveMode,
            _camera.Followed(hudTick)),
         _camera.ViewMatrix(),
         ViewFrameNow);

    /// <summary>Draws the whole world at a moment: players and every model-bearing entity.</summary>
    /// <param name="tick">The moment to show, which may fall between ticks.</param>
    /// <remarks>
    /// Takes a fractional tick rather than a whole one so the interpolation actually reaches the
    /// picture. Truncating here would leave every pose snapped to the last packet and make the
    /// whole interpolation layer a no-op that still passed its own tests.
    /// </remarks>
    public void ShowMoment(double tick)
    {
        if (_timeline is null)
        {
            return;
        }

        // What a seek rebuilds on the way is the frame presenter's (D214); it comes back to show each tick it needs.
        _effects.ShowMoment(
            ViewFrameNow,
            tick,
            _device,
            shown =>
            {
                Window.ShowOnly(shown);

                return ViewFrameNow;
            },
            _jumps.Jumps);
    }

    /// <summary>Whether `--windows` asked for the window cache beside the whole timeline (D217 step 4).</summary>
    public bool CheckWindows { get; internal set; }

    /// <summary>The window cache's check for the demo on screen, or null without `--windows`.</summary>
    private WindowCheck? _windowCheck;

    /// <summary>The transport, asked only how many times playback has jumped — what makes a move a skip (D221).</summary>
    private readonly PlaybackPresenter _jumps;

    /// <summary>`TF2VIEW_PICK="x y"`: what lies under that pixel of the shot — <see cref="PickReport"/> — or null.</summary>
    /// <param name="asked">The variable's text, or null.</param>
    public string? Pick(string? asked) => _effects.Pick(asked, ViewFrameNow, _lastViewing);

    /// <summary>This frame's VGUI: the client's HUD, then the tools panel and `CFPSPanel` on it above.</summary>
    /// <returns>The overlay, to hand to <see cref="DrawScene"/>.</returns>
    public FrameOverlay BuildOverlay()
    {
        WindowFrame window = Window.Frame();

        // Every drawn frame, paused or playing, so a window finished after its seek is still checked.
        if (_timeline is { } whole)
        {
            _windowCheck?.Observe(_effects.Skip, whole);
        }

        return new FrameOverlay(_effects.BuildOverlay(
            new OverlayFrame(
                _timeline,
                _loaded,
                _demo?.MapName,
                window.Tick,
                window.ViewportWidth,
                window.ViewportHeight,
                _settings.Crosshair,
                _settings.ShowFrameRate,
                Window.ScoresHeld(),
                Window.LastFrameSeconds()),
            _device));
    }

    /// <summary>Writes a demo's assembly text — the CLI's `--asm` — and says how much.</summary>
    /// <param name="demoPath">The demo.</param>
    /// <param name="output">Where to write.</param>
    /// <returns>The words for the status line.</returns>
    public static string ExportDemo(string demoPath, string output) =>
        string.Create(CultureInfo.CurrentCulture, $"Exported {DemoAssembly.Export(demoPath, output):N0} commands to {output}");

    /// <summary>Rebuilds a demo from its assembly text, and says how much.</summary>
    /// <param name="source">The assembly text.</param>
    /// <param name="output">Where to write the demo.</param>
    /// <returns>The words for the status line.</returns>
    public static string CompileDemo(string source, string output)
    {
        (int commands, int bytes) = DemoAssembly.Compile(source, output);

        return string.Create(CultureInfo.CurrentCulture, $"Compiled {commands:N0} commands to {output} ({bytes:N0} bytes)");
    }

    /// <summary>Gives Core and Content somewhere to say what they lost: both are libraries and neither can see the log.</summary>
    /// <param name="sink">Takes an area and a finished message, as a warning.</param>
    /// <param name="notes">Takes an area and a finished message, as an ordinary observation.</param>
    public static void AttachDecodeLog(Action<string, string> sink, Action<string, string> notes)
    {
        Core.Diagnostics.DecodeLog.Sink = sink;
        Core.Diagnostics.DecodeLog.Notes = notes;
    }
}
