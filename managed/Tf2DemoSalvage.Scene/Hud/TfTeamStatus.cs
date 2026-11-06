using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFTeamStatusPlayerPanel` (game/client/tf/tf_teamstatus.cpp:28): one portrait in the match HUD's team bar.</summary>
/// <remarks>
/// **Not ported:** Halloween's hell ghosts and a feigning spy marked dead (:100-119), which need the other player's
/// conditions, not decoded for them.
/// </remarks>
public sealed class TfTeamStatusPlayerPanel : TfPlayerPanel
{
    private const int FirstGameTeam = 2;
    private const int TeamRed = SceneTeams.Red;

    // `g_pszItemClassImagesRed` and `...Blue` (tf_shareddefs.cpp:1902-1946): a class, then its grey copy nine on.
    private static readonly string[] ClassNames = ["scout", "sniper", "soldier", "demoman", "medic", "heavy", "pyro", "spy", "engineer"];

    private readonly VguiContinuousProgressBar _healthBar;
    private readonly VguiContinuousProgressBar _overhealBar;
    private readonly VguiPanel _classImageBackground;
    private readonly VguiImagePanel _deathFlag;
    private int _previousTeam;
    private bool _previousAlive;
    private int _previousClass = -999;
    private int _previousHealth = -999;
    private int _previousRespawnWait = -999;
    private int? _previousState;

    /// <summary>`CTFTeamStatusPlayerPanel( parent, name )`: the two bars, the portrait's background and the death flag.</summary>
    /// <param name="parent">The team status.</param>
    /// <param name="name">`playerpanel%d`.</param>
    public TfTeamStatusPlayerPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        _healthBar = new VguiContinuousProgressBar(this, "healthbar");
        _overhealBar = new VguiContinuousProgressBar(this, "overhealbar");
        _classImageBackground = new VguiPanel(this, "classimagebg");
        _deathFlag = new VguiImagePanel(this, "DeathPanel");

        DeclareAnimationVar("color_portrait_bg_red_local_player", VguiPanelVarType.Color, "195 169 168 255");
        DeclareAnimationVar("color_portrait_bg_blue_local_player", VguiPanelVarType.Color, "168 169 195 255");
        DeclareAnimationVar("color_portrait_bg_red", VguiPanelVarType.Color, "119 62 61 255");
        DeclareAnimationVar("color_portrait_bg_blue", VguiPanelVarType.Color, "62 81 101 255");
        DeclareAnimationVar("color_portrait_bg_red_dead", VguiPanelVarType.Color, "49 44 42 255");
        DeclareAnimationVar("color_portrait_bg_blue_dead", VguiPanelVarType.Color, "44 49 51 255");
        DeclareAnimationVar("color_bar_health_high", VguiPanelVarType.Color, "84 191 58 255");
        DeclareAnimationVar("percentage_health_med", VguiPanelVarType.Real, "0.6", 0.6f);
        DeclareAnimationVar("color_bar_health_med", VguiPanelVarType.Color, "191 183 58 255");
        DeclareAnimationVar("percentage_health_low", VguiPanelVarType.Real, "0.3", 0.3f);
        DeclareAnimationVar("color_bar_health_low", VguiPanelVarType.Color, "191 58 58 255");
        DeclareAnimationVar("color_portrait_blend_dead_red", VguiPanelVarType.Color, "255 255 255 255");
        DeclareAnimationVar("color_portrait_blend_dead_blue", VguiPanelVarType.Color, "255 255 255 255");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFTeamStatusPlayerPanel";

    /// <summary>`GetPreviousTeam()`: `m_iTeam`.</summary>
    public int PreviousTeam => _previousTeam;

    /// <summary>`m_pHealthBar`.</summary>
    public VguiContinuousProgressBar HealthBar => _healthBar;

    /// <summary>The portrait a slot shows (:200-227): connecting, a silhouette, a dead enemy's grey class, or a team mate's class.</summary>
    /// <param name="team">`m_iTeam`.</param>
    /// <param name="playerClass">The class shown: -1 not connected, 0 hidden or none.</param>
    /// <param name="alive">Whether the player lives.</param>
    /// <param name="sameTeam">Whether the local player shares the team.</param>
    /// <param name="deadClass">`GetPlayerClassWhenKilled`.</param>
    /// <returns>The image name.</returns>
    public static string Portrait(int team, int playerClass, bool alive, bool sameTeam, int deadClass)
    {
        if (playerClass < 0)
        {
            return "hud_connecting";
        }

        if (playerClass == 0)
        {
            return !alive && !sameTeam && team >= FirstGameTeam && deadClass is > 0 and <= 9
                ? PortraitImage(team, deadClass, grey: true)
                : "class_portraits/silhouette_alpha";
        }

        return PortraitImage(team, playerClass, grey: !alive);
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        (_previousClass, _previousHealth, _previousRespawnWait, _previousAlive, _previousState) = (-999, -999, -999, false, null);
    }

    /// <inheritdoc/>
    /// <remarks>`UpdateBorder` (:49): none.</remarks>
    protected override void UpdateBorder()
    {
        // "do nothing": the team bar's portraits keep no border.
    }

    /// <inheritdoc/>
    /// <remarks>`Update` (:57).</remarks>
    public override bool Update(HudState state)
    {
        if (!state.Rules.Present || !state.HasLocalPlayer || state.Team < FirstGameTeam)
        {
            return false;
        }

        bool changed = false;
        int team = RefreshTeam(state);
        bool visible = team >= FirstGameTeam;
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

        bool sameTeam = team == ResourceTeam(state, state.LocalIndex);
        int playerClass = -1;
        bool alive = false;
        int health = -1;
        bool isLocal = false;
        SceneScoreboardPlayer? slot = ResourceSlot(state, PlayerIndex);

        if (PlayerIndex > 0)
        {
            isLocal = state.LocalIndex == PlayerIndex;
            playerClass = slot?.PlayerClass ?? 0;

            if (playerClass != 0)
            {
                alive = slot?.Alive ?? false;
            }

            if (alive)
            {
                health = slot?.Health ?? -1;
            }

            if (!alive && playerClass != 0 && slot is { } dead)
            {
                // `flRespawnAt - gpGlobals->curtime`, truncated to an int (:129-132); the clock the spectator panel reads.
                respawnWait = (int)(dead.NextRespawnTime - state.ServerTime);

                if (respawnWait <= 0)
                {
                    respawnWait = -1;
                }
            }

            // "Hide class info from the other team?"
            if (!sameTeam)
            {
                playerClass = 0;
            }
        }

        if (_previousTeam != team)
        {
            _previousTeam = team;
            changed = true;
        }

        string background = (team == TeamRed, isLocal, alive) switch
        {
            (true, true, _) => "color_portrait_bg_red_local_player",
            (true, false, true) => "color_portrait_bg_red",
            (true, false, false) => "color_portrait_bg_red_dead",
            (false, true, _) => "color_portrait_bg_blue_local_player",
            (false, false, true) => "color_portrait_bg_blue",
            _ => "color_portrait_bg_blue_dead",
        };

        _classImageBackground.BgColor = GetColor(background);

        if (base.ClassImage is { } image)
        {
            if (_previousAlive != alive)
            {
                changed = true;
                _previousAlive = alive;

                if (!alive)
                {
                    image.DrawColor = GetColor(team == TeamRed ? "color_portrait_blend_dead_red" : "color_portrait_blend_dead_blue");
                }

                _deathFlag.SetImage(team == TeamRed ? "../HUD/comp_player_status" : "../HUD/comp_player_status_blue");
                HudViewport.Of(this)?.Animations.StartAnimationSequence(this, alive ? "TeamStatus_PlayerAlive" : "TeamStatus_PlayerDead");
            }

            if (_previousClass != playerClass || changed)
            {
                changed = true;
                _previousClass = playerClass;
                image.SetImage(Portrait(team, playerClass, alive, sameTeam, slot?.PlayerClassWhenKilled ?? 0));
            }
        }

        if (health != _previousHealth)
        {
            _previousHealth = health;
            UpdateHealth(health, sameTeam, slot?.MaxHealth ?? 0);
            changed = true;
        }

        if (respawnWait != _previousRespawnWait)
        {
            _previousRespawnWait = respawnWait;
            SetDialogVariable("respawntime", respawnWait < 0 || !sameTeam ? string.Empty : respawnWait.ToString(CultureInfo.InvariantCulture));
            changed = true;
        }

        if (state.RoundState != _previousState)
        {
            _previousState = state.RoundState;
            changed = true;
        }

        return changed;
    }

    private static string PortraitImage(int team, int playerClass, bool grey)
    {
        string name = ClassNames[Math.Clamp(playerClass, 1, 9) - 1];
        string colour = team == TeamRed ? string.Empty : "_blue";
        string shade = grey ? "_grey" : string.Empty;

        return $"class_portraits/{name}{colour}{shade}_alpha";
    }

    private void UpdateHealth(int health, bool sameTeam, int maxHealth)
    {
        if (health <= 0 || !sameTeam)
        {
            _healthBar.Visible = false;
            _overhealBar.Visible = false;
            return;
        }

        float progress = health / (float)maxHealth;

        _healthBar.Visible = true;
        _healthBar.SetProgress(progress >= 1f ? 1f : progress);
        string colour = "color_bar_health_low";

        if (progress > GetFloat("percentage_health_med"))
        {
            colour = "color_bar_health_high";
        }
        else if (progress > GetFloat("percentage_health_low"))
        {
            colour = "color_bar_health_med";
        }

        _healthBar.FgColor = GetColor(colour);

        _overhealBar.Visible = progress > 1f;

        if (progress > 1f)
        {
            _overhealBar.SetProgress(progress - 1f);
        }
    }
}

/// <summary>`CTFTeamStatus` (game/client/tf/tf_teamstatus.cpp:327): the match HUD's bar of portraits, each team growing out from the centre.</summary>
/// <remarks>Valve ticks it every 100 ms (`AddTickSignal( GetVPanel(), 100 )`); this updates each think.</remarks>
/// <param name="parent">The match status.</param>
public sealed class TfTeamStatus(VguiPanel parent) : VguiEditablePanel(parent, "TeamStatus")
{
    private const int TeamBlue = 3;
    private const int TeamRed = 2;
    private const int FirstGameTeam = 2;
    private const int MaxPlayers = 101;

    private readonly List<TfTeamStatusPlayerPanel> _panels = [];
    private KeyValuesTree? _playerPanelKvs;
    private bool _team1GrowsWest;
    private bool _team2GrowsWest;
    private bool _declared;

    /// <inheritdoc/>
    public override string ClassName => "CTFTeamStatus";

    /// <summary>`m_PlayerPanels`.</summary>
    public IReadOnlyList<TfTeamStatusPlayerPanel> PlayerPanels => _panels;

    /// <summary>`ShouldDraw` (:512): a local player on a game team, with the match HUD.</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    public static bool ShouldDraw(HudState state)
    {
        if (!state.HasLocalPlayer || TfPlayerPanel.ResourceTeam(state, state.LocalIndex) < FirstGameTeam)
        {
            return false;
        }

        if (state.Rules.Present)
        {
            if ((TfMatchGroupDescription.For(state.Rules.MatchGroup) is { UsesMatchHud: false }) || !TfHudMatchStatus.ShouldUseMatchHud(state))
            {
                return false;
            }

            if (state.Rules.ShowMatchSummary)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>`RemapValClamped( iTeamCount, 6, 12, 6v6_gap, 12v12_gap )`, truncated to an int (:452).</summary>
    /// <param name="count">The team's players.</param>
    /// <param name="gap6">`6v6_gap`.</param>
    /// <param name="gap12">`12v12_gap`.</param>
    /// <returns>The gap.</returns>
    public static int Gap(int count, int gap6, int gap12) => (int)(gap6 + ((gap12 - gap6) * Math.Clamp((count - 6) / 6f, 0f, 1f)));

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:368): the grow directions and `playerpanels_kv`.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);

        DeclareOnce();
        base.ApplySettings(block, context);

        _team1GrowsWest = GrowsWest(block.Find("team1_grow_dir")?.Value, _team1GrowsWest);
        _team2GrowsWest = GrowsWest(block.Find("team2_grow_dir")?.Value, _team2GrowsWest);

        if (block.Find("playerpanels_kv") is { } kvs)
        {
            _playerPanelKvs = kvs;
        }
    }

    /// <inheritdoc/>
    /// <remarks>`PerformLayout` (:400): each team's panels from its base, the local player innermost.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();
        DeclareOnce();

        HudState state = HudViewport.Of(this)?.State ?? default;
        int blueCount = 0;
        int redCount = 0;

        foreach (TfTeamStatusPlayerPanel panel in _panels)
        {
            blueCount += panel.Team == TeamBlue ? 1 : 0;
            redCount += panel.Team == TeamRed ? 1 : 0;
        }

        int localTeam = state.HasLocalPlayer ? state.Team : 4;
        int localIndex = state.HasLocalPlayer ? state.LocalIndex : -1;
        int blueProcessed = 0;
        int redProcessed = 0;

        foreach (TfTeamStatusPlayerPanel panel in _panels)
        {
            if (panel.PlayerIndex <= 0)
            {
                panel.Visible = false;
                continue;
            }

            bool local = localIndex == panel.PlayerIndex;
            bool blue = panel.Team == TeamBlue;
            int count = blue ? blueCount : redCount;
            int processed = blue ? blueProcessed : redProcessed;
            int gap = Gap(count, GetInt("6v6_gap"), GetInt("12v12_gap"));
            int index = SlotIndex(local, panel.Team == localTeam, processed);
            string maxExpand = blue ? "team1_max_expand" : "team2_max_expand";
            int wide = count <= 6 ? GetInt("max_size") : (GetInt(maxExpand) - (gap * (count - 1))) / count;
            int offset = (gap + wide) * index;
            int baseX = GetInt(blue ? "team1_base_x" : "team2_base_x");

            if (wide != panel.Wide && _playerPanelKvs is not null && HudViewport.Of(this)?.Context is { } context)
            {
                // "We change the width in the KV's then reapply settings so the children will do their magical
                // proportionaltoparent layout." `GetProportionalNormalizedValueEx`: back to the 480-tall scale.
                panel.ApplySettings(_playerPanelKvs, context);
                panel.Wide = wide;
                panel.InvalidateLayout(reloadScheme: true);
                panel.Update(state);
            }

            if (!local)
            {
                if (blue)
                {
                    blueProcessed++;
                }
                else
                {
                    redProcessed++;
                }
            }

            bool growsWest = blue ? _team1GrowsWest : _team2GrowsWest;

            panel.X = growsWest ? baseX - offset - wide : baseX + offset;
        }
    }

    /// <summary>
    /// `OnTick` (:540): shown by `ShouldDraw`, then the panels recounted. A tick signal reaches a hidden panel, so the match
    /// status calls this every think rather than leaving it to this panel's own, which a hidden panel never gets.
    /// </summary>
    public void Tick()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;

        Visible = ShouldDraw(state);

        if (Visible)
        {
            RecalculatePlayerPanels(state, viewport.Context);
        }
    }

    /// <summary>"Local player is always the innermost panel" (:454-457): the local player 0, team mates one out, the other team as counted.</summary>
    private static int SlotIndex(bool local, bool localTeam, int processed)
    {
        if (local)
        {
            return 0;
        }

        return localTeam ? processed + 1 : processed;
    }

    private static bool GrowsWest(string? text, bool current) => text switch
    {
        "east" => false,
        "west" => true,
        _ => current,
    };

    private void DeclareOnce()
    {
        if (_declared)
        {
            return;
        }

        _declared = true;
        DeclareAnimationVar("max_size", VguiPanelVarType.ProportionalInt, "19");
        DeclareAnimationVar("6v6_gap", VguiPanelVarType.ProportionalInt, "19");
        DeclareAnimationVar("12v12_gap", VguiPanelVarType.ProportionalInt, "9");
        DeclareAnimationVar("team1_base_x", VguiPanelVarType.ProportionalXPos, "0");
        DeclareAnimationVar("team1_max_expand", VguiPanelVarType.ProportionalInt, "0");
        DeclareAnimationVar("team2_base_x", VguiPanelVarType.ProportionalXPos, "0");
        DeclareAnimationVar("team2_max_expand", VguiPanelVarType.ProportionalInt, "0");
    }

    /// <summary>`RecalculatePlayerPanels` (:577): a panel per connected player on a game team, in slot order.</summary>
    private void RecalculatePlayerPanels(HudState state, VguiContext? context)
    {
        if (!state.HasLocalPlayer)
        {
            return;
        }

        int next = 0;
        bool needsLayout = false;

        if (TfPlayerPanel.ResourceTeam(state, state.LocalIndex) >= FirstGameTeam)
        {
            for (int index = 1; index <= MaxPlayers; index++)
            {
                if (!Connected(state, index) || TfPlayerPanel.ResourceTeam(state, index) < FirstGameTeam)
                {
                    continue;
                }

                TfTeamStatusPlayerPanel panel = GetOrAddPanel(next, context);

                needsLayout |= panel.PlayerIndex != index;
                panel.SetPlayerIndex(index, state);
                needsLayout |= panel.PreviousTeam != panel.Team;
                next++;
            }
        }

        for (int at = next; at < _panels.Count; at++)
        {
            needsLayout |= _panels[at].PlayerIndex != 0;
            _panels[at].SetPlayerIndex(0, state);
        }

        foreach (TfTeamStatusPlayerPanel panel in _panels)
        {
            panel.Update(state);
        }

        if (needsLayout)
        {
            InvalidateLayout();
        }
    }

    private static bool Connected(HudState state, int index)
    {
        foreach (SceneScoreboardPlayer player in state.ScoreboardPlayers ?? [])
        {
            if (player.EntityIndex == index)
            {
                return player.Connected;
            }
        }

        return false;
    }

    /// <summary>`GetOrAddPanel` (:556).</summary>
    private TfTeamStatusPlayerPanel GetOrAddPanel(int index, VguiContext? context)
    {
        if (index < _panels.Count)
        {
            return _panels[index];
        }

        TfTeamStatusPlayerPanel panel = new(this, $"playerpanel{index}");

        if (_playerPanelKvs is not null && context is not null)
        {
            panel.ApplySettings(_playerPanelKvs, context);
            panel.InvalidateLayout(reloadScheme: true);
            InvalidateLayout();
        }

        _panels.Add(panel);
        return panel;
    }
}
