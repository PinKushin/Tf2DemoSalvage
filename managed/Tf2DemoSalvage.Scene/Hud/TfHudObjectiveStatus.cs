namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CTFHudObjectiveStatus` (game/client/tf/tf_hud_objectivestatus.cpp:53): the objective panels' host, whose child the control-point icons become.</summary>
/// <remarks>
/// **Not ported:** its flag, escort, multiple escort, training, robot destruction and passtime panels, and the control-point
/// progress bar it owns (B709). `ShouldDraw`'s Halloween kart and match summary checks are the HUD's.
/// </remarks>
public sealed class TfHudObjectiveStatus : VguiEditablePanel, IHudElement
{
    private const int GameTypeCp = 2;
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
        : base(viewport, "HudObjectiveStatus") => _controlPointIcons = controlPointIcons;

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
        if (HudViewport.Of(this) is { State: { InGame: true, Rules.Present: true } } viewport)
        {
            _controlPointIcons.Visible = ControlPointsVisible(viewport.State.Rules);
        }
    }
}
