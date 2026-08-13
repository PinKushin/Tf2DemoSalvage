using System;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Bsp;

/// <summary>
/// The one curve between Source's stored light and the screen.
/// </summary>
/// <remarks>
/// **Shared by two lighting paths that must agree.** Lightmap samples and static prop vertex colours
/// are both stored linear and both drawn by the same shader; if only one is taken through the curve,
/// that class of surface is darker than everything around it. That is exactly what happened — every
/// prop on the map read as a black blob for as long as this was applied to lightmaps alone.
/// </remarks>
public sealed class SourceGammaTests
{
    [Test]
    public void ToDisplay_LeavesBlackAndWhiteWhereTheyAre()
    {
        // The endpoints are fixed under any gamma, which is why they are the wrong thing to test on
        // their own - but a curve that moves them is not a gamma curve at all.
        SourceGamma.ToDisplay(0f).ShouldBe(0f);
        SourceGamma.ToDisplay(1f).ShouldBe(1f);
    }

    [Test]
    public void ToDisplay_BrightensTheMiddle()
    {
        // **The direction matters and is easy to invert.** Linear to display raises mid tones,
        // while the inverse darkens them - which reads as a deliberate moody grade rather than as
        // a defect. Half of full linear light lands near three quarters on screen.
        SourceGamma.ToDisplay(0.5f).ShouldBeInRange(0.72f, 0.75f);
        SourceGamma.ToDisplay(0.25f).ShouldBeGreaterThan(0.25f);
    }

    [Test]
    public void ToDisplay_IsTheCurveValvesOwnTableIsBuiltFrom()
    {
        // vrad builds lineartovertex as pow(i / 1024.0, 1.0 / gamma) and lineartolightmap from the
        // same value scaled to a byte. Pinning a few points keeps this honest against the table it
        // is standing in for.
        SourceGamma.ToDisplay(0.25f).ShouldBeInRange(0.53f, 0.54f);
        SourceGamma.ToDisplay(0.75f).ShouldBeInRange(0.87f, 0.88f);
    }

    [Test]
    public void ToDisplay_ClampsOutOfRangeLight()
    {
        // HDR samples exceed one legitimately, and a negative cannot happen but costs nothing to
        // refuse. Neither should produce NaN, which propagates silently through a whole frame.
        SourceGamma.ToDisplay(4f).ShouldBe(1f);
        SourceGamma.ToDisplay(-1f).ShouldBe(0f);
    }

    [Test]
    public void ToDisplayByte_NormalisesAgainstAFullByte()
    {
        // A sample of 255 at exponent zero is full brightness; the byte form has to divide by 255
        // before the curve or every value saturates.
        SourceGamma.ToDisplayByte(255f).ShouldBe((byte)255);
        SourceGamma.ToDisplayByte(0f).ShouldBe((byte)0);
        SourceGamma.ToDisplayByte(64f).ShouldBeGreaterThan((byte)64);
    }
}
