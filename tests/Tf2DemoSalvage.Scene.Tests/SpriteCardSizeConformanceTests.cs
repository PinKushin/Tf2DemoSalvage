using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>SpriteCard's size clamps and fades, as its vertex shader applies them (B529).</summary>
/// <remarks>
/// <code>
/// // spritecard_vsxx.fxc:199-229, l = |worldPos − cEyePos|
/// rad = max( rad, MINIMUM_SIZE_FACTOR * l );
/// if ( rad > START_FADE_SIZE_FACTOR * l )
///     if ( rad > END_FADE_SIZE_FACTOR * l ) { tint = 0; rad = 0; }
///     else tint *= 1 - ( rad - START*l ) / ( END*l - START*l );
/// tscale = 1 - min( 1, max( 0, ( l - START_FAR_FADE ) * FAR_FADE_FACTOR ) );  tint *= tscale;  if ( tscale &lt;= 0 ) rad = 0;
/// rad = min( rad, MAXIMUM_SIZE_FACTOR * l );
///
/// // spritecard.cpp:104-108, :426-431 — $minsize 0, $maxsize 20, $startfadesize 10, $endfadesize 20,
/// // $maxdistance 100000, $farfadeinterval 400; start far fade = max( 1, maxdistance − interval ), factor = 1 / the rest
/// </code>
/// `effects/sc_softglow` states .35, .475 and .65.
/// </remarks>
public sealed class SpriteCardSizeConformanceTests
{
    private static readonly SpriteCardSize SoftGlow = new(Minimum: 0f, Maximum: 0.35f, StartFade: 0.475f, EndFade: 0.65f);

    [Test]
    public void Apply_TheShadersDefaults_LeaveAnOrdinaryCardAlone() =>
        SpriteCardSize.Default.Apply(radius: 20f, distance: 30f).ShouldBe((20f, 1f));

    [Test]
    public void Apply_ACardSmallOnScreen_IsUntouched() =>
        SoftGlow.Apply(radius: 20f, distance: 100f).ShouldBe((20f, 1f));

    [Test]
    public void Apply_ACardPastTheStartFade_FadesOnItsUnclampedRadiusAndIsThenClamped()
    {
        (float radius, float fade) = SoftGlow.Apply(radius: 20f, distance: 40f);

        radius.ShouldBe(14f, 1e-4f, "0.35 of 40");
        fade.ShouldBe(1f - (1f / 7f), 1e-4f, "20 is one unit past 19 on the way to 26");
    }

    [Test]
    public void Apply_ACardPastTheEndFade_IsNotDrawn() =>
        SoftGlow.Apply(radius: 20f, distance: 30f).ShouldBe((0f, 0f));

    [Test]
    public void Apply_ACardUnderTheMinimum_GrowsWithDistance() =>
        (SpriteCardSize.Default with { Minimum = 0.1f }).Apply(radius: 5f, distance: 100f).ShouldBe((10f, 1f));

    [Test]
    public void Apply_ACardInTheFarFadeInterval_FadesAndPastItIsNotDrawn()
    {
        SpriteCardSize far = SpriteCardSize.Default with { MaximumDistance = 1000f, FarFadeInterval = 400f };

        far.Apply(radius: 5f, distance: 800f).ShouldBe((5f, 0.5f));
        far.Apply(radius: 5f, distance: 1000f).ShouldBe((0f, 0f));
    }

    [Test]
    public void Of_ASpriteCardStatingItsSizes_ReadsThemAndAnotherShaderDoesNot()
    {
        Dictionary<string, string> stated = new()
        {
            ["$maxsize"] = ".35",
            ["$startfadesize"] = ".475",
            ["$endfadesize"] = ".65",
        };

        SpriteCardSize.Of("Spritecard", stated.GetValueOrDefault).ShouldBe(SoftGlow);
        SpriteCardSize.Of("UnlitGeneric", stated.GetValueOrDefault).ShouldBe(SpriteCardSize.Default);
    }
}
