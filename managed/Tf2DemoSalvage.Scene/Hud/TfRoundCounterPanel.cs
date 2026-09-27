using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CRoundCounterPanel` (game/client/tf/tf_hud_match_status.cpp:57-260): the round-win dots beside the match timer.</summary>
/// <remarks>
/// Five round indicators and five win indicators per side (`g_nMaxSupportedRounds`), created from three `.res` template
/// blocks — `RoundIndicatorPanel_kv`, `RoundWinPanelRed_kv`, `RoundWinPanelBlue_kv` — the same shape `CreateRoundPanels`
/// (:132) builds. `PerformLayout` (:180) positions them from `mp_winlimit` and lays out win indicators only up to
/// <c>Min(mp_winlimit, team's m_iScore)</c>; every indicator past the visible count is hidden by index rather than removed.
/// **Not modelled:** `tf_attack_defend_map`'s stopwatch veto on `VisibleCondition` (:167) — out of this port's scope, which
/// wires only `mp_tournament`, `mp_tournament_stopwatch` and `mp_winlimit`; the `winlimit_changed`/`winpanel_show_scores`/
/// `stop_watch_changed`/`teamplay_round_start` dirty-flag dance (:206, :225), replaced here by recomputing every think —
/// this project's per-frame <c>HudState</c> makes staleness a non-issue where the engine polls a ConVar; and an open
/// viewport panel screenshot test.
/// </remarks>
public sealed class TfRoundCounterPanel : VguiEditablePanel
{
    private const int MaxSupportedRounds = 5;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    private enum Alignment
    {
        West,
        East,
    }

    private readonly List<VguiImagePanel> _blueRoundIndicators = [];
    private readonly List<VguiImagePanel> _redRoundIndicators = [];
    private readonly List<VguiImagePanel> _blueWinIndicators = [];
    private readonly List<VguiImagePanel> _redWinIndicators = [];

    /// <summary>`CRoundCounterPanel( parent, "RoundCounter" )`.</summary>
    /// <param name="parent">The parent — `CTFHudMatchStatus` itself.</param>
    public TfRoundCounterPanel(VguiPanel? parent)
        : base(parent, "RoundCounter")
    {
        DeclareAnimationVar("starting_width", VguiPanelVarType.ProportionalInt, "10");
        DeclareAnimationVar("width_per_round", VguiPanelVarType.ProportionalInt, "10");
        DeclareAnimationVar("indicator_start_offset", VguiPanelVarType.ProportionalInt, "8");
        DeclareAnimationVar("indicator_max_wide", VguiPanelVarType.ProportionalInt, "10");
    }

    /// <inheritdoc/>
    public override string ClassName => "CRoundCounterPanel";

    /// <summary>`ShouldUseMatchHUD()`, as the parent decided it — gates <see cref="PerformLayout"/> exactly as it gates the parent's own visibility.</summary>
    public bool UseMatchHud { get; set; } = true;

    /// <summary>Every blue round indicator, in index order — for a test to read without a render.</summary>
    public IReadOnlyList<VguiImagePanel> BlueRoundIndicators => _blueRoundIndicators;

    /// <summary>Every red round indicator, in index order.</summary>
    public IReadOnlyList<VguiImagePanel> RedRoundIndicators => _redRoundIndicators;

    /// <summary>Every blue win indicator, in index order.</summary>
    public IReadOnlyList<VguiImagePanel> BlueWinIndicators => _blueWinIndicators;

    /// <summary>Every red win indicator, in index order.</summary>
    public IReadOnlyList<VguiImagePanel> RedWinIndicators => _redWinIndicators;

    /// <inheritdoc/>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(context);

        base.ApplySettings(block, context);

        CreateRoundPanels(_blueRoundIndicators, "RoundIndicator", block.Find("RoundIndicatorPanel_kv"), context);
        CreateRoundPanels(_redRoundIndicators, "RoundIndicator", block.Find("RoundIndicatorPanel_kv"), context);
        CreateRoundPanels(_blueWinIndicators, "WinIndicatorBlue", block.Find("RoundWinPanelBlue_kv"), context);
        CreateRoundPanels(_redWinIndicators, "WinIndicatorRed", block.Find("RoundWinPanelRed_kv"), context);
    }

    /// <summary>`CreateRoundPanels` (:132): five image panels, each given the template block's settings.</summary>
    private void CreateRoundPanels(List<VguiImagePanel> images, string name, KeyValuesTree? settings, VguiContext context)
    {
        if (images.Count != MaxSupportedRounds)
        {
            images.Clear();

            for (int i = 0; i < MaxSupportedRounds; i++)
            {
                images.Add(new VguiImagePanel(this, name));
            }
        }

        if (settings is null)
        {
            return;
        }

        foreach (VguiImagePanel image in images)
        {
            image.ApplySettings(settings, context);
        }
    }

    /// <inheritdoc/>
    protected override void OnThink()
    {
        // Recomputed every think rather than cached behind a dirty flag: HudState is rebuilt fresh
        // per frame already, so there is nothing here for `winlimit_changed` et al. to invalidate.
        PerformLayout();
    }

    /// <inheritdoc/>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        // `if ( !TFGameRules() || !ShouldUseMatchHUD() ) return;` (:183).
        if (!UseMatchHud || HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;

        // `if ( !pTeams[TF_TEAM_RED] || !pTeams[TF_TEAM_BLUE] ) return;` (:191).
        if (state.TeamStanding(TeamRed) is not { } red || state.TeamStanding(TeamBlue) is not { } blue)
        {
            return;
        }

        int startOffset = GetInt("indicator_start_offset");
        int step = GetInt("indicator_max_wide");
        int center = Wide / 2;

        // `mp_winlimit.GetInt()` (teamplayroundbased_gamerules.cpp:227).
        int winLimit = state.ConVars.GetInt("mp_winlimit");

        LayoutPanels(_blueRoundIndicators, Alignment.West, center - startOffset, step, winLimit);
        VisibleCondition(_blueRoundIndicators, winLimit);

        LayoutPanels(_redRoundIndicators, Alignment.East, center + startOffset, step, winLimit);
        VisibleCondition(_redRoundIndicators, winLimit);

        LayoutPanels(_blueWinIndicators, Alignment.West, center - startOffset, step, winLimit);
        VisibleCondition(_blueWinIndicators, Math.Min(winLimit, blue.Score));

        LayoutPanels(_redWinIndicators, Alignment.East, center + startOffset, step, winLimit);
        VisibleCondition(_redWinIndicators, Math.Min(winLimit, red.Score));
    }

    /// <summary>`VisibleCondition` (:167), minus the stopwatch veto (out of scope — see the class remarks).</summary>
    private static void VisibleCondition(List<VguiImagePanel> images, int max)
    {
        for (int i = 0; i < images.Count; i++)
        {
            images[i].Visible = i < max;
        }
    }

    /// <summary>`LayoutPanels` (:242).</summary>
    private static void LayoutPanels(List<VguiImagePanel> images, Alignment alignment, int startPos, int maxWide, int winLimit)
    {
        if (winLimit == 0)
        {
            return;
        }

        int step = maxWide / winLimit;

        for (int i = 0; i < images.Count; i++)
        {
            VguiImagePanel panel = images[i];
            int offset = step * i;

            panel.X = alignment == Alignment.East
                ? startPos + offset - (panel.Wide / 2) + (step / 2)
                : startPos - offset - (panel.Wide / 2) - (step / 2);
        }
    }
}
