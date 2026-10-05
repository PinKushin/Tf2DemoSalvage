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
    private bool _minMode;
    private HudState? _thinkState;
    private int _meterPlayer;
    private int _lastClass;
    private int _lastSpawnCounter;

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
        MatchStatus = new TfHudMatchStatus(Viewport, mdlCache);
        KothTimeStatus = new TfHudKothTimeStatus(Viewport);
        Chat = new TfHudChat(Viewport);

        // `CTFClientScoreBoardDialog` is a viewport panel added by `CBaseViewport::CreatePanelByName`, not a
        // `CHudElement` — `ShowPanel( PANEL_SCOREBOARD, ... )` drives its visibility directly rather than
        // `CHud::Think`'s per-element `ShouldDraw`, which is why it is not parented alongside the elements above and
        // starts hidden here rather than defaulting to `VguiPanel.Visible`'s true.
        Scoreboard = new TfClientScoreBoardDialog(Viewport) { Visible = false };
        ItemEffectMeters = new TfItemEffectMeterManager(Viewport);
    }

    /// <summary>`g_ItemEffectMeterManager`.</summary>
    public TfItemEffectMeterManager ItemEffectMeters { get; }

    /// <summary>`CHudChat`.</summary>
    public TfHudChat Chat { get; }

    /// <summary>`CTFHudKothTimeStatus`.</summary>
    public TfHudKothTimeStatus KothTimeStatus { get; }

    /// <summary>Every event any element's `ListenForGameEvent` asked for — the only ones the feed resolves.</summary>
    public static IReadOnlySet<string> ListensFor { get; } = new HashSet<string>(
        [
            .. TfHudDeathNotice.ListensFor, .. TfHudTimeStatus.ListensFor, .. TfHudChat.ListensFor, .. TfHudMatchStatus.ListensFor,
            .. TfHudPlayerClass.ListensFor, .. TfItemEffectMeterManager.ListensFor,
        ],
        StringComparer.Ordinal);

    /// <summary>Every model a HUD model panel will draw, for precaching.</summary>
    /// <returns>The class panel's, then the match doors' and the round sign's.</returns>
    public IEnumerable<string> ModelsToPrecache() =>
    [
        .. PlayerStatus.PlayerClass.PlayerModelPanel.ModelsToPrecache(),
        .. MatchStatus.MatchStartModelPanel.ModelsToPrecache(),
        .. MatchStatus.RoundSignModel.ModelsToPrecache(),
    ];

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
        // **No `ClientScheme.res`, no HUD.** An install with no files opens as an empty `GameContent`, not a null one, and
        // an empty scheme's `Panel.BgColor` falls back to opaque white, so the viewport panel painted the whole screen
        // white — the CI runner's capture, one colour with every pixel lit. The game never runs its HUD without the file.
        if (_context is null && _host.Read(SchemePath) is null)
        {
            return;
        }

        // `system()->GetCurrentTime()`, which a `RichText` fades on.
        VguiRichText.HudClock = state.RealTime;

        // `CMDLPanel::GetAutoPlayTime()` — the model panel's cycle clock is real time too (mdlpanel.cpp:638) — and its
        // particles step by `gpGlobals->frametime` (basemodel_panel.cpp:908), the game clock's step, 0 when paused.
        TfPlayerModelPanel modelPanel = PlayerStatus.PlayerClass.PlayerModelPanel;

        modelPanel.RealTimeSeconds = state.RealTime;
        modelPanel.FrameTime = Math.Max(0f, state.CurTime - _lastCurTime);
        MatchStatus.MatchStartModelPanel.FrameTime = modelPanel.FrameTime;
        MatchStatus.RoundSignModel.FrameTime = modelPanel.FrameTime;
        modelPanel.ParticleSystems = Viewport.ParticleSystems;
        modelPanel.ParticleMaterials = Viewport.ParticleMaterials;
        _lastCurTime = state.CurTime;

        // `cl_hud_minmode`'s change callback runs `hud_reloadscheme` (clientmode_tf.cpp:284-287), which this reload is; the
        // ConVars are in place first, as the engine's are before any `.res` loads.
        Viewport.SetConVars(state.ConVars);
        bool minMode = state.ConVars.GetBool("cl_hud_minmode");

        if (_context is null || !ReferenceEquals(_context.Surface, _host.List) || minMode != _minMode)
        {
            _minMode = minMode;
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
            Crosshair.LevelShutdown();
        }

        // The host frame (engine.dll `_Host_RunFrame`, 0x1801a4570) runs `_Host_RunFrame_Input` (0x1801a5b90) first, whose
        // `ClientDLL_ProcessInput` (0x18006ed60) calls `HudProcessInput` and so `CHud::Think` (hud.cpp:1003); only then does
        // `_Host_RunFrame_Client` (0x1801a5860) read the frame's packets in `CL_ReadPackets` (0x18008d1f0). So the HUD's
        // show-or-hide sees the state the LAST frame's packets left. **Interpolated:** the first frame, and the first after
        // a seek, think on this frame's state, where the game's reloaded client would think on none.
        Viewport.Think(reset ? state : _thinkState ?? state);
        Viewport.SetState(state);
        _thinkState = state;

        // Events are dispatched as their packets are read, after the HUD's think and before the render stage — each to the
        // listeners that asked for it, as `IGameEventManager` delivers.
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

            if (TfHudCrosshair.ListensFor.Contains(fired.Event.Name))
            {
                Crosshair.HandleGameEvent(fired);
            }

            if (TfHudPlayerClass.ListensFor.Contains(fired.Event.Name))
            {
                PlayerStatus.PlayerClass.HandleGameEvent(fired.Event, state);
            }

            if (TfItemEffectMeterManager.ListensFor.Contains(fired.Event.Name))
            {
                ItemEffectMeters.HandleGameEvent(fired.Event, state);
            }
        }

        LocalPlayerLifecycle(state, reset);

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
            // `USER_MESSAGE( PlayerPickupWeapon )` fires `localplayer_pickup_weapon` client-side (clientmode_tf.cpp:2469-2475).
            if (message.Name == Core.Scene.SceneUserMessage.PlayerPickupWeapon)
            {
                SceneGameEvent pickup = ClientEvent(message.Tick, "localplayer_pickup_weapon");

                PlayerStatus.PlayerClass.HandleGameEvent(pickup, state);
                ItemEffectMeters.HandleGameEvent(pickup, state);
                continue;
            }

            Chat.HandleUserMessage(message, state);
        }

        // `C_TFPlayer::ClientThink`'s `g_ItemEffectMeterManager.Update( this )` for the local player (c_tf_player.cpp:6021),
        // from `SimulateEntities` at `FRAME_RENDER_START` (cdll_client_int.cpp:2005, :2207, :2279) — which `SCR_UpdateScreen`
        // (0x1800e8b40) raises in `_Host_RunFrame_Render` (0x1801a5d30), after the packets.
        ItemEffectMeters.ScoreboardVisible = showScoreboard;
        ItemEffectMeters.Update(local);

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

    /// <summary>An event the client fires itself, with no fields — `FireEventClientSide`.</summary>
    private static SceneGameEvent ClientEvent(int tick, string name) =>
        new(tick, name, new Dictionary<string, object?>(), new Dictionary<int, Core.Net.PlayerInfo>());

    /// <summary>
    /// What the local `C_TFPlayer` does for the item effect meters as its data changes: `OnPlayerClassChange` sets the meters'
    /// player (c_tf_player.cpp:4536, :5257), a new `m_iSpawnCounter` runs `ClientPlayerRespawn`, which fires
    /// `localplayer_respawn` (:4543, :7951), and the entity's destructor clears them (:4072).
    /// </summary>
    /// <remarks>
    /// A seek restarts playback, so the local player is made again: its `m_iOldPlayerClass` and `m_iOldSpawnCounter` start
    /// from a new entity's 0. **Interpolated:** once per frame rather than per packet.
    /// </remarks>
    private void LocalPlayerLifecycle(HudState state, bool reset)
    {
        ScenePlayer? local = state.HasLocalPlayer ? state.Player(state.LocalIndex) : null;

        if (reset || local?.EntityIndex != _meterPlayer)
        {
            if (_meterPlayer != 0)
            {
                ItemEffectMeters.ClearExistingMeters();
            }

            _meterPlayer = local?.EntityIndex ?? 0;
            _lastClass = 0;
            _lastSpawnCounter = 0;
        }

        if (local is not { } player)
        {
            return;
        }

        if ((player.PlayerClass ?? 0) != _lastClass)
        {
            _lastClass = player.PlayerClass ?? 0;
            ItemEffectMeters.SetPlayer(player);
        }

        if ((player.SpawnCounter ?? 0) != _lastSpawnCounter)
        {
            _lastSpawnCounter = player.SpawnCounter ?? 0;
            ItemEffectMeters.HandleGameEvent(ClientEvent(0, "localplayer_respawn"), state);
        }
    }
}
