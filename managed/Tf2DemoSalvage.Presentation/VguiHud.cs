using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
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
    private const string ChatSchemePath = "resource/ChatScheme.res";

    private readonly VguiSurfaceHost _host;
    private VguiContext? _context;
    private ScenePlayer? _lastLocal;
    private float _lastCurTime;

    /// <summary>The viewport, and every element `DECLARE_HUDELEMENT` makes at `CHud::Init`, before the layout is read.</summary>
    /// <param name="host">The surface the HUD shares with the other roots.</param>
    /// <param name="mdlCache">`vgui::MDLCache()` — the viewer's model set — for the model panels.</param>
    public VguiHud(VguiSurfaceHost host, IMdlCache mdlCache)
    {
        _host = host ?? throw new System.ArgumentNullException(nameof(host));
        PlayerStatus = new TfHudPlayerStatus(Viewport, mdlCache);
        WeaponAmmo = new TfHudWeaponAmmo(Viewport);
        DeathNotice = new TfHudDeathNotice(Viewport);

        // The weapon element first, so the icon it picks is the one the crosshair paints in the same frame.
        // **Interpolated:** the game's element order comes from its factory list, which the SDK does not fix.
        Weapon = new TfHudWeapon(Viewport);
        Crosshair = new TfHudCrosshair(Viewport);
        // The secondary before the other two, so the index they subtract is this frame's.
        // **Interpolated:** the element factory's order.
        SecondaryTargetId = new TfSecondaryTargetId(Viewport);
        SpectatorTargetId = new TfSpectatorTargetId(Viewport);
        MainTargetId = new TfMainTargetId(Viewport);
        MatchStatus = new TfHudMatchStatus(Viewport);
        KothTimeStatus = new TfHudKothTimeStatus(Viewport);
        Chat = new TfHudChat(Viewport);

        // `CTFClientScoreBoardDialog` is a viewport panel added by `CBaseViewport::CreatePanelByName`, not a
        // `CHudElement` — `ShowPanel( PANEL_SCOREBOARD, ... )` drives its visibility directly rather than
        // `CHud::Think`'s per-element `ShouldDraw`, which is why it is not parented alongside the elements above and
        // starts hidden here rather than defaulting to `VguiPanel.Visible`'s true.
        Scoreboard = new TfClientScoreBoardDialog(Viewport) { Visible = false };
    }

    /// <summary>`CHudChat`.</summary>
    public TfHudChat Chat { get; }

    /// <summary>`CTFHudKothTimeStatus`.</summary>
    public TfHudKothTimeStatus KothTimeStatus { get; }

    /// <summary>Every event any element's `ListenForGameEvent` asked for — the only ones the feed resolves.</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(
        [
            .. TfHudDeathNotice.ListensFor, .. TfHudTimeStatus.ListensFor, .. TfHudChat.ListensFor, .. TfHudMatchStatus.ListensFor,
            .. TfHudPlayerClass.ListensFor,
        ],
        StringComparer.Ordinal);

    /// <summary>`CTFHudMatchStatus`, which carries the round timer.</summary>
    public TfHudMatchStatus MatchStatus { get; }

    /// <summary>`CSecondaryTargetID`: the local medic's heal target, or the local player's healer.</summary>
    public TfSecondaryTargetId SecondaryTargetId { get; }

    /// <summary>`CMainTargetID`: a living local player's own crosshair target.</summary>
    public TfMainTargetId MainTargetId { get; }

    /// <summary>`CSpectatorTargetID`.</summary>
    public TfSpectatorTargetId SpectatorTargetId { get; }

    /// <summary>`CHudTFCrosshair`.</summary>
    public TfHudCrosshair Crosshair { get; }

    /// <summary>`CHudWeapon`.</summary>
    public TfHudWeapon Weapon { get; }

    /// <summary>`CTFHudDeathNotice`.</summary>
    public TfHudDeathNotice DeathNotice { get; }

    /// <summary>`CTFClientScoreBoardDialog`, shown only while <see cref="Frame"/>'s <c>showScoreboard</c> is held.</summary>
    public TfClientScoreBoardDialog Scoreboard { get; }

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
    /// <param name="userMessages">The chat's user messages read since the last frame, or null for none.</param>
    /// <param name="showScoreboard">
    /// Whether <c>+showscores</c> is currently held — `ShowPanel( PANEL_SCOREBOARD, ... )`'s argument, decided by the
    /// view from <see cref="ConfigConsole.IsHeld"/> rather than read here.
    /// </param>
    public void Frame(
        HudState state,
        IReadOnlyList<HudGameEvent>? events = null,
        bool reset = false,
        IReadOnlyList<Core.Scene.SceneUserMessage>? userMessages = null,
        bool showScoreboard = false)
    {
        // `system()->GetCurrentTime()`, which a `RichText` fades on.
        VguiRichText.HudClock = state.RealTime;

        // `CMDLPanel::GetAutoPlayTime()` — the model panel's cycle clock is real time too (mdlpanel.cpp:638) — and its
        // particles step by `gpGlobals->frametime` (basemodel_panel.cpp:908), the game clock's step, 0 when paused.
        TfPlayerModelPanel modelPanel = PlayerStatus.PlayerClass.PlayerModelPanel;

        modelPanel.RealTimeSeconds = state.RealTime;
        modelPanel.FrameTime = Math.Max(0f, state.CurTime - _lastCurTime);
        modelPanel.ParticleSystems = Viewport.ParticleSystems;
        modelPanel.ParticleMaterials = Viewport.ParticleMaterials;
        _lastCurTime = state.CurTime;

        if (_context is null || !ReferenceEquals(_context.Surface, _host.List))
        {
            _context = _host.LoadScheme(SchemePath);
            (Viewport.Wide, Viewport.Tall) = (_host.Wide, _host.Tall);
            Viewport.Context = _context;

            // The chat's own scheme, which `CBaseHudChat` loads and sets on itself and its history (hud_basechat.cpp:611).
            Chat.SchemeContext = _host.LoadScheme(ChatSchemePath);

            // `CHud::Init` loads the icons once, so a font icon keeps the size it measured at the first screen.
            Viewport.Icons ??= HudTextures.Load(_context);

            // `CBaseViewport::ReloadScheme`: the animation scripts, then the layout.
            Viewport.LoadHudAnimations(_context);
            Viewport.LoadControlSettings(LayoutPath, _context);
            Viewport.InvalidateLayout(reloadScheme: true);

            // The scheme applied now, as the game applies it at `CHud::Init` long before any packet: an element caches
            // icons and fonts there, and the events below may be the first thing it handles.
            VguiLayout.SolveTraverse(Viewport, _context);

            // **Forced rather than left to `SolveTraverse`'s own scheme pass**, which only visits VISIBLE children
            // (`VguiLayout.SchemeSettingsTraverse`) — a simplification this port's tree walk makes that real vgui's
            // scheme reload does not. The scoreboard starts hidden, so without this its `.res` and score font would
            // never load until the first time TAB is held, which is also the first frame it needs to paint correctly.
            Scoreboard.ApplySchemeSettings(_context);
        }

        if (reset)
        {
            DeathNotice.Clear();
        }

        // Events are dispatched as their packets are read, before the frame's think and paint — each to the listeners that
        // asked for it, as `IGameEventManager` delivers.
        foreach (HudGameEvent fired in events ?? [])
        {
            if (TfHudDeathNotice.ListensFor.Contains(fired.Event.Name))
            {
                DeathNotice.HandleGameEvent(fired with { RealTime = state.RealTime });
            }

            if (TfHudTimeStatus.ListensFor.Contains(fired.Event.Name))
            {
                MatchStatus.TimePanel.HandleGameEvent(fired);
                KothTimeStatus.BluePanel.HandleGameEvent(fired);
                KothTimeStatus.RedPanel.HandleGameEvent(fired);
            }

            if (TfHudChat.ListensFor.Contains(fired.Event.Name))
            {
                Chat.HandleGameEvent(fired.Event, state);
            }

            if (TfHudMatchStatus.ListensFor.Contains(fired.Event.Name))
            {
                MatchStatus.HandleGameEvent(fired);
            }

            if (TfHudPlayerClass.ListensFor.Contains(fired.Event.Name))
            {
                PlayerStatus.PlayerClass.HandleGameEvent(fired.Event, state);
            }
        }

        // `localplayer_changedisguise` is fired client-side by `C_TFPlayer::OnDataChanged` (c_tf_player.cpp:4788-4797), so
        // no demo carries it: it is derived here from the local player's last and current state, before the think.
        // **Interpolated:** once per frame rather than per packet, so two changes inside one frame fire once.
        ScenePlayer? local = state.HasLocalPlayer ? state.Player(state.LocalIndex) : null;

        if (!reset && _lastLocal is { } before && local is { } after
            && TfHudPlayerClass.LocalPlayerChangeDisguise(before, after) is { } disguised)
        {
            PlayerStatus.PlayerClass.HandleGameEvent(
                new SceneGameEvent(0, "localplayer_changedisguise", new Dictionary<string, object?> { ["disguised"] = disguised }, new Dictionary<int, Core.Net.PlayerInfo>()),
                state);
        }

        _lastLocal = local;

        // `HOOK_HUD_MESSAGE`: the chat's user messages, as they are read.
        foreach (Core.Scene.SceneUserMessage message in userMessages ?? [])
        {
            Chat.HandleUserMessage(message, state);
        }

        Viewport.Think(state);

        // `CTFClientScoreBoardDialog::ShowPanel`/`OnTick` (tf_clientscoreboard.cpp:400, :939-951): shown or hidden
        // directly by the view rather than `CHud::Think`'s `ShouldDraw`, and rebuilt from this tick's state on every
        // frame it is shown — TF's own `Update()` override drops `CClientScoreBoardDialog`'s 1-second throttle
        // (`NeedsUpdate`, ClientScoreBoardDialog.cpp:260-301) and runs unconditionally from `OnTick` instead, and a
        // demo tick is already coarser than a real frame, so there is no throttle left to reproduce.
        Scoreboard.Visible = showScoreboard;

        if (showScoreboard)
        {
            Scoreboard.UpdateTeamInfo(state);
            Scoreboard.UpdatePlayerList(state.ScoreboardPlayers, state.Names, state.LocalIndex);
        }

        VguiLayout.SolveTraverse(Viewport, _context);

        // A seek lands where the game, skipping forward through every packet, would have had the last
        // `teamplay_update_timer`; the feed replays only a short window, so the panels it sets are brought up to date here.
        // **Interpolated:** they are set from this tick's rules, where the game used the rules at that event.
        if (reset)
        {
            MatchStatus.TimePanel.RefreshExtraTimePanels();
            KothTimeStatus.BluePanel.RefreshExtraTimePanels();
            KothTimeStatus.RedPanel.RefreshExtraTimePanels();
        }
        Viewport.PaintTraverse(_host.List, _context);
    }
}
