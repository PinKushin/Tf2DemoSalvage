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
    public void PulseAlphaAt_PastAFinishPulsesLength_HasStopped() =>
        TfControlPointPulseable.PulseAlphaAt(new CapPulse(0, 12.2f, 0.5f, false), 12.8f, 0f, false).ShouldBeNull();

    [Test]
    public void PulseAlphaAt_NoStart_IsNull() =>
        TfControlPointPulseable.PulseAlphaAt(default, 12f, 0f, false).ShouldBeNull();

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
