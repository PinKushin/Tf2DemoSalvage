using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudRobotDestruction_StateImage` (game/client/tf/tf_hud_robot_destruction_status.cpp:31): one robot state's circle and robot.</summary>
public class TfRobotStateImage : VguiEditablePanel
{
    private const int TeamRed = 2;
    private readonly string _resFile;

    /// <summary>`CTFHudRobotDestruction_StateImage( parent, name, pszResFile )`.</summary>
    /// <param name="parent">The robot state container.</param>
    /// <param name="name">The panel's name.</param>
    /// <param name="resFile">The layout it loads.</param>
    public TfRobotStateImage(VguiPanel parent, string name, string resFile)
        : base(parent, name)
    {
        _resFile = resFile;
        Image = new VguiImagePanel(this, "Image");
        RobotImage = new VguiImagePanel(this, "RobotImage");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudRobotDestruction_StateImage";

    /// <summary>`m_pImage`.</summary>
    public VguiImagePanel Image { get; }

    /// <summary>`m_pRobotImage`.</summary>
    public VguiImagePanel RobotImage { get; }

    /// <summary>`GetParent()->GetParent()` as the robot indicator, or null.</summary>
    protected TfRobotIndicator? Indicator => Parent?.Parent as TfRobotIndicator;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:44): the layout, then the glow at alpha 0.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings(_resFile, context);
        FindChildByName("GlowImage", recurseDown: true)?.SetAlpha(0f);
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:60): "redimage" for RED or no indicator, else "blueimage".</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        base.ApplySettings(block, context);

        int team = Indicator?.Team ?? TeamRed;

        if (block.Find(team == TeamRed ? "redimage" : "blueimage")?.Value is { Length: > 0 } image)
        {
            Image.SetImage(image);
        }
    }
}

/// <summary>`CTFHudRobotDestruction_DeadImage` (:83): the state with the respawn dial.</summary>
public sealed class TfRobotDeadImage : TfRobotStateImage
{
    /// <summary>`CTFHudRobotDestruction_DeadImage( parent, name, pszResFile )`.</summary>
    /// <param name="parent">The robot state container.</param>
    /// <param name="name">The panel's name.</param>
    /// <param name="resFile">The layout it loads.</param>
    public TfRobotDeadImage(VguiPanel parent, string name, string resFile)
        : base(parent, name, resFile) => RespawnProgressBar = new TfProgressBar(this, "RespawnProgressBar");

    /// <summary>`m_pRespawnProgressBar`.</summary>
    public TfProgressBar RespawnProgressBar { get; }
}

/// <summary>`CTFHudRobotDestruction_ActiveImage` (:102): the state whose robot is the group's own icon.</summary>
/// <param name="parent">The robot state container.</param>
/// <param name="name">The panel's name.</param>
/// <param name="resFile">The layout it loads.</param>
public sealed class TfRobotActiveImage(VguiPanel parent, string name, string resFile) : TfRobotStateImage(parent, name, resFile)
{
    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:109): the group's `GetHUDIcon()` over the layout's robot.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        base.ApplySettings(block, context);

        if (Indicator?.Group is { } group)
        {
            RobotImage.SetImage(group.HudIcon);
        }
    }
}

/// <summary>`CTFHudRobotDestruction_RobotIndicator` (:124): one robot group's icon in the row.</summary>
public sealed class TfRobotIndicator : VguiEditablePanel
{
    /// <summary>`ATTACK_BLINK_TIME`.</summary>
    public const float AttackBlinkTime = 2f;

    /// <summary>`ROBOT_STATE_INACIVE` (`eRobotUIState`, tf_logic_robot_destruction.h).</summary>
    public const int StateInactive = 0;

    /// <summary>`ROBOT_STATE_ACTIVE`.</summary>
    public const int StateActive = 1;

    /// <summary>`ROBOT_STATE_DEAD`.</summary>
    public const int StateDead = 2;

    /// <summary>`ROBOT_STATE_SHIELDED`.</summary>
    public const int StateShielded = 3;

    private readonly TfControlPointSwoop _swoop;

    /// <summary>`CTFHudRobotDestruction_RobotIndicator( pParent, pszName, pGroup )`.</summary>
    /// <param name="parent">The robot destruction panel.</param>
    /// <param name="name">"red_group_N" or "blue_group_N".</param>
    /// <param name="group">The group.</param>
    public TfRobotIndicator(VguiPanel parent, string name, SceneRobotGroup group)
        : base(parent, name)
    {
        Group = group;
        _swoop = new TfControlPointSwoop(this, "Swoop") { Visible = false };
        RobotStateContainer = new VguiEditablePanel(this, "RobotStateContainer");
        DeadPanel = new TfRobotDeadImage(RobotStateContainer, "DeadState", "resource/UI/TFHudRobotDestruction_DeadState.res");
        ActivePanel = new TfRobotActiveImage(RobotStateContainer, "ActiveState", "resource/UI/TFHudRobotDestruction_ActiveState.res");
        ShieldedPanel = new TfRobotStateImage(RobotStateContainer, "ShieldedState", "resource/UI/TFHudRobotDestruction_ShieldedState.res");
    }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudRobotDestruction_RobotIndicator";

    /// <summary>`m_hGroup`, or null once its entity is gone.</summary>
    public SceneRobotGroup? Group { get; set; }

    /// <summary>`GetTeamNumber()`: the group's, 0 without one.</summary>
    public int Team => Group?.Team ?? 0;

    /// <summary>`GetGroupNumber()`: the group's, 0 without one.</summary>
    public int GroupNumber => Group?.GroupNumber ?? 0;

    /// <summary>`m_eState`: dead until the first `UpdateState`.</summary>
    public int State { get; private set; } = StateDead;

    /// <summary>`m_pRobotStateContainer`.</summary>
    public VguiEditablePanel RobotStateContainer { get; }

    /// <summary>`m_pDeadPanel`.</summary>
    public TfRobotDeadImage DeadPanel { get; }

    /// <summary>`m_pActivePanel`.</summary>
    public TfRobotActiveImage ActivePanel { get; }

    /// <summary>`m_pShieldedPanel`.</summary>
    public TfRobotStateImage ShieldedPanel { get; }

    /// <summary>`m_pPrevRobotIndicator`.</summary>
    public TfRobotIndicator? Previous { get; set; }

    /// <summary>`m_pNextRobotIndicator`.</summary>
    public TfRobotIndicator? Next { get; set; }

    /// <summary>The respawn seconds `UpdateState` (:259) shows: `%0.0f` of what is left, never below 0.</summary>
    /// <param name="endTime">`GetRespawnEndTime()`.</param>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    /// <returns>The text.</returns>
    /// <remarks>The CRT's `%.0f` rounds a tie to even.</remarks>
    public static string RespawnText(float endTime, float curTime) =>
        Math.Round((double)Math.Max(0f, endTime - curTime), MidpointRounding.ToEven).ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>`DoUnderAttackBlink`'s glow alpha (:190): `|sin( curtime * 10 )| * 255` for the own team's group attacked in the last two seconds, else 0.</summary>
    /// <param name="lastAttacked">`GetLastAttackedTime()`.</param>
    /// <param name="groupTeam">The group's team.</param>
    /// <param name="localTeam">`GetLocalPlayerTeam()`.</param>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    /// <returns>The alpha, truncated as `SetAlpha( int )` takes it.</returns>
    public static int GlowAlpha(float lastAttacked, int groupTeam, int localTeam, float curTime) =>
        curTime < lastAttacked + AttackBlinkTime && localTeam == groupTeam ? (int)(MathF.Abs(MathF.Sin(curTime * 10f)) * 255f) : 0;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:147).</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        base.ApplySchemeSettings(context);
        LoadControlSettings("resource/UI/TFHudRobotDestruction_RobotIndicator.res", context);
    }

    /// <summary>`OnTick` (:175): hidden once its group is gone, then the state and the attack blink.</summary>
    /// <param name="localTeam">`GetLocalPlayerTeam()`.</param>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void Tick(int localTeam, float curTime)
    {
        if (Group is null)
        {
            Visible = false;
        }

        UpdateState(curTime);

        if (Group is { } group && FindChildByName("GlowImage", recurseDown: true) is { } glow)
        {
            glow.SetAlpha(GlowAlpha(group.LastAttackedTime, group.Team, localTeam, curTime));
        }
    }

    /// <summary>`UpdateState` (:246): the respawn time and dial, and which state panel shows.</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void UpdateState(float curTime)
    {
        if (Group is not { } group)
        {
            return;
        }

        DeadPanel.SetDialogVariable("time", RespawnText(group.RespawnEndTime, curTime));
        DeadPanel.RespawnProgressBar.Percentage = (curTime - group.RespawnStartTime) / (group.RespawnEndTime - group.RespawnStartTime);

        State = group.State;
        RobotStateContainer.Visible = State != StateInactive;

        bool showDead = State == StateDead;

        ActivePanel.Image.Visible = !showDead;
        DeadPanel.Visible = showDead;
        ActivePanel.Visible = State == StateActive;
        ShieldedPanel.Visible = State == StateShielded;
    }

    /// <inheritdoc/>
    /// <remarks>`PerformLayout` (:157): the swoop over the whole indicator.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();
        _swoop.X = 0;
        _swoop.Y = 0;
        _swoop.Wide = Wide;
        _swoop.Tall = Tall;
    }
}

/// <summary>`CTFHUDRobotDestruction::CProgressPanel` (:1104): a score bar that approaches its target and blinks as it scores.</summary>
public sealed class TfRobotDestructionProgressPanel : VguiImagePanel
{
    private const float BlinkPeriod = 0.25f;

    private float _xpos;
    private int _xOrg;
    private int _yOrg;
    private int _wideOrg;
    private int _tallOrg;
    private float _lastTick;
    private bool _ticking;

    /// <summary>`CProgressPanel( parent, name )`, with its panel variables (tf_hud_robot_destruction_status.h:184-191).</summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The panel's name.</param>
    public TfRobotDestructionProgressPanel(VguiPanel? parent, string? name)
        : base(parent, name)
    {
        DeclareAnimationVar("left_offset", VguiPanelVarType.ProportionalInt, "25");
        DeclareAnimationVar("right_offset", VguiPanelVarType.ProportionalInt, "25");
        DeclareAnimationVar("standard_color", VguiPanelVarType.Color, "255 255 255 255");
        DeclareAnimationVar("bright_color", VguiPanelVarType.Color, "255 255 255 255");
        DeclareAnimationVar("left_to_right", VguiPanelVarType.Bool, "1");
        DeclareAnimationVar("approach_speed", VguiPanelVarType.Real, "1.f");
        DeclareAnimationVar("blink_threshold", VguiPanelVarType.Real, "2.f");
        DeclareAnimationVar("blink_rate", VguiPanelVarType.Real, "3.f");
    }

    /// <summary>`m_flCurrentProgress`.</summary>
    public float CurrentProgress { get; private set; }

    /// <summary>`m_flEndProgress`.</summary>
    public float EndProgress { get; private set; }

    /// <summary>`m_flLastScoreTime`.</summary>
    public float LastScoreTime { get; private set; }

    /// <summary>`m_flApproachSpeed`.</summary>
    public float ApproachSpeed
    {
        get => GetFloat("approach_speed");
        set => SetAnimationValue("approach_speed", value);
    }

    /// <summary>`m_StandardColor`, which `SetColor` sets.</summary>
    public (byte Red, byte Green, byte Blue, byte Alpha) StandardColor
    {
        get => GetColor("standard_color");
        set => SetAnimationValue("standard_color", value);
    }

    /// <summary>`Approach( target, value, speed )` (mathlib_base.cpp).</summary>
    /// <param name="target">The target.</param>
    /// <param name="value">The value.</param>
    /// <param name="speed">The most it moves.</param>
    /// <returns>The value moved toward the target.</returns>
    public static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
        {
            return value + speed;
        }

        return delta < -speed ? value - speed : target;
    }

    /// <summary>`PaintBackground`'s colour lerp (:1177-1182): rising from bright to standard over the quarter second after a score, else a pulse at or past the threshold, else standard.</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    /// <param name="lastScoreTime">`m_flLastScoreTime`.</param>
    /// <param name="progress">`m_flCurrentProgress`.</param>
    /// <param name="threshold">`m_flBlinkThreshold`.</param>
    /// <param name="rate">`m_flBlinkRate`.</param>
    /// <returns>The lerp toward the standard colour, clamped to 0..1.</returns>
    public static float ColorLerp(float curTime, float lastScoreTime, float progress, float threshold, float rate)
    {
        if (curTime <= lastScoreTime + BlinkPeriod)
        {
            return Math.Clamp((curTime - lastScoreTime) / BlinkPeriod, 0f, 1f);
        }

        return progress >= threshold ? Math.Clamp((MathF.Sin(curTime * rate) * 0.5f) + 0.5f, 0f, 1f) : 1f;
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:1195): moved right by its left offset, its bounds kept as the full bar's.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        base.ApplySettings(block, context);
        X += GetInt("left_offset");
        (_xOrg, _yOrg, _wideOrg, _tallOrg) = (X, Y, Wide, Tall);
        Image?.SetSize(_wideOrg, _tallOrg);
        CalculateSize();
    }

    /// <summary>`SetProgress( flProgress, bInstant )` (:1127).</summary>
    /// <param name="progress">The target.</param>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    /// <param name="instant">Whether it jumps there.</param>
    public void SetProgress(float progress, float curTime, bool instant = false)
    {
        if (instant)
        {
            EndProgress = CurrentProgress = progress;
            CalculateSize();
            return;
        }

        // A second `AddTickSignal` only restarts the tick's clock.
#pragma warning disable S1244 // Valve's own exact comparison (:1137): any change at all restarts the tick.
        if (EndProgress != progress)
#pragma warning restore S1244
        {
            _ticking = true;
            _lastTick = curTime;
        }

        EndProgress = progress;
    }

    /// <summary>`OnTick` (:1148), while its tick signal is on.</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void Tick(float curTime)
    {
        if (!_ticking)
        {
            return;
        }

        float delta = curTime - _lastTick;
        _lastTick = curTime;
        CurrentProgress = Approach(EndProgress, CurrentProgress, delta * ApproachSpeed);

#pragma warning disable S1244 // Valve's own exact comparison (:1157): `Approach` lands on the target itself.
        if (CurrentProgress == EndProgress)
#pragma warning restore S1244
        {
            _ticking = false;
        }

        CalculateSize();
    }

    /// <summary>`Blink` (:1207).</summary>
    /// <param name="curTime">`gpGlobals->curtime`.</param>
    public void Blink(float curTime) => LastScoreTime = curTime;

    /// <summary>`FireGameEvent` (:1213) for `teamplay_round_start`: emptied at once.</summary>
    public void RoundStart()
    {
        CurrentProgress = 0f;
        EndProgress = 0f;
        CalculateSize();
    }

    /// <inheritdoc/>
    /// <remarks>`PaintBackground` (:1165): the shared image placed for this bar, then the colour.</remarks>
    public override void PaintBackground(IVguiSurface surface, VguiContext context)
    {
        if (Image is { } image)
        {
            image.SetPos(GetBool("left_to_right") ? -GetInt("left_offset") : -(int)_xpos, _yOrg);
            image.SetSize(_wideOrg, _tallOrg);
        }

        float curTime = HudViewport.Of(this)?.State.ServerTime ?? 0f;
        float lerp = ColorLerp(curTime, LastScoreTime, CurrentProgress, GetFloat("blink_threshold"), GetFloat("blink_rate"));
        float inverse = 1f - lerp;
        (byte Red, byte Green, byte Blue, byte Alpha) bright = GetColor("bright_color");
        (byte Red, byte Green, byte Blue, byte Alpha) standard = StandardColor;

        DrawColor = (
            (byte)(int)((inverse * bright.Red) + (lerp * standard.Red)),
            (byte)(int)((inverse * bright.Green) + (lerp * standard.Green)),
            (byte)(int)((inverse * bright.Blue) + (lerp * standard.Blue)),
            255);

        base.PaintBackground(surface, context);
    }

    /// <summary>`CalculateSize` (:1228): the filled part of the bar, from the left or growing from the right.</summary>
    private void CalculateSize()
    {
        int progressWidth = _wideOrg - GetInt("right_offset") - GetInt("left_offset");

        _xpos = GetBool("left_to_right") ? _xOrg : ((1f - CurrentProgress) * progressWidth) + _xOrg;

        X = (int)_xpos;
        Y = _yOrg;
        Wide = (int)(CurrentProgress * progressWidth);
        Tall = _tallOrg;
    }
}

/// <summary>`CTFHUDRobotDestruction` (tf_hud_robot_destruction_status.cpp:293): robot destruction's scores, robots, stolen and carried cores.</summary>
/// <remarks>
/// **From state each think, where the engine ticks every 50 ms**, with its `PaintBackground` stolen-point placement run as the
/// same think. The freeze-cam screenshot check is not ported: no screenshot is taken in playback. `m_pPlayingTo` is never
/// assigned (:371), so `SetPlayingToLabelVisible` does nothing and the label stays as its layout leaves it — Valve's, kept.
/// </remarks>
public sealed class TfHudRobotDestruction : VguiEditablePanel
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int LastSharedTeam = 1;

    /// <summary>`SCORE_REACTOR_STEAL` (`RDScoreMethod_t`).</summary>
    private const int ScoreReactorSteal = 3;

    /// <summary>"This is where in the texture the dotted line is" (:982).</summary>
    private const float WhereTheDottedLineIs = 0.25f;

    private readonly List<TfRobotIndicator> _redRobots = [];
    private readonly List<TfRobotIndicator> _blueRobots = [];
    private Parts _parts;
    private KeyValuesTree? _robotIndicatorKvs;
    private bool _playingRd;
    private int _stealLeftEdge;
    private int _stealRightEdge;
    private int _redFlag;
    private int _blueFlag;

    /// <summary>`CTFHUDRobotDestruction( parent, name )`.</summary>
    /// <param name="parent">The objective status.</param>
    /// <param name="name">"ObjectiveStatusRobotDestruction".</param>
    public TfHudRobotDestruction(VguiPanel parent, string name)
        : base(parent, name)
    {
        DeclareAnimationVar("left_steal_edge_offset", VguiPanelVarType.ProportionalInt, "25");
        DeclareAnimationVar("right_steal_edge_offset", VguiPanelVarType.ProportionalInt, "100");
        DeclareAnimationVar("robot_x_offset", VguiPanelVarType.ProportionalInt, "6");
        DeclareAnimationVar("robot_y_offset", VguiPanelVarType.ProportionalInt, "25");
        DeclareAnimationVar("robot_x_step", VguiPanelVarType.ProportionalInt, "5");
        DeclareAnimationVar("robot_y_step", VguiPanelVarType.ProportionalInt, "0");
        DeclareAnimationVar("color_blue", VguiPanelVarType.Color, "0 0 255 255");
        DeclareAnimationVar("color_red", VguiPanelVarType.Color, "255 0 0 255");
        DeclareAnimationVar("player_name_font", VguiPanelVarType.Font, "HudFontSmallBold");
        DeclareAnimationVar("text_color", VguiPanelVarType.Color, "255 255 255 255");
        _parts = ReinitializeEverything();
    }

    /// <summary>The events the panel and its bars listen for (:303-306, :1115).</summary>
    public static IReadOnlySet<string> ListensFor { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "rd_rules_state_changed", "flagstatus_update", "rd_team_points_changed", "teamplay_round_start" };

    /// <summary>`m_vecRedRobots`, highest group first.</summary>
    public IReadOnlyList<TfRobotIndicator> RedRobots => _redRobots;

    /// <summary>`m_vecBlueRobots`, highest group first.</summary>
    public IReadOnlyList<TfRobotIndicator> BlueRobots => _blueRobots;

    /// <summary>`m_pCarriedContainer`.</summary>
    public VguiEditablePanel CarriedContainer => _parts.CarriedContainer;

    /// <summary>`m_pCarriedImage`.</summary>
    public VguiImagePanel CarriedImage => _parts.CarriedImage;

    /// <summary>`m_pCarriedFlagProgressBar`.</summary>
    public TfRobotDestructionProgressPanel CarriedFlagProgressBar => _parts.CarriedFlagProgressBar;

    /// <summary>`m_pProgressBarsContainer`.</summary>
    public VguiEditablePanel ProgressBarsContainer => _parts.ProgressBarsContainer;

    /// <summary>`m_pBlueStolenContainer`.</summary>
    public VguiEditablePanel BlueStolenContainer => _parts.BlueStolenContainer;

    /// <summary>`m_pRedStolenContainer`.</summary>
    public VguiEditablePanel RedStolenContainer => _parts.RedStolenContainer;

    /// <summary>`m_pBlueScoreValueContainer`.</summary>
    public VguiEditablePanel BlueScoreValueContainer => _parts.BlueScoreValueContainer;

    /// <summary>`m_pRedScoreValueContainer`.</summary>
    public VguiEditablePanel RedScoreValueContainer => _parts.RedScoreValueContainer;

    /// <summary>`m_pBlueVictoryPanel`.</summary>
    public VguiEditablePanel BlueVictoryPanel => _parts.BlueVictoryPanel;

    /// <summary>`m_pRedVictoryPanel`.</summary>
    public VguiEditablePanel RedVictoryPanel => _parts.RedVictoryPanel;

    /// <summary>`m_pBlueProgressBar`.</summary>
    public TfRobotDestructionProgressPanel BlueProgressBar => _parts.BlueProgressBar;

    /// <summary>`m_pBlueProgressBarEscrow`.</summary>
    public TfRobotDestructionProgressPanel BlueProgressBarEscrow => _parts.BlueProgressBarEscrow;

    /// <summary>`m_pRedProgressBar`.</summary>
    public TfRobotDestructionProgressPanel RedProgressBar => _parts.RedProgressBar;

    /// <summary>`m_pRedProgressBarEscrow`.</summary>
    public TfRobotDestructionProgressPanel RedProgressBarEscrow => _parts.RedProgressBarEscrow;

    /// <summary>`m_pCountdownContainer`: player destruction's, or null.</summary>
    public VguiEditablePanel? CountdownContainer { get; private set; }

    /// <summary>`m_pTeamLeaderImage`: player destruction's, or null.</summary>
    public VguiPanel? TeamLeaderImage { get; private set; }

    /// <summary>`SortRobotVec` (:355): highest group number first.</summary>
    /// <param name="robots">The indicators.</param>
    internal static void SortRobots(List<TfRobotIndicator> robots)
    {
        ArgumentNullException.ThrowIfNull(robots);
        robots.Sort((a, b) => b.GroupNumber - a.GroupNumber);
    }

    /// <summary>The carried bar's progress (:976-993): the points to the minimum steal map to the texture's dotted line at a quarter, the rest above it.</summary>
    /// <param name="pointValue">`GetPointValue()`.</param>
    /// <param name="maxPoints">`GetMaxPoints()`.</param>
    /// <param name="minToSteal">`tf_rd_min_points_to_steal`.</param>
    /// <returns>The bar's progress.</returns>
    public static float CarriedProgress(int pointValue, int maxPoints, int minToSteal)
    {
        float progress = pointValue / (float)maxPoints;
        float atDottedLine = minToSteal / (float)maxPoints;

        return progress <= atDottedLine
            ? RemapValClamped(progress, 0f, atDottedLine, 0f, WhereTheDottedLineIs)
            : RemapValClamped(progress, atDottedLine, 1f, WhereTheDottedLineIs, 1f);
    }

    /// <summary>`UpdateStolenPoints` (:896-905)'s lerp: the flag's distance from the far zone over the zones' gap, edges less their radii, flipped for BLU.</summary>
    /// <param name="team">The flag's team.</param>
    /// <param name="flag">The flag's position: its carrier's origin when carried.</param>
    /// <param name="start">The team's own zone.</param>
    /// <param name="end">The other zone.</param>
    /// <returns>The progress across the bar.</returns>
    public static float StolenProgress(int team, (float X, float Y, float Z) flag, SceneCaptureZone start, SceneCaptureZone end)
    {
        float totalDist = Distance(end.Center, start.Center) - end.BoundingRadius - start.BoundingRadius;
        float flagDist = Distance(end.Center, flag) - end.BoundingRadius;
        float lerp = Math.Clamp(flagDist / totalDist, 0f, 1f);

        return team == TeamBlue ? 1f - lerp : lerp;
    }

    /// <summary>`UpdateStolenPoints`'s zones (:878-894): of those neither dormant nor disabled, the last of the team's and the last other.</summary>
    /// <param name="zones">The capture zones, in list order.</param>
    /// <param name="team">The stolen flag's team.</param>
    /// <returns>The team's zone and the other, each null when none qualifies.</returns>
    public static (SceneCaptureZone? Start, SceneCaptureZone? End) Zones(IReadOnlyList<SceneCaptureZone> zones, int team)
    {
        ArgumentNullException.ThrowIfNull(zones);

        SceneCaptureZone? start = null;
        SceneCaptureZone? end = null;

        foreach (SceneCaptureZone zone in zones)
        {
            if (zone.Dormant || zone.Disabled)
            {
                continue;
            }

            if (zone.Team == team)
            {
                start = zone;
            }
            else
            {
                end = zone;
            }
        }

        return (start, end);
    }

    /// <summary>The approach speed `teamplay_round_start` sets (:1094): `( points per approach / interval ) / max points`.</summary>
    /// <param name="pointsPerApproach">`tf_rd_points_per_approach`.</param>
    /// <param name="interval">`tf_rd_points_approach_interval`.</param>
    /// <param name="maxPoints">`GetMaxPoints()`.</param>
    /// <returns>The speed.</returns>
    public static float ApproachSpeedFor(int pointsPerApproach, float interval, int maxPoints) => pointsPerApproach / interval / maxPoints;

    /// <inheritdoc/>
    /// <remarks>`ApplySettings` (:336): the `robot_kv` block kept for the indicators.</remarks>
    public override void ApplySettings(KeyValuesTree block, VguiContext context)
    {
        ArgumentNullException.ThrowIfNull(block);
        base.ApplySettings(block, context);

        if (block.Find("robot_kv") is { Value: null } robot)
        {
            _robotIndicatorKvs = robot;
        }
    }

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:405): everything made again, the logic's layout loaded, and an indicator for each group.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        _parts = ReinitializeEverything();
        base.ApplySchemeSettings(context);

        if (HudViewport.Of(this)?.State.RobotDestruction is not { } logic)
        {
            return;
        }

        LoadControlSettings(logic.ResFile, context);

        foreach (SceneRobotGroup group in logic.Groups)
        {
            // :426.
            if (group.Dormant)
            {
                continue;
            }

            List<TfRobotIndicator> robots = group.Team == TeamRed ? _redRobots : _blueRobots;
            string panelName = group.Team == TeamRed ? "red" : "blue";

            // `CFmtStr( "%s_group_%d", ..., robotVec.Count() )` reads the count after `AddToTail`.
            robots.Add(new TfRobotIndicator(this, $"{panelName}_group_{robots.Count + 1}", group));
        }

        SortRobots(_redRobots);
        SortRobots(_blueRobots);
        CarriedContainer.Visible = false;
        CountdownContainer = FindChildByName("CountdownContainer") as VguiEditablePanel;
        TeamLeaderImage = CarriedContainer.FindChildByName("TeamLeaderImage");
    }

    /// <summary>`Reset` (:533).</summary>
    public void Reset() => HudViewport.Of(this)?.Animations.StartAnimationSequence("FlagOutlineHide");

    /// <summary>`OnTick` (:560) and `PaintBackground`'s stolen points (:788), from this frame's state.</summary>
    /// <param name="state">The game state.</param>
    public void Update(HudState state)
    {
        SceneRobotDestruction? logic = state.RobotDestruction;
        float curTime = state.ServerTime;

        if (_playingRd != logic is not null)
        {
            if (logic is not null && HudViewport.Of(this) is { Context: { } context })
            {
                InvalidateLayout(layoutNow: true, reloadScheme: true, context);
            }

            _playingRd = logic is not null;
        }

        TickChildren(state, logic, curTime);

        if (logic is null)
        {
            return;
        }

        IReadOnlyList<SceneFlag> flags = state.CaptureFlags?.Flags ?? [];

        RedScoreValueContainer.SetDialogVariable("score", logic.Score(TeamRed));
        BlueScoreValueContainer.SetDialogVariable("score", logic.Score(TeamBlue));

        (int blueEscrow, int redEscrow) = logic.PlayerDestruction ? PlayerDestructionEscrow(state, logic, flags) : RobotDestructionEscrow(flags, curTime);

        UpdateFinale(logic, TeamRed, RedVictoryPanel, RedScoreValueContainer, curTime);
        UpdateFinale(logic, TeamBlue, BlueVictoryPanel, BlueScoreValueContainer, curTime);

        float maxPoints = logic.MaxPoints;
        int targetPoints = logic.TargetScore(TeamBlue);
        BlueProgressBar.SetProgress(targetPoints / maxPoints, curTime);
        BlueProgressBarEscrow.SetProgress((targetPoints + blueEscrow) / maxPoints, curTime);

        targetPoints = logic.TargetScore(TeamRed);
        RedProgressBar.SetProgress(targetPoints / maxPoints, curTime);
        RedProgressBarEscrow.SetProgress((targetPoints + redEscrow) / maxPoints, curTime);

        SetDialogVariable("rounds", logic.MaxPoints);

        // "HACK!  Fix the events".
        UpdateCarriedFlagStatus(state, logic, flags, newOwner: null, flagEntity: null);

        UpdateStolenPoints(state, TeamRed, RedStolenContainer);
        UpdateStolenPoints(state, TeamBlue, BlueStolenContainer);
    }

    /// <summary>`FireGameEvent` (:1040), and the bars' own (:1213).</summary>
    /// <param name="fired">The event and the world it fired in.</param>
    /// <param name="state">The game state.</param>
    public void HandleGameEvent(HudGameEvent fired, HudState state)
    {
        ArgumentNullException.ThrowIfNull(fired);

        SceneGameEvent e = fired.Event;

        if (e.Name == "teamplay_round_start")
        {
            foreach (TfRobotDestructionProgressPanel bar in Bars())
            {
                bar.RoundStart();
            }
        }

        if (state.RobotDestruction is not { } logic)
        {
            return;
        }

        switch (e.Name)
        {
            case "rd_rules_state_changed":
                UpdateRobotElements();
                break;
            case "flagstatus_update":
            {
                IReadOnlyList<SceneFlag> flags = state.CaptureFlags?.Flags ?? [];

                if (Find(flags, e.GetInt("entindex")) is { } flag)
                {
                    UpdateCarriedFlagStatus(state, logic, flags, e.PlayerForUserId(e.GetInt("userid")), flag);
                    UpdateStolenFlagStatus(flag.Team, flag);
                }

                break;
            }

            case "rd_team_points_changed":
                OnTeamPointsChanged(fired, state.ServerTime);
                break;
            case "teamplay_round_start":
            {
                float speed = ApproachSpeedFor(
                    state.ConVars.GetInt("tf_rd_points_per_approach"), state.ConVars.GetFloat("tf_rd_points_approach_interval"), logic.MaxPoints);

                BlueProgressBar.ApproachSpeed = speed;
                BlueProgressBarEscrow.ApproachSpeed = speed;
                RedProgressBar.ApproachSpeed = speed;
                RedProgressBarEscrow.ApproachSpeed = speed;
                break;
            }

            default:
                break;
        }
    }

    /// <summary>`PaintPDPlayerScore`'s text (:822-823): `%d` into a three-character buffer, so at most two digits.</summary>
    /// <param name="points">The carried flag's `GetPointValue()`, or 0.</param>
    /// <returns>The text.</returns>
    public static string LeaderScoreText(int points)
    {
        string text = points.ToString(CultureInfo.InvariantCulture);

        return text.Length > 2 ? text[..2] : text;
    }

    /// <inheritdoc/>
    /// <remarks>`Paint` (:837): player destruction's team leaders' carried points over their heads, then the panel.</remarks>
    public override void Paint(IVguiSurface surface, VguiContext context)
    {
        if (HudViewport.Of(this) is { } viewport && viewport.State.RobotDestruction is { PlayerDestruction: true } logic)
        {
            PaintPDPlayerScore(surface, context, viewport.State, logic.TeamLeaders.Red);
            PaintPDPlayerScore(surface, context, viewport.State, logic.TeamLeaders.Blue);
        }

        base.Paint(surface, context);
    }

    /// <summary>`PaintPDPlayerScore` (:796): above the leader's hull, 20 higher, never over the local player.</summary>
    private void PaintPDPlayerScore(IVguiSurface surface, VguiContext context, HudState state, int leader)
    {
        if (leader == 0 || leader == state.LocalIndex || state.Player(leader) is not { } player || state.WorldToScreen is not { } matrix)
        {
            return;
        }

        // `VEC_HULL_MAX_SCALED( pPlayer ).z`: the hull's top times `GetModelScale()`.
        float hullTop = LeaderHullMaxZ * (player.ModelScale ?? 1f);
        (bool onScreen, int x, int y) = HudSpace.GetVectorInHudSpace(matrix, (player.X, player.Y, player.Z + hullTop + 20f), context.ScreenWide, context.ScreenTall);

        // A font the scheme lacks is handle 0, which draws nothing.
        if (!onScreen || GetFont("player_name_font") is not { } font)
        {
            return;
        }

        string score = LeaderScoreText(CarriedBy(state.CaptureFlags?.Flags ?? [], leader)?.PointValue ?? 0);
        int width = score.Length * 15;

        surface.DrawSetTextFont(font);
        surface.DrawSetTextPos(x - (width / 2), y);
        surface.DrawSetTextColor(GetColor("text_color"));
        surface.DrawPrintText(score, VguiFontDrawType.NonAdditive);
    }

    /// <summary>`VEC_HULL_MAX`'s z (tf_shareddefs).</summary>
    private const float LeaderHullMaxZ = 82f;

    /// <inheritdoc/>
    /// <remarks>`PerformLayout` (:455): the robots sorted and placed, and the steal edges kept.</remarks>
    protected override void PerformLayout()
    {
        base.PerformLayout();

        SortRobots(_redRobots);
        SortRobots(_blueRobots);

        HudViewport? viewport = HudViewport.Of(this);
        float curTime = viewport?.State.ServerTime ?? 0f;

        PerformRobotLayout(_redRobots, TeamRed, viewport?.Context, curTime);
        PerformRobotLayout(_blueRobots, TeamBlue, viewport?.Context, curTime);

        int x = ProgressBarsContainer.X;

        _stealLeftEdge = (int)(x + GetInt("left_steal_edge_offset") - (RedStolenContainer.Wide / 2f));
        _stealRightEdge = (int)(x + ProgressBarsContainer.Wide - GetInt("right_steal_edge_offset") + (RedStolenContainer.Wide / 2f));
    }

    private static float RemapValClamped(float value, float a, float b, float c, float d)
    {
#pragma warning disable S1244 // `RemapValClamped`'s own exact test (mathlib.h): a zero span divides by nothing.
        if (a == b)
#pragma warning restore S1244
        {
            return value >= b ? d : c;
        }

        float t = Math.Clamp((value - a) / (b - a), 0f, 1f);

        return c + ((d - c) * t);
    }

    private static float Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b)
    {
        float x = a.X - b.X;
        float y = a.Y - b.Y;
        float z = a.Z - b.Z;

        return MathF.Sqrt((x * x) + (y * y) + (z * z));
    }

    private static SceneFlag? Find(IReadOnlyList<SceneFlag> flags, int entity)
    {
        for (int at = 0; at < flags.Count; at++)
        {
            if (entity != 0 && flags[at].EntityIndex == entity)
            {
                return flags[at];
            }
        }

        return null;
    }

    private static SceneFlag? CarriedBy(IReadOnlyList<SceneFlag> flags, int player)
    {
        for (int at = 0; at < flags.Count; at++)
        {
            if (player != 0 && flags[at].Carrier == player)
            {
                return flags[at];
            }
        }

        return null;
    }

    /// <summary>`m_hGroup.Get()`: the group at the handle's index while its serial still matches, else null for good.</summary>
    /// <param name="logic">This frame's state, or null.</param>
    /// <param name="held">The group the handle was made from.</param>
    /// <returns>The group now, or null.</returns>
    internal static SceneRobotGroup? Resolve(SceneRobotDestruction? logic, SceneRobotGroup held)
    {
        foreach (SceneRobotGroup group in logic?.Groups ?? [])
        {
            if (group.EntityIndex == held.EntityIndex && group.SerialNumber == held.SerialNumber)
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>:750-767: the big score swaps for the finale countdown once a team is at its maximum.</summary>
    private static void UpdateFinale(SceneRobotDestruction logic, int team, VguiEditablePanel victory, VguiEditablePanel score, float curTime)
    {
        float finaleTime = logic.FinaleLength;
        // `clamp` (basetypes.h): the bounds in order, never a throw on a negative length.
        float remaining = logic.FinaleWinTime(team) - curTime;
        float progress = remaining < 0f ? 0f : Math.Min(remaining, logic.FinaleLength);

        victory.Visible = progress < finaleTime;
        score.Visible = progress >= finaleTime;

        if (progress < finaleTime)
        {
            victory.SetDialogVariable("victorytime", (int)progress);
        }
    }

    private static void UpdateCountdown(HudState state, SceneRobotDestruction logic, VguiEditablePanel countdown)
    {
        float remaining = logic.CountdownEndTime - state.ServerTime;

        if (remaining <= -1f)
        {
            countdown.Visible = false;
            return;
        }

        countdown.Visible = true;

        if (countdown.FindChildByName("CountdownImage", recurseDown: true) is VguiImagePanel image)
        {
            bool visible = true;

            if (logic.UsingCountdownImage)
            {
                if (logic.CountdownImage.Length > 0)
                {
                    image.SetImage(logic.CountdownImage);
                }
                else
                {
                    visible = false;
                }
            }

            image.Visible = visible;
        }

        if (countdown.FindChildByName("CountdownTime", recurseDown: true) is { } time)
        {
            time.Visible = true;
        }

        if (countdown.FindChildByName("CountdownTimeShadow", recurseDown: true) is { } shadow)
        {
            shadow.Visible = true;
        }

        int seconds = (int)remaining;
        countdown.SetDialogVariable("countdowntime", seconds < 0 ? 0 : seconds);
    }

    private TfRobotDestructionProgressPanel[] Bars() =>
        [CarriedFlagProgressBar, BlueProgressBar, BlueProgressBarEscrow, RedProgressBar, RedProgressBarEscrow];

    /// <summary>`rd_team_points_changed` (:1066): the team's score pulses, its bar blinks, and a steal blinks the other team's escrow.</summary>
    private void OnTeamPointsChanged(HudGameEvent fired, float curTime)
    {
        SceneGameEvent e = fired.Event;
        int team = e.GetInt("team");
        int points = e.GetInt("points");
        int localTeam = fired.LocalPlayerTeam;
        VguiPanel panel = team == TeamRed ? RedScoreValueContainer : BlueScoreValueContainer;
        bool positive = (team == localTeam && points > 0) || (team != localTeam && points < 0);

        HudViewport.Of(this)?.Animations.StartAnimationSequence(panel, positive ? "RDPositiveScorePulse" : "RDNegativeScorePulse");
        // On the clock the bars paint by, which is the server's.
        (team == TeamRed ? RedProgressBar : BlueProgressBar).Blink(curTime);

        if (e.GetInt("method") == ScoreReactorSteal)
        {
            (team != TeamRed ? RedProgressBarEscrow : BlueProgressBarEscrow).Blink(curTime);
        }
    }

    /// <summary>The indicators' and bars' own tick signals.</summary>
    private void TickChildren(HudState state, SceneRobotDestruction? logic, float curTime)
    {
        foreach (TfRobotIndicator robot in _redRobots.Concat(_blueRobots))
        {
            robot.Group = robot.Group is { } held ? Resolve(logic, held) : null;
            robot.Tick(state.Team, curTime);
        }

        foreach (TfRobotDestructionProgressPanel bar in Bars())
        {
            bar.Tick(curTime);
        }
    }

    /// <summary>`ReinitializeEverything` (:360): every child deleted and the fixed ones made again, so no map's layout leaks into the next.</summary>
    private Parts ReinitializeEverything()
    {
        foreach (VguiPanel child in Children.ToArray())
        {
            OwnGroup.PanelRemoved(child);
            child.SetParent(null);
        }

        CountdownContainer = null;
        TeamLeaderImage = null;
        _redRobots.Clear();
        _blueRobots.Clear();

        return new Parts(this);
    }

    /// <summary>`UpdateRobotElements` (:1006): the indicators deleted and the scheme applied again, which makes them anew.</summary>
    private void UpdateRobotElements()
    {
        foreach (TfRobotIndicator robot in _redRobots.Concat(_blueRobots))
        {
            OwnGroup.PanelRemoved(robot);
            robot.SetParent(null);
        }

        _redRobots.Clear();
        _blueRobots.Clear();

        // `InvalidateLayout( false, true )`: the scheme, and so the new indicators, at once (Panel.cpp:3924).
        if (HudViewport.Of(this) is { Context: { } context })
        {
            InvalidateLayout(layoutNow: false, reloadScheme: true, context);
        }
    }

    /// <summary>`PerformRobotLayout` (:478): spanning out from the bottom centre, RED to the right and BLU to the left.</summary>
    private void PerformRobotLayout(List<TfRobotIndicator> robots, int team, VguiContext? context, float curTime)
    {
        bool isRed = team == TeamRed;
        int centerX = (int)(X + (Wide * 0.5f));
        int activeIndex = 0;
        int xOffset = GetInt("robot_x_offset");
        int yOffset = Tall - GetInt("robot_y_offset");
        int xStep = GetInt("robot_x_step");
        int yStep = GetInt("robot_y_step");
        TfRobotIndicator? previous = null;

        // `FOR_EACH_VEC_BACK`.
        for (int i = robots.Count - 1; i >= 0; i--)
        {
            TfRobotIndicator robot = robots[i];

            if (_robotIndicatorKvs is not null && context is not null)
            {
                robot.ApplySettings(_robotIndicatorKvs, context);
            }

            robot.UpdateState(curTime);
            robot.ZPos = robots.Count - i;

            if (previous is not null)
            {
                robot.Previous = previous;
                previous.Next = robot;
            }

            int wide = robot.Wide;
            int startPos = (int)((xOffset + (wide * 0.5f)) * (isRed ? 1 : -1));
            int offset = isRed ? xStep : -xStep;

            robot.X = (int)(centerX + startPos + (offset * activeIndex) - (wide * 0.5));
            robot.Y = yOffset - robot.Tall - (yStep * i);
            if (context is not null)
            {
                robot.InvalidateLayout(layoutNow: true, reloadScheme: true, context);
            }

            if (robot.State != TfRobotIndicator.StateInactive)
            {
                previous = robot;
                activeIndex++;
            }
        }
    }

    /// <summary>:584-686: player destruction's escrow from every stolen flag's last owner, the leader's image and the countdown.</summary>
    private (int Blue, int Red) PlayerDestructionEscrow(HudState state, SceneRobotDestruction logic, IReadOnlyList<SceneFlag> flags)
    {
        int blue = 0;
        int red = 0;

        foreach (SceneFlag flag in flags)
        {
            if (flag.IsStolen && flag.PrevOwner != 0)
            {
                if (state.Player(flag.PrevOwner)?.Team == TeamRed)
                {
                    red += flag.PointValue;
                }
                else
                {
                    blue += flag.PointValue;
                }

                // RED's, whichever team: Valve's.
                _parts.RedDroppedPanel.SetAlpha(255f);
            }
        }

        ProgressBarsContainer.SetDialogVariable("red_escrow", red);
        ProgressBarsContainer.SetDialogVariable("blue_escrow", blue);

        if (TeamLeaderImage is not null)
        {
            TeamLeaderImage.Visible = state.LocalIndex != 0 && state.Team > LastSharedTeam && state.LocalIndex == logic.TeamLeader(state.Team);
        }

        if (CountdownContainer is not null)
        {
            UpdateCountdown(state, logic, CountdownContainer);
        }

        return (blue, red);
    }

    /// <summary>:689-747: a held flag counts toward the stealing team's escrow, a dropped one toward its own, blinking near its return.</summary>
    private (int Blue, int Red) RobotDestructionEscrow(IReadOnlyList<SceneFlag> flags, float curTime)
    {
        if (Find(flags, _redFlag) is null || Find(flags, _blueFlag) is null)
        {
            foreach (SceneFlag flag in flags)
            {
                if (flag.Team == TeamBlue)
                {
                    _blueFlag = flag.EntityIndex;
                }
                else
                {
                    _redFlag = flag.EntityIndex;
                }
            }
        }

        int blue = 0;
        int red = 0;

        if (Find(flags, _redFlag) is not { } redFlag || Find(flags, _blueFlag) is not { } blueFlag)
        {
            return (blue, red);
        }

        int blink = (int)(curTime * 10) % 10 < 5 ? 255 : 0;

        if (redFlag.IsDropped)
        {
            red += redFlag.PointValue;

            if (redFlag.ReturnProgress(curTime) > 0.8f)
            {
                _parts.RedDroppedPanel.SetAlpha(blink);
            }
        }
        else
        {
            blue += redFlag.PointValue;
            _parts.RedDroppedPanel.SetAlpha(255f);
        }

        if (blueFlag.IsDropped)
        {
            blue += blueFlag.PointValue;

            if (blueFlag.ReturnProgress(curTime) > 0.8f)
            {
                _parts.BlueDroppedPanel.SetAlpha(blink);
            }
        }
        else
        {
            red += blueFlag.PointValue;
            _parts.BlueDroppedPanel.SetAlpha(255f);
        }

        return (blue, red);
    }

    /// <summary>`UpdateStolenPoints` (:853): the stolen core's value, and its marker placed between the zones.</summary>
    private void UpdateStolenPoints(HudState state, int team, VguiEditablePanel container)
    {
        IReadOnlyList<SceneFlag> flags = state.CaptureFlags?.Flags ?? [];
        SceneFlag? theirFlag = Find(flags, team == TeamRed ? _redFlag : _blueFlag);
        int stolenPoints = theirFlag?.PointValue ?? 0;

        container.Visible = stolenPoints > 0;
        container.SetDialogVariable("intelvalue", stolenPoints);

        if (theirFlag is not { IsHome: false } stolen)
        {
            return;
        }

        if (Zones(state.CaptureFlags?.Zones ?? [], team) is not ({ } startZone, { } endZone))
        {
            return;
        }

        // "Use the player's pos if the flag is being carried": `SceneFlag.Center` already reads a carried flag at its carrier.
        float progress = StolenProgress(team, stolen.Center, startZone, endZone);

        container.X = (int)(((_stealRightEdge - (_stealLeftEdge + container.Wide)) * progress) + _stealLeftEdge);
    }

    /// <summary>`UpdateCarriedFlagStatus` (:921): the local player's carried core and its value bar.</summary>
    private void UpdateCarriedFlagStatus(HudState state, SceneRobotDestruction logic, IReadOnlyList<SceneFlag> flags, int? newOwner, SceneFlag? flagEntity)
    {
        // "If this is about the other team, we dont care".
        // `USERID2PLAYER` finding nobody leaves `pNewOwner` null, which goes on.
        if (newOwner is > 0 and int owner && state.Player(owner) is { } ownerPlayer && ownerPlayer.Team != state.Team)
        {
            return;
        }

        SceneFlag? playerFlag = null;

        if (CarriedBy(flags, state.LocalIndex) is { } carried && (newOwner is null or 0 || newOwner == state.LocalIndex))
        {
            playerFlag = carried;
        }

        if (playerFlag is null && state.LocalIndex != 0 && state.LocalIndex == newOwner)
        {
            playerFlag = flagEntity;
        }

        // :946: `!IsMarkedForDeletion() && !IsDormant()`; a flag deleted is gone from the frame.
        if (playerFlag is { Dormant: false } flag)
        {
            CarriedContainer.Visible = true;
            CarriedContainer.SetDialogVariable("flagvalue", flag.PointValue);

            if (!CarriedImage.Visible)
            {
                CarriedImage.Visible = true;
                CarriedFlagProgressBar.SetProgress(0f, state.ServerTime, instant: true);
                CarriedFlagProgressBar.StandardColor = GetColor(state.Team == TeamRed ? "color_red" : "color_blue");
            }

            CarriedFlagProgressBar.SetProgress(
                CarriedProgress(flag.PointValue, logic.MaxPoints, state.ConVars.GetInt("tf_rd_min_points_to_steal")), state.ServerTime);
        }
        else if (CarriedImage.Visible)
        {
            CarriedContainer.Visible = false;
            CarriedImage.Visible = false;
        }
    }

    /// <summary>`UpdateStolenFlagStatus` (:1017): the thief's image while carried, the dropped one while down.</summary>
    private void UpdateStolenFlagStatus(int team, SceneFlag flag)
    {
        VguiEditablePanel container = team == TeamRed ? RedStolenContainer : BlueStolenContainer;

        if (container.FindChildByName("IntelImage") is not { } carriedImage || container.FindChildByName("DroppedIntelContainer") is not { } downImage)
        {
            return;
        }

        carriedImage.Visible = !flag.IsDropped;
        downImage.Visible = flag.IsDropped;
    }

    /// <summary>The children `ReinitializeEverything` makes, in its order (:373-392).</summary>
    private sealed class Parts
    {
        public Parts(TfHudRobotDestruction owner)
        {
            CarriedContainer = new VguiEditablePanel(owner, "CarriedContainer");
            CarriedImage = new VguiImagePanel(CarriedContainer, "CarriedImage");
            CarriedFlagProgressBar = new TfRobotDestructionProgressPanel(CarriedContainer, "CarriedProgressBar");
            ScoreContainer = new VguiEditablePanel(owner, "ScoreContainer");
            BlueStolenContainer = new VguiEditablePanel(ScoreContainer, "BlueStolenContainer");
            BlueDroppedPanel = new VguiEditablePanel(BlueStolenContainer, "DroppedIntelContainer");
            RedStolenContainer = new VguiEditablePanel(ScoreContainer, "RedStolenContainer");
            RedDroppedPanel = new VguiEditablePanel(RedStolenContainer, "DroppedIntelContainer");
            BlueScoreValueContainer = new VguiEditablePanel(ScoreContainer, "BlueScoreValueContainer");
            RedScoreValueContainer = new VguiEditablePanel(ScoreContainer, "RedScoreValueContainer");
            ProgressBarsContainer = new VguiEditablePanel(ScoreContainer, "ProgressBarContainer");
            BlueVictoryPanel = new VguiEditablePanel(ProgressBarsContainer, "BlueVictoryContainer");
            BlueProgressBar = new TfRobotDestructionProgressPanel(ProgressBarsContainer, "BlueProgressBarFill");
            BlueProgressBarEscrow = new TfRobotDestructionProgressPanel(ProgressBarsContainer, "BlueProgressBarEscrow");
            RedVictoryPanel = new VguiEditablePanel(ProgressBarsContainer, "RedVictoryContainer");
            RedProgressBar = new TfRobotDestructionProgressPanel(ProgressBarsContainer, "RedProgressBarFill");
            RedProgressBarEscrow = new TfRobotDestructionProgressPanel(ProgressBarsContainer, "RedProgressBarEscrow");
        }

        public VguiEditablePanel CarriedContainer { get; }

        public VguiImagePanel CarriedImage { get; }

        public TfRobotDestructionProgressPanel CarriedFlagProgressBar { get; }

        public VguiEditablePanel ScoreContainer { get; }

        public VguiEditablePanel BlueStolenContainer { get; }

        public VguiEditablePanel BlueDroppedPanel { get; }

        public VguiEditablePanel RedStolenContainer { get; }

        public VguiEditablePanel RedDroppedPanel { get; }

        public VguiEditablePanel BlueScoreValueContainer { get; }

        public VguiEditablePanel RedScoreValueContainer { get; }

        public VguiEditablePanel ProgressBarsContainer { get; }

        public VguiEditablePanel BlueVictoryPanel { get; }

        public TfRobotDestructionProgressPanel BlueProgressBar { get; }

        public TfRobotDestructionProgressPanel BlueProgressBarEscrow { get; }

        public VguiEditablePanel RedVictoryPanel { get; }

        public TfRobotDestructionProgressPanel RedProgressBar { get; }

        public TfRobotDestructionProgressPanel RedProgressBarEscrow { get; }
    }
}
