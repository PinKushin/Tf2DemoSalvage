using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The water views on a real map: <c>ctf_2fort</c>'s own <c>Water</c> material over a floor, drawn offscreen (B62).
/// </summary>
/// <remarks>
/// **The property that differs between right and wrong** (<c>docs/memory/a-picture-is-assertable.md</c>): the water
/// surface shows what lies beneath it. Its <c>Water</c> material refracts against <c>_rt_WaterRefraction</c>, which the
/// refraction view (<c>viewrender.cpp:6033-6076</c>) fills with the under-water world — so a red floor and a green
/// floor under the same water give two different water pixels. With no refraction view they give the same one, and
/// that is the control: it proves the difference comes through the target and not past the water.
///
/// **The floor sits five units down, and that is the condition.** The shader fogs, and the cheap pass covers, by the
/// refraction's alpha — the volume's height fog factor (<c>CalcWaterFogAlpha</c>). Five units under 2fort's
/// <c>$fogstart -100</c>/<c>$fogend 400</c> is a factor near 0.01, under the 0.05 at which both
/// <c>water_ps2x_helper.h</c>'s fog lerp and <c>WaterCheap_ps2x.fxc</c>'s REFRACTALPHA start to cover it.
/// </remarks>
public sealed class WaterViewRenderTests
{
    private static MapAssets? Assets
    {
        get
        {
            string tf = GameInstall.Root ?? string.Empty;
            string map = System.IO.Path.Combine(tf, "maps", "ctf_2fort.bsp");

            return System.IO.File.Exists(map)
                ? MapAssets.Load(System.IO.File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256)
                : null;
        }
    }

    private static readonly FreeCamera Camera = new()
    {
        Origin = (0f, 0f, 200f),
        Angles = (89f, 0f, 0f),
        Aspect = 1f,
    };

    [Test]
    public void DrawWaterWorld_FloorUnderRefractingWater_ShowsThroughTheSurface()
    {
        if (!Direct3DApi.IsAvailable)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("TF2 is not installed, so ctf_2fort's water material cannot be read");
            return;
        }

        int water = Enumerable.Range(0, assets.Waters.Count)
            .First(index => assets.Waters[index] is { View.RefractTexture: true, AboveWater: true });
        int floor = Enumerable.Range(0, assets.Textures.Count)
            .First(index => assets.Textures[index] is { IsTranslucent: false, IsTransparent: false } && assets.Waters[index] is null);

        WaterRenderInfo info = WaterRenderInfo.Determine(
            assets.Waters[water]!.View, distanceToWater: 0f, cheapWaterEndDistance: assets.WaterLod.End, WaterConVars.Defaults);

        IReadOnlyList<WaterView> views = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        // The condition: the plan carries a refraction view, or nothing here could differ.
        views.Select(view => view.Kind).ShouldContain(WaterViewKind.Refraction);

        IReadOnlyList<WaterView> without = [.. views.Where(view => view.Kind != WaterViewKind.Refraction)];

        ((int R, int G, int B) Red, (int R, int G, int B) Green) with = Pair(assets, floor, water, views);
        ((int R, int G, int B) Red, (int R, int G, int B) Green) control = Pair(assets, floor, water, without);

        TestContext.Out.WriteLine($"WATER OVER RED {with.Red} OVER GREEN {with.Green}; NO REFRACTION {control.Red} {control.Green}");

        (with.Red.R - with.Red.G).ShouldBeGreaterThan(
            with.Green.R - with.Green.G + 10, "the floor under the water does not show through it");

        control.Red.ShouldBe(control.Green, "with no refraction view the floor still reached the water pixel");
    }

    /// <summary>The water pixel over a red floor and over a green one, on a fresh target so no view leaks between plans.</summary>
    private static ((int, int, int) Red, (int, int, int) Green) Pair(
        MapAssets assets, int floor, int water, IReadOnlyList<WaterView> plan)
    {
        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        (int, int, int) Draw((float Red, float Green, float Blue) colour)
        {
            List<WorldVertex> vertices = [.. Quad(-5f, colour), .. Quad(0f, (1f, 1f, 1f))];
            WorldBatch[] batches = [new(floor, 0, 6), new(water, 6, 6)];

            target.Clear(0f, 0f, 0f);

            (int expensive, _, _) = target.DrawWaterWorld(
                vertices, batches, Camera, assets, new WaterDraw(plan, water, 0f, [], [], null, null));

            expensive.ShouldBeGreaterThan(0, "the water surface did not draw with the Water shader's expensive pass");

            return target.PixelAt(32, 32);
        }

        return (Draw((1f, 0f, 0f)), Draw((0f, 1f, 0f)));
    }

    private static List<WorldVertex> Quad(float z, (float Red, float Green, float Blue) colour)
    {
        const float half = 400f;
        (float r, float g, float b) = colour;

        return
        [
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, r, g, b),
            new(half, -half, z, 4f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-half, half, z, 0f, 4f, 0f, 0f, 0f, r, g, b),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, r, g, b),
        ];
    }
}
