using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
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
    private static MapAssets? Assets => MapCache.TryLoad("ctf_2fort", maximumTextureSize: 256);

    private static readonly FreeCamera Camera = new()
    {
        Origin = (0f, 0f, 200f),
        Angles = (89f, 0f, 0f),
        Aspect = 1f,
    };

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
            assets.Waters[water]!.View, distanceToWater: 0f, cheapWaterEndDistance: new WaterLodSession().Current.End, WaterConVars.Defaults);

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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
                view =>
                {
                    drew.Add(view.Kind);
                    return false;
                }));

        // The main view's entities are the frame's own model pass, not the water views'.
        drew.ShouldBe([WaterViewKind.Refraction]);
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
            assets.Waters[water]!.View, 0f, new WaterLodSession().Current.End, WaterConVars.Defaults);
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

        // Faint, because the glass blends by its own low alpha into a target cleared to the fog colour — but in the
        // floor's direction, which nothing else in the scene can supply.
        (red.R - green.R).ShouldBeGreaterThan(1, "a translucent surface under the water never reached the refraction");
    }

    [Test]
    public void Hdr_Ctf2fort_IsIntegerHdr()
    {
        // Map_CheckForHDR (engine.dll 0x1800ffa10) finds 2fort's HDR lumps, and shaderapidx9's caps make the type
        // integer at mat_hdr_level 2 (HdrTypeConformanceTests) — so its water runs the ×0.25 / ×4 branch.
        if (Assets is not { } assets)
        {
            Assert.Ignore("TF2 is not installed, so ctf_2fort cannot be read");
            return;
        }

        assets.Hdr.ShouldBe(HdrType.IntegerHdr);
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWaterWorld_ReflectionViewUnderIntegerHdr_DrawsAtAQuarterToneMapScale()
    {
        // Valve scales in the SHADERS, not after: PushView's SetLightmapScaleForWater (viewrender.cpp:5351, :2726-2736)
        // sets the tone-map scale to a quarter for the reflection view, every shader's FinalOutput multiplies by it
        // (common_ps_fxc.h:345-350) into the 8-bit target, and PopView restores it (:5399-5403). So the scale is what the
        // view's own renderables are drawn with, and the main view's is back to one afterwards.
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, int floor) = Materials(assets);
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: true, ReflectEntities: true, DrawWaterSurface: true, OpaqueWater: false);
        List<(WaterViewKind Kind, float Scale)> seen = [];

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.DrawWaterWorld(
            [.. Quad(-5f, (1f, 0f, 0f)), .. Quad(0f, (1f, 1f, 1f))],
            [new(floor, 0, 6), new(water, 6, 6)],
            Camera,
            assets,
            new WaterDraw(
                WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), water, 0f, [], [], null, null,
                view =>
                {
                    seen.Add((view.Kind, target.WorldLinearLightScale));
                    return false;
                }),
            HdrType.IntegerHdr);

        seen.ShouldContain((WaterViewKind.Reflection, 0.25f));
        seen.ShouldContain((WaterViewKind.Refraction, 1f));
        target.WorldLinearLightScale.ShouldBe(1f, "PopView restores the main view's scale");
    }

    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWaterWorld_ReflectionUnderIntegerHdr_IsStoredAtAQuarter()
    {
        // PushView's SetLightmapScaleForWater (viewrender.cpp:5351, :2726-2736): under HDR_TYPE_INTEGER the reflection
        // view renders at a quarter of the tone-map scale, and water.cpp:296-312 multiplies it back by 4. The control is
        // the same draw under HDR_TYPE_NONE, which stores the reflection as drawn.
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, int floor) = Materials(assets);
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: true, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false);
        IReadOnlyList<WaterView> plan = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        plan.Select(view => view.Kind).ShouldContain(WaterViewKind.Reflection);

        // **2fort's own water names no $reflecttexture**, so neither TF2 nor this renderer draws its reflection view or
        // reads one — the plan here forces the view on so the storage can be measured at all.
        assets.Waters[water]!.View.ReflectTexture.ShouldBeFalse();

        // A dim ceiling above the main camera, which only the mirrored camera looks at; dim so neither run clips.
        double Stored(HdrType hdr)
        {
            using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

            target.Clear(0f, 0f, 0f);
            target.DrawWaterWorld(
                [.. Quad(300f, (0.5f, 0.5f, 0.5f)), .. Quad(0f, (1f, 1f, 1f))],
                [new(floor, 0, 6), new(water, 6, 6)],
                Camera, assets, new WaterDraw(plan, water, 0f, [], [], null, null), hdr);

            (int red, int green, int blue) = target.WaterTargetPixel(refraction: false, 512, 512);

            return Linear(red) + Linear(green) + Linear(blue);
        }

        double none = Stored(HdrType.None);
        double integer = Stored(HdrType.IntegerHdr);

        TestContext.Out.WriteLine($"REFLECTION STORED NONE {none:F4} INTEGER {integer:F4}");

        none.ShouldBeGreaterThan(0.05, "the reflection view drew nothing, so no scale could show");
        (integer / none).ShouldBe(0.25, 0.02);
    }

    /// <remarks>
    /// **The refraction view's fog clear, through the production draw, under both HDR types** (fog-open-items, LDR):
    /// <c>SetClearColorToFogColor</c> scales the bytes by <c>pow( 0.5, 1/2.2 )</c> = 0.72974 only under
    /// <c>HDR_TYPE_INTEGER</c> (<c>client.dll</c> <c>0x18034d560</c>, <c>viewrender.cpp:759</c>), truncated, and shaderapi
    /// stores the bytes (<c>0x180014bd0</c>). Nothing but the water surface is drawn, which the refraction view leaves out,
    /// so the target is the clear. Under no HDR the same draw at the same scale stores the bytes themselves. Literals:
    /// 2fort's <c>$fogcolor {51 43 13}</c> times 0.729740 is 37.2, 31.4, 9.49, stored 37, 31, 9.
    /// </remarks>
    [TestCase(HdrType.IntegerHdr, 37, 31, 9)]
    [TestCase(HdrType.None, 51, 43, 13)]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWaterWorld_TheRefractionViewsFogClearAtHalfScale_IsScaledOnlyUnderIntegerHdr(HdrType hdr, int red, int green, int blue)
    {
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int water, _) = Materials(assets);
        MapWater material = assets.Waters[water]!;
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: false, ReflectEntities: false, DrawWaterSurface: true, OpaqueWater: false);
        IReadOnlyList<WaterView> plan = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        material.FogEnable.ShouldBeTrue("the clear needs the volume's fog");
        (FogConstants.StoredByte(material.FogColor.Red), FogConstants.StoredByte(material.FogColor.Green),
            FogConstants.StoredByte(material.FogColor.Blue)).ShouldBe((51, 43, 13), "the subject: 2fort's water fog");

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld([.. Quad(0f, (1f, 1f, 1f))], [new(water, 0, 6)], Camera, assets,
            new WaterDraw(plan, water, 0f, [], [], null, null), hdr, toneMapScale: 0.5f);

        target.WaterTargetPixel(refraction: true, 512, 512).ShouldBe((red, green, blue));
        target.WorldLinearLightScale.ShouldBe(1f, "the draw's scale is restored when it returns");
    }

    /// <remarks>
    /// **B657: a water volume's surface is TWO coplanar faces, and only the one facing the eye draws.** vbsp gives the
    /// downward face the surface material's <c>$bottommaterial</c> (<c>utils/vbsp/faces.cpp:1255-1289</c>), so 2fort's
    /// surface is <c>water_2fort</c> facing up and <c>water_2fort_beneath</c> facing down, in one plane. The engine's
    /// world walk keeps both: a surface in a water leaf skips the plane-side test (engine.dll <c>0x1800e0600</c>,
    /// <c>VisibleWorld.Surfaces</c>). What removes the far one is the hardware cull the <c>Water</c> shader leaves on:
    /// its shadow state turns culling off only in the editor (<c>water.cpp:393-397</c>, "In edit mode, use nocull").
    ///
    /// **Expected, Valve's arithmetic:** from above, the downward face is culled whatever order it draws in, so the
    /// pixel is exactly the up-facing surface's own. Literal comparison of two draws, no curve. The bottom face draws
    /// FIRST, the only order that can fail: the opaque depth test is <c>Less</c>, so a coplanar face drawn second loses
    /// the tie with or without a cull (review: the second-order case could not fail, and is gone).
    ///
    /// **The control: the bottom material facing UP draws a different pixel from the same camera.** Both materials
    /// refract the same target, so without it `both == alone` could hold with no cull at all.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWaterWorld_TheSurfacesTwoCoplanarFaces_OnlyTheOneFacingTheEyeDraws()
    {
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (int top, int floor) = Materials(assets);
        int bottom = Enumerable.Range(0, assets.Waters.Count).First(index => assets.Waters[index] is { AboveWater: false });

        // The subject: 2fort's beneath material is not the surface's, so either face drawing shows.
        assets.Waters[top]!.BottomMaterial.ShouldNotBeNull();
        assets.Waters[bottom]!.FogColor.ShouldNotBe(assets.Waters[top]!.FogColor);

        WaterRenderInfo info = WaterRenderInfo.Determine(
            assets.Waters[top]!.View, 0f, new WaterLodSession().Current.End, WaterConVars.Defaults);
        IReadOnlyList<WaterView> views = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        (int, int, int) Draw(params (int Material, List<WorldVertex> Face)[] faces)
        {
            using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;
            List<WorldVertex> vertices = [.. Quad(-5f, (1f, 0f, 0f))];
            List<WorldBatch> batches = [new(floor, 0, 6)];

            foreach ((int material, List<WorldVertex> face) in faces)
            {
                batches.Add(new(material, vertices.Count, face.Count));
                vertices.AddRange(face);
            }

            target.Clear(0f, 0f, 0f);
            target.DrawWaterWorld(vertices, batches, Camera, assets, new WaterDraw(views, top, 0f, [], [], null, null));

            return target.PixelAt(32, 32);
        }

        (int, int, int) alone = Draw((top, Quad(0f, (1f, 1f, 1f))));
        (int, int, int) bottomFacingUp = Draw((bottom, Quad(0f, (1f, 1f, 1f))));
        (int, int, int) both = Draw((bottom, Reversed(Quad(0f, (1f, 1f, 1f)))), (top, Quad(0f, (1f, 1f, 1f))));

        TestContext.Out.WriteLine($"SURFACE ALONE {alone}; BOTTOM MATERIAL FACING UP {bottomFacingUp}; BOTTOM FACE FIRST, THEN THE SURFACE {both}");

        bottomFacingUp.ShouldNotBe(alone, "the bottom material draws the surface's pixel, so the cull cannot be seen");
        both.ShouldBe(alone, "the downward face drew from above");
    }

    /// <remarks>
    /// **The same claim on 2fort's OWN faces, as the world builder triangulates them** — the hand-built quad above
    /// proves the cull, this proves the BSP's winding agrees with the cull's convention, which no fixture can. The
    /// surface's run and its beneath run from <c>MapWorldBuilder</c>, from straight above the river: the pixel with
    /// both runs drawn, the beneath first, is exactly the surface run's alone.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawWaterWorld_TwoFortsBuiltSurfaceFromAbove_DrawsTheSurfaceNotTheBeneath()
    {
        if (!Direct3DApi.IsAvailable || !MapCache.Has("ctf_2fort"))
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        (_, MapWorld world) = WorldCullingMapTests.Built("ctf_2fort");
        MapAssets assets = MapCache.Load(mapName: "ctf_2fort");
        WorldBatch top = world.Batches.First(batch => assets.Waters[batch.MaterialIndex] is { AboveWater: true, View.RefractTexture: true });
        WorldBatch bottom = world.Batches.First(batch => assets.Waters[batch.MaterialIndex] is { AboveWater: false });

        // From the fog-open camera A's spot, looking straight down at the river.
        FreeCamera above = new() { Origin = (-294f, 29f, -60f), Angles = (89f, 0f, 0f), Aspect = 1f };
        WaterRenderInfo info = WaterRenderInfo.Determine(
            assets.Waters[top.MaterialIndex]!.View, 0f, new WaterLodSession().Current.End, WaterConVars.Defaults);
        IReadOnlyList<WaterView> views = WaterViews.Plan(info, new WaterFrame(false, false, -128f, false), ViewClears.Depth);

        // The control looks UP from under the river at the same spot, where the beneath run faces the eye and must draw.
        FreeCamera below = new() { Origin = (-294f, 29f, -200f), Angles = (-89f, 0f, 0f), Aspect = 1f };
        IReadOnlyList<WaterView> under = WaterViews.Plan(info, new WaterFrame(true, false, -128f, false), ViewClears.Depth);

        // The beneath run's own triangles wound the other way, appended: the same run, facing UP. Drawn from above it is
        // kept by the cull, so if it reaches the pixel the above-water view does not reject the run's sort group, and
        // the beneath's black from above below is the cull's (review: RenderUnderWater is unset when info.Refract).
        List<WorldVertex> vertices = [.. world.Vertices];
        List<WorldVertex> beneathRun = [.. world.Vertices.Skip(bottom.FirstVertex).Take(bottom.VertexCount)];
        WorldBatch beneathFacingUp = bottom with { FirstVertex = vertices.Count };

        for (int at = 0; at < beneathRun.Count; at += 3)
        {
            vertices.Add(beneathRun[at]);
            vertices.Add(beneathRun[at + 2]);
            vertices.Add(beneathRun[at + 1]);
        }

        (int, int, int) Draw(WorldBatch[] batches, FreeCamera camera, IReadOnlyList<WaterView> plan)
        {
            using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

            target.Clear(0f, 0f, 0f);
            target.DrawWaterWorld(vertices, batches, camera, assets, new WaterDraw(plan, top.MaterialIndex, -128f, [], [], null, null));

            return target.PixelAt(32, 32);
        }

        (int, int, int) alone = Draw([top], above, views);
        (int, int, int) beneath = Draw([bottom], above, views);
        (int, int, int) both = Draw([bottom, top], above, views);
        (int, int, int) beneathFromBelow = Draw([bottom], below, under);
        (int, int, int) beneathTurnedUp = Draw([beneathFacingUp], above, views);

        TestContext.Out.WriteLine(
            $"2FORT FROM ABOVE: SURFACE {alone} BENEATH {beneath} BOTH {both} BENEATH TURNED UP {beneathTurnedUp}; " +
            $"BENEATH FROM BELOW {beneathFromBelow}");

        beneathFromBelow.ShouldNotBe((0, 0, 0), "the beneath run does not reach this pixel, so its cull from above proves nothing");
        beneathTurnedUp.ShouldNotBe((0, 0, 0), "the above-water view rejects the beneath run, so its black from above is not the cull");
        alone.ShouldNotBe((0, 0, 0), "the surface run does not reach this pixel from above");
        beneath.ShouldBe((0, 0, 0), "2fort's beneath face drew from above");
        both.ShouldBe(alone, "2fort's beneath face drew over the surface from above");
    }

    /// <remarks>
    /// **B658: from inside 2fort's water, the frame is drawn through <c>effects/water_warp_2fort</c>.** The beneath
    /// material names it (<c>$underwateroverlay</c>), <c>CUnderWaterView::Setup</c> takes it (<c>viewrender.cpp:6115-6128</c>)
    /// and <c>DrawUnderwaterOverlay</c> draws it over the frame (<c>:1300-1340</c>). It is a <c>Refract</c> with
    /// <c>$refracttint {185 215 245}</c>, so over a grey frame it must draw, and darken red most and blue least. The
    /// measured pair is in RISKS B658; this holds that the map's own load makes it drawable.
    /// </remarks>
    [Test]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void DrawScreenOverlay_TwoFortsUnderwaterOverlay_TintsTheFrameBlue()
    {
        if (!Direct3DApi.IsAvailable || !MapCache.Has("ctf_2fort"))
        {
            Assert.Ignore("needs Direct3D and TF2's ctf_2fort");
            return;
        }

        MapAssets assets = MapCache.Load(mapName: "ctf_2fort");
        MapWater beneath = assets.Waters.OfType<MapWater>().First(water => !water.AboveWater);

        beneath.UnderwaterOverlay.ShouldBe("effects/water_warp_2fort");
        assets.ScreenOverlays.TryGetValue(beneath.UnderwaterOverlay!, out ScreenOverlayMaterial? overlay).ShouldBeTrue("the overlay was not loaded with the map");
        overlay.Refract.ShouldNotBeNull("the overlay loaded but this port would not draw it");

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.Clear(0.5f, 0.5f, 0.5f);

        (int red, int green, int blue) under = target.PixelAt(32, 32);

        target.DrawScreenOverlay(overlay, 0.0).ShouldBeTrue();

        (int red, int green, int blue) drawn = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"GREY {under} THROUGH water_warp_2fort {drawn}");

        drawn.red.ShouldBeLessThan(under.red - 20, "the tint's 185 red did not darken the frame");
        drawn.red.ShouldBeLessThan(drawn.green);
        drawn.green.ShouldBeLessThan(drawn.blue);
    }

    /// <summary>2fort's surface water alone over black, from above with its own plan, on a fresh renderer; null without 2fort or Direct3D.</summary>
    /// <remarks>A fresh target reads TF2VIEW_MODEL_CULL afresh, which is what <see cref="ModelCullOverrideReleaseTests"/> needs.</remarks>
    internal static (int, int, int)? SurfaceFromAbove()
    {
        if (!Direct3DApi.IsAvailable || Assets is not { } assets)
        {
            return null;
        }

        (int top, _) = Materials(assets);
        WaterRenderInfo info = WaterRenderInfo.Determine(
            assets.Waters[top]!.View, 0f, new WaterLodSession().Current.End, WaterConVars.Defaults);
        IReadOnlyList<WaterView> views = WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth);

        using OffscreenTarget target = OffscreenTarget.TryCreate(64, 64)!;

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(Quad(0f, (1f, 1f, 1f)), [new(top, 0, 6)], Camera, assets, new WaterDraw(views, top, 0f, [], [], null, null));

        return target.PixelAt(32, 32);
    }

    private static List<WorldVertex> Reversed(List<WorldVertex> face)
    {
        List<WorldVertex> flipped = [];

        for (int at = 0; at < face.Count; at += 3)
        {
            flipped.Add(face[at] with { NormalZ = -1f });
            flipped.Add(face[at + 2] with { NormalZ = -1f });
            flipped.Add(face[at + 1] with { NormalZ = -1f });
        }

        return flipped;
    }

    private static double Linear(int srgb)
    {
        double c = srgb / 255.0;

        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
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
