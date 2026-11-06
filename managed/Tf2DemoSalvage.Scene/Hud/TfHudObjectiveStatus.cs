namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudObjectiveStatus` (game/client/tf/tf_hud_objectivestatus.cpp:53): the objective panels' host, whose child the control-point icons become.</summary>
/// <remarks>
/// **Not ported:** its flag, training, robot destruction and passtime panels (B709). `ShouldDraw`'s Halloween kart and match summary checks are the HUD's.
/// </remarks>
public sealed class TfHudObjectiveStatus : VguiEditablePanel, IHudElement
{
    private const int GameTypeCp = 2;
    private const int GameTypeEscort = 3;
    private const int GameTypeArena = 4;
    // `ETFGameType` and the `TF_HUDTYPE_*` enum (tf_shareddefs.h:277-311).
    private const int GameTypeMvm = 5;
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
    }

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
        LoadControlSettings("resource/UI/HudObjectiveStatus.res", context);

        if (!_iconsAdopted)
        {
            _iconsAdopted = true;
            _controlPointIcons.SetParent(this);
        }

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
            _controlPointIcons.Visible = ControlPointsVisible(state.Rules);

            // :286-325: one track, or one per team with several trains (Helltower's hell aside).
            bool escort = EscortVisible(state.Rules);

            Escort.Visible = escort && !state.Rules.MultipleTrains;
            MultipleEscort.Visible = escort && state.Rules.MultipleTrains;
        }

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
