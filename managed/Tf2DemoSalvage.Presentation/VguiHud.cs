using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The client's HUD: `CBaseViewport` under `resource/ClientScheme.res`, laid out by `scripts/HudLayout.res`.</summary>
/// <remarks>
/// On the first frame and on every new screen size — `ClientModeShared::Layout` (clientmode_shared.cpp:967) sizing the
/// viewport to the root, then `CBaseViewport::ReloadScheme` (baseviewport.cpp:717) — the scheme loads, the layout file
/// is applied to the elements by name, and every panel lays out and applies its scheme again. Each frame, `CHud::Think`
/// shows or hides the elements, then the viewport is solved and painted into the host's list, beneath the tools panel.
/// TF adds no layout conditions (`ClientModeTFNormal::ComputeVguiResConditions`, clientmode_tf.cpp:1989); `if_vr` is VR's.
/// </remarks>
public sealed class VguiHud
{
    private const string SchemePath = "resource/ClientScheme.res";
    private const string LayoutPath = "scripts/HudLayout.res";

    private readonly VguiSurfaceHost _host;
    private VguiContext? _context;

    /// <summary>The viewport, and every element `DECLARE_HUDELEMENT` makes at `CHud::Init`, before the layout is read.</summary>
    /// <param name="host">The surface the HUD shares with the other roots.</param>
    public VguiHud(VguiSurfaceHost host)
    {
        _host = host ?? throw new System.ArgumentNullException(nameof(host));
        PlayerStatus = new TfHudPlayerStatus(Viewport);
        WeaponAmmo = new TfHudWeaponAmmo(Viewport);
        DeathNotice = new TfHudDeathNotice(Viewport);
    }

    /// <summary>`CTFHudDeathNotice`.</summary>
    public TfHudDeathNotice DeathNotice { get; }

    /// <summary>`CTFHudWeaponAmmo`.</summary>
    public TfHudWeaponAmmo WeaponAmmo { get; }

    /// <summary>`CBaseViewport`, which the elements are parented to.</summary>
    public HudViewport Viewport { get; } = new();

    /// <summary>`CTFHudPlayerStatus`.</summary>
    public TfHudPlayerStatus PlayerStatus { get; }

    /// <summary>Lays out and paints this frame into the host's list, begun already.</summary>
    /// <param name="state">What `CHud::IsHidden` reads.</param>
    /// <param name="events">The game events fired since the last frame, or null for none.</param>
    /// <param name="reset">Whether this frame follows a seek — `VidInit` empties the feed first.</param>
    public void Frame(HudState state, IReadOnlyList<HudGameEvent>? events = null, bool reset = false)
    {
        if (_context is null || !ReferenceEquals(_context.Surface, _host.List))
        {
            _context = _host.LoadScheme(SchemePath);
            (Viewport.Wide, Viewport.Tall) = (_host.Wide, _host.Tall);
            Viewport.Context = _context;

            // `CHud::Init` loads the icons once, so a font icon keeps the size it measured at the first screen.
            Viewport.Icons ??= HudTextures.Load(_context);

            // `CBaseViewport::ReloadScheme`: the animation scripts, then the layout.
            Viewport.LoadHudAnimations(_context);
            Viewport.LoadControlSettings(LayoutPath, _context);
            Viewport.InvalidateLayout(reloadScheme: true);

            // The scheme applied now, as the game applies it at `CHud::Init` long before any packet: an element caches
            // icons and fonts there, and the events below may be the first thing it handles.
            VguiLayout.SolveTraverse(Viewport, _context);
        }

        if (reset)
        {
            DeathNotice.Clear();
        }

        // Events are dispatched as their packets are read, before the frame's think and paint.
        foreach (HudGameEvent fired in events ?? [])
        {
            DeathNotice.HandleGameEvent(fired with { RealTime = state.RealTime });
        }

        Viewport.Think(state);
        VguiLayout.SolveTraverse(Viewport, _context);
        Viewport.PaintTraverse(_host.List, _context);
    }
}
