namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A distant entity fades out, on the engine's curve.
/// </summary>
/// <remarks>
/// **<c>ComputeDistanceFade</c>, <c>client/cdll_util.cpp:1074</c>**, reached from
/// <c>C_BaseAnimating::GetClientSideFade</c> (<c>c_baseanimating.cpp:6532</c>) and multiplied into
/// the render blend by <c>C_BaseEntity::ComputeFxBlend</c>:
///
/// <code>
///   static unsigned char ComputeDistanceFade( C_BaseEntity *pEntity, float flMinDist, float flMaxDist )
///   {
///       if ((flMinDist &lt;= 0) &amp;&amp; (flMaxDist &lt;= 0))
///           return 255;
///
///       if( flMinDist &gt; flMaxDist )
///       {
///           ::V_swap( flMinDist, flMaxDist );
///       }
///
///       // If a negative value is provided for the min fade distance, then base it off the max.
///       if( flMinDist &lt; 0 )
///       {
///           flMinDist = flMaxDist - 400;
///           if( flMinDist &lt; 0 )
///           {
///               flMinDist = 0;
///           }
///       }
///
///       flMinDist *= flMinDist;
///       flMaxDist *= flMaxDist;
///       ...
///       if ( flCurrentDistanceSq &lt;= flMinDist )
///           return 255;
///
///       if ( flCurrentDistanceSq &gt;= flMaxDist )
///           return 0;
///
///       float flFalloffFactor = 255.0f / (flMaxDist - flMinDist);
///       int nAlpha = flFalloffFactor * (flMaxDist - flCurrentDistanceSq);
///       return clamp( nAlpha, 0, 255 );
///   }
/// </code>
///
/// **The interpolation is on SQUARED distances**, which is the detail a reasonable implementation
/// gets wrong: the distances are squared before the falloff is computed, so the curve is not linear
/// in distance. Halfway between 826 and 900 units is not alpha 128.
///
/// **`m_fadeMinDist -1` is not a hypothetical.** Measured on the 2013 SourceTV foundry demo, which
/// is where this stopped being theory: 8 entities declare a real 826→900 band, and **28 declare
/// `-1`** — the branch that derives the minimum from the maximum. A version that skipped it would
/// be wrong on more entities than the ordinary case covers.
///
/// **What is NOT implemented, deliberately**: `UTIL_ComputeEntityFade` also takes the minimum of
/// this and two SCREEN-SIZE fades, `ComputeLevelScreenFade` and `ComputeViewScreenFade`, which live
/// behind `modelinfo` and are driven by `r_screenfademinsize`/`maxsize` — engine convars a demo
/// does not carry. The distance fade is the part the wire gives us.
/// </remarks>
public sealed class DistanceFadeConformanceTests
{
    [Test]
    public void Alpha_WithNoFadeDistances_IsFullyOpaque()
    {
        // The first branch, and the common case: 203 of the foundry demo's entities are 0/0.
        EntityFade.DistanceAlpha(minimum: 0f, maximum: 0f, distance: 5000f).ShouldBe((byte)255);
    }

    [Test]
    public void Alpha_InsideTheMinimum_IsFullyOpaque()
    {
        EntityFade.DistanceAlpha(minimum: 826f, maximum: 900f, distance: 100f).ShouldBe((byte)255);
        EntityFade.DistanceAlpha(minimum: 826f, maximum: 900f, distance: 826f).ShouldBe((byte)255);
    }

    [Test]
    public void Alpha_BeyondTheMaximum_IsInvisible()
    {
        EntityFade.DistanceAlpha(minimum: 826f, maximum: 900f, distance: 900f).ShouldBe((byte)0);
        EntityFade.DistanceAlpha(minimum: 826f, maximum: 900f, distance: 9000f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// **The squared curve, and the reason this test exists.** At 863 units — exactly halfway
    /// between 826 and 900 — a linear fade would give 128. The engine squares first, so the
    /// answer is <c>255 * (900² − 863²) / (900² − 826²)</c> = 255 × 65,231 / 127,676 ≈ 130. Any
    /// implementation that lerps on distance rather than distance-squared passes every other test
    /// in this file and fails this one.
    /// </remarks>
    [Test]
    public void Alpha_HalfwayBetween_FollowsTheSquaredCurve()
    {
        const float Minimum = 826f;
        const float Maximum = 900f;
        const float Halfway = 863f;

        float expected = 255f * (((Maximum * Maximum) - (Halfway * Halfway))
            / ((Maximum * Maximum) - (Minimum * Minimum)));

        EntityFade.DistanceAlpha(Minimum, Maximum, Halfway)
            .ShouldBe((byte)expected, "the falloff is computed on squared distances, not on distance");

        // Stated as a number too, so the formula above cannot quietly agree with a wrong reading
        // of itself.
        EntityFade.DistanceAlpha(Minimum, Maximum, Halfway).ShouldBe((byte)130);
    }

    /// <remarks>
    /// **The `-1` branch, exercised by 28 entities in a real demo.** A negative minimum means
    /// "start fading 400 units before the maximum", so −1 with a 900 maximum behaves as 500→900.
    /// </remarks>
    [Test]
    public void Alpha_WithANegativeMinimum_StartsFourHundredShortOfTheMaximum()
    {
        EntityFade.DistanceAlpha(minimum: -1f, maximum: 900f, distance: 499f)
            .ShouldBe((byte)255, "inside 500 is still fully opaque");

        EntityFade.DistanceAlpha(minimum: -1f, maximum: 900f, distance: 700f)
            .ShouldBe(
                EntityFade.DistanceAlpha(minimum: 500f, maximum: 900f, distance: 700f),
                "a negative minimum is exactly a 400-unit band below the maximum");
    }

    /// <remarks>
    /// A maximum under 400 would drive the derived minimum negative, and the engine clamps it to
    /// zero rather than letting the band invert.
    /// </remarks>
    [Test]
    public void Alpha_WithANegativeMinimumAndACloseMaximum_ClampsTheDerivedMinimumToZero()
    {
        EntityFade.DistanceAlpha(minimum: -1f, maximum: 300f, distance: 150f)
            .ShouldBe(
                EntityFade.DistanceAlpha(minimum: 0f, maximum: 300f, distance: 150f),
                "300 - 400 is negative, so the minimum becomes 0 and the whole range fades");
    }

    /// <remarks>
    /// **The swap, which is a real guard rather than tidiness**: a model whose minimum exceeds its
    /// maximum still fades, over the same band, rather than producing a negative falloff factor
    /// and a nonsense alpha.
    /// </remarks>
    [Test]
    public void Alpha_WithTheDistancesInverted_FadesOverTheSameBand()
    {
        EntityFade.DistanceAlpha(minimum: 900f, maximum: 826f, distance: 863f)
            .ShouldBe(EntityFade.DistanceAlpha(minimum: 826f, maximum: 900f, distance: 863f));
    }

    /// <remarks>
    /// The control for the whole file: a band that fades must actually produce intermediate
    /// values, or every assertion above could be satisfied by something that only ever returns
    /// 0 and 255.
    /// </remarks>
    [Test]
    public void Alpha_AcrossTheBand_PassesThroughIntermediateValues()
    {
        byte near = EntityFade.DistanceAlpha(826f, 900f, 840f);
        byte far = EntityFade.DistanceAlpha(826f, 900f, 890f);

        near.ShouldBeGreaterThan(far);
        near.ShouldBeLessThan((byte)255);
        far.ShouldBeGreaterThan((byte)0);
    }
}
