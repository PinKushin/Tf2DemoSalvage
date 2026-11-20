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
/// recorder starts observing, carried as <see cref="HudState.SpectatorGuiShown"/>.
/// </para>
/// <para>
/// **Which layout.** `GetResFile` (tf_spectatorgui.cpp:1037): `SpectatorTournament.res` when <see cref="InTournamentGui"/>,
/// else `Spectator.res`. `InTournamentGUI` (:1060) needs `mp_tournament`, NOT `IsCompetitiveMode()` (a matchmaking match),
/// and `cl_use_tournament_specgui` — an archived client ConVar whose default is 0 — or MvM. So a stock client draws the plain
/// bars over every STV demo; the tournament panels are what a watcher who set the ConVar sees.
/// </para>
/// <para>
/// **Not modelled:** the coached student's Steam avatar (a demo cannot fetch it), the Steam Controller layout and hint
/// icons (the watcher's input device; no controller drives this viewer, so the engine's keyboard branch is taken), the
/// carried-item panel (`cl_spec_carrieditems`, a `CItemModelPanel`), MvM's centred row and
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

    /// <summary>`Resource/UI/SpectatorCoach.res`, a coach's layout (:1040).</summary>
    public const string CoachRes = "Resource/UI/SpectatorCoach.res";

    /// <summary>`kInchesToMeters` (:324).</summary>
    private const float InchesToMeters = 0.0254f;

    /// <summary>`wzText[256]` (:328).</summary>
    private const int StudentDistanceChars = 256;

    /// <summary>`m_bCoaching`: the local player's `m_bIsCoaching` as last taken by <see cref="Update"/> (:314).</summary>
    private bool _coaching;

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
    private readonly TfHudObjectiveStatus? _objectiveStatus;
    private KeyValuesTree? _playerPanelKvs;
    private bool _reapplyPlayerPanelKvs;
    private bool _prevTournamentMode;
    private float _nextPlayerPanelUpdate;
    private string? _loadedRes;
    private bool _newPlayerPanels;

    /// <summary>The tip's step: `curtime + 10.0f` (:490).</summary>
    private const float TipInterval = 10f;

    /// <summary>`wzTipLabel[512]` (:483).</summary>
    private const int TipLabelChars = 512;

    /// <summary>`g_TFTips` (tf_tips.cpp), which a POV recorder's tip is drawn from (:484).</summary>
    private readonly TfTips _tips;

    /// <summary>`szMapName[32]` (:689), which `Q_strncpy` fills to one short of.</summary>
    private const int MapNameChars = 32;

    /// <summary>`wLabel[256]` (:688).</summary>
    private const int LabelChars = 256;

    // `OBS_ALLOW_TEAM`, `OBS_ALLOW_NONE` (shareddefs.h:510-511).
    private const int ObsAllowTeam = 1;
    private const int ObsAllowNone = 2;

    private float _nextTipChangeTime;
    private int _tipClass;
    private string _classOrTeamText = string.Empty;
    private VguiLabel? _switchCamModeKeyLabel;
    private readonly BoundText _classOrTeamBound = new();
    private readonly BoundText _switchBound = new();
    private readonly BoundText _fwdBound = new();
    private readonly BoundText _revBound = new();
    // `m_flNextItemPanelUpdate`, `m_iPrevItemShown`, `m_iFirstItemShown`, `m_bShownItems`, `m_hPrevItemPlayer` (:109-114),
    // and each player's observer target last seen, whose change is `spec_target_updated`. *Interpolated:* the engine's
    // handles also compare serials; here entity indices.
    private float _nextItemPanelUpdate;
    private int _prevItemShown;
    private int _firstItemShown;
    private bool _shownItems;
    private int? _prevItemPlayer;
    private readonly Dictionary<int, int> _observerTargets = [];
    private bool _reloadedOnFirstShow;

    /// <summary>`curtime + 8.0` (:959).</summary>
    private const float ItemPanelInterval = 8f;

    /// <summary>`GR_STATE_TEAM_WIN`.</summary>
    private const int RoundTeamWin = 5;

    private string? _mapLevel;
    private string? _mapFormat;
    private string _mapText = string.Empty;

    /// <summary>Whether a card was made since the last call, which clears it — for the host to fill it once laid out.</summary>
    /// <returns>Whether any card is new.</returns>
    public bool TakeNewPlayerPanels()
    {
        bool taken = _newPlayerPanels;

        _newPlayerPanels = false;
        return taken;
    }

    /// <summary>`CTFSpectatorGUI( pViewPort )`: the base's bars and label, then TF's labels, then the layout variables.</summary>
    /// <param name="parent">The viewport.</param>
    /// <param name="objectiveStatus">`GET_HUDELEMENT( CTFHudObjectiveStatus )`, which <see cref="ShowPanel"/> takes in; null for none.</param>
    public TfSpectatorGui(VguiPanel? parent, TfHudObjectiveStatus? objectiveStatus = null)
        : base(parent, "specgui")
    {
        _objectiveStatus = objectiveStatus;

        // A fresh `RandomInt` stream and no hours played: see `TfTips` for why both are interpolated.
        _tips = new TfTips(new Core.Primitives.UniformRandomStream());

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
        ClassOrTeamLabel = new VguiLabel(this, "ClassOrTeamLabel");
        CycleTargetFwdKeyLabel = new VguiLabel(this, "CycleTargetFwdKeyLabel");
        CycleTargetRevKeyLabel = new VguiLabel(this, "CycleTargetRevKeyLabel");
        MapLabel = new VguiLabel(this, "MapLabel");
        ItemPanel = new TfItemModelPanel(this, "itempanel");
        StudentHealth = new TfSpectatorGuiHealth(this, "StudentGUIHealth");

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

    /// <summary>`m_pClassOrTeamLabel`.</summary>
    public VguiLabel ClassOrTeamLabel { get; }

    /// <summary>`m_pCycleTargetFwdKeyLabel`.</summary>
    public VguiLabel CycleTargetFwdKeyLabel { get; }

    /// <summary>`m_pCycleTargetRevKeyLabel`.</summary>
    public VguiLabel CycleTargetRevKeyLabel { get; }

    /// <summary>`m_pMapLabel`: the top bar's "Map: NAME".</summary>
    public VguiLabel MapLabel { get; }

    /// <summary>`m_pItemPanel` (:102): the spectated player's items, one at a time (B756).</summary>
    public TfItemModelPanel ItemPanel { get; }

    /// <summary>`m_pStudentHealth` (:106): a coach's student's health, placed on the layout's `HealthPositioning`.</summary>
    public TfSpectatorGuiHealth StudentHealth { get; }

    /// <summary>
    /// `m_pSwitchCamModeKeyLabel`: null from the constructor (:97) and found by name from the loaded `.res` in
    /// `ApplySchemeSettings` (:217), so the label `UpdateKeyLabels` drives (:560) whenever the layout has one.
    /// </summary>
    public VguiLabel? SwitchCamModeKeyLabel => _switchCamModeKeyLabel;

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
    public static bool IsShown(HudState state) =>
        state.InGame && (state.IsHltv || state.SpectatorGuiShown) && !state.Rules.ShowMatchSummary;

    /// <summary>
    /// `C_BasePlayer::GetLocalPlayer()->GetTeamNumber()`. On SourceTV the local player is the SourceTV client itself, a
    /// spectator (team 1) — so a recording whose ServerInfo names no slot the timeline found still has one, on team 1.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <returns>The local team.</returns>
    public static int LocalTeam(HudState state) => state.IsHltv && !state.HasLocalPlayer ? TeamSpectator : state.Team;

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

        // `GetResFile` (:1036): the coach's layout before every other.
        _loadedRes = _coaching ? CoachRes : ResFile(state);
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

        // `m_pSwitchCamModeKeyLabel = dynamic_cast< Label* >( FindChildByName( "SwitchCamModeKeyLabel" ) )` (:217).
        _switchCamModeKeyLabel = FindChildByName("SwitchCamModeKeyLabel") as VguiLabel;

        // A coach's bars and hint (:231-254): the top bar see-through, the bottom bar and the class-or-team label hidden.
        // `m_pClassOrTeamKeyLabel` is null from the constructor (:98) and never found, so its half does nothing.
        if (_coaching)
        {
            TopBar.BgColor = (255, 255, 255, 0);
            BottomBarBlank.Visible = false;
            ClassOrTeamLabel.Visible = false;
        }

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

        // The student's health on `HealthPositioning`, which only the coach's layout has (:191-201).
        if (FindChildByName("HealthPositioning") is { } positioning)
        {
            (StudentHealth.X, StudentHealth.Y, StudentHealth.Wide, StudentHealth.Tall) =
                (positioning.X, positioning.Y, positioning.Wide, positioning.Tall);
            StudentHealth.ZPos = positioning.ZPos;
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
        // `spec_target_updated` (`FireGameEvent`, :906), heard whether shown or not: `C_BasePlayer::SetObserverTarget`, the
        // receive proxy of EVERY player's `m_hObserverTarget`, fires it when one arrives changed (c_baseplayer.cpp:598-609).
        // A second in the same frame finds the eight-second gate shut (:946), so one update stands for all.
        if (ObserverTargetsChanged(state))
        {
            UpdateItemPanel(state);
        }

        bool shown = IsShown(state);

        ShowPanel(shown);

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
        UpdateKeyLabels(state);
        UpdateStudent(state);

        if (_nextItemPanelUpdate < state.CurTime)
        {
            UpdateItemPanel(state);
        }

        bool tournamentGui = InTournamentGui(state);

        // "If we need to flip tournament mode, do it now" (:315).
        if (_prevTournamentMode != tournamentGui)
        {
            InvalidateLayout(reloadScheme: true);
            _prevTournamentMode = tournamentGui;
        }

        UpdateCoaching(state);

        if (_nextPlayerPanelUpdate < state.CurTime || _nextPlayerPanelUpdate - state.CurTime > PlayerPanelInterval)
        {
            RecalculatePlayerPanels(state);
            _nextPlayerPanelUpdate = state.CurTime + PlayerPanelInterval;
        }
    }

    /// <summary>
    /// `Update`'s coaching half (:313-331): the local player's `m_bIsCoaching` taken, with a scheme reload into
    /// <see cref="CoachRes"/> when it changes; then, while coaching a student, `student_distance` in whole metres.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <remarks>
    /// *Interpolated:* the student's `GetAbsOrigin()` is read off the players the frame carries, so a student out of the
    /// PVS — a dormant entity the client still holds — leaves the last distance standing.
    /// </remarks>
    private void UpdateCoaching(HudState state)
    {
        if (!state.HasLocalPlayer && !state.IsHltv)
        {
            return;
        }

        // SourceTV's own client never coaches.
        bool coaching = state.HasLocalPlayer && state.Coaching;

        if (_coaching != coaching)
        {
            _coaching = coaching;
            InvalidateLayout(reloadScheme: true);
        }

        if (_coaching && state.Student > 0 && state.Player(state.Student) is { } student && state.Player(state.LocalIndex) is { } local)
        {
            float dx = local.X - student.X;
            float dy = local.Y - student.Y;
            float dz = local.Z - student.Z;

            // `RoundFloatToInt`: to nearest, as the FPU and `cvtss2si` round.
            int metres = (int)MathF.Round(MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) * InchesToMeters);

            SetDialogVariable(
                "student_distance",
                VguiLocalize.ConstructString(
                    Localize("TR_DistanceToStudent"), StudentDistanceChars, metres.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// The "coaching stuff" closing `UpdateKeyLabels` (:714-785), for any layout: with a student, his name under
    /// `#TF_Coach_Student_Prefix`, the two coach commands bound, the crosshair hidden in eye, and his health shown;
    /// without one, the health hidden.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <remarks>
    /// Gated on `m_hStudent` alone, not on coaching, as the engine gates it. *Not ported:* the student's Steam avatar,
    /// which a demo cannot fetch (`VguiAvatarImagePanel` never draws). *Interpolated:* a student out of the PVS is not in
    /// the frame's players, so he reads as no student.
    /// </remarks>
    private void UpdateStudent(HudState state)
    {
        if (!state.HasLocalPlayer || state.Student <= 0 || state.Player(state.Student) is not { } student)
        {
            StudentHealth.Visible = false;
            return;
        }

        Func<string, string?>? localize = HudViewport.Of(this)?.Context?.Localize;

        SetDialogVariable(
            "student_name",
            VguiLocalize.ConstructString(
                localize?.Invoke("TF_Coach_Student_Prefix"), LabelChars, state.Names?.GetValueOrDefault(student.EntityIndex) ?? string.Empty));

        for (int slot = 1; slot <= 2; slot++)
        {
            // `wLabel[256] = L""`: an unfound token binds nothing.
            string? format = localize?.Invoke(string.Create(CultureInfo.InvariantCulture, $"TF_Coach_Slot{slot}Label"));

            SetDialogVariable(
                string.Create(CultureInfo.InvariantCulture, $"coach_command_{slot}"),
                string.IsNullOrEmpty(format) ? string.Empty : TfKeyBindings.Replace(format, state.KeyLookupBinding, localize));
        }

        // "don't show crosshair when viewing the world from the student's POV".
        if (FindChildByName("Crosshair") is { } crosshair)
        {
            crosshair.Visible = state.ObserverMode != Core.Scene.ObserverModes.InEye;
        }

        int health = student.EntityHealth ?? 0;
        int maxHealth = student.MaxHealth ?? 1;

        StudentHealth.SetHealth(health, maxHealth, HudState.GetMaxBuffedHealth(student.MaxHealthForBuffing ?? 1, maxHealth, health));
        StudentHealth.Visible = true;
    }

    /// <summary>
    /// `UpdateKeyLabels` (:452) outside the tournament layout (B733): the tip, the class-or-team hint, the camera keys and the
    /// map name, each set from the game in place of the `.res`'s placeholders (`map: cp_bridge`, `%tip%`). SourceTV shows
    /// `#Tip_HLTV`; a point-of-view recorder his desired class's tips through <see cref="TfTips"/>.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <remarks>
    /// **Not modelled:** the Steam Controller hint icons — `IsSteamControllerActive()` is the watcher's input device, and
    /// no controller drives this viewer, so the engine's own keyboard branch is the one taken.
    /// **Cheaper than the engine on purpose:** the engine rebuilds every string each frame; here each is rebuilt only when
    /// its format, the bindings or the level change, and a label is set only when its text differs.
    /// </remarks>
    private void UpdateKeyLabels(HudState state)
    {
        if (InTournamentGui(state))
        {
            return;
        }

        Func<string, string?>? localize = HudViewport.Of(this)?.Context?.Localize;

        // `C_TFPlayer::GetLocalTFPlayer()`: on SourceTV the SourceTV client itself, a spectator.
        bool hasPlayer = state.HasLocalPlayer || state.IsHltv;
        int localTeam = LocalTeam(state);

        // `m_Shared.GetDesiredPlayerClassIndex()` (:466): the recorder's networked desired class; `TF_CLASS_UNDEFINED` for
        // SourceTV's own client, which never asks for one.
        int tipClass = state.DesiredPlayerClass;

        // "if it's time to change the tip, or the player has changed desired class, update the tip" (:470) — and, as the
        // player panels' timer does, when a seek back left the next change further off than one step.
        if (state.CurTime >= _nextTipChangeTime || _nextTipChangeTime - state.CurTime > TipInterval || tipClass != _tipClass)
        {
            if (state.IsHltv)
            {
                if (localize?.Invoke("Tip_HLTV") is { } tip)
                {
                    SetDialogVariable("tip", tip);
                }
            }
            else
            {
                // `GetNextClassTip( iClass )` under `#Tip_Fmt` into `wzTipLabel[512]` (:483-487).
                string tip = _tips.NextClassTip(
                    tipClass, state.Rules.GameType == Core.Scene.SceneGameRules.GameTypeArena, localize, state.KeyLookupBinding);

                SetDialogVariable("tip", VguiLocalize.ConstructString(localize?.Invoke("Tip_Fmt"), TipLabelChars, tip));
            }

            _nextTipChangeTime = state.CurTime + TipInterval;
            _tipClass = tipClass;
        }

        if (hasPlayer)
        {
            string hint = "TF_Spectator_ChangeClass";

            if (state.IsHltv)
            {
                hint = "TF_Spectator_AutoDirector";
            }
            else if (localTeam == TeamSpectator && state.Rules.GameType != Core.Scene.SceneGameRules.GameTypeArena)
            {
                hint = "TF_Spectator_ChangeTeam";
            }

            // `IsInTraining()` first (:502): `wzTemp = L""`, which `UTIL_ReplaceKeyBindings` writes as an empty `wzFinal`
            // (cdll_util.cpp:872-875) — found, so it replaces the last text rather than leaving it.
            if (state.Rules.InTraining)
            {
                _classOrTeamText = string.Empty;
            }

            // `wzFinal` is a function static (:498): a token not found leaves the last text.
            else if (localize?.Invoke(hint) is { } format)
            {
                _classOrTeamText = _classOrTeamBound.For(format, state, localize);
            }

            SetIfChanged(ClassOrTeamLabel, _classOrTeamText);
        }

        // `m_bCoaching || !cl_hud_minmode` (:558): minmode hides the rest, except for a coach.
        if (!_coaching && state.ConVars.GetBool("cl_hud_minmode"))
        {
            return;
        }

        int forceCamera = state.ConVars.GetInt("mp_forcecamera");
        bool fadeToBlack = state.ConVars.GetBool("mp_fadetoblack");
        bool playing = hasPlayer && localTeam > TeamSpectator;

        // Found in `ApplySchemeSettings` (:217): a `.res` label the code does not make, so null when the layout has none.
        if (_switchCamModeKeyLabel is { } switchKey)
        {
            UpdateKeyLabel(
                switchKey, "SwitchCamModeLabel", playing && (forceCamera is ObsAllowTeam or ObsAllowNone || fadeToBlack),
                "TF_Spectator_SwitchCamModeKey", _switchBound, state, localize);
        }

        bool suppressCycle = playing && (fadeToBlack || forceCamera == ObsAllowNone);

        UpdateKeyLabel(
            CycleTargetFwdKeyLabel, "CycleTargetFwdLabel", suppressCycle, "TF_Spectator_CycleTargetFwdKey", _fwdBound, state, localize);
        UpdateKeyLabel(
            CycleTargetRevKeyLabel, "CycleTargetRevLabel", suppressCycle, "TF_Spectator_CycleTargetRevKey", _revBound, state, localize);

        // The map (:685): `GetMapDisplayName( Q_FileBase( engine->GetLevelName() ) )` into 32 chars, under `#Spec_Map`.
        string? mapFormat = localize?.Invoke("Spec_Map");

        if (!string.Equals(_mapLevel, state.LevelBaseName, StringComparison.Ordinal) || !ReferenceEquals(_mapFormat, mapFormat))
        {
            string display = TfMapDisplayName.Of(state.LevelBaseName ?? string.Empty);

            if (display.Length > MapNameChars - 1)
            {
                display = display[..(MapNameChars - 1)];
            }

            (_mapLevel, _mapFormat) = (state.LevelBaseName, mapFormat);
            _mapText = VguiLocalize.ConstructString(mapFormat, LabelChars, display);
        }

        SetIfChanged(MapLabel, _mapText);
    }

    /// <summary>`SetText`, only when the text differs — a set invalidates the layout.</summary>
    private static void SetIfChanged(VguiLabel label, string text)
    {
        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.SetText(text, null);
        }
    }

    /// <summary>One key label of `UpdateKeyLabels` (:560-679): hidden with its caption when suppressed, else shown and bound.</summary>
    private void UpdateKeyLabel(
        VguiLabel keyLabel, string captionName, bool suppressed, string token, BoundText bound, HudState state, Func<string, string?>? localize)
    {
        // Only a change of visibility touches the caption (:564, :577).
        if (keyLabel.Visible == suppressed)
        {
            keyLabel.Visible = !suppressed;

            if (FindChildByName(captionName) is VguiLabel caption)
            {
                caption.Visible = !suppressed;
            }
        }

        if (!suppressed)
        {
            // `wLabel[256] = L""`: an unfound token binds nothing.
            SetIfChanged(keyLabel, localize?.Invoke(token) is { } format ? bound.For(format, state, localize) : string.Empty);
        }
    }

    /// <summary>A `%command%` string with its keys bound, kept until its format or the bindings change.</summary>
    /// <remarks>
    /// The bindings are known by the lookup's target — the viewer's binding table, which a config reload replaces rather
    /// than edits. A lookup with no target (a static method) is never seen to change.
    /// </remarks>
    private sealed class BoundText
    {
        private string? _format;
        private object? _bindings;
        private string _text = string.Empty;

        public string For(string format, HudState state, Func<string, string?>? localize)
        {
            object? bindings = state.KeyLookupBinding?.Target;

            if (!ReferenceEquals(_format, format) || !ReferenceEquals(_bindings, bindings))
            {
                (_format, _bindings) = (format, bindings);
                _text = TfKeyBindings.Replace(format, state.KeyLookupBinding, localize);
            }

            return _text;
        }
    }

    /// <summary>
    /// `CTFSpectatorGUI::ShowPanel` (tf_spectatorgui.cpp:791): on a change, the objective status taken in as a child and made
    /// proportional when shown (:809-813) — so the control-point row paints over the bottom bar — and given back to the
    /// viewport when hidden (:869-872); on a show, the player panels rebuilt at once (:887-893). Then `BaseClass::ShowPanel`.
    /// </summary>
    /// <param name="show">Whether to show it.</param>
    /// <remarks>
    /// **Not ported:** the Engineer and Spy building panels moved under the top bar (:815-829, :874-882), which this viewer
    /// does not draw, the item attribute tracker's relayout and the replay message. The tip and key labels are set every
    /// <see cref="Update"/> instead (B733).
    /// </remarks>
    public void ShowPanel(bool show)
    {
        if (show != Visible)
        {
            if (_objectiveStatus is { } status)
            {
                if (show)
                {
                    status.SetParent(this);

                    // Redundant, as in the engine: `Panel::SetParent` already copies the new parent's flag
                    // (Panel.cpp:1462), and this panel is proportional. Kept to mirror :812.
                    status.Proportional = true;
                }
                else
                {
                    status.SetParent(Parent);
                }
            }

            if (show)
            {
                InvalidateLayout();
                _nextPlayerPanelUpdate = 0f;
                _nextItemPanelUpdate = 0f;

                // :887-893, before `BaseClass::ShowPanel`'s reload below.
                HudState state = HudViewport.Of(this)?.State ?? default;

                UpdateItemPanel(state);
                RecalculatePlayerPanels(state);
            }
        }

        // `CSpectatorGUI::ShowPanel` (SpectatorGUI.cpp:593-613): the first show — `m_iWasSteamController` still -1 — reloads
        // the scheme at once, which loads the `.res` again over what `UpdateItemPanel` just set.
        if (show && !Visible && !_reloadedOnFirstShow && HudViewport.Of(this)?.Context is { } context)
        {
            _reloadedOnFirstShow = true;
            InvalidateLayout(layoutNow: true, reloadScheme: true, context);
        }

        Visible = show;
    }

    /// <summary>Whether any player's networked `m_hObserverTarget` arrived different from the last frame's.</summary>
    /// <remarks>A first sighting counts when its handle is set: the client's starts invalid, so the proxy sees a change.</remarks>
    private bool ObserverTargetsChanged(HudState state)
    {
        bool changed = false;

        foreach (ScenePlayer player in state.Players ?? [])
        {
            int target = player.ObserverTarget ?? 0;

            changed |= _observerTargets.TryGetValue(player.EntityIndex, out int before) ? before != target : target != 0;
            _observerTargets[player.EntityIndex] = target;
        }

        return changed;
    }

    /// <summary>
    /// The item walk after a seek, which rebuilds the HUD as the game's reloaded client would find it: no player walked, no
    /// update due, and every observer target seen afresh.
    /// </summary>
    /// <remarks>*Interpolated:* a game playing to the same tick would be part-way through some player's walk.</remarks>
    public void ResetItemPanel()
    {
        _nextItemPanelUpdate = 0f;
        _prevItemShown = 0;
        _firstItemShown = 0;
        _shownItems = false;
        _prevItemPlayer = null;
        _observerTargets.Clear();
        ItemPanel.Visible = false;
    }

    /// <summary>
    /// `UpdateItemPanel` (:928): every eight seconds the spectated player's next item — weapons then wearables, by
    /// `GetInspectItem` — until the walk comes round to the first again, which hides the panel for that player.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <remarks>*Not ported:* `CTFStatPanel`, whose showing hides the panel; `ForceItemPanelCycle`, a key bind.</remarks>
    private void UpdateItemPanel(HudState state)
    {
        bool visible = false;

        if (state.RoundState != RoundTeamWin && state.ConVars.GetBool("cl_spec_carrieditems")
            && (state.HasLocalPlayer || state.IsHltv)
            && state.Player(state.ObserverTarget) is { } player
            && (!state.HasLocalPlayer || player.EntityIndex != state.LocalIndex))
        {
            if (_nextItemPanelUpdate != 0f && _nextItemPanelUpdate > state.CurTime && _prevItemPlayer == player.EntityIndex)
            {
                return;
            }

            // `TF_TEAM_PVE_INVADERS`: MvM's robots are BLU.
            if (state.Rules.MannVsMachine && player.Team == SceneTeams.Blu)
            {
                return;
            }

            if (_prevItemPlayer != player.EntityIndex)
            {
                _prevItemShown = 0;
                _firstItemShown = 0;
                _shownItems = false;
                _prevItemPlayer = player.EntityIndex;
            }

            _nextItemPanelUpdate = state.CurTime + ItemPanelInterval;

            // "Don't reshow the items for a player unless we're being forced to".
            if (!_shownItems)
            {
                visible = ShowNextItem(state, player);
            }
        }

        ItemPanel.Visible = visible;
    }

    /// <summary>The body of :964-1012: the next item into the panel, or the loop closed. Whether it shows.</summary>
    private bool ShowNextItem(HudState state, ScenePlayer player)
    {
        if (HudViewport.Of(this) is not { Items: { } schema } viewport)
        {
            return false;
        }

        TfItemView? item = InspectItem(player, schema, ref _prevItemShown);

        // "If we've looped, we're done. Hide the item."
        if (_firstItemShown != 0 && _firstItemShown == _prevItemShown)
        {
            _firstItemShown = 0;
            _prevItemShown = 0;
            _shownItems = true;
            return false;
        }

        if (item is null)
        {
            return false;
        }

        if (_firstItemShown == 0)
        {
            _firstItemShown = _prevItemShown;
        }

        Core.Scene.ScenePlayer? originalOwner = state.PlayerByAccountId(item.AccountId);

        // `FindControl<Label>( "ItemLabel" )`: a direct child. The spectated player's own account against the item's.
        if (ItemPanel.FindChildByName("ItemLabel", false) is VguiLabel itemLabel)
        {
            bool originalOwnerHolds = originalOwner is null || state.AccountId(player.EntityIndex) == item.AccountId;

            itemLabel.SetText(originalOwnerHolds ? "#FreezePanel_Item" : "#FreezePanel_ItemOtherOwner", Localize);
        }

        ItemPanel.SetDialogVariable("killername", state.Names?.GetValueOrDefault(player.EntityIndex) ?? string.Empty);

        if (originalOwner is { } owner)
        {
            ItemPanel.SetDialogVariable("ownername", state.Names?.GetValueOrDefault(owner.EntityIndex) ?? string.Empty);
        }

        ItemPanel.SetItem(item);

        // "force update description to get the correct panel size".
        ItemPanel.UpdateDescription();
        (ItemPanel.X, ItemPanel.Y) = (
            (int)(viewport.Wide - HudViewport.XRes(this, 10) - ItemPanel.Wide),
            (int)(viewport.Tall - HudViewport.YRes(this, 12) - ItemPanel.Tall));
        return true;
    }

    /// <summary>
    /// `C_TFPlayer::GetInspectItem( &amp;iLast )` (c_tf_player.cpp:10257): the item after the <paramref name="last"/>th — the
    /// valid weapons that are not hidden, then the valid wearables — else the first, as item 1; null with none.
    /// </summary>
    private static TfItemView? InspectItem(ScenePlayer player, ItemSchema schema, ref int last)
    {
        // Counting, as the engine does; only the chosen item is resolved into a view.
        SceneItem? first = null;
        int found = 0;

        foreach (bool weapons in (bool[])[true, false])
        {
            foreach (SceneItem carried in player.Items ?? [])
            {
                if (carried.IsWeapon != weapons || carried.DefinitionIndex is not { } definition || !schema.HasDefinition(definition)
                    || (weapons && schema.IsItemHidden(definition)))
                {
                    continue;
                }

                first ??= carried;

                // "Found the next item, we're done."
                if (++found > last)
                {
                    last = found;
                    return TfItemView.From(carried, schema);
                }
            }
        }

        // "If we didn't find an item, go back to the first one."
        if (first is not { } wrapped)
        {
            return null;
        }

        last = 1;
        return TfItemView.From(wrapped, schema);
    }

    /// <summary>`g_pVGuiLocalize->Find`, by token without its `#`.</summary>
    private string? Localize(string token) => HudViewport.Of(this)?.Context?.Localize?.Invoke(token);

    /// <summary>`RecalculatePlayerPanels` (:1071): one panel per connected RED or BLU player, in class-menu order.</summary>
    /// <param name="state">The game state.</param>
    public void RecalculatePlayerPanels(HudState state)
    {
        if (!InTournamentGui(state))
        {
            // "Delete any player panels we have, we've turned off the tourney GUI."
            foreach (TfPlayerPanel panel in _playerPanels)
            {
                HudViewport.DeletePanel(panel);
            }

            _playerPanels.Clear();
            return;
        }

        if (!state.HasLocalPlayer && !state.IsHltv)
        {
            return;
        }

        int localTeam = LocalTeam(state);
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
                    _newPlayerPanels = true;
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
        int localTeam = state.LocalIndex > 0 ? TfPlayerPanel.ResourceTeam(state, state.LocalIndex) : LocalTeam(state);

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
public class TfPlayerPanel : VguiEditablePanel
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

    /// <summary>`GetTeam()`: the resource's team for the player, as last read.</summary>
    public int Team => _team;

    /// <summary>The slot of a player in the resource, or null.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="index">The entity index.</param>
    /// <returns>The slot.</returns>
    protected static SceneScoreboardPlayer? ResourceSlot(HudState state, int index) => Slot(state, index);

    /// <summary>`GetTeam()` read again for the panel's player, as `CTFPlayerPanel::GetTeam` does each call.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>The team.</returns>
    protected int RefreshTeam(HudState state) => _team = PlayerIndex > 0 ? ResourceTeam(state, PlayerIndex) : 0;

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
    public virtual void Reset()
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
    public virtual bool Update(HudState state)
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

            // "Calc respawn time remaining": `flRespawnAt - gpGlobals->curtime` (:150). The client's curtime in playback runs
            // on its tick count, kept on the server's `net_Tick` — the clock `HudState.ServerTime` carries, and the one the
            // target ID's respawn subtracts too (TfTargetId). `cl_interp` delays where entities are drawn, not `curtime`, so
            // no lerp belongs here. **Interpolated:** the tick sync is in the closed engine and was not read for this.
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
                // No `bChanged` on the clear (tf_playerpanel.cpp:234-237): only setting a time asks for a layout. A clear
                // comes with a respawn, whose change of alive (:168) asks for it anyway.
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
    protected virtual void UpdateBorder()
    {
        if (HudViewport.Of(this)?.Context is not { } context)
        {
            return;
        }

        string living = _team == TeamRed ? "TFFatLineBorderRedBG" : "TFFatLineBorderBlueBG";

        Border = context.Borders.Get(_prevAlive ? living : "TFFatLineBorder");
    }
}
