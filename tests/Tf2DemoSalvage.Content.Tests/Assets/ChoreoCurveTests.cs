using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>CCurveData::GetIntensity</c> on hand-built ramps whose answers are worked by hand (D38, B513).
/// </summary>
/// <remarks>
/// One sample at (1, 1) over a two-second event: the closing edges are (0, 0) and (2, 0), because the binary restore
/// carries no edge info (<c>choreoevent.cpp:3558</c>).
/// </remarks>
public sealed class ChoreoCurveTests
{
    private static readonly SceneCurveSample[] Peak = [new(1f, 1f)];

    [Test]
    public void Intensity_AnEmptyRamp_IsFull() => ChoreoCurve.Intensity([], 0.3f, 2f, hasEndTime: true).ShouldBe(1f);

    [Test]
    public void Intensity_AnEventWithNoEnd_IsZero() =>
        ChoreoCurve.Intensity(Peak, 1f, 2f, hasEndTime: false).ShouldBe(0f);

    [Test]
    public void Intensity_AtASample_IsItsValue() => ChoreoCurve.Intensity(Peak, 1f, 2f, true).ShouldBe(1f);

    [Test]
    public void Intensity_TheDefaultCatmullRom_BetweenTheEdgeAndThePeak_IsWorkedByHand()
    {
        // p1 = p2 = (0, 0) (the clamped edge takes the start's X), p3 = (1, 1), p4 = (1, 0) (the clamped end takes the
        // end's X). At t = 0.5 the three rows sum to -0.1875 + 0.5 + 0.25 = 0.5625.
        ChoreoCurve.Intensity(Peak, 0.5f, 2f, true).ShouldBe(0.5625f);
    }

    [Test]
    public void Intensity_LinearByDefault_IsHalfWayUpTheRamp() =>
        ChoreoCurve.Intensity(Peak, 0.5f, 2f, true, defaultCurve: 0x0606).ShouldBe(0.5f);

    [Test]
    public void Intensity_LinearByDefault_FallsToTheClosingEdge() =>
        ChoreoCurve.Intensity(Peak, 1.75f, 2f, true, defaultCurve: 0x0606).ShouldBe(0.25f);

    [TestCase(0.5f, 0f)]
    [TestCase(1.5f, 1f)]
    public void Intensity_AHold_KeepsTheStartsValue(float time, float expected) =>
        ChoreoCurve.Intensity(Peak, time, 2f, true, defaultCurve: 0x0F0F).ShouldBe(expected);

    [Test]
    public void Intensity_EaseIn_IsTheSineOfAQuarterTurn() =>
        ChoreoCurve.Intensity(Peak, 0.5f, 2f, true, defaultCurve: 0x0202).ShouldBe((float)Math.Sin(Math.PI / 4), 1e-7f);

    [Test]
    public void Between_TwoDifferentCurves_LerpTheirAnswersByTheFraction()
    {
        // The start's OUTBOUND is linear (0.5); the end's INBOUND is ease-in (0.7071); half way between them.
        float value = ChoreoCurve.Between(0x0006, 0x0200, (0f, 0f), (0f, 0f), (1f, 1f), (2f, 0f), 0.5f);

        value.ShouldBe(0.5f + ((MathF.Sin(MathF.PI / 4) - 0.5f) * 0.5f), 1e-6f);
    }

    [Test]
    public void Between_AnInboundHold_TakesTheEndsValue() =>
        ChoreoCurve.Between(0x0006, 0x0F00, (0f, 0f), (0f, 0.2f), (1f, 0.8f), (2f, 0f), 0.1f).ShouldBe(0.8f);

    [Test]
    public void Interpolate_AnUnknownType_FallsThroughToCatmullRomNormalizeX() =>
        ChoreoCurve.Interpolate(99, (0f, 0f), (0f, 0f), (1f, 1f), (1f, 0f), 0.5f)
            .ShouldBe(ChoreoCurve.Interpolate(1, (0f, 0f), (0f, 0f), (1f, 1f), (1f, 0f), 0.5f));
}
