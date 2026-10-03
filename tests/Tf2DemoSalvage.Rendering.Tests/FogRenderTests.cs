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
/// blend squares it — 0.25 — so the pixel is a quarter of the way to the fog colour. A prediction
/// written for an sRGB target first said 138 and the target answered 74: the offscreen target is
/// UNORM, so the shader's linear output is what is stored.
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

        // lerp( shader, fog, 0.5 * 0.5 ) per channel; fog is (1, 0, 0). The offscreen target is
        // UNORM, not sRGB, so the shader's linear output is stored as it is and read back the same.
        static int Predict(int channel, float fog) =>
            (int)MathF.Round(((channel / 255f * 0.75f) + (fog * 0.25f)) * 255f);

        TestContext.Out.WriteLine($"FOG clear {clear} / half-density red fog {fogged}");

        // One step either way for eight-bit rounding on both reads.
        fogged.Red.ShouldBeInRange(Predict(clear.Red, 1f) - 1, Predict(clear.Red, 1f) + 1);
        fogged.Green.ShouldBeInRange(Predict(clear.Green, 0f) - 1, Predict(clear.Green, 0f) + 1);
        fogged.Blue.ShouldBeInRange(Predict(clear.Blue, 0f) - 1, Predict(clear.Blue, 0f) + 1);
    }

    [Test]
    public void Draw_AnAdditiveMaterialInFullFog_FogsToBlackSoAddsNothing()
    {
        // An additive surface ADDS its output to what is behind it, so fogging it toward the fog
        // colour would paint the fog colour on top of the haze; fogged toward black it fades out.
        // Over a black clear, full fog therefore leaves black — and the control, unfogged, does not.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null || Assets is not { } assets)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        // **The first additive material that ADDS something at the sampled texel**, found rather
        // than assumed: the first one cp_process lists draws black there, which made the control
        // fail before the prediction could mean anything.
        int additive = -1;
        (int Red, int Green, int Blue) clear = default;

        for (int index = 0; index < assets.Textures.Count && additive < 0; index++)
        {
            if (assets.Textures[index] is { IsAdditive: true } &&
                Draw(target, assets, null, index) is var drawn && drawn != (0, 0, 0))
            {
                additive = index;
                clear = drawn;
            }
        }

        if (additive < 0)
        {
            Assert.Ignore("this map has no additive material that draws at the sampled texel");
            return;
        }

        (int Red, int Green, int Blue) fogged = Draw(target, assets, new SceneFog(0f, Depth, 1f, 1f, 1f, 1f), additive);

        TestContext.Out.WriteLine($"FOG additive material {additive}: clear {clear} / full white fog {fogged}");

        clear.ShouldNotBe((0, 0, 0), "the control: unfogged, the additive surface must add something");
        fogged.ShouldBe((0, 0, 0));
    }

    [Test]
    public void Draw_RadialFogAtTheEdgeOfTheView_FogsByDistanceNotDepth()
    {
        // **The condition separates the two by construction.** At pixel (2, 32) the wall is at
        // depth 0.9 but about 1.29 from the eye (x ≈ -0.92; the identity camera's eye is the
        // origin). Fog ending at 1.0: range fog reaches 0.9 there, radial fog saturates — so only
        // radial gives the pure fog colour.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null || Assets is not { } assets)
        {
            Assert.Ignore("no Direct3D, or the map or the game is not installed");
            return;
        }

        SceneFog range = new(0f, 1f, 1f, 0f, 0f, 1f);

        (int Red, int Green, int Blue) byDepth = Draw(target, assets, range, pixelX: 2);
        (int Red, int Green, int Blue) byDistance = Draw(target, assets, range with { Radial = true }, pixelX: 2);

        TestContext.Out.WriteLine($"FOG edge pixel: range {byDepth} / radial {byDistance}");

        byDepth.ShouldNotBe((255, 0, 0), "the control: by depth the edge is not yet fully fogged");
        byDistance.ShouldBe((255, 0, 0));
    }

    private static (int Red, int Green, int Blue) Draw(
        OffscreenTarget target, MapAssets assets, SceneFog? fog, int material = 0, int pixelX = 32)
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
        target.DrawWorld(wall, [new WorldBatch(material, 0, wall.Count)], Identity, assets, fog: fog);

        return target.PixelAt(pixelX, 32);
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
