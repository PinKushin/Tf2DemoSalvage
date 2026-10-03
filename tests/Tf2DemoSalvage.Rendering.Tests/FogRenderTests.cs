using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Range fog through the real world shader, with predicted pixels (B139).
/// </summary>
/// <remarks>
/// **The condition is chosen so the prediction is exact.** The quad sits at clip depth 0.9 under an
/// identity camera, so <c>flProjPosZ</c> is 0.9. Fog from 0 to 0.9 puts that depth at factor 1:
/// the pixel IS the fog colour. A max density of 0.5 caps the factor before the saturate, and the
/// blend squares it — 0.25 — so the pixel is a quarter of the way to the fog colour in LINEAR light
/// (the back buffer is sRGB, so the comparison decodes and re-encodes).
/// </remarks>
public sealed class FogRenderTests
{
    private static MapAssets? Assets
    {
        get
        {
            string tf = GameInstall.Root ?? string.Empty;
            string map = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            return !Directory.Exists(tf) || !File.Exists(map)
                ? null
                : MapAssets.Load(
                    File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    private const float Depth = 0.9f;

    [Test]
    public void Draw_FullDensityFogAtTheSurfacesDepth_IsTheFogColour()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null || Assets is not { } assets)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (int Red, int Green, int Blue) clear = Draw(target, assets, null);
        (int Red, int Green, int Blue) fogged = Draw(target, assets, new SceneFog(0f, Depth, 1f, 0f, 0f, 1f));

        TestContext.Out.WriteLine($"FOG clear {clear} / full red fog {fogged}");

        clear.ShouldNotBe((255, 0, 0), "the control: an unfogged wall must not already be the fog colour");
        fogged.ShouldBe((255, 0, 0));
    }

    [Test]
    public void Draw_MaxDensityHalf_BlendsAQuarterInLinearLight()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null || Assets is not { } assets)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        (int Red, int Green, int Blue) clear = Draw(target, assets, null);
        (int Red, int Green, int Blue) fogged = Draw(target, assets, new SceneFog(0f, Depth, 1f, 0f, 0f, 0.5f));

        // lerp( shader, fog, 0.5 * 0.5 ) per channel in linear light; fog is (1, 0, 0).
        static int Predict(int channel, float fog) =>
            (int)MathF.Round(Encode((Decode(channel) * 0.75f) + (fog * 0.25f)) * 255f);

        TestContext.Out.WriteLine($"FOG clear {clear} / half-density red fog {fogged}");

        fogged.Red.ShouldBe(Predict(clear.Red, 1f), 1);
        fogged.Green.ShouldBe(Predict(clear.Green, 0f), 1);
        fogged.Blue.ShouldBe(Predict(clear.Blue, 0f), 1);
    }

    private static (int Red, int Green, int Blue) Draw(OffscreenTarget target, MapAssets assets, SceneFog? fog)
    {
        const float lit = 0.5f;

        List<WorldVertex> wall =
        [
            new(-1f, -1f, Depth, 0f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, 1f, Depth, 1f, 1f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, -1f, Depth, 1f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(-1f, -1f, Depth, 0f, 0f, lit, lit, 0f, 1f, 1f, 1f),
            new(-1f, 1f, Depth, 0f, 1f, lit, lit, 0f, 1f, 1f, 1f),
            new(1f, 1f, Depth, 1f, 1f, lit, lit, 0f, 1f, 1f, 1f),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [new WorldBatch(0, 0, wall.Count)], Identity, assets, fog: fog);

        return target.PixelAt(32, 32);
    }

    private static float Decode(int channel)
    {
        float c = channel / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static float Encode(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
