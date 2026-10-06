using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The per-frame proxies of the TF2 screen overlays, run with no entity bound (`ScreenOverlayMaterial.Bind`).</summary>
public sealed class ScreenOverlayMaterialTests
{
    private static readonly RefractMaterial Refract =
        new(new MapTexture(1, 1, 1, 1, TextureImage.None, IsTransparent: false), 0.03f, (1f, 1f, 1f), null, 0, false, true);

    [Test]
    public void Bind_TheBleedOverlay_PulsesGreenAndBlueOfTheTint()
    {
        // bleed_overlay.vmt: Sine( period 1, min 1.0, max 0.8 ) → $bleedalpha → Equals into $refracttint[1] and [2].
        // At a quarter period the sine is at its peak: 0.9 + ( −0.1 ) · sin( π / 2 ) = 0.8.
        ScreenOverlayMaterial bleed = Overlay("""
            "Refract" { "$refractamount" ".03" "$bleedalpha" "0"
              "Proxies" {
                "Sine" { "resultVar" "$bleedalpha" "sineperiod" "1" "sinemin" "1.0" "sinemax" "0.8" }
                "Equals" { "srcVar1" "$bleedalpha" "resultVar" "$refracttint[1]" }
                "Equals" { "srcVar1" "$bleedalpha" "resultVar" "$refracttint[2]" } } }
            """);

        (float amount, (float Red, float Green, float Blue) tint, TextureTransform _) = bleed.Bind(0.25);

        amount.ShouldBe(0.03f);
        tint.Red.ShouldBe(1f);
        tint.Green.ShouldBe(0.8f, 1e-5f);
        tint.Blue.ShouldBe(0.8f, 1e-5f);
    }

    [Test]
    public void Bind_TheInvulnOverlay_SwingsTheRefractAmount()
    {
        // invuln_overlay_*.vmt: Sine( period .81, −.1 to .1 ) → $refractamount; a quarter period in is the maximum.
        ScreenOverlayMaterial invuln = Overlay("""
            "Refract" { "$refractamount" ".02"
              "Proxies" { "sine" { "sinemax" ".1" "sinemin" "-.1" "sineperiod" ".81" "resultvar" "$refractamount" } } }
            """);

        invuln.Bind(0.81 / 4).RefractAmount.ShouldBe(0.1f, 1e-5f);
        invuln.Bind(0).RefractAmount.ShouldBe(0f, 1e-6f);
    }

    [Test]
    public void Bind_TheJarateOverlay_ScrollsTheBumpTransform()
    {
        // TextureScroll( rate .1, angle 45 ) on $bumptransform: s = t · cos 45° · 0.1.
        ScreenOverlayMaterial jarate = Overlay("""
            "Refract" { "$refractamount" ".05"
              "Proxies" { "TextureScroll" { "texturescrollvar" "$bumptransform" "texturescrollrate" .1 "texturescrollangle" 45.00 } } }
            """);

        TextureTransform bump = jarate.Bind(5).BumpTransform;

        bump.Row0.W.ShouldBe(0.35355f, 1e-4f);
        bump.Row1.W.ShouldBe(0.35355f, 1e-4f);
    }

    [Test]
    public void Bind_AnOverlayThisPortDrawsAsNothing_IsNeutral()
    {
        new ScreenOverlayMaterial("effects/imcookin", null, [], new Dictionary<string, (float, float, float)>())
            .Bind(3).ShouldBe((0f, (1f, 1f, 1f), TextureTransform.Identity));
    }

    private static ScreenOverlayMaterial Overlay(string text)
    {
        VmtMaterial vmt = VmtMaterial.Parse(Encoding.UTF8.GetBytes(text));

        return new ScreenOverlayMaterial(
            "effects/test", Refract with { RefractAmount = vmt.Number("$refractamount", 2f) }, vmt.Proxies, vmt.NumericValues());
    }
}
