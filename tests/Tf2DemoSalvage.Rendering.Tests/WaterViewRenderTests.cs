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

    [Test]
    public void DrawWaterWorld_TheRefractionView_DrawsTheEntitiesAndTheReflectionDoesNot()
    {
        // DF_DRAW_ENTITITES: the refraction view always (viewrender.cpp:6041), the reflection only with
        // $reflectentities (:5985) — and DrawExecute then draws the opaque and translucent renderables (:5524-5538).
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, int floor) = Materials(assets);
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: true, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false);
        List<WaterViewKind> drew = [];

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.DrawWaterWorld(
            [.. Quad(-5f, (1f, 0f, 0f)), .. Quad(0f, (1f, 1f, 1f))],
            [new(floor, 0, 6), new(water, 6, 6)],
            Camera,
            assets,
            new WaterDraw(
                WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), water, 0f, [], [], null, null,
                view => drew.Add(view.Kind)));

        // The main view's entities are the frame's own model pass, not the water views'.
        drew.ShouldBe([WaterViewKind.Refraction]);
    }

    [Test]
    public void DrawWaterWorld_UnderWater_TheRefractionIsDrawnIntoTheFrameAndCopiedOut()
    {
        // CUnderWaterView::CRefractionView (viewrender.cpp:6203-6248): "Refraction renders into the back buffer, over
        // the top of the 3D skybox. It is then blitted out into the refraction target." The main under-water view
        // clears depth only (:6134), so what it does not cover is still the refraction view's picture.
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, int floor) = Materials(assets);
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: false, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false);
        FreeCamera under = new() { Origin = (0f, 0f, -20f), Angles = (-89f, 0f, 0f), Aspect = 1f };

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(
            Quad(50f, (1f, 0f, 0f)),
            [new(floor, 0, 6)],
            under,
            assets,
            new WaterDraw(
                WaterViews.Plan(info, new WaterFrame(true, false, 0f, false), ViewClears.Depth), water, 0f, [], [], null, null));

        (int red, int green, _) = target.PixelAt(32, 32);
        (int targetRed, int targetGreen, _) = target.WaterTargetPixel(refraction: true, 512, 512);

        TestContext.Out.WriteLine($"FRAME {red},{green}  REFRACTION TARGET {targetRed},{targetGreen}");

        (red - green).ShouldBeGreaterThan(20, "the frame does not hold the refraction view's picture");
        (targetRed - targetGreen).ShouldBeGreaterThan(20, "the refraction target was not filled from the frame");
    }

    [Test]
    public void DrawWaterWorld_ATranslucentFloorUnderWater_ShowsThroughTheSurface()
    {
        // DrawExecute draws the translucent world in every water view (viewrender.cpp:5537 with entities, :5553 without).
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, _) = Materials(assets);
        int glass = Enumerable.Range(0, assets.Textures.Count)
            .First(index => assets.Textures[index] is { IsTranslucent: true } && assets.Waters[index] is null);

        WaterRenderInfo info = WaterRenderInfo.Determine(
            assets.Waters[water]!.View, 0f, assets.WaterLod.End, WaterConVars.Defaults);
        IReadOnlyList<WaterView> views = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        (int, int, int) Draw((float, float, float) colour)
        {
            using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

            target.Clear(0f, 0f, 0f);
            target.DrawWaterWorld(
                [.. Quad(-5f, colour), .. Quad(0f, (1f, 1f, 1f))],
                [new(glass, 0, 6), new(water, 6, 6)],
                Camera, assets, new WaterDraw(views, water, 0f, [], [], null, null));

            return target.PixelAt(32, 32);
        }

        (int R, int G, int B) red = Draw((1f, 0f, 0f));
        (int R, int G, int B) green = Draw((0f, 1f, 0f));

        TestContext.Out.WriteLine($"GLASS UNDER WATER RED {red} GREEN {green}");

        red.ShouldNotBe(green, "a translucent surface under the water never reached the refraction");
    }

    private static (int Water, int Floor) Materials(MapAssets assets) =>
        (Enumerable.Range(0, assets.Waters.Count).First(index => assets.Waters[index] is { View.RefractTexture: true, AboveWater: true }),
         Enumerable.Range(0, assets.Textures.Count)
             .First(index => assets.Textures[index] is { IsTranslucent: false, IsTransparent: false } && assets.Waters[index] is null));

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
