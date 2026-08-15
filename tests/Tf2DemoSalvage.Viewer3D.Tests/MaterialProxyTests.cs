using System;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The material proxies that make a surface move, checked against the engine's own arithmetic.
/// </summary>
/// <remarks>
/// **Predicted from Valve's source rather than from what looks plausible.** Every expected value
/// below is worked from <c>CTextureScrollMaterialProxy::OnBind</c>, so a test that passes is
/// evidence about parity with the engine and not about self-consistency.
/// </remarks>
public sealed class MaterialProxyTests
{
    [Test]
    public void AtTimeZero_TheScrollHasNotMoved()
    {
        // curtime 0 makes both offsets zero, so the transform is a pure scale — and with the
        // default scale of one, the identity. A material that scrolled at time zero would start
        // every demo mid-stripe.
        TextureTransform transform = MaterialProxies.TextureScroll(0d);

        transform.IsIdentity.ShouldBeTrue();
    }

    [Test]
    public void ScrollingAtNinetyDegrees_MovesTheSecondCoordinateOnly()
    {
        // **The capture point beam's own numbers**: rate .2, angle 90. cos(90) is 0 and sin(90) is
        // 1, so the whole rate goes into t and s stays put. Getting the two the wrong way round
        // scrolls the stripes across the beam instead of along it, which looks deliberate and is
        // not.
        TextureTransform transform = MaterialProxies.TextureScroll(1d, rate: 0.2f, angle: 90f);

        transform.Row0.W.ShouldBe(0f, 1e-5f);
        transform.Row1.W.ShouldBe(0.2f, 1e-5f);
    }

    [Test]
    public void TheOffsetWrapsIntoOneRepeat()
    {
        // 7 seconds at rate 1 is 7 whole repeats, which is the same picture as none. Valve takes
        // the fractional part for exactly this reason: unbounded growth loses precision, and on a
        // twenty-minute demo the texture visibly jitters and then stops.
        TextureTransform transform = MaterialProxies.TextureScroll(7.25d, rate: 1f, angle: 0f);

        transform.Row0.W.ShouldBe(0.25f, 1e-4f);
    }

    [Test]
    public void ANegativeRateWrapsForwards_NotBackwards()
    {
        // **The case a modulo gets wrong, and the reason the engine's odd-looking two-step is
        // copied rather than simplified.** Valve lifts a negative offset by `1 + -(int)offset`
        // before taking the fractional part, so −0.25 becomes 0.75 — a texture a quarter of the way
        // round, scrolling the other way. A plain `offset % 1` answers −0.25, which most samplers
        // wrap to the same place by accident and some do not, and it is not what the engine feeds
        // the shader either way.
        TextureTransform transform = MaterialProxies.TextureScroll(1d, rate: -0.25f, angle: 0f);

        transform.Row0.W.ShouldBe(0.75f, 1e-4f);
        transform.Row0.W.ShouldBeGreaterThanOrEqualTo(0f);
    }

    [Test]
    public void TheScaleSitsOnTheDiagonal()
    {
        // textureScale multiplies the coordinate, so it belongs at [0][0] and [1][1] — the same
        // places the identity puts its ones. Putting it in the translation column would scroll
        // rather than tile.
        TextureTransform transform = MaterialProxies.TextureScroll(0d, scale: 4f);

        transform.Row0.X.ShouldBe(4f);
        transform.Row1.Y.ShouldBe(4f);
    }

    [Test]
    public void TheIdentityLeavesACoordinateAlone()
    {
        // **The control for the whole type**, and the value a material without a transform gets.
        // A zeroed struct would pass an "is it a transform" check and send every coordinate to the
        // texture's first texel, which reads as a flat-coloured surface rather than as a missing
        // transform.
        TextureTransform identity = TextureTransform.Identity;

        float u = (0.3f * identity.Row0.X) + (0.7f * identity.Row0.Y) + identity.Row0.W;
        float v = (0.3f * identity.Row1.X) + (0.7f * identity.Row1.Y) + identity.Row1.W;

        u.ShouldBe(0.3f, 1e-6f);
        v.ShouldBe(0.7f, 1e-6f);
    }

    [Test]
    public void TheSineRunsBetweenItsBounds()
    {
        // The dark blue capture point sign: period .3, from .6 to .7. Sampled across a full cycle,
        // it must reach both ends and never leave them.
        float low = float.MaxValue;
        float high = float.MinValue;

        for (int step = 0; step <= 300; step++)
        {
            float value = MaterialProxies.Sine(step * 0.001d, period: 0.3f, minimum: 0.6f, maximum: 0.7f);

            low = MathF.Min(low, value);
            high = MathF.Max(high, value);
        }

        low.ShouldBe(0.6f, 1e-3f);
        high.ShouldBe(0.7f, 1e-3f);
    }

    [Test]
    public void ASineWithNoPeriod_HoldsStillRatherThanDividingByZero()
    {
        // A material naming no period is not asking for an oscillation, and it must not produce a
        // NaN that silently blanks the surface it paints.
        float value = MaterialProxies.Sine(3d, period: 0f, minimum: 0.2f, maximum: 0.9f);

        value.ShouldBe(0.9f);
        float.IsFinite(value).ShouldBeTrue();
    }

    [Test]
    public void AProxyArgumentFallsBackToTheEnginesDefault()
    {
        // Absent, blank and unparseable all mean "the material did not say", and the engine's Init
        // supplies the default — rate 1, angle 0, scale 1. Reading a missing key as zero would
        // stop every scroll that omits one.
        MaterialProxies.Number(null, 1f).ShouldBe(1f);
        MaterialProxies.Number("", 1f).ShouldBe(1f);
        MaterialProxies.Number("not a number", 1f).ShouldBe(1f);

        // And the form TF2's own materials use, which has no leading zero.
        MaterialProxies.Number(".2", 1f).ShouldBe(0.2f, 1e-6f);
    }
}
