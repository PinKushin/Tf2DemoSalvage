using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>SpriteCard reads a particle's tint as a GAMMA colour and lights with its linear value (B538).</summary>
/// <remarks>
/// <code>
/// // spritecard_vsxx.fxc:179-183
/// #if SHADER_MODEL_VS_1_1
///     float4 tint = v.vTint;
/// #else
///     float4 tint = GammaToLinear( v.vTint );
/// #endif
///
/// // common_fxc.h:195-198
/// float4 GammaToLinear( const float4 gamma ) { return float4( pow( gamma.xyz, 2.2f ), gamma.w ); }
///
/// // spritecard_vsxx.fxc:207-217, AFTER it: the size fade scales the converted tint
/// tint *= 1-(rad-START_FADE_SIZE_FACTOR*l)/(END_FADE_SIZE_FACTOR*l-START_FADE_SIZE_FACTOR*l);
///
/// // splinecard_vsxx.fxc:60, the sprite TRAIL's vertex shader, converts nothing
/// o.argbcolor = v.vTint;
/// </code>
/// The medigun beam's glow is `Color Random` (106 141 255) fading to (0 120 255): as linear light its green is 0.27
/// and 0.19 of its blue, where the stored bytes read as light are 0.55 and 0.47 — a pale cyan for TF2's blue.
/// </remarks>
public sealed class SpriteCardTintConformanceTests
{
    private const float Power = 2.2f;

    private static List<DetailSpriteVertex> Built(bool gammaTint, Vector3? eye = null, SpriteCardSize? size = null)
    {
        ParticleStore particles = new();

        particles.Add(new Vector3(10f, 20f, 30f), lives: 1f);
        particles.Colour(0, new Vector3(106f, 141f, 255f));
        particles.Fade(0, 0.3f);

        List<DetailSpriteVertex> corners = [];

        ParticleSprites.Build(
            particles, Vector3.UnitX, Vector3.UnitY, corners, [], 1f, false, false, eye, size, gammaTint);

        return corners;
    }

    [Test]
    public void Build_ASpriteCardTint_IsRaisedToTwoPointTwoAndItsAlphaIsNot()
    {
        List<DetailSpriteVertex> corners = Built(gammaTint: true);

        corners.Count.ShouldBe(ParticleSprites.CornersPerParticle);

        foreach (DetailSpriteVertex corner in corners)
        {
            corner.Red.ShouldBe(MathF.Pow(106f / 255f, Power), 1e-6f);
            corner.Green.ShouldBe(MathF.Pow(141f / 255f, Power), 1e-6f);
            corner.Blue.ShouldBe(1f, 1e-6f);
            corner.Alpha.ShouldBe(0.3f, 1e-6f, "gamma.w passes through");
        }
    }

    [Test]
    public void Build_AnotherShadersTint_IsTheStoredColour()
    {
        foreach (DetailSpriteVertex corner in Built(gammaTint: false))
        {
            corner.Red.ShouldBe(106f / 255f, 1e-6f);
            corner.Green.ShouldBe(141f / 255f, 1e-6f);
        }
    }

    [Test]
    public void Build_ASpriteCardFadedBySize_ScalesTheConvertedTint()
    {
        // Radius 1 at two units, fading from 0.2 to 0.8 of the distance: half way, so half the LINEAR tint.
        SpriteCardSize size = new(Minimum: 0f, Maximum: 20f, StartFade: 0.2f, EndFade: 0.8f);

        foreach (DetailSpriteVertex corner in Built(gammaTint: true, new Vector3(10f, 20f, 32f), size))
        {
            corner.Green.ShouldBe(0.5f * MathF.Pow(141f / 255f, Power), 1e-6f);
            corner.Alpha.ShouldBe(0.15f, 1e-6f);
        }
    }

    [TestCase("SpriteCard", true)]
    [TestCase("Spritecard", true)]
    [TestCase("UnlitGeneric", false)]
    [TestCase("Sprite", false)]
    [TestCase(null, false)]
    public void TintIsGammaOf_AShader_IsSpriteCardAlone(string? shader, bool expected) =>
        ParticleMaterial.TintIsGammaOf(shader).ShouldBe(expected);
}
