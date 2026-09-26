using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Where a `.res` puts a panel, as `vgui_controls/Panel.cpp` computes it.</summary>
/// <remarks>
/// `ComputePos` (:8863), `ComputeWide` (:8698) and `ComputeTall`, called by `Panel::ApplySettings` (:4340). The proportional
/// scale is `scheme()->GetProportionalScaledValueEx`, which lives in the closed `vgui2.dll`; here it is a stand-in that
/// doubles, so each case's arithmetic is visible — the real scale is a separate, disassembled fact.
/// </remarks>
public sealed class PanelLayoutConformanceTests
{
    private static int Double(int value) => value * 2;

    /// <remarks>
    /// `vgui2.dll` 0x18000d590 (`CSchemeManager_GetProportionalScaledValue`, renamed in `tf2vgui2.gpr`):
    /// `(int)((double)screenTall / (double)baseTall * value)`, the base from `ISurface::GetProportionalBase` —
    /// `vguimatsurface.dll` stores 640 and 480 (`mov [rdx],0x280; mov [r8],0x1e0`, the one such pair in the file).
    /// </remarks>
    [TestCase(10, 480, 10)]
    [TestCase(10, 1080, 22)]
    [TestCase(-5, 1080, -11)]
    [TestCase(250, 720, 375)]
    [TestCase(7, 1440, 21)]
    public void ProportionalScaled_AtAScreenTall_IsTallOver480TimesTheValueTruncated(int value, int tall, int expected) =>
        PanelLayout.ProportionalScaled(value, tall).ShouldBe(expected);

    [TestCase("10", 20)]
    [TestCase("r10", 1000 - 20)]
    [TestCase("c-50", 500 - 100)]
    [TestCase("c-50+10", 500 - 100 + 20)]
    [TestCase("r10-5", 1000 - 20 - 10)]
    [TestCase("s0.5", 30)]
    [TestCase("p0.25", 250)]
    [TestCase("rs1", 1000 - 60)]
    [TestCase("cp-0.1", 500 - 100)]
    public void Position_EachForm_IsComputePos(string input, int expected) =>
        PanelLayout.Position(input, current: 7, size: 60, parentSize: 1000, proportional: true, Double)
            .ShouldBe(expected);

    [Test]
    public void Position_NotProportional_IsTheNumberAsWritten() =>
        PanelLayout.Position("r10", current: 0, size: 60, parentSize: 1000, proportional: false, Double).ShouldBe(990);

    [Test]
    public void Position_NoKey_KeepsWhereThePanelWas() =>
        PanelLayout.Position(null, current: 7, size: 60, parentSize: 1000, proportional: true, Double).ShouldBe(7);

    [TestCase("100", 200)]
    [TestCase("f0", 1000)]
    [TestCase("f10", 1000 - 20)]
    [TestCase("p0.5", (1000 - 0) / 2)]
    [TestCase("s2", 80)]
    public void Wide_EachForm_IsComputeWide(string input, int expected) =>
        Sizes(input, null).Wide.ShouldBe(expected);

    [Test]
    public void Wide_AProportionalFraction_ScalesAtoiThenMultipliesByAtof()
    {
        // `wide = scale( atoi("p0.5") ) ...`: atoi of "0.5" is 0, so the scaled value is 0 and the fill is the parent's.
        Sizes("p0.5", null).Wide.ShouldBe(500);
    }

    /// <remarks>`o`: the other axis computed, normalized when proportional, then scaled and multiplied by the `atof`.</remarks>
    [TestCase("o1", "100", 200, 200)]
    [TestCase("o0.5", "100", 100, 200)]
    [TestCase("o1", null, 40, 40)]
    [TestCase("f10", "o2", 980, 1960)]
    public void Sizes_TheOForm_IsTheOtherAxisRescaled(string wide, string? tall, int expectedWide, int expectedTall) =>
        Sizes(wide, tall).ShouldBe((expectedWide, expectedTall));

    [Test]
    public void Sizes_TheOFormOnAPanelNotProportional_IsStillScaled()
    {
        // Only the normalize is gated on `IsProportional`; `GetProportionalScaledValueEx` runs regardless (:8747).
        PanelLayout.Sizes("o1", "100", 40, 40, 1000, 1000, proportional: false, Double, Halve).ShouldBe((200, 100));
    }

    [Test]
    public void Sizes_BothAxesTheOForm_AreZero() =>
        Sizes("o1", "o1").ShouldBe((0, 0));

    /// <remarks>`vgui2.dll` 0x18000d460: `(int)((float)480 * (float)value / (float)screenTall)`, single precision.</remarks>
    [TestCase(22, 1080, 9)]
    [TestCase(1080, 1080, 480)]
    [TestCase(-11, 1080, -4)]
    public void ProportionalNormalized_AtAScreenTall_Is480OverTallTimesTheValueTruncated(int value, int tall, int expected) =>
        PanelLayout.ProportionalNormalized(value, tall).ShouldBe(expected);

    private static int Halve(int value) => value / 2;

    private static (int Wide, int Tall) Sizes(string? wide, string? tall) =>
        PanelLayout.Sizes(wide, tall, currentWide: 40, currentTall: 40, 1000, 1000, proportional: true, Double, Halve);
}
