using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFHudFlagObjectives`, `CTFFlagStatus` and `CTFArrowPanel` (game/client/tf/tf_hud_flagstatus.cpp).</summary>
public sealed class TfHudFlagObjectivesConformanceTests
{
    private static SceneFlag Flag(int status) => new(51, 2, status, false, false, 0, string.Empty, default, 0, 0f);

    /// <remarks>`UpdateStatus` (:794-870): `m_bCarryingFlag` set while the local player holds a flag, cleared when it is let go.</remarks>
    [Test]
    public void Update_PickingUpThenDropping_SetsThenClearsCarrying()
    {
        TfHudFlagObjectives panel = new(new VguiPanel(null, "Owner"));
        HudState state = new(true, true, 0, 125, true, Team: 3, LocalIndex: 1);
        SceneFlag carried = new(51, 2, 1, false, false, 0, string.Empty, default, 1, 0f);

        panel.Update(state with { CaptureFlags = new SceneFlags([carried], []) });
        bool held = panel.CarryingFlag;
        panel.Update(state with { CaptureFlags = new SceneFlags([Flag(2)], []) });

        (held, panel.CarryingFlag).ShouldBe((true, false));
    }

    /// <remarks>
    /// :834-838 and :863-868: `FlagOutline` starts once on picking up — `m_bFlagAnimationPlayed` holds it over a second update
    /// — and once more on letting go. Its sequence here animates a marker, so each start is one more active animation.
    /// </remarks>
    [Test]
    public void Update_CarryingTwiceThenDropping_StartsFlagOutlineOnceEach()
    {
        (HudViewport viewport, TfHudFlagObjectives panel) = WithOutlineScript();
        HudState state = new(true, true, 0, 125, true, Team: 3, LocalIndex: 1);
        HudState carrying = state with { CaptureFlags = new SceneFlags([new SceneFlag(51, 2, 1, false, false, 0, string.Empty, default, 1, 0f)], []) };

        panel.Update(carrying);
        panel.Update(carrying);
        int afterCarry = viewport.Animations.ActiveAnimationCount;
        viewport.Animations.CancelAllAnimations();
        panel.Update(state with { CaptureFlags = new SceneFlags([Flag(2)], []) });

        (afterCarry, viewport.Animations.ActiveAnimationCount).ShouldBe((1, 1));
    }

    private static (HudViewport Viewport, TfHudFlagObjectives Panel) WithOutlineScript()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/hudanimations_manifest.txt"] = Encoding.UTF8.GetBytes("hudanimations_manifest { file scripts/hud.txt }"),
            ["scripts/hud.txt"] = Encoding.UTF8.GetBytes("event FlagOutline { Animate Marker Alpha 255 Linear 0.0 1.0 }"),
        };
        Content.Assets.KeyValuesTree scheme = Content.Assets.KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Read = path => files.GetValueOrDefault(path),
        };
        HudViewport viewport = new() { Context = context };

        _ = new VguiPanel(viewport, "Marker");
        viewport.LoadHudAnimations(context).ShouldBeTrue();
        return (viewport, new TfHudFlagObjectives(viewport));
    }

    /// <remarks>`ApplySchemeSettings` (:401-468).</remarks>
    [TestCase(0, false, "if_no_flags")]
    [TestCase(1, false, "if_hybrid_single")]
    [TestCase(2, true, "if_hybrid if_hybrid_double")]
    [TestCase(1, true, "if_hybrid if_hybrid_single")]
    public void Conditions_ByFlagCountAndHybrid_AreValves(int flags, bool hybrid, string conditions) =>
        string.Join(' ', TfHudFlagObjectives.Conditions(flags, hybrid)!).ShouldBe(conditions);

    [Test]
    public void Conditions_TwoFlagsOnPlainCtf_AreNone() =>
        TfHudFlagObjectives.Conditions(2, false).ShouldBeNull();

    /// <remarks>`CTFFlagStatus::UpdateStatus` (:327).</remarks>
    [TestCase(0, "../hud/objectives_flagpanel_ico_flag_home")]
    [TestCase(1, "../hud/objectives_flagpanel_ico_flag_moving")]
    [TestCase(2, "../hud/objectives_flagpanel_ico_flag_dropped")]
    public void StatusImage_ByStatus_IsValves(int status, string image) =>
        TfFlagStatus.StatusImage(Flag(status)).ShouldBe(image);

    /// <remarks>`UpdateStatus` (:819-833): CTF carries the enemy's flag, attack/defend through resource control the own team's.</remarks>
    [TestCase(0, 2, 3)]
    [TestCase(0, 3, 2)]
    [TestCase(1, 3, 3)]
    [TestCase(4, 2, 2)]
    [TestCase(5, 3, 2)]
    public void CarriedImageTeam_ByTypeAndTeam_IsValves(int type, int localTeam, int team) =>
        TfHudFlagObjectives.CarriedImageTeam(type, localTeam).ShouldBe(team);

    /// <remarks>`CCaptureFlag::GetHudIcon` (entity_capture_flag.cpp:311).</remarks>
    [TestCase("", 3, "../hud/objectives_flagpanel_carried_blue")]
    [TestCase("../hud/x", 0, "../hud/x_red")]
    public void HudIcon_ByIconAndTeam_IsValves(string icon, int team, string image) =>
        TfHudFlagObjectives.HudIcon(icon, team).ShouldBe(image);

    /// <remarks>`GetAngleRotation` (:167): yaw 0 looks down +X; +Y is to the left, so a flag there is -90.</remarks>
    [TestCase(100f, 0f, 0f)]
    [TestCase(0f, 100f, -90f)]
    [TestCase(0f, -100f, 90f)]
    [TestCase(-100f, 0f, 180f)]
    public void AngleRotation_FromYawZero_IsSignedTowardTheRight(float x, float y, float degrees) =>
        TfArrowPanel.AngleRotation((0f, 0f, 0f), (30f, 0f, 0f), (x, y, 50f)).ShouldBe(degrees, 0.01f);

    [TestCase(2, false, "hud/objectives_flagpanel_compass_red")]
    [TestCase(3, true, "hud/objectives_flagpanel_compass_blue_noArrow")]
    [TestCase(0, true, "hud/objectives_flagpanel_compass_grey")]
    public void Material_ByTeamAndCarrier_IsValves(int team, bool carried, string material) =>
        TfArrowPanel.Material(team, carried).ShouldBe(material);

    /// <remarks>tf_hud_objectivestatus.cpp:205-209.</remarks>
    [TestCase(1, 0, false, true)]
    [TestCase(2, 1, false, true)]
    [TestCase(1, 2, false, false)]
    [TestCase(3, 0, true, true)]
    [TestCase(2, 0, false, false)]
    public void FlagsVisible_ByGameAndHudType_FollowsSetVisiblePanels(int gameType, int hudType, bool hybrid, bool visible) =>
        TfHudObjectiveStatus.FlagsVisible(new SceneGameRules(false, 0, false) { GameType = gameType, HudType = hudType, PlayingHybridCtfCp = hybrid })
            .ShouldBe(visible);
}
