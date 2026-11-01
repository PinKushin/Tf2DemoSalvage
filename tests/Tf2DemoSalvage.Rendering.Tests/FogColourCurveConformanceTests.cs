using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A lit surface's fog colour is made linear by the exact sRGB decode, as shaderapi's c29 upload does (B653): hand-built
/// material, no game install, every expected value Valve's arithmetic written out.
/// </summary>
/// <remarks>
/// **Valve, read** (<c>shaderapidx9.dll</c> <c>0x180025a50</c>; addresses in
/// <c>docs/measurements/2026-11-02-fog-colour-curve-plan.md</c>): under range fog with sRGB writes on, which every lit
/// shader sets (<c>lightmappedgeneric_dx9_helper.cpp:614</c>), each channel goes through the sRGB decode
/// <c>0x18003ddf0</c>. Under height fog it goes through the same decode at the default <c>r_shader_srgb 0</c>. The shader
/// then lerps toward it in linear light, by the factor squared for range fog and by the factor itself for height fog
/// (<c>common_ps_fxc.h:284-298</c>).
///
/// **The condition puts the effect above the resolution.** Near mid grey `pow 2.2` and the sRGB decode differ by about
/// one stored level, which a ±1 test cannot see. So the fog colour is dark, (10, 26, 58), where they differ by 7, 7 and
/// 3 levels at full fog. The surface is black, so a half fog does not dilute the difference with the surface's own
/// light. Every case computes the `pow 2.2` answer too and first asserts that the two are further apart than the
/// tolerance.
///
/// **The geometry makes the factor exact** (<see cref="ModulateFogConformanceTests"/>' trick): an identity camera puts
/// the quad at clip z 0.9. Range fog over 0..0.9 reaches 1 there, so the max density IS the factor. Height fog with the
/// water surface at z −0.9 puts the whole line of sight under water, and the factor is 0.9 over the range.
/// </remarks>
[NonParallelizable]
public sealed class FogColourCurveConformanceTests
{
    private const int Black = 0;

    /// <summary>The quad's clip depth under the identity camera, which is also its world z.</summary>
    private const float Depth = 0.9f;

    /// <summary>The fog colour as stored bytes: dark, where the two curves differ by more than a level.</summary>
    private static readonly (int Red, int Green, int Blue) FogBytes = (10, 26, 58);

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(0)],
        ["LightmappedGeneric"]);

    /// <summary>The factor squared is one half: range fog's max density is √½.</summary>
    private static readonly float HalfSquared = MathF.Sqrt(0.5f);

    [Test]
    public void Draw_ABlackLitSurfaceWithNoFog_IsBlack()
    {
        (Draw() ?? Skip()).ShouldBe((0, 0, 0),"the control: the surface adds no light of its own, so every fogged pixel is the fog's");
    }

    /// <remarks>
    /// **Full range fog: the pixel is the controller's own bytes.** The sRGB target encodes by the inverse of the decode,
    /// so the decode stores 10, 26, 58 exactly. `pow 2.2` stores 3, 19, 55.
    /// </remarks>
    [Test]
    public void Draw_ALitSurfaceUnderFullRangeFog_StoresTheFogColoursOwnBytes()
    {
        (int, int, int) drawn = Draw(fog: Range(1f)) ?? Skip();

        AssertCurve(drawn, weight: 1.0, exact: true, "full range fog");
    }

    /// <remarks>
    /// **Half range fog (factor squared 0.5) over black:** `encode( 0.5 · decode( fog ) )` = 5, 16, 40. `pow 2.2` gives
    /// 1, 10, 38.
    /// </remarks>
    [Test]
    public void Draw_ALitSurfaceUnderHalfRangeFog_LerpsTowardTheSrgbDecodedColour()
    {
        (int, int, int) drawn = Draw(fog: Range(HalfSquared)) ?? Skip();

        AssertCurve(drawn, weight: 0.5, exact: false, "half range fog");
    }

    /// <remarks>**Full height fog**: the water volume's colour takes the same decode, so the same bytes.</remarks>
    [Test]
    public void Draw_ALitSurfaceUnderFullHeightFog_StoresTheFogColoursOwnBytes()
    {
        (int, int, int) drawn = Draw(heightFog: Height(Depth)) ?? Skip();

        AssertCurve(drawn, weight: 1.0, exact: true, "full height fog");
    }

    /// <remarks>**Half height fog (factor 0.5, unsquared)**: 5, 16, 40, as half range fog.</remarks>
    [Test]
    public void Draw_ALitSurfaceUnderHalfHeightFog_LerpsTowardTheSrgbDecodedColour()
    {
        (int, int, int) drawn = Draw(heightFog: Height(2f * Depth)) ?? Skip();

        AssertCurve(drawn, weight: 0.5, exact: false, "half height fog");
    }

    /// <summary>Valve's store, and the `pow 2.2` store beside it; proves they are apart, then checks the drawn pixel.</summary>
    private static void AssertCurve((int Red, int Green, int Blue) drawn, double weight, bool exact, string condition)
    {
        int tolerance = exact ? 0 : 1;
        int[] fog = [FogBytes.Red, FogBytes.Green, FogBytes.Blue];
        int[] actual = [drawn.Red, drawn.Green, drawn.Blue];

        for (int channel = 0; channel < 3; channel++)
        {
            int valve = SrgbTarget.Encode(weight * SrgbTarget.Decode(fog[channel]));
            int powCurve = SrgbTarget.Encode(weight * Math.Pow(fog[channel] / 255.0, 2.2));

            Math.Abs(valve - powCurve).ShouldBeGreaterThan(
                tolerance, $"the instrument: under {condition}, channel {channel} must separate the curves");
            actual[channel].ShouldBeInRange(
                valve - tolerance,
                valve + tolerance,
                $"under {condition} drew {drawn}; the sRGB decode stores {valve} on channel {channel}, pow 2.2 {powCurve}");
        }
    }

    private static SceneFog Range(float maxDensity) =>
        new(0f, Depth, FogBytes.Red / 255f, FogBytes.Green / 255f, FogBytes.Blue / 255f, maxDensity);

    /// <summary>Height fog over the whole line of sight; the factor is <see cref="Depth"/> over <paramref name="range"/>.</summary>
    private static VolumeFog Height(float range) =>
        new(HeightFog: true, FogZ: -Depth, Color: (FogBytes.Red / 255f, FogBytes.Green / 255f, FogBytes.Blue / 255f), Start: 0f, End: range, MaxDensity: 1f);

    /// <summary>The black lit quad over a black clear, at tone-map scale 1. Null without a device.</summary>
    private static (int, int, int)? Draw(SceneFog? fog = null, VolumeFog? heightFog = null)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null)
        {
            return null;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(Wall(), [new WorldBatch(Black, 0, 6)], Identity, Assets, fog: fog, toneMapScale: 1f, heightFog: heightFog);

        return target.PixelAt(8, 8);
    }

    /// <summary>The whole view at clip depth 0.9, every corner on one texel, white corners.</summary>
    private static List<WorldVertex> Wall() =>
    [
        new(-1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(-1f, -1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(-1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
        new(1f, 1f, Depth, 0.5f, 0.5f, 0f, 0f, 0f, 1f, 1f, 1f),
    ];

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private static (int, int, int) Skip()
    {
        Assert.Ignore("no Direct3D device on this machine");
        return default;
    }
}
