using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CControlPointIcon::IsPointLocked` (hud_controlpointicons.cpp:414), `TeamMayCapturePoint` (tf_gamerules.cpp:16770) and the capture arrow (:1684-1731).</summary>
public sealed class TfControlPointIconConformanceTests
{
    private const int Red = 2;
    private const int Blue = 3;
    private const int NoPrevious = 0xFFFFFF;

    private static SceneControlPoint Point(int owner, int previous = NoPrevious, bool locked = false) => new(
        true,
        owner,
        0,
        0,
        locked,
        false,
        0f,
        0f,
        default,
        default,
        default,
        new TeamQuad<bool>(false, false, true, true),
        default,
        true,
        previous,
        previous);

    // A five point map as f12's: blue owns 0 and 1 from its base at 0, red owns 2, 3 and 4 from its base at 4.
    private static SceneObjectives FivePoints() => new(
        [Point(Blue), Point(Blue), Point(Red), Point(Red), Point(Red)],
        string.Empty,
        new Dictionary<int, string>(),
        BaseRed: 4,
        BaseBlue: 0);

    /// <remarks>Blue's farthest owned is 1 and red's is 2; a team may take a point within one of its farthest. TF2's frame of f12 shows this line.</remarks>
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    public void IsPointLocked_FivePointsSplitTwoAndThree_LocksAllButTheFronts(int index, bool locked) =>
        TfControlPointIcon.IsPointLocked(default, FivePoints(), index).ShouldBe(locked);

    [Test]
    public void IsPointLocked_APointMarkedLocked_IsLocked()
    {
        SceneObjectives objectives = FivePoints() with { Points = [Point(Blue), Point(Blue, locked: true), Point(Red), Point(Red), Point(Red)] };

        TfControlPointIcon.IsPointLocked(default, objectives, 1).ShouldBeTrue();
    }

    [Test]
    public void IsPointLocked_APointRequiringItself_IsOpen()
    {
        int self = SceneObjectives.PackPrevious(0, -1, -1);
        SceneObjectives objectives = FivePoints() with { Points = [Point(Blue, self), Point(Blue), Point(Red), Point(Red), Point(Red)] };

        TfControlPointIcon.IsPointLocked(default, objectives, 0).ShouldBeFalse();
    }

    /// <summary>`o.detailTexCoord.xy = v.vTexCoord1.xy` (vertexlit_and_unlit_generic_vs20.fxc:275-276): `$detailscale` does not reach a separate coordinate.</summary>
    [TestCase("\"$separatedetailuvs\" \"1\"", 1f)]
    [TestCase("", 4f)]
    public void Read_ADetailWithOrWithoutSeparateUvs_ScalesOnlyWithout(string separate, float scale)
    {
        byte[] vmt = System.Text.Encoding.UTF8.GetBytes($$"""
            "UnlitGeneric" { "$detail" "x" "$detailscale" "4" {{separate}} }
            """);
        VguiShading shading = VguiShading.Read(Content.Assets.VmtMaterial.Load(vmt, _ => null), default(MapTexture)).ShouldNotBeNull();

        shading.Detail.ShouldNotBeNull().Scale.ShouldBe((scale, scale));
    }

    [Test]
    public void IsPointLocked_ABaseIndexPastThePoints_DoesNotThrow() =>
        TfControlPointIcon.IsPointLocked(default, FivePoints() with { BaseRed = 9, BaseBlue = 9 }, 1).ShouldBeTrue();

    [Test]
    public void IsPointLocked_ACannotCapPoint_IsLocked()
    {
        SceneControlPoint noTeam = Point(Blue) with { CanCap = default };

        TfControlPointIcon.IsPointLocked(default, FivePoints() with { Points = [Point(Blue), noTeam, Point(Red), Point(Red), Point(Red)] }, 1).ShouldBeTrue();
    }

    [Test]
    public void Next_ACaptureStarting_PulsesFromNowAcceleratingWithNoEnd() =>
        default(CapPulse).Next(Red, Blue, true, 10f).ShouldBe(new CapPulse(Red, 10f, 0f, true));

    [Test]
    public void Next_ACaptureAbandoned_StopsButRemembersTheCappers() =>
        new CapPulse(Red, 10f, 0f, true).Next(0, Blue, true, 11f).ShouldBe(new CapPulse(Red, 0f, 0f, true));

    [Test]
    public void Next_TheCappersBecomingOwner_PulsesHalfASecondAfterTheSwoop() =>
        new CapPulse(Red, 0f, 0f, true).Next(0, Red, false, 12f).ShouldBe(new CapPulse(0, 12.2f, 0.5f, false));

    [Test]
    public void Next_NothingChanged_KeepsThePulse() =>
        new CapPulse(Red, 10f, 0f, true).Next(Red, Blue, false, 11f).ShouldBe(new CapPulse(Red, 10f, 0f, true));

    /// <remarks>By hand: at half a capture the speed is 2 + 3 * 0.5 = 3.5; one second in, 255 * |sin 3.5| = 89.45.</remarks>
    [Test]
    public void PulseAlphaAt_HalfCapturedOneSecondIn_RunsAtTheRemappedSpeed() =>
        TfControlPointPulseable.PulseAlphaAt(new CapPulse(Red, 10f, 0f, true), 11f, 0.5f, false).ShouldNotBeNull().ShouldBe(89.45f, 0.01f);

    [Test]
    public void PulseAlphaAt_ADefenderBeforeTheSwitch_HasNotStarted() =>
        TfControlPointPulseable.PulseAlphaAt(new CapPulse(Red, 10f, 0f, true), 10.1f, 0f, true).ShouldBeNull();

    [Test]
    public void Step_TheFramePastAFinishPulsesLength_DrawsOnceThenStops()
    {
        TfControlPointPulseable image = new(new VguiPanel(null, "parent"), "BaseImage");
        CapPulse pulse = new(0, 12.2f, 0.5f, false);

        image.Step(pulse, 12.8f, 0f, false);
        image.PulseAlpha.ShouldNotBeNull();

        image.Step(pulse, 12.85f, 0f, false);
        image.PulseAlpha.ShouldBeNull();
    }

    [Test]
    public void Step_ASeekBackIntoAStoppedPulse_DrawsItAgain()
    {
        TfControlPointPulseable image = new(new VguiPanel(null, "parent"), "BaseImage");
        CapPulse pulse = new(0, 12.2f, 0.5f, false);

        image.Step(pulse, 12.8f, 0f, false);
        image.Step(pulse, 12.85f, 0f, false);
        image.PulseAlpha.ShouldBeNull("the control: stopped");

        image.Step(pulse, 12.4f, 0f, false);

        image.PulseAlpha.ShouldNotBeNull();
    }

    [Test]
    public void Step_TheLocalTeamChangingMidPulse_KeepsTheDelayFixedAtTheStart()
    {
        TfControlPointPulseable image = new(new VguiPanel(null, "parent"), "BaseImage");
        CapPulse pulse = new(Red, 10f, 0f, true);

        image.Step(pulse, 10f, 0f, true);
        image.Step(pulse, 10.1f, 0f, false);

        image.PulseAlpha.ShouldBeNull();
    }

    /// <remarks>By hand: a quarter of the way through 0.4 s over a 400 tall panel is y 100.</remarks>
    [Test]
    public void ImageY_AQuarterThroughTheSwoop_IsAQuarterDown() =>
        TfControlPointSwoop.ImageY(0.1f, 400).ShouldBe(100);

    /// <remarks>By hand: halfway through 0.2 s from 150 to 50 is 100.</remarks>
    [Test]
    public void ImageSize_HalfwayThroughTheCapturePulse_IsHalfwayToTheIcon() =>
        TfControlPointCapturePulse.ImageSize(0.1f, 150, 50).ShouldBe(100);

    [Test]
    public void Step_ASwoopPastItsLength_ShowsOneFrameThenHides()
    {
        TfControlPointSwoop swoop = new(new VguiPanel(null, "viewport"), "CapHighlightImage");

        swoop.Step(10f, 10.41f, false);
        swoop.Visible.ShouldBeTrue();

        swoop.Step(10f, 10.45f, false);
        swoop.Visible.ShouldBeFalse();
    }

    [Test]
    public void Step_ACapturePulseAtItsStart_IsNotYetShown()
    {
        TfControlPointCapturePulse flash = new(new VguiPanel(null, "viewport"), "CapPulse");

        flash.Step(10f, 50, 10f, false);

        flash.Visible.ShouldBeFalse();
    }

    [Test]
    public void Step_ASwoopInTheFreezeCam_IsHidden()
    {
        TfControlPointSwoop swoop = new(new VguiPanel(null, "viewport"), "CapHighlightImage");

        swoop.Step(10f, 10.1f, true);

        swoop.Visible.ShouldBeFalse();
    }

    /// <remarks>`(int)( 13.5 - 10 )` is 3, inside the five seconds `OnTick` shows (:1851-1876).</remarks>
    [Test]
    public void Update_ThreeAndAHalfSecondsBeforeUnlock_Shows3()
    {
        TfControlPointCountdown countdown = new(new VguiPanel(null, "icon"), "Countdown");

        countdown.Update(Running(10f), 13.5f);

        countdown.Visible.ShouldBeTrue();
    }

    [Test]
    public void Update_SixSecondsBeforeUnlock_IsHidden()
    {
        TfControlPointCountdown countdown = new(new VguiPanel(null, "icon"), "Countdown");

        countdown.Update(Running(10f), 16f);

        countdown.Visible.ShouldBeFalse();
    }

    [Test]
    public void Update_PastTheUnlock_ClearsTheTimeAsOnTickDoes()
    {
        TfControlPointCountdown countdown = new(new VguiPanel(null, "icon"), "Countdown");

        countdown.Update(Running(10f), 13.5f);
        countdown.Update(Running(14f), 13.5f);

        (countdown.Visible, countdown.UnlockTime).ShouldBe((false, 0f));
    }

    /// <remarks>`ceil( 14.5 - 10 )` is 5; under ten seconds the first tick, at `SetTimerTime`, turns it red (:593-603).</remarks>
    [Test]
    public void Update_APointTimerFourAndAHalfSecondsOut_Shows5InRed()
    {
        TfControlPointIcon icon = new(new VguiPanel(null, "icons"), "ControlPointIcon");
        SceneObjectives objectives = FivePoints() with { Points = [Point(Blue) with { TimerTime = 14.5f }, Point(Blue), Point(Red), Point(Red), Point(Red)] };

        icon.Update(Running(10f) with { Objectives = objectives }, 0);

        (icon.TimerLabel.Visible, icon.TimerLabel.Text, icon.TimerLabel.FgColor).ShouldBe((true, "5", ((byte)192, (byte)28, (byte)0, (byte)255)));
    }

    /// <remarks>Set 30 s out at 0: ticks every 0.15 s; the first at or under ten seconds left is tick 134 (20.1 s), red.</remarks>
    [TestCase(20.05f, false)]
    [TestCase(20.12f, true)]
    [TestCase(20.27f, false)]
    [TestCase(20.42f, true)]
    public void TimerIsRed_ATimerSetThirtySecondsOut_FlashesFromTheTenSecondMark(float now, bool red) =>
        TfControlPointIcon.TimerIsRed(0f, 30f, now).ShouldBe(red);

    [Test]
    public void Update_NoPointTimer_HidesTheLabel()
    {
        TfControlPointIcon icon = new(new VguiPanel(null, "icons"), "ControlPointIcon");

        icon.Update(Running(10f) with { Objectives = FivePoints() }, 0);

        icon.TimerLabel.Visible.ShouldBeFalse();
    }

    private static HudState Running(float now) => default(HudState) with { InGame = true, CurTime = now, RoundState = 4 };

    [Test]
    public void PulseAlphaAt_NoStart_IsNull() =>
        TfControlPointPulseable.PulseAlphaAt(default, 12f, 0f, false).ShouldBeNull();

    /// <remarks>`SetVisiblePanels` (tf_hud_objectivestatus.cpp:259-263) over tf_shareddefs.h:277-311: game types 2 (CP), 4 (arena)
    /// and 5 (MvM), unless the HUD type is CTF (1), escort (3) or training (5).</remarks>
    [TestCase(2, 0, false, true)]
    [TestCase(3, 0, false, false)]
    [TestCase(1, 0, true, true)]
    [TestCase(2, 3, false, false)]
    [TestCase(2, 5, false, false)]
    [TestCase(4, 4, false, true)]
    [TestCase(5, 0, false, true)]
    [TestCase(6, 0, false, false)]
    public void ControlPointsVisible_ByGameAndHudType_FollowsSetVisiblePanels(int gameType, int hudType, bool hybrid, bool visible) =>
        TfHudObjectiveStatus.ControlPointsVisible(new Core.Scene.SceneGameRules(false, 0, false) { GameType = gameType, HudType = hudType, PlayingHybridCtfCp = hybrid })
            .ShouldBe(visible);

    /// <remarks>`UpdateBarText` (hud_controlpointicons.cpp:1585-1599): a point of the recorder's own the enemy can take back.</remarks>
    [Test]
    public void BarText_OnAnOwnPointTheEnemyCanTake_AsksToDefendIt() =>
        TfControlPointProgressBar.BarText(Running(10f) with { Team = Blue }, FivePoints(), 1).ShouldBe("#Team_Capture_OwnPoint");

    [Test]
    public void BarText_OnALockedPoint_IsNotNow()
    {
        SceneObjectives objectives = FivePoints() with { Points = [Point(Blue), Point(Blue), Point(Red, locked: true), Point(Red), Point(Red)] };

        TfControlPointProgressBar.BarText(Running(10f) with { Team = Blue }, objectives, 2).ShouldBe("#Team_Capture_NotNow");
    }

    [Test]
    public void BarText_InSetupWithARoundTimer_IsNotNow() =>
        TfControlPointProgressBar.BarText(
            Running(10f) with { Team = Blue, Rules = new SceneGameRules(false, 0, false) { Setup = true }, RoundTimers = [default] },
            FivePoints(),
            2).ShouldBe("#Team_Capture_NotNow");

    /// <remarks>Blue's farthest is 1, so 3 is two away: `TeamMayCapturePoint` refuses, linear (:1602-1612).</remarks>
    [Test]
    public void BarText_OnAPointBeyondTheFront_IsLinear() =>
        TfControlPointProgressBar.BarText(Running(10f) with { Team = Blue }, FivePoints(), 3).ShouldBe("#Team_Capture_Linear");

    [Test]
    public void BarText_AFrontPointWithNobodyCapping_WaitsForATeammate() =>
        TfControlPointProgressBar.BarText(Running(10f) with { Team = Blue }, FivePoints(), 2).ShouldBe("#Team_Waiting_for_teammate");

    /// <remarks>`PerformLayout` (hud_controlpointicons.cpp:976-1046): "3,1 2" is a pyramid of 3 over 1 and 2.</remarks>
    [Test]
    public void ParseLayout_APyramid_IsOneOverTwo() =>
        TfHudControlPointIcons.ParseLayout("3,1 2", point => point).ShouldBe([[3], [1, 2]]);

    /// <remarks>An unknown point moves neither the line nor the position, so the comma after it is lost: 9 is no icon.</remarks>
    [Test]
    public void ParseLayout_AnUnknownPointBeforeAComma_LosesTheComma() =>
        TfHudControlPointIcons.ParseLayout("1 9,2", point => point == 9 ? -1 : point).ShouldBe([[1, 2]]);

    /// <remarks>"1, 2": the space after the comma is stepped over, so no empty number reads as point 0, and " 2" trails.</remarks>
    [Test]
    public void ParseLayout_ACommaThenASpace_ReadsNoPointZero() =>
        TfHudControlPointIcons.ParseLayout("1, 2", point => point).ShouldBe([[1], [2]]);

    /// <remarks>tf_hud_arena_capturepoint.cpp:117-159: arena (4), the stalemate state (7), `ceil( 13.5 - 10 )` is 4.</remarks>
    [TestCase(4, 7, 4)]
    [TestCase(2, 7, null)]
    [TestCase(4, 4, null)]
    public void SecondsShown_ArenaInItsStalemate_CountsDownFromFive(int gameType, int roundState, int? shown) =>
        TfHudArenaCapPointCountdown.SecondsShown(
            Running(10f) with
            {
                HasLocalPlayer = true,
                RoundState = roundState,
                Rules = new SceneGameRules(false, 0, false) { Present = true, GameType = gameType, CapturePointEnableTime = 13.5f },
            }).ShouldBe(shown);

    [Test]
    public void PackPrevious_ThreeSlots_UnpacksEachWithItsSign()
    {
        int packed = SceneObjectives.PackPrevious(3, -1, 7);

        (SceneObjectives.UnpackPrevious(packed, 0), SceneObjectives.UnpackPrevious(packed, 1), SceneObjectives.UnpackPrevious(packed, 2))
            .ShouldBe((3, -1, 7));
    }

    /// <remarks>By hand from the engine's constants: movement 48/69, arrow 15/69, indent 0.1.</remarks>
    [Test]
    public void TextureRange_RedAtTheStart_RunsFromTheFarEndToTheIndent()
    {
        (float s0, _, float s1, _) = TfControlPointCapArrow.TextureRange(0f, false, Red);

        s0.ShouldBe(0.9f + (48f / 69f), 0.0001f);
        s1.ShouldBe(0.9f, 0.0001f);
    }

    [Test]
    public void TextureRange_BlueAtTheEnd_IsRedsMirrored()
    {
        (float redS0, _, float redS1, _) = TfControlPointCapArrow.TextureRange(1f, false, Red);
        (float blueS0, _, float blueS1, _) = TfControlPointCapArrow.TextureRange(1f, false, Blue);

        (blueS0, blueS1).ShouldBe((redS1, redS0));
    }

    [Test]
    public void TextureRange_SwipingUp_MovesOnlyTheVertical()
    {
        (float s0, float t0, float s1, float t1) = TfControlPointCapArrow.TextureRange(0.5f, true, Red);

        (s0, s1).ShouldBe((0f, 1f));
        t0.ShouldBeLessThan(t1);
    }
}
