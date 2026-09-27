using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudKothTimeStatus` (game/client/tf/tf_time_panel.cpp:893): KOTH's two timers, the running team's marked.</summary>
/// <remarks>
/// Drawn in KOTH outside waiting for players, the match summary and freeze cam; hidden by no bits. `Think` (:1051) points
/// each panel at its team's timer and marks the team whose timer runs — `UpdateActiveTeam` (:1008): under the match HUD
/// the running panel's `ActiveTimerHighlight` and the other's `ActiveTimerDim`, otherwise `ActiveTimerBG` moved to
/// `blue_active_xpos` or `red_active_xpos` and pulsed, or hidden with neither running.
/// **Not modelled:** the render groups, which nothing here locks.
/// </remarks>
public sealed class TfHudKothTimeStatus : VguiEditablePanel, IHudElement
{
    private const int TeamUnassigned = 0;
    private const int TeamRed = 2;
    private const int TeamBlue = 3;

    private VguiPanel? _activeTimerBg;
    private int _activeTeam = TeamUnassigned;

    /// <summary>`CTFHudKothTimeStatus( "HudKothTimeStatus" )`: parented to the viewport, its two panels made, RED's set to RED.</summary>
    /// <param name="viewport">The viewport.</param>
    public TfHudKothTimeStatus(VguiPanel viewport)
        : base(viewport, "HudKothTimeStatus")
    {
        BluePanel = new TfHudTimeStatus(this, "BlueTimer");
        RedPanel = new TfHudTimeStatus(this, "RedTimer") { Team = TeamRed };
        DeclareAnimationVar("blue_active_xpos", VguiPanelVarType.ProportionalInt, "0");
        DeclareAnimationVar("red_active_xpos", VguiPanelVarType.ProportionalInt, "0");
    }

    /// <summary>`m_pBluePanel`.</summary>
    public TfHudTimeStatus BluePanel { get; }

    /// <summary>`m_pRedPanel`.</summary>
    public TfHudTimeStatus RedPanel { get; }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudKothTimeStatus";

    /// <inheritdoc/>
    public int HiddenBits => 0;

    /// <summary>`CTFHudKothTimeStatus::ShouldDraw` (:928).</summary>
    /// <param name="state">The game state.</param>
    /// <returns>Whether it draws.</returns>
    /// <remarks>`SetVisible` (:954) runs `UpdateActiveTeam` under the match HUD when `CHud::Think` changes the visibility.</remarks>
    public bool ShouldDraw(HudState state)
    {
        bool draw = !state.Rules.ShowMatchSummary
            && state.Rules.Koth
            && !state.Rules.WaitingForPlayers
            && state.HasLocalPlayer
            && state.ObserverMode != ObserverModes.FreezeCam
            && !HudVisibility.IsHidden(state, HiddenBits);

        if (draw != Visible && ShouldUseMatchHud(state))
        {
            Visible = draw;
            UpdateActiveTeam(state);
        }

        return draw;
    }

    /// <inheritdoc/>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);

        HudState state = HudViewport.Of(this)?.State ?? default;
        bool matchHud = ShouldUseMatchHud(state);

        BluePanel.UseMatchHud = matchHud;
        RedPanel.UseMatchHud = matchHud;
        LoadControlSettings("resource/UI/HudObjectiveKothTimePanel.res", context, matchHud ? ["if_match"] : null);
        _activeTimerBg = FindChildByName("ActiveTimerBG");
        UpdateActiveTeam(state);
    }

    /// <summary>`Think` (:1051): the panels on their timers, and the running team marked when it changes.</summary>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport || !Visible)
        {
            return;
        }

        HudState state = viewport.State;
        int activeTeam = TeamUnassigned;

        if (state.Rules.BlueKothTimer is { } blue && state.RoundTimer(blue) is { } blueTimer)
        {
            BluePanel.TimerIndex = blue;
            activeTeam = blueTimer.Paused ? activeTeam : TeamBlue;
        }

        if (state.Rules.RedKothTimer is { } red && state.RoundTimer(red) is { } redTimer)
        {
            RedPanel.TimerIndex = red;
            activeTeam = redTimer.Paused ? activeTeam : TeamRed;
        }

        if (activeTeam != _activeTeam)
        {
            _activeTeam = activeTeam;
            UpdateActiveTeam(state);
        }
    }

    private static bool ShouldUseMatchHud(HudState state) => TfHudMatchStatus.ShouldUseMatchHud(state);

    /// <summary>`UpdateActiveTeam` (:1008).</summary>
    private void UpdateActiveTeam(HudState state)
    {
        VguiAnimationController? animations = HudViewport.Of(this)?.Animations;

        if (ShouldUseMatchHud(state))
        {
            animations?.StartAnimationSequence(RedPanel, _activeTeam == TeamRed ? "ActiveTimerHighlight" : "ActiveTimerDim", canBeCancelled: false);
            animations?.StartAnimationSequence(BluePanel, _activeTeam == TeamBlue ? "ActiveTimerHighlight" : "ActiveTimerDim", canBeCancelled: false);
            return;
        }

        if (_activeTimerBg is null)
        {
            return;
        }

        if (_activeTeam == TeamUnassigned)
        {
            _activeTimerBg.Visible = false;
            return;
        }

        _activeTimerBg.Visible = true;
        _activeTimerBg.SetAnimationValue("alpha", 255f);
        _activeTimerBg.X = GetInt(_activeTeam == TeamRed ? "red_active_xpos" : "blue_active_xpos");
        animations?.StartAnimationSequence("ActiveTimerBGPulse");
    }
}
