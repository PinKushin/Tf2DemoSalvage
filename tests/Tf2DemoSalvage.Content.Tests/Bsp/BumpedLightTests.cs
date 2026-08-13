using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Combining three directional lightmaps against a surface normal.
/// </summary>
/// <remarks>
/// **Two combines that share nothing, and on cp_process_final it is 13 materials against 14.**
/// Neither is the exotic case, so getting either wrong leaves half the bumped surfaces lit
/// incorrectly — and incorrectly in the direction that still looks like lighting, which is the
/// failure this project keeps finding.
///
/// The ordinary path squares saturated dot products against the basis and divides by their sum.
/// The self-shadowing path uses the normal's components directly, with no dots, no squaring and no
/// division. Both are transcribed from <c>lightmappedgeneric_ps2_3_x.h</c>.
/// </remarks>
public sealed class BumpedLightTests
{
    private static readonly (float Red, float Green, float Blue) First = (1f, 0f, 0f);
    private static readonly (float Red, float Green, float Blue) Second = (0f, 1f, 0f);
    private static readonly (float Red, float Green, float Blue) Third = (0f, 0f, 1f);

    [Test]
    public void Combine_ANormalStraightOutOfTheSurface_MixesTheThreeEvenly()
    {
        // (0,0,1) is equidistant from all three basis vectors - each has z = 1/sqrt(3) - so the
        // weights are equal and the result is the plain average. **The division by the sum is what
        // makes this an average rather than a third of one**: the three squared dots come to 1/3,
        // not 1, so without it the surface would be a third as bright as it should be.
        (float red, float green, float blue) = BumpedLight.Combine(
            (0f, 0f, 1f), First, Second, Third, selfShadowing: false);

        red.ShouldBe(1f / 3f, 0.0001);
        green.ShouldBe(1f / 3f, 0.0001);
        blue.ShouldBe(1f / 3f, 0.0001);
    }

    [Test]
    public void Combine_ANormalLeaningIntoOneBasisVector_FavoursThatLightmap()
    {
        // Leaning towards basis vector 0, which points along +x. A transcription that dropped the
        // squaring still favours the right one, so the test is the MAGNITUDE: squared weights make
        // the leading term dominate far harder than linear ones would.
        (float red, float green, float blue) = BumpedLight.Combine(
            (0.8f, 0f, 0.6f), First, Second, Third, selfShadowing: false);

        red.ShouldBeGreaterThan(0.75f, "the basis vector it leans into should dominate");
        green.ShouldBe(blue, 0.0001, "the other two are symmetric about the x axis");
        (red + green + blue).ShouldBe(1f, 0.0001, "the weights are normalised, so the mix sums to one");
    }

    [Test]
    public void Combine_AnyNormal_ProducesWeightsThatSumToOne()
    {
        // **The property the division exists for**, swept rather than spot-checked: whatever
        // direction the surface faces, the three lightmaps are mixed and never scaled. Without the
        // division brightness swings with the normal direction, which reads as a surface that
        // ripples with light rather than with shape.
        foreach ((float x, float y, float z) in new[]
        {
            (0f, 0f, 1f), (0.5f, 0.5f, 0.707f), (-0.6f, 0.2f, 0.77f),
            (0.1f, -0.9f, 0.42f), (-0.3f, -0.3f, 0.9f),
        })
        {
            (float red, float green, float blue) = BumpedLight.Combine(
                (x, y, z), First, Second, Third, selfShadowing: false);

            (red + green + blue).ShouldBe(1f, 0.0001, $"normal ({x}, {y}, {z})");
        }
    }

    [Test]
    public void Combine_ANormalFacingAwayFromEverything_IsBlackRatherThanNotANumber()
    {
        // **Valve's shader divides here without checking, and a GPU is content to return infinity
        // or NaN.** A normal pointing into the surface saturates all three dots to zero, so the sum
        // is zero. It should not happen on well-formed content and it costs nothing to refuse -
        // and a NaN colour propagates through the rest of the frame as a black or white pixel that
        // nothing explains.
        (float red, float green, float blue) = BumpedLight.Combine(
            (0f, 0f, -1f), First, Second, Third, selfShadowing: false);

        float.IsNaN(red).ShouldBeFalse();
        (red, green, blue).ShouldBe((0f, 0f, 0f));
    }

    [Test]
    public void Combine_SelfShadowing_UsesTheComponentsDirectly()
    {
        // No dots, no squaring, no division. An ssbump texel is already three weights, so the
        // combine is a weighted sum and nothing else: 0.5 of the first plus 0.25 of the second.
        (float red, float green, float blue) = BumpedLight.Combine(
            (0.5f, 0.25f, 0f), First, Second, Third, selfShadowing: true);

        red.ShouldBe(0.5f, 0.0001);
        green.ShouldBe(0.25f, 0.0001);
        blue.ShouldBe(0f, 0.0001);
    }

    [Test]
    public void Combine_SelfShadowing_IsNotNormalised()
    {
        // **The distinguishing case, and it needs an input where the two paths disagree.** Weights
        // that sum to two must produce twice the light, where the ordinary path would divide it
        // back to one. A test using weights that already sum to one could not tell them apart.
        (float red, float green, float blue) = BumpedLight.Combine(
            (1f, 1f, 0f), First, Second, Third, selfShadowing: true);

        (red + green + blue).ShouldBe(2f, 0.0001, "ssbump does not divide by the sum");
    }

    [Test]
    public void Combine_SelfShadowingAndOrdinary_DisagreeOnTheSameInput()
    {
        // The control for the pair above. If the two paths ever returned the same thing for the
        // same input, every test here would pass against an implementation that had only one.
        (float Red, float Green, float Blue) ordinary = BumpedLight.Combine(
            (0.5f, 0.25f, 0.8f), First, Second, Third, selfShadowing: false);

        (float Red, float Green, float Blue) selfShadowing = BumpedLight.Combine(
            (0.5f, 0.25f, 0.8f), First, Second, Third, selfShadowing: true);

        ordinary.ShouldNotBe(selfShadowing);
    }

    [Test]
    public void Decode_TurnsAStoredTexelIntoASignedNormal()
    {
        // A normal map stores -1 as 0 and +1 as 255, so the midpoint is flat. Skipping this step
        // leaves every normal in the positive octant, which points every surface the same way and
        // lights the map as though it were faceted.
        BumpedLight.Decode(128, 128, 255).Z.ShouldBeGreaterThan(0.99f);
        BumpedLight.Decode(0, 128, 128).X.ShouldBe(-1f, 0.01);
        BumpedLight.Decode(255, 128, 128).X.ShouldBe(1f, 0.01);
    }

    [Test]
    public void DecodeSelfShadowing_LeavesTheStoredValueAlone()
    {
        // **An ssbump texel is not a normal and must not be decoded like one.** Its channels are
        // already weights in nought-to-one. Applying the signed decode to them sends a flat 128 to
        // zero, and the surface goes black where it should be evenly lit.
        BumpedLight.DecodeSelfShadowing(128, 64, 255).X.ShouldBe(128f / 255f, 0.001);
        BumpedLight.DecodeSelfShadowing(0, 0, 0).ShouldBe((0f, 0f, 0f));
    }
}
