using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// `CTFSpectatorGUI` (game/client/tf/vgui/tf_spectatorgui.cpp) over `CSpectatorGUI` (game_controls/spectatorgui.cpp): the
/// viewport panel `specgui` — the spectator's black bars, or in tournament mode both teams' player panels (B516).
/// </summary>
/// <remarks>
/// <para>
/// **When it shows.** A viewport panel, not a `CHudElement`: `CBaseViewport::FireGameEvent( "game_newmap" )` shows it when
/// `engine->IsHLTV()` (baseviewport.cpp:700-707), and `CSpectatorGUI::OnThink` hides it under the match summary
/// (spectatorgui.cpp, `TF_CLIENT_DLL`). On a point-of-view demo the server shows it with a `VGUIMenu` user message when the
/// recorder starts observing; that path is not modelled, so only a SourceTV recording draws it here.
/// </para>
/// <para>
/// **Which layout.** `GetResFile` (tf_spectatorgui.cpp:1037): `SpectatorTournament.res` when <see cref="InTournamentGui"/>,
/// else `Spectator.res`. `InTournamentGUI` (:1060) needs `mp_tournament`, NOT `IsCompetitiveMode()` (a matchmaking match),
/// and `cl_use_tournament_specgui` — an archived client ConVar whose default is 0 — or MvM. So a stock client draws the plain
/// bars over every STV demo; the tournament panels are what a watcher who set the ConVar sees.
/// </para>
/// <para>
/// **Not modelled:** coaching (`SpectatorCoach.res`, the student's health), the Steam Controller layout and hint icons, the
/// carried-item panel (`cl_spec_carrieditems`, a `CItemModelPanel`), the key-binding labels' text, MvM's centred row and
/// bot hiding, the lobby (GC) team and connect timer of `CTFPlayerPanel::Update`, the ready-up images, and the overview map's
/// trimming of the top bar.
/// </para>
/// </remarks>
public sealed class TfSpectatorGui : VguiEditablePanel
{
    /// <summary>`Resource/UI/Spectator.res`.</summary>
    public const string SpectatorRes = "Resource/UI/Spectator.res";

    /// <summary>`Resource/UI/SpectatorTournament.res`.</summary>
    public const string TournamentRes = "Resource/UI/SpectatorTournament.res";

    /// <summary>`CTFSpectatorGUI::GetBlackBarColor` (tf_spectatorgui.h:68).</summary>
    public static readonly (byte, byte, byte, byte) BlackBarColor = (52, 48, 45, 255);

    /// <summary>`m_flNextPlayerPanelUpdate`'s step: `curtime + 0.1f` (tf_spectatorgui.cpp:332).</summary>
    private const float PlayerPanelInterval = 0.1f;

    // `TF_TEAM_RED`, `TF_TEAM_BLUE`, `TEAM_SPECTATOR`, `FIRST_GAME_TEAM`.
    private const int TeamRed = SceneTeams.Red;
    private const int TeamBlue = SceneTeams.Blu;
    private const int TeamSpectator = 1;

    /// <summary>`g_ClassDefinesRemap` (tf_classmenu.cpp:1391), walked from `TF_FIRST_NORMAL_CLASS` to `TF_LAST_NORMAL_CLASS`.</summary>
    private static readonly int[] ClassDefinesRemap = [0, 1, 3, 7, 4, 6, 9, 5, 2, 8, 10];

    private readonly List<TfPlayerPanel> _playerPanels = [];
    private KeyValuesTree? _playerPanelKvs;
    private bool _reapplyPlayerPanelKvs;
    private bool _prevTournamentMode;
    private float _nextPlayerPanelUpdate;
    private string? _loadedRes;

    /// <summary>`CTFSpectatorGUI( pViewPort )`: the base's bars and label, then TF's labels, then the layout variables.</summary>
    /// <param name="parent">The viewport.</param>
    public TfSpectatorGui(VguiPanel? parent)
        : base(parent, "specgui")
    {
        // `CSpectatorGUI` (spectatorgui.cpp:408): hidden, proportional, the bars and the hidden player label.
        Visible = false;
        Proportional = true;
        TopBar = new VguiPanel(this, "topbar");
        BottomBarBlank = new VguiPanel(this, "bottombarblank");
        PlayerLabel = new VguiLabel(this, "playerlabel") { Visible = false };
        PaintBorderEnabled = false;
        PaintBackgroundEnabled = false;

        // `CTFSpectatorGUI` (tf_spectatorgui.cpp:86-103).
        _ = new TfExLabel(this, "BuyBackLabel");
        ReinforcementsLabel = new VguiLabel(this, "ReinforcementsLabel");
        _ = new VguiLabel(this, "ClassOrTeamLabel");
        _ = new VguiLabel(this, "CycleTargetFwdKeyLabel");
        _ = new VguiLabel(this, "CycleTargetRevKeyLabel");
        _ = new VguiLabel(this, "MapLabel");

        // `CPanelAnimationVarAliasType( int, ..., "proportional_int" )` (tf_spectatorgui.h:132-141).
        foreach (string variable in LayoutVariables)
        {
            DeclareAnimationVar(variable, VguiPanelVarType.ProportionalInt, "0");
        }
    }

    private static readonly string[] LayoutVariables =
    [
        "team1_player_base_offset_x", "team1_player_base_x", "team1_player_base_y", "team2_player_base_x",
        "team2_player_base_offset_x", "team2_player_base_y", "team1_player_delta_x", "team1_player_delta_y",
        "team2_player_delta_x", "team2_player_delta_y",
    ];

    /// <inheritdoc/>
    public override string ClassName => "CTFSpectatorGUI";

    /// <summary>`m_pTopBar`.</summary>
    public VguiPanel TopBar { get; }

    /// <summary>`m_pBottomBarBlank`.</summary>
    public VguiPanel BottomBarBlank { get; }

    /// <summary>`m_pPlayerLabel` — never shown by TF: `ShouldShowPlayerLabel` answers false (tf_spectatorgui.h:65).</summary>
    public VguiLabel PlayerLabel { get; }

    /// <summary>`m_pReinforcementsLabel`.</summary>
    public VguiLabel ReinforcementsLabel { get; }

    /// <summary>`m_PlayerPanels`.</summary>
    public IReadOnlyList<TfPlayerPanel> PlayerPanels => _playerPanels;

    /// <summary>The `.res` the last scheme pass loaded — `GetResFile()` at that moment.</summary>
    public string? LoadedRes => _loadedRes;

    /// <summary>`CTFSpectatorGUI::InTournamentGUI` (tf_spectatorgui.cpp:1060).</summary>
    /// <param name="state">The game state and ConVars.</param>
    /// <returns>Whether the tournament layout is used.</returns>
    public static bool InTournamentGui(HudState state)
    {
        // `IsInTournamentMode()`: `mp_tournament.GetBool()` (teamplayroundbased_gamerules.cpp:3488).
        bool tournament = state.ConVars.GetBool("mp_tournament");

        return tournament && !state.Rules.IsCompetitiveMode
            && (state.ConVars.GetBool("cl_use_tournament_specgui") || state.Rules.MannVsMachine);
    }

    /// <summary>`GetResFile` (tf_spectatorgui.cpp:1037), coaching and the Steam Controller aside.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>The layout file.</returns>
    public static string ResFile(HudState state) => InTournamentGui(state) ? TournamentRes : SpectatorRes;

    /// <summary>
    /// Whether the panel is up: shown at `game_newmap` on SourceTV (baseviewport.cpp:706), hidden under the match summary
    /// (`CSpectatorGUI::OnThink`).
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it is visible.</returns>
    public static bool IsShown(HudState state) => state.InGame && state.IsHltv && !state.Rules.ShowMatchSummary;

    /// <inheritdoc/>
    /// <remarks>`CTFSpectatorGUI::ApplySettings` (:244): `playerpanels_kv` kept for every player panel made later.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        base.ApplySettings(block, context);

        if (block.Find("playerpanels_kv") is { } kvs)
        {
            // `CopySubkeys`: each load parses a new tree, so holding this one is the same as copying it.
            _playerPanelKvs = kvs;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// `CSpectatorGUI::ApplySchemeSettings` (spectatorgui.cpp): `GetResFile()` loaded (with `if_mvm`), the bars shown and
    /// painted black-bar brown, this panel transparent and borderless; then `CTFSpectatorGUI`'s (:211) reapply flag.
    /// </remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        bool visible = Visible;
        HudState state = HudViewport.Of(this)?.State ?? default;

        _loadedRes = ResFile(state);
        LoadControlSettings(_loadedRes, context, state.Rules.MannVsMachine ? ["if_mvm"] : null);
        BottomBarBlank.Visible = true;
        TopBar.Visible = true;

        base.ApplySchemeSettings(context);

        BgColor = (0, 0, 0, 0);
        TopBar.BgColor = BlackBarColor;
        BottomBarBlank.BgColor = BlackBarColor;
        PaintBorderEnabled = false;
        Border = null;

        _reapplyPlayerPanelKvs = true;
        _prevTournamentMode = InTournamentGui(state);

        // "Stay the same visibility as before the scheme reload."
        Visible = visible;
    }

    /// <inheritdoc/>
    /// <remarks>`CSpectatorGUI::PerformLayout` then `CTFSpectatorGUI::PerformLayout` (:167).</remarks>
    protected override void PerformLayout()
    {
        if (Parent is { } parent)
        {
            (X, Y, Wide, Tall) = (0, 0, parent.Wide, parent.Tall);
        }

        BottomBarBlank.Wide = Wide;
        BottomBarBlank.Tall = Tall - BottomBarBlank.Y;

        if (_reapplyPlayerPanelKvs)
        {
            _reapplyPlayerPanelKvs = false;

            if (_playerPanelKvs is not null && HudViewport.Of(this)?.Context is { } context)
            {
                foreach (TfPlayerPanel panel in _playerPanels)
                {
                    panel.ApplySettings(_playerPanelKvs, context);
                    panel.InvalidateLayout(reloadScheme: true);
                }
            }
        }

        UpdatePlayerPanels(HudViewport.Of(this)?.State ?? default);
    }

    /// <summary>
    /// `CBaseViewport`'s per-frame `Update` of a visible panel: `CSpectatorGUI::Update` then `CTFSpectatorGUI::Update`
    /// (:302) — the top bar, the reinforcements label, a layout flip, and the player panels every 0.1 s.
    /// </summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        bool shown = IsShown(state);

        if (shown && !Visible)
        {
            // `CTFSpectatorGUI::ShowPanel` (:768): the player panels rebuilt at once.
            _nextPlayerPanelUpdate = 0f;
        }

        Visible = shown;

        if (!shown)
        {
            return;
        }

        // `CSpectatorGUI::Update`: the top bar keeps its height and spans the screen.
        TopBar.Wide = Wide;
        (TopBar.X, TopBar.Y) = (0, 0);
        PlayerLabel.Visible = false;

        // `UpdateReinforcements` (:347): hidden for `IsHLTV()` and for any non-player.
        ReinforcementsLabel.Visible = false;

        bool tournamentGui = InTournamentGui(state);

        // "If we need to flip tournament mode, do it now" (:315).
        if (_prevTournamentMode != tournamentGui)
        {
            InvalidateLayout(reloadScheme: true);
            _prevTournamentMode = tournamentGui;
        }

        if (_nextPlayerPanelUpdate < state.CurTime || _nextPlayerPanelUpdate - state.CurTime > PlayerPanelInterval)
        {
            RecalculatePlayerPanels(state);
            _nextPlayerPanelUpdate = state.CurTime + PlayerPanelInterval;
        }
    }

    /// <summary>`RecalculatePlayerPanels` (:1071): one panel per connected RED or BLU player, in class-menu order.</summary>
    /// <param name="state">The game state.</param>
    public void RecalculatePlayerPanels(HudState state)
    {
        if (!InTournamentGui(state))
        {
            // "Delete any player panels we have, we've turned off the tourney GUI."
            foreach (TfPlayerPanel panel in _playerPanels)
            {
                OwnGroup.PanelRemoved(panel);
                panel.SetParent(null);
            }

            _playerPanels.Clear();
            return;
        }

        if (!state.HasLocalPlayer && !state.IsHltv)
        {
            return;
        }

        int localTeam = state.Team;
        bool mannVsMachine = state.Rules.MannVsMachine;
        int index = 0;
        List<SceneScoreboardPlayer> slots = [.. state.ScoreboardPlayers ?? []];
        slots.Sort((a, b) => a.EntityIndex.CompareTo(b.EntityIndex));

        for (int classSlot = 1; classSlot <= 9; classSlot++)
        {
            int currentClass = ClassDefinesRemap[classSlot];

            foreach (SceneScoreboardPlayer player in slots)
            {
                if (!player.Connected)
                {
                    continue;
                }

                int team = player.Team ?? 0;

                if ((team != localTeam && localTeam != TeamSpectator) || (team != TeamRed && team != TeamBlue)
                    || (mannVsMachine && team == TeamRed) || (player.PlayerClass ?? 0) != currentClass)
                {
                    continue;
                }

                if (_playerPanels.Count <= index)
                {
                    TfPlayerPanel panel = new(this, string.Create(CultureInfo.InvariantCulture, $"playerpanel{player.EntityIndex - 1}"));

                    if (_playerPanelKvs is not null && HudViewport.Of(this)?.Context is { } context)
                    {
                        panel.ApplySettings(_playerPanelKvs, context);
                    }

                    _playerPanels.Add(panel);
                }

                _playerPanels[index].SetPlayerIndex(player.EntityIndex, state);
                index++;
            }
        }

        for (int rest = index; rest < _playerPanels.Count; rest++)
        {
            _playerPanels[rest].SetPlayerIndex(0, state);
        }

        UpdatePlayerPanels(state);
    }

    /// <summary>`UpdatePlayerPanels` (:1144): each panel updated, then placed by team down its column when any changed.</summary>
    /// <param name="state">The game state.</param>
    public void UpdatePlayerPanels(HudState state)
    {
        bool needsLayout = false;

        foreach (TfPlayerPanel panel in _playerPanels)
        {
            needsLayout |= panel.Update(state);
        }

        if (!needsLayout)
        {
            return;
        }

        // "Try and always put the local player's team on team1, if he's in a team".
        int team1 = TeamBlue;
        int team2 = TeamRed;
        int localTeam = TfPlayerPanel.ResourceTeam(state, state.LocalIndex);

        if (localTeam is TeamRed or TeamBlue)
        {
            team1 = localTeam;
            team2 = team1 == TeamBlue ? TeamRed : TeamBlue;
        }

        int team1Count = 0;
        int team2Count = 0;
        int center = (int)(Wide * 0.5);

        foreach (TfPlayerPanel panel in _playerPanels)
        {
            if (panel.PlayerIndex <= 0)
            {
                panel.Visible = false;
                continue;
            }

            int team = TfPlayerPanel.ResourceTeam(state, panel.PlayerIndex);

            if (team == team1)
            {
                Place(panel, "team1", center, team1Count, 6 - team1Count);
                team1Count++;
            }
            else if (team == team2)
            {
                Place(panel, "team2", center, team2Count, 7 + team2Count);
                team2Count++;
            }
        }
    }

    private void Place(TfPlayerPanel panel, string team, int center, int count, int specIndex)
    {
        int offset = GetInt($"{team}_player_base_offset_x");
        int x = (offset != 0 ? center + offset : GetInt($"{team}_player_base_x")) + (count * GetInt($"{team}_player_delta_x"));
        int y = GetInt($"{team}_player_base_y") + (count * GetInt($"{team}_player_delta_y"));

        panel.SetSpecIndex(specIndex);
        (panel.X, panel.Y) = (x, y);
        panel.Visible = true;
    }
}

/// <summary>`CTFPlayerPanelGUIHealth` (tf_playerpanel.h:18): the spectator health cross under its own `.res`.</summary>
/// <param name="parent">The parent.</param>
/// <param name="name">The name.</param>
public sealed class TfPlayerPanelGuiHealth(VguiPanel? parent, string? name) : TfSpectatorGuiHealth(parent, name)
{
    /// <inheritdoc/>
    protected override string ResFilename => "resource/UI/SpectatorTournamentGUIHealth.res";
}

/// <summary>`CTFPlayerPanel` (game/client/tf/vgui/tf_playerpanel.cpp): one player's card in the tournament spectator GUI.</summary>
public sealed class TfPlayerPanel : VguiEditablePanel
{
    private const int TeamRed = SceneTeams.Red;
    private const int FirstGameTeam = 2;
    private const int ClassMedic = 5;
    private const int HealthDead = -1;
    private const int HealthNotInGame = -2;

    /// <summary>`tf_max_health_boost`'s value (`FCVAR_DEVELOPMENTONLY`, so always its default).</summary>
    private const float MaxHealthBoost = 1.5f;

    private static readonly string[] ClassImagesRed =
    [
        "class_portraits/all_class", "class_portraits/scout", "class_portraits/sniper", "class_portraits/soldier",
        "class_portraits/demoman", "class_portraits/medic", "class_portraits/heavy", "class_portraits/pyro",
        "class_portraits/spy", "class_portraits/engineer",
    ];

    private static readonly string[] ClassImagesBlue =
    [
        "class_portraits/all_class", "class_portraits/scout_blue", "class_portraits/sniper_blue", "class_portraits/soldier_blue",
        "class_portraits/demoman_blue", "class_portraits/medic_blue", "class_portraits/heavy_blue", "class_portraits/pyro_blue",
        "class_portraits/spy_blue", "class_portraits/engineer_blue",
    ];

    private TfClassImage? _classImage;
    private int _prevHealth = -999;
    private int _prevClass = -999;
    private bool _prevAlive;
    private int _prevRespawnWait = -999;
    private int _prevCharge = -1;
    private string _playerName = string.Empty;
    private int _team;

    /// <summary>`CTFPlayerPanel( parent, name )` (:50): the health cross and the ready images, and an empty charge.</summary>
    /// <param name="parent">The spectator GUI.</param>
    /// <param name="name">`playerpanel%d`.</param>
    public TfPlayerPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        HealthIcon = new TfPlayerPanelGuiHealth(this, "HealthIcon");
        _ = new VguiScalableImagePanel(this, "ReadyBG");
        _ = new VguiImagePanel(this, "ReadyImage");
        SetDialogVariable("chargeamount", string.Empty);
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFPlayerPanel";

    /// <summary>`m_pHealthIcon`.</summary>
    public TfPlayerPanelGuiHealth HealthIcon { get; }

    /// <summary>`m_iPlayerIndex`.</summary>
    public int PlayerIndex { get; private set; }

    /// <summary>`m_iSpecIndex`.</summary>
    public int SpecIndex { get; private set; }

    /// <summary>`m_pClassImage` — the `.res`'s `classimage`, or null.</summary>
    public TfClassImage? ClassImage => _classImage;

    /// <summary>`g_TF_PR->GetTeam( index )`: the resource's team for that slot, 0 when it lists none.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="index">The entity index.</param>
    /// <returns>The team.</returns>
    public static int ResourceTeam(HudState state, int index) => Slot(state, index)?.Team ?? (index == state.LocalIndex ? state.Team : 0);

    private static SceneScoreboardPlayer? Slot(HudState state, int index)
    {
        foreach (SceneScoreboardPlayer player in state.ScoreboardPlayers ?? [])
        {
            if (player.EntityIndex == index)
            {
                return player;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:304): the `classimage` found, then `Reset`.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        _classImage = FindChildByName("classimage") as TfClassImage;
        Reset();
    }

    /// <summary>`Reset` (:64).</summary>
    public void Reset()
    {
        _prevHealth = -999;
        _prevClass = -999;
        _prevAlive = false;
        _prevRespawnWait = -999;
        _prevCharge = -1;
    }

    /// <summary>`SetPlayerIndex` (:316), through `Setup` (:331): a new player resets the panel and names it.</summary>
    /// <param name="index">The entity index, or 0 for none.</param>
    /// <param name="state">The game state, for the name and team.</param>
    public void SetPlayerIndex(int index, HudState state)
    {
        string name = index <= 0 ? string.Empty : state.Names?.GetValueOrDefault(index) ?? string.Empty;

        if (PlayerIndex != index || !string.Equals(_playerName, name, StringComparison.Ordinal))
        {
            Reset();
            PlayerIndex = index;
            _playerName = name;
            SetDialogVariable("playername", name);
        }

        _team = ResourceTeam(state, index);

        if (PlayerIndex > 0)
        {
            UpdateBorder();
        }
    }

    /// <summary>`SetSpecIndex` (:356): the slot key's number, 1 through 12, else nothing.</summary>
    /// <param name="index">The index.</param>
    public void SetSpecIndex(int index)
    {
        SpecIndex = index;
        SetDialogVariable("specindex", index is > 0 and <= 12 ? index.ToString(CultureInfo.InvariantCulture) : string.Empty);
    }

    /// <summary>`Update` (:81): class, health, respawn time and charge from the player resource.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether anything changed that needs the GUI to lay the panels out again.</returns>
    public bool Update(HudState state)
    {
        if (!state.HasLocalPlayer && !state.IsHltv)
        {
            return false;
        }

        bool changed = false;
        bool observer = state.ObserverMode != 0;
        _team = PlayerIndex > 0 ? ResourceTeam(state, PlayerIndex) : 0;
        bool visible = _team >= FirstGameTeam;
        int respawnWait = -1;

        if (Visible != visible)
        {
            Visible = visible;
            changed = true;
        }

        if (!visible)
        {
            return changed;
        }

        int playerClass = -1;
        bool alive = false;
        int health = HealthNotInGame;
        SceneScoreboardPlayer? slot = Slot(state, PlayerIndex);

        if (PlayerIndex > 0 && slot is { } player)
        {
            playerClass = player.PlayerClass ?? 0;

            if (playerClass != 0)
            {
                alive = player.Alive;
                health = HealthDead;
            }

            if (alive)
            {
                health = player.Health;
            }

            // "Calc respawn time remaining": the resource's time is the server's clock.
            if (playerClass != 0 && !alive)
            {
                respawnWait = (int)(player.NextRespawnTime - state.ServerTime);

                if (respawnWait <= 0)
                {
                    respawnWait = -1;
                }
            }

            // "Hide class info from the other team?"
            if (!observer && state.Rules.IsCompetitiveMode && _team != ResourceTeam(state, state.LocalIndex))
            {
                playerClass = 0;
            }
        }

        if (_classImage is { } image)
        {
            if (_prevAlive != alive)
            {
                changed = true;
                _prevAlive = alive;
                image.DrawColor = alive ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)96, (byte)96, (byte)96, (byte)255);
                UpdateBorder();
            }

            if (_prevClass != playerClass)
            {
                changed = true;
                _prevClass = playerClass;
                image.SetImage(playerClass switch
                {
                    < 0 => "hud_connecting",
                    0 => "hud_class_not_chosen",
                    _ => (_team == TeamRed ? ClassImagesRed : ClassImagesBlue)[Math.Min(playerClass, 9)] + "_alpha",
                });
            }
        }

        if (health != _prevHealth)
        {
            _prevHealth = health;

            if (health == HealthNotInGame)
            {
                HealthIcon.Visible = false;
            }
            else
            {
                HealthIcon.Visible = true;

                if (health < 0)
                {
                    HealthIcon.SetHealth(-1, 1, 1);
                }
                else
                {
                    int maxHealth = slot?.MaxHealth ?? 1;

                    // "Hacky, but it'll work." — a float passed to an int parameter.
                    HealthIcon.SetHealth(health, maxHealth, (int)(maxHealth * MaxHealthBoost));
                    changed = true;
                }
            }
        }

        if (respawnWait != _prevRespawnWait)
        {
            _prevRespawnWait = respawnWait;

            if (respawnWait < 0)
            {
                SetDialogVariable("respawntime", string.Empty);
            }
            else
            {
                SetDialogVariable("respawntime", string.Create(CultureInfo.InvariantCulture, $"{respawnWait} s"));
                changed = true;
            }
        }

        int charge = playerClass == ClassMedic ? slot?.ChargeLevel ?? 0 : 0;

        if (charge != _prevCharge)
        {
            if (charge > 0)
            {
                SetDialogVariable("chargeamount", string.Create(CultureInfo.InvariantCulture, $"{charge}%"));
                changed = true;
            }
            else
            {
                SetDialogVariable("chargeamount", string.Empty);
            }

            _prevCharge = charge;
        }

        return changed;
    }

    /// <summary>`UpdateBorder` (:373): a dead player's plain border, a living one's in his team's colour.</summary>
    private void UpdateBorder()
    {
        if (HudViewport.Of(this)?.Context is not { } context)
        {
            return;
        }

        string living = _team == TeamRed ? "TFFatLineBorderRedBG" : "TFFatLineBorderBlueBG";

        Border = context.Borders.Get(_prevAlive ? living : "TFFatLineBorder");
    }
}
