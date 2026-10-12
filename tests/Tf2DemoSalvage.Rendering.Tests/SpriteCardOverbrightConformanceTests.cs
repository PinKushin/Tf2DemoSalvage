using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// <c>SpriteCard</c>'s <c>$overbrightfactor</c> (B523): the texel is multiplied by it before the vertex colour.
/// </summary>
/// <remarks>
/// *Read from published source.* `spritecard.cpp:81` declares <c>OVERBRIGHTFACTOR</c>, `:110` sets it to 1.0 when the
/// material states none (<c>INIT_FLOAT_PARM</c>, which does assign — unlike a bare <c>SHADER_PARAM</c> default string),
/// `:438-439` hands it to the pixel shader as <c>g_Parameters.y</c>, and `spritecard_ps2x.fxc:143-144` is
///
/// <code>
/// // Overbright
/// blended_rgb.rgb *= fOverbrightFactor;
/// </code>
///
/// before <c>blended_rgb *= i.argbcolor</c> (`:188`). *Read from the shipped data:* <c>effects/medicbeam_curl</c>, the
/// medigun beam's body, states <c>"srgb?$overbrightfactor" 3</c>, and 191 shipped materials state one. This viewer read
/// none of them, so every one drew at a third, a fifth or a fifteenth of its light — found as the heal beam's glow on
/// its patient, strong in TF2 and all but absent here.
/// </remarks>
public sealed class SpriteCardOverbrightConformanceTests
{
    /// <remarks>
    /// The controls are the two ways to one: a SpriteCard material stating nothing, and the same number under a shader
    /// that has no such parameter. An unparsable value is the unset one.
    /// </remarks>
    [TestCase("SpriteCard", "3", 3f)]
    [TestCase("Spritecard", "1.5", 1.5f)]
    [TestCase("spritecard", null, 1f)]
    [TestCase("SpriteCard", "bright", 1f)]
    [TestCase("UnlitGeneric", "3", 1f)]
    [TestCase(null, "3", 1f)]
    public void OverbrightOf_AShaderAndItsStatedFactor_IsSpriteCardsFactorOrOne(string? shader, string? stated, float expected)
    {
        ParticleMaterial.OverbrightOf(shader, stated).ShouldBe(expected);
    }

    /// <remarks>
    /// **The picture**: one additive quad of a dim colour over black, drawn by the real sprite pass with the factor at
    /// one and at three. The stored value rises as three times the light does — and a pass that ignored the factor
    /// draws the two the same.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawSprites_AMaterialWithAnOverbrightOfThree_AddsThreeTimesTheLight()
    {
        const int Size = 32;

        using OffscreenTarget? target = OffscreenTarget.TryCreate(Size, Size);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        MapAssets assets = MapCache.Effects();
        // A shipped glow's sheet, taken off the Sprite shader's gamma path: only the texture is borrowed.
        ParticleMaterial glow = assets.SpriteMaterials["sprites/light_glow03.vmt"].Material with
        {
            Blend = SpriteBlend.Additive,
            GammaSpace = false,
        };

        float once = Light(target, assets, glow);
        float thrice = Light(target, assets, glow with { Overbright = 3f });

        once.ShouldBeGreaterThan(0.001f, "the control: the quad must draw at all");
        (thrice / once).ShouldBe(3f, 0.25f);
    }

    /// <summary>The centre pixel's red as linear light, after one dim quad is added to black.</summary>
    private static float Light(OffscreenTarget target, MapAssets assets, ParticleMaterial material)
    {
        const float Dim = 0.05f;

        List<DetailSpriteVertex> quad =
        [
            Corner(-1f, -1f, 0f, 1f), Corner(1f, 1f, 1f, 0f), Corner(1f, -1f, 1f, 1f),
            Corner(-1f, -1f, 0f, 1f), Corner(-1f, 1f, 0f, 0f), Corner(1f, 1f, 1f, 0f),
        ];

        StripPicture.Draw(target, Identity, assets, [new ParticleBatch(quad, material)]);

        // The target stores sRGB; the light is what the blend added.
        float stored = target.PixelAt(16, 16).Red / 255f;

        return stored <= 0.04045f ? stored / 12.92f : System.MathF.Pow((stored + 0.055f) / 1.055f, 2.4f);

        static DetailSpriteVertex Corner(float x, float y, float u, float v) =>
            new(x, y, 0.5f, u, v, Dim, Dim, Dim, 1f, u, v, 0f);
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
