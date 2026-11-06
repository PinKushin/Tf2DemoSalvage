namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudObjectiveStatus` (game/client/tf/tf_hud_objectivestatus.cpp:53): the objective panels' host, whose child the control-point icons become.</summary>
/// <remarks>
/// `ShouldDraw`'s Halloween kart and match summary checks are the HUD's.
/// </remarks>
public sealed class TfHudObjectiveStatus : VguiEditablePanel, IHudElement
{
    private const int GameTypeCtf = 1;
    private const int GameTypeCp = 2;
    private const int GameTypeEscort = 3;
    private const int GameTypeArena = 4;
    // `ETFGameType` and the `TF_HUDTYPE_*` enum (tf_shareddefs.h:277-311).
    private const int GameTypeMvm = 5;
    private const int GameTypePasstime = 7;
    private const int HudTypeCtf = 1;
    private const int HudTypeCp = 2;
    private const int HudTypeEscort = 3;
    private const int HudTypeTraining = 5;

    private readonly TfHudControlPointIcons _controlPointIcons;
    private bool _iconsAdopted;

    /// <summary>`CTFHudObjectiveStatus( "CTFHudObjectiveStatus" )`: on the viewport.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="controlPointIcons">`GET_HUDELEMENT( CHudControlPointIcons )`.</param>
    public TfHudObjectiveStatus(VguiPanel viewport, TfHudControlPointIcons controlPointIcons)
        : base(viewport, "HudObjectiveStatus")
    {
        _controlPointIcons = controlPointIcons;
        ProgressBar = new TfControlPointProgressBar(this);
        Escort = new TfHudEscort(this, "ObjectiveStatusEscort");
        MultipleEscort = new TfHudMultipleEscort(this);
        FlagPanel = new TfHudFlagObjectives(this);
        Passtime = new TfHudPasstime(this);
        Training = new TfHudTraining(this);
    }

    /// <summary>`m_pRobotDestructionPanel`: made at each scheme pass, so null before the first; shown by `IsPlayingRobotDestructionMode()` (:199-203).</summary>
    public TfHudRobotDestruction? RobotDestruction { get; private set; }

    /// <summary>`m_pTrainingPanel`: shown for the training HUD, else by `IsTrainingHUDVisible()` (:220-252).</summary>
    public TfHudTraining Training { get; }

    /// <summary>`m_pHudPasstime`, shown for `TF_GAMETYPE_PASSTIME` (:304-309).</summary>
    public TfHudPasstime Passtime { get; }

    /// <summary>`m_pFlagPanel`.</summary>
    public TfHudFlagObjectives FlagPanel { get; }

    /// <summary>`bCTFVisible` (:205): CTF by game or HUD type, never with a control-point or payload HUD; always on a hybrid map.</summary>
    /// <param name="rules">The game rules.</param>
    /// <returns>Whether the flag panel shows.</returns>
    /// <remarks>**Not ported:** Mann vs. Machine's round-state rule (:211-216).</remarks>
    public static bool FlagsVisible(Core.Scene.SceneGameRules rules) =>
        rules.HudType != HudTypeTraining
        && (rules.PlayingHybridCtfCp
            || ((rules.GameType == GameTypeCtf || rules.HudType == HudTypeCtf) && rules.HudType != HudTypeCp && rules.HudType != HudTypeEscort));

    /// <summary>`m_pEscortPanel`.</summary>
    public TfHudEscort Escort { get; }

    /// <summary>`m_pMultipleEscortPanel`.</summary>
    public TfHudMultipleEscort MultipleEscort { get; }

    /// <summary>`bEscortVisible` (:286): payload, unless the HUD type is CTF or control points.</summary>
    /// <param name="rules">The game rules.</param>
    /// <returns>Whether a payload panel shows.</returns>
    public static bool EscortVisible(Core.Scene.SceneGameRules rules) =>
        rules.HudType != HudTypeTraining
        && (rules.GameType == GameTypeEscort || rules.HudType == HudTypeEscort)
        && rules.HudType != HudTypeCtf
        && rules.HudType != HudTypeCp
        && !rules.PlayingHybridCtfCp;

    /// <summary>`m_pControlPointProgressBar`.</summary>
    public TfControlPointProgressBar ProgressBar { get; }

    /// <inheritdoc/>
    public override string ClassName => "CTFHudObjectiveStatus";

    /// <inheritdoc/>
    public int HiddenBits => 0;

    /// <inheritdoc/>
    /// <remarks>`ApplySchemeSettings` (:85): the layout, then the icons taken as a child once.</remarks>
    public override void ApplySchemeSettings(VguiContext context)
    {
        // :87-92: a new robot destruction panel each pass, the last one deleted.
        if (RobotDestruction is { } previous)
        {
            OwnGroup.PanelRemoved(previous);
            previous.SetParent(null);
        }

        RobotDestruction = new TfHudRobotDestruction(this, "ObjectiveStatusRobotDestruction");
        LoadControlSettings("resource/UI/HudObjectiveStatus.res", context);

        if (!_iconsAdopted)
        {
            _iconsAdopted = true;
            _controlPointIcons.SetParent(this);
        }

        // :103-106: the progress bar's scheme and layout at once.
        ProgressBar.InvalidateLayout(layoutNow: true, reloadScheme: true, context);

        base.ApplySchemeSettings(context);
    }

    /// <summary>`SetVisiblePanels` (:180) for the icons: shown for control points, arena and Mann vs. Machine.</summary>
    /// <param name="rules">The game rules.</param>
    /// <returns>`bCPVisible`; false in training, which hides the icons.</returns>
    public static bool ControlPointsVisible(Core.Scene.SceneGameRules rules)
    {
        if (rules.HudType == HudTypeTraining)
        {
            return false;
        }

        return rules.PlayingHybridCtfCp
            || ((rules.GameType is GameTypeCp or GameTypeArena or GameTypeMvm || rules.HudType == HudTypeCp)
                && rules.HudType != HudTypeCtf
                && rules.HudType != HudTypeEscort);
    }

    /// <inheritdoc/>
    /// <remarks>`Think` (:301): nothing without game rules.</remarks>
    protected override void OnThink()
    {
        if (HudViewport.Of(this) is not { } viewport)
        {
            return;
        }

        HudState state = viewport.State;

        if (state is { InGame: true, Rules.Present: true })
        {
            if (RobotDestruction is not null)
            {
                RobotDestruction.Visible = state.Rules.PlayingRobotDestruction;
            }

            _controlPointIcons.Visible = ControlPointsVisible(state.Rules);
            FlagPanel.Visible = FlagsVisible(state.Rules);
            Passtime.Visible = state.Rules.HudType != HudTypeTraining && state.Rules.GameType == GameTypePasstime;
            Training.Visible = state.Rules.HudType == HudTypeTraining || state.Rules.TrainingHudVisible;

            // :286-325: one track, or one per team with several trains (Helltower's hell aside).
            bool escort = EscortVisible(state.Rules);

            Escort.Visible = escort && !state.Rules.MultipleTrains;
            MultipleEscort.Visible = escort && state.Rules.MultipleTrains;
        }

        if (FlagPanel.Visible)
        {
            FlagPanel.Update(state);
        }

        if (Passtime.Visible)
        {
            Passtime.Update(state);
        }

        // Its own 10 ms tick signal (:128), which reaches it hidden as well.
        Training.Update(state);

        // Its own 50 ms tick signal (tf_hud_robot_destruction_status.cpp:301), hidden as well.
        RobotDestruction?.Update(state);

        if (Escort.Visible)
        {
            Escort.Update(state);
        }

        if (MultipleEscort.Visible)
        {
            MultipleEscort.Update(state);
        }

        // `UpdateProgressBarFor` (hud_controlpointicons.cpp:1183): nothing while the icons are hidden or off the points.
        int touched = state.TouchedPoint;
        bool onPoint = _controlPointIcons.Visible && touched >= 0 && touched < (state.Objectives?.Points.Count ?? 0);

        ProgressBar.Update(state, onPoint ? _controlPointIcons.IconFor(touched) : null, touched, viewport.Context);
    }
}
