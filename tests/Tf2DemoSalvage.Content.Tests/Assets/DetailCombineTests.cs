using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The twelve ways a detail texture combines with a base texture.
/// </summary>
/// <remarks>
/// **Transcribed from <c>TextureCombine</c> in <c>common_ps_fxc.h</c>, and tested against inputs
/// where a wrong transcription would differ.** The temptation with a table of one-line formulas is
/// to test each with round numbers, but the modes are near-identical at their endpoints - every one
/// of them leaves the base alone at a blend factor of zero, and several agree at one. The
/// interesting inputs are mid-range factors, detail values either side of the 0.5 that mode 0
/// treats as its identity, and base alphas that only modes 4, 7 and 9 read at all.
/// </remarks>
public sealed class DetailCombineTests
{
    private static readonly MaterialColour Base = new(0.4f, 0.6f, 0.8f, 0.25f);

    [Test]
    public void Mode0_WithGreyDetail_ChangesNothing()
    {
        // **The identity Valve relies on.** When the shader's fast path disables detail it binds
        // TEXTURE_GREY rather than unbinding the sampler, because lerp(1, 2*0.5, f) is 1 for any
        // blend factor. A wrong "2 *", a swapped lerp argument, or a missing lerp all break this
        // exactly - and unlike "the picture changed", it predicts a value.
        MaterialColour grey = new(0.5f, 0.5f, 0.5f, 1f);

        DetailCombine.Apply(Base, grey, 0, 1f).ShouldBe(Base);
        DetailCombine.Apply(Base, grey, 0, 0.3f).ShouldBe(Base);
    }

    [Test]
    public void Mode0_WithWhiteDetail_DoublesAtFullBlend()
    {
        // White is 2x, black is 0x. Those are the two ends the mode exists to span, and a
        // transcription that dropped the doubling would leave white as 1x.
        MaterialColour white = new(1f, 1f, 1f, 1f);

        MaterialColour result = DetailCombine.Apply(Base, white, 0, 1f);

        result.Red.ShouldBe(0.8f, 0.0001);
        result.Green.ShouldBe(1.2f, 0.0001, "the combine does not clamp; the shader saturates later");
        result.Alpha.ShouldBe(Base.Alpha, "mode 0 touches colour only");
    }

    [Test]
    public void Mode0_AtHalfBlend_IsHalfwayToTheDoubling()
    {
        // lerp(1, 2*0.75, 0.5) = 1.25. Picked because 0.75 detail and 0.5 factor give a multiplier
        // that no argument-order mistake reproduces: swapping the lerp ends gives 1.75 instead.
        MaterialColour detail = new(0.75f, 0.75f, 0.75f, 1f);

        DetailCombine.Apply(Base, detail, 0, 0.5f).Red.ShouldBe(0.5f, 0.0001);
    }

    [Test]
    public void Mode1_AddsTheDetailScaledByTheFactor()
    {
        MaterialColour detail = new(0.2f, 0.1f, 0f, 1f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 1, 0.5f);

        result.Red.ShouldBe(0.5f, 0.0001);
        result.Green.ShouldBe(0.65f, 0.0001);
        result.Blue.ShouldBe(0.8f, 0.0001);
    }

    [Test]
    public void Mode2_BlendsDetailOverBaseThroughItsOwnAlpha()
    {
        // The detail's alpha multiplies the factor here, which is what separates this from mode 3.
        // A detail alpha of 0.5 at a factor of 0.5 gives a quarter blend.
        MaterialColour detail = new(1f, 0f, 0f, 0.5f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 2, 0.5f);

        result.Red.ShouldBe(0.55f, 0.0001);
        result.Green.ShouldBe(0.45f, 0.0001);
        result.Alpha.ShouldBe(Base.Alpha, "mode 2 leaves the base alpha alone");
    }

    [Test]
    public void Mode3_FadesAllFourChannelsIncludingAlpha()
    {
        // **The alpha is the point.** Modes 2 and 3 agree on colour when the detail is opaque and
        // differ only in that 3 also replaces alpha - and alpha is what the alpha test reads, so
        // getting this wrong changes which pixels survive rather than what colour they are.
        MaterialColour detail = new(1f, 1f, 1f, 1f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 3, 0.5f);

        result.Red.ShouldBe(0.7f, 0.0001);
        result.Alpha.ShouldBe(0.625f, 0.0001);
    }

    [Test]
    public void Mode4_BlendsThroughTheInverseOfTheBaseAlphaAndTakesTheDetails()
    {
        // Base alpha 0.25 gives 1-0.25 = 0.75, times a factor of 1 = a 0.75 blend toward detail.
        // A test with an opaque base would predict the same result as a wrong "1 - detail.a".
        MaterialColour detail = new(0f, 0f, 0f, 0.9f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 4, 1f);

        result.Red.ShouldBe(0.1f, 0.0001);
        result.Alpha.ShouldBe(0.9f, "mode 4 takes the detail's alpha outright");
    }

    [Test]
    public void Mode4_TakesTheDetailsAlphaEvenAtZeroBlend()
    {
        // **The alpha assignment sits outside the blend**, so it is not scaled by the factor and
        // not skipped when the factor is zero. Valve's block lerps the colour by
        // fBlendFactor * (1 - base.a) and then assigns base.a = detail.a unconditionally.
        //
        // Found by the sweep below rather than by reading, and it is the second mode where the
        // assumption "factor zero means off" turned out to be wrong - which is why the sweep is
        // worth having even though it now carries two exceptions.
        MaterialColour detail = new(0f, 0f, 0f, 0.9f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 4, 0f);

        result.Red.ShouldBe(Base.Red, "no colour blend at all at a factor of zero");
        result.Alpha.ShouldBe(0.9f, "but the alpha is still replaced");
    }

    [Test]
    public void ModesAppliedAfterLighting_LeaveTheAlbedoAlone()
    {
        // Modes 5 and 6 are handled by TextureCombinePostLighting, and TextureCombine has no case
        // for them - so the albedo passes through untouched. Applying them here as well would add
        // the detail twice, once before the lightmap multiply and once after.
        MaterialColour detail = new(1f, 1f, 1f, 1f);

        DetailCombine.Apply(Base, detail, 5, 1f).ShouldBe(Base);
        DetailCombine.Apply(Base, detail, 6, 1f).ShouldBe(Base);
    }

    [Test]
    public void Mode5_AfterLighting_AddsTheDetail()
    {
        (float red, float green, float blue) = DetailCombine.ApplyAfterLighting(
            (0.2f, 0.2f, 0.2f), new MaterialColour(0.4f, 0.2f, 0f, 1f), 5, 0.5f);

        red.ShouldBe(0.4f, 0.0001);
        green.ShouldBe(0.3f, 0.0001);
        blue.ShouldBe(0.2f, 0.0001);
    }

    [Test]
    public void Mode6_AfterLighting_RemapsAWideningBandOfTheDetail()
    {
        // Valve's own comment calls this "an unusual way" to fade. Below a factor of 0.5 the
        // multiplier is 4*f and the offset is -0.5*fMult; above it the multiplier is 1/f. The two
        // branches are tested separately because a transcription that used one for both is
        // correct at exactly f = 0.5 and nowhere else.
        (float low, float _, float _) = DetailCombine.ApplyAfterLighting(
            (0f, 0f, 0f), new MaterialColour(0.75f, 0f, 0f, 1f), 6, 0.25f);

        // fMult = 1, fAdd = -0.5, so saturate(0.75 - 0.5) = 0.25.
        low.ShouldBe(0.25f, 0.0001);

        (float high, float _, float _) = DetailCombine.ApplyAfterLighting(
            (0f, 0f, 0f), new MaterialColour(0.75f, 0f, 0f, 1f), 6, 0.8f);

        // fMult = 1.25, fAdd = -0.25, so saturate(0.9375 - 0.25) = 0.6875.
        high.ShouldBe(0.6875f, 0.0001);
    }

    [Test]
    public void ModesAppliedBeforeLighting_AreUntouchedByThePostLightingPass()
    {
        // The control for the pair above: every mode that TextureCombine handles must pass through
        // TextureCombinePostLighting unchanged, or it is applied twice.
        (float red, float green, float blue) = DetailCombine.ApplyAfterLighting(
            (0.2f, 0.3f, 0.4f), new MaterialColour(1f, 1f, 1f, 1f), 0, 1f);

        red.ShouldBe(0.2f);
        green.ShouldBe(0.3f);
        blue.ShouldBe(0.4f);
    }

    [Test]
    public void Mode7_SelectsBetweenTwoPatternsWithTheBaseAlpha()
    {
        // The detail's red and alpha are two separate patterns, and the base's alpha picks between
        // them. At base alpha 0.25 the result is a quarter of the way from red to alpha:
        // lerp(0.2, 0.6, 0.25) = 0.3, so the multiplier is lerp(1, 0.6, 1) = 0.6.
        MaterialColour detail = new(0.2f, 0f, 0f, 0.6f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 7, 1f);

        result.Red.ShouldBe(0.24f, 0.0001);
        result.Green.ShouldBe(0.36f, 0.0001);
    }

    [Test]
    public void Mode8_MultipliesAllFourChannels()
    {
        // Mode 8 differs from mode 0 by the missing doubling, and from mode 3 by multiplying
        // rather than replacing. A grey detail halves the surface here where mode 0 leaves it.
        MaterialColour grey = new(0.5f, 0.5f, 0.5f, 0.5f);

        MaterialColour result = DetailCombine.Apply(Base, grey, 8, 1f);

        result.Red.ShouldBe(0.2f, 0.0001);
        result.Alpha.ShouldBe(0.125f, 0.0001);
    }

    [Test]
    public void Mode9_TouchesAlphaAndNothingElse()
    {
        MaterialColour detail = new(1f, 0f, 0f, 0.5f);

        MaterialColour result = DetailCombine.Apply(Base, detail, 9, 1f);

        result.Red.ShouldBe(Base.Red, "mode 9 masks alpha, and must not tint");
        result.Alpha.ShouldBe(0.125f, 0.0001);
    }

    [Test]
    public void Mode10_LeavesTheAlbedoToTheBumpedLightingPath()
    {
        // SSBUMP_BUMP modulates lighting rather than albedo, and TextureCombine has no case for
        // it. It is listed here so the absence is deliberate rather than an oversight.
        DetailCombine.Apply(Base, new MaterialColour(1f, 0f, 0f, 1f), 10, 1f).ShouldBe(Base);
    }

    [Test]
    public void Mode11_ScalesTheBaseByTheDetailsSummedIntensity()
    {
        // dot(detail.rgb, 2.0/3.0) in HLSL sums the channels against a constant, so a detail of
        // (0.5, 0.5, 0.5) gives 1.5 * 2/3 = 1.0 - another grey identity, and one that a reading of
        // "average the channels" would get wrong by a factor of two.
        MaterialColour grey = new(0.5f, 0.5f, 0.5f, 1f);

        DetailCombine.Apply(Base, grey, 11, 1f).ShouldBe(Base);
    }

    [Test]
    public void EveryModeAtZeroBlend_LeavesTheBaseExactlyWhereItWas()
    {
        // A sweep rather than a case, because this is the property that has to hold across most of
        // them: a blend factor of zero is off. Mode 1 is the one that would survive a wrong formula
        // elsewhere and fail here, since it adds rather than lerps.
        //
        // **Two modes are exceptions and both were found by running this, not by reading.** Mode 4
        // replaces alpha outside the blend, and mode 11 contains no blend factor at all. Each has
        // its own test above; the exclusions here are deliberate rather than a sweep quietly tuned
        // until it passed.
        MaterialColour detail = new(0.9f, 0.1f, 0.4f, 0.7f);

        for (int mode = 0; mode <= 11; mode++)
        {
            if (mode is 4 or 11)
            {
                continue;
            }

            DetailCombine.Apply(Base, detail, mode, 0f)
                .ShouldBe(Base, $"mode {mode} must be off at a blend factor of zero");
        }
    }

    [Test]
    public void Mode11_IgnoresTheBlendFactorEntirely()
    {
        // **The one mode with no blend factor in it at all**, and the reason the sweep above skips
        // it. Valve's line is a bare multiply of the base by the detail's summed intensity, with
        // the blend factor appearing nowhere in it, so a $detailblendfactor of zero does not turn
        // mode 11 off the way it turns every other mode off. Writing the sweep first and finding
        // this is what established it - the assumption that every mode fades to nothing was the
        // natural one and it is wrong.
        MaterialColour detail = new(0.9f, 0.1f, 0.4f, 0.7f);

        MaterialColour off = DetailCombine.Apply(Base, detail, 11, 0f);

        off.ShouldNotBe(Base);
        off.Red.ShouldBe(0.4f * (1.4f * 2f / 3f), 0.0001);
        DetailCombine.Apply(Base, detail, 11, 1f).ShouldBe(off, "the factor changes nothing here");
    }

    [Test]
    public void AModeOutsideTheRange_IsRefusedRatherThanIgnored()
    {
        // The shader's combo declares "0..11". A material naming mode 12 is malformed, and
        // Valve's chain of ifs would silently draw it as though it had no detail at all. This
        // project does not do silent fallbacks - a wrong mode is a finding, not a shrug.
        Should.Throw<InvalidDataException>(
            () => DetailCombine.Apply(Base, new MaterialColour(1f, 1f, 1f, 1f), 12, 1f));

        Should.Throw<InvalidDataException>(
            () => DetailCombine.Apply(Base, new MaterialColour(1f, 1f, 1f, 1f), -1, 1f));
    }
}
