namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// How far a detail sprite is drawn, and how it fades out (B361).
/// </summary>
/// <remarks>
/// **`CDetailObjectSystem::BuildDetailObjectRenderLists`, `detailobjectsystem.cpp:2821`**, which
/// prepares three numbers once per view:
///
/// <code>
///   m_flCurMaxSqDist  = cl_detaildist.GetFloat() * cl_detaildist.GetFloat();
///   m_flCurFadeSqDist = cl_detaildist.GetFloat() - cl_detailfade.GetFloat();
///   m_flCurMaxSqDist  /= factor;
///   m_flCurFadeSqDist /= factor;
///   if ( m_flCurFadeSqDist &gt; 0) m_flCurFadeSqDist *= m_flCurFadeSqDist;
///   else                        m_flCurFadeSqDist = 0;
///   m_flCurFadeSqDist = MIN( m_flCurFadeSqDist, m_flCurMaxSqDist -1 );
///   m_flCurFalloffFactor = 255.0f / ( m_flCurMaxSqDist - m_flCurFadeSqDist );
/// </code>
///
/// then per object (`EnumerateLeaf`, `detailobjectsystem.cpp:2762`):
///
/// <code>
///   if ( sqDist &lt; m_flCurMaxSqDist )
///       model.SetAlpha( sqDist &gt; m_flCurFadeSqDist
///           ? m_flCurFalloffFactor * ( m_flCurMaxSqDist - sqDist ) : 255 );
///   else
///       model.SetAlpha( 0 );
/// </code>
///
/// **The asymmetry in the FOV divide is Valve's and is transcribed, not tidied.** The maximum is
/// divided AFTER squaring and the fade BEFORE, so the two are not scaled by the same amount. At the
/// default FOV the factor is exactly 1 and the difference cannot be seen, which is precisely why it
/// would survive being "corrected".
///
/// **`SetAlpha` takes an `unsigned char`**, so the product truncates rather than rounding.
/// </remarks>
public sealed class DetailFadeConformanceTests
{
    /// <summary>Valve's shipped defaults, from the two ConVar registrations.</summary>
    private const float Distance = 1200f;
    private const float Fade = 400f;

    /// <remarks>
    /// Inside the fade start, everything is fully opaque — 800 units at the defaults, so 640,000
    /// squared. The boundary is asserted as well as a point inside it, because the comparison is
    /// `sqDist > fade` and an off-by-one to `>=` would only show at exactly that value.
    /// </remarks>
    [Test]
    public void Alpha_InsideTheFadeStart_IsFullyOpaque()
    {
        DetailFade fade = DetailFade.For(Distance, Fade);

        fade.Alpha(0f).ShouldBe((byte)255);
        fade.Alpha(640_000f).ShouldBe((byte)255);
    }

    /// <remarks>
    /// **Halfway across the fade band is 127, not 128.** The falloff is
    /// `255 / (1,440,000 − 640,000)`, so at a squared distance of 1,040,000 the product is exactly
    /// 127.5 and the cast to `unsigned char` truncates. A transcription that rounded would report
    /// 128 and be wrong by one everywhere in the band.
    /// </remarks>
    [Test]
    public void Alpha_HalfwayAcrossTheFadeBand_TruncatesRatherThanRounds()
    {
        DetailFade.For(Distance, Fade).Alpha(1_040_000f).ShouldBe((byte)127);
    }

    /// <remarks>
    /// At and beyond the maximum the sprite is not drawn. 1,440,000 is the maximum itself, where
    /// the comparison is a strict less-than, so it belongs to the far side.
    /// </remarks>
    [Test]
    public void Alpha_AtAndBeyondTheMaximum_IsZero()
    {
        DetailFade fade = DetailFade.For(Distance, Fade);

        fade.Alpha(1_440_000f).ShouldBe((byte)0);
        fade.Alpha(9_000_000f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// **`low.cfg` ships `cl_detaildist 0`**, which must draw nothing at all rather than dividing by
    /// zero or drawing everything. Valve's arithmetic gets there by itself: the maximum is 0, the
    /// fade goes negative and is clamped to 0, then `MIN(0, −1)` makes it −1 — and no squared
    /// distance is below a maximum of zero.
    /// </remarks>
    [Test]
    public void Alpha_WithTheDistanceTurnedOff_DrawsNothing()
    {
        DetailFade fade = DetailFade.For(0f, Fade);

        fade.Alpha(0f).ShouldBe((byte)0);
        fade.Alpha(1f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// A fade band as wide as the whole distance leaves no opaque core: the fade start is zero, so
    /// everything past the eye is already fading. The control is that a sprite AT the eye is still
    /// 255 — the comparison is `sqDist > fade`, and zero is not greater than zero.
    /// </remarks>
    [Test]
    public void Alpha_WithAFadeAsWideAsTheDistance_FadesFromTheEye()
    {
        DetailFade fade = DetailFade.For(Fade, Fade);

        fade.Alpha(0f).ShouldBe((byte)255);
        fade.Alpha(80_000f).ShouldBe((byte)127);
    }

    /// <remarks>
    /// **`ultra.cfg` ships `cl_detaildist 8592`**, so the shipped range of this setting spans zero
    /// to seven times the default and the arithmetic has to hold across it. At 8592 the fade start
    /// is 8192 units, and a sprite a kilometre away is still fully opaque where the default would
    /// have dropped it entirely.
    /// </remarks>
    [Test]
    public void Alpha_AtTheUltraDistance_KeepsWhatTheDefaultWouldHaveDropped()
    {
        DetailFade.For(8592f, Fade).Alpha(4_000_000f).ShouldBe((byte)255);
        DetailFade.For(Distance, Fade).Alpha(4_000_000f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// **The FOV factor divides the two limits differently, and that is Valve's.** A zoomed sniper
    /// has a factor below one, which pushes both limits OUT — the maximum by `1/factor` and the fade
    /// by `1/factor²`, because it is divided before being squared. Predicted here from the source
    /// rather than from the code: at a factor of 0.5 the maximum squared is 2,880,000 and the fade
    /// squared is 2,560,000.
    /// </remarks>
    [Test]
    public void For_AZoomedView_ScalesTheTwoLimitsByDifferentPowersOfTheFactor()
    {
        DetailFade fade = DetailFade.For(Distance, Fade, factor: 0.5f);

        fade.MaximumSquared.ShouldBe(2_880_000f);
        fade.FadeSquared.ShouldBe(2_560_000f);
    }
}
