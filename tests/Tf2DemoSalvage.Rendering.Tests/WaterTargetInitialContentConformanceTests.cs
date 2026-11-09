using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What a <c>Water</c> material samples from <c>_rt_WaterRefraction</c> before any water view has drawn into it (B662).
/// </summary>
/// <remarks>
/// **The engine, read in disassembly:**
/// - A render target is created through <c>CTexture::ReconstructTexture</c> (materialsystem.dll <c>0x18003cd60</c>,
///   "%s - RT Stuff"). Unless its format is a depth format, it is then cleared: push it as the render target,
///   <c>ClearColor4ub( 0, 0, 0, 255 )</c>, <c>ClearBuffers( true, false, false, w, h )</c>, pop ("%s - Clearing").
/// - shaderapi's own creation (<c>CreateD3DTexture</c>, shaderapidx9.dll <c>0x18002f480</c>) clears nothing.
/// - So until a view writes the target it is opaque black: colour 0 and alpha 1. Alpha is the refraction's fog depth.
///
/// **Measured against it:** TF2 launched with <c>+r_WaterDrawRefraction 0 +r_WaterDrawReflection 0</c>, so no water
/// view ever ran. koth_toxic's vat water then draws 19 86 77, its own <c>$fogcolor</c>: a full fog, which needs a
/// high alpha (<c>D:/measure-f12/b659/tf2-camB-fresh-noviews.png</c>).
///
/// **Expected, by the shader's arithmetic** (<c>water_ps2x_helper.h</c>, as ported): with <c>$abovewater 1</c>, the
/// fog depth is the refraction alpha, 1. The refraction lerps to the fog colour by <c>saturate( 1 - 0.05 )</c>, and with
/// no reflection that is the result: 0.95 · <c>GammaToLinear( $fogcolor )</c>, stored through the sRGB curve.
/// The subject is hand-built (D38): one refracting water and no view plan beyond the simple view.
/// </remarks>
public sealed class WaterTargetInitialContentConformanceTests
{
    private static readonly (byte Red, byte Green, byte Blue) FogColor = (200, 100, 50);

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(40)], ["Water"], [Refracting()]);

    private static readonly FreeCamera Above = new() { Origin = (0f, 0f, 200f), Angles = (89f, 0f, 0f), Aspect = 1f };

    [Test]
    public void DrawWaterWorld_RefractingWaterBeforeAnyView_SamplesAnOpaqueBlackTarget()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        // The simple view alone: cheap water, so no refraction view writes the target this frame or before it.
        WaterRenderInfo info = new(CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: true);

        target.Clear(1f, 0f, 1f);
        (int expensive, _, _) = target.DrawWaterWorld(
            Quad(), [new(0, 0, 6)], Above, Assets,
            new WaterDraw(WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), 0, 0f, [], [], null, null));

        expensive.ShouldBe(1, "the water did not take the expensive pass");

        (int red, int green, int blue) = target.PixelAt(32, 32);
        (int Red, int Green, int Blue) expected = (Stored(FogColor.Red), Stored(FogColor.Green), Stored(FogColor.Blue));

        TestContext.Out.WriteLine($"WATER BEFORE ANY VIEW {(red, green, blue)}; EXPECTED {expected}");

        red.ShouldBeInRange(expected.Red - 2, expected.Red + 2, "red");
        green.ShouldBeInRange(expected.Green - 2, expected.Green + 2, "green");
        blue.ShouldBeInRange(expected.Blue - 2, expected.Blue + 2, "blue");
    }

    /// <summary>
    /// A map load that flips the HDR-enabled state recreates the targets, cleared opaque black, so the next map's water
    /// samples black where the old map's view had drawn (B662's second half).
    /// </summary>
    /// <remarks>
    /// engine.dll <c>Mod_LoadMapFlags</c> (<c>0x1800ffa10</c>, read in disassembly): the HDR-enabled state wanted is the
    /// map's HDR lighting and <c>mat_hdr_level</c> above 1 and DX level above 89, our <c>MapAssets.Hdr != None</c>. When
    /// it differs from materialsystem's current one (vtable <c>+0x228</c>), it sets it (<c>+0x230</c>) and shuts down and
    /// re-initialises the well-known render targets (<c>FUN_1800f6920</c>), which <c>CTexture::ReconstructTexture</c>
    /// clears opaque black.
    /// </remarks>
    [TestCase(Content.Bsp.HdrType.None, Content.Bsp.HdrType.IntegerHdr)]
    [TestCase(Content.Bsp.HdrType.IntegerHdr, Content.Bsp.HdrType.None)]
    public void DrawWaterWorld_MapLoadThatFlipsHdr_SamplesAnOpaqueBlackTarget(
        Content.Bsp.HdrType first, Content.Bsp.HdrType second)
    {
        if (TwoMaps(first, second) is not { } run)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        (int Red, int Green, int Blue) expected = (Stored(FogColor.Red), Stored(FogColor.Green), Stored(FogColor.Blue));

        TestContext.Out.WriteLine(
            $"FLIP {first} -> {second}: FIRST MAP {run.First}; SECOND {run.Second}; EXPECTED {expected}; TEXEL {run.Texel}");

        run.Texel.ShouldBe((0, 0, 0), "the target kept the first map's view across a load that flips HDR");
        run.Second.Red.ShouldBeInRange(expected.Red - 2, expected.Red + 2, "red");
        run.Second.Green.ShouldBeInRange(expected.Green - 2, expected.Green + 2, "green");
        run.Second.Blue.ShouldBeInRange(expected.Blue - 2, expected.Blue + 2, "blue");
    }

    /// <summary>
    /// The control: a map load whose HDR-enabled state equals the current one keeps the targets and what the old map's
    /// view drew, so the water samples exactly what it sampled on the first map.
    /// </summary>
    [TestCase(Content.Bsp.HdrType.None)]
    [TestCase(Content.Bsp.HdrType.IntegerHdr)]
    public void DrawWaterWorld_MapLoadThatKeepsHdr_SamplesTheOldViewsContent(Content.Bsp.HdrType hdr)
    {
        if (TwoMaps(hdr, hdr) is not { } run)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"KEEP {hdr}: FIRST MAP {run.First}; SECOND {run.Second}; TEXEL {run.Texel}");

        run.Texel.ShouldBe((255, 0, 0), "the target lost the first map's view across a load that keeps HDR");
        run.Second.ShouldBe(run.First, "the second map's water did not sample what the first map's view drew");
    }

    /// <summary>
    /// The monitor builds its own water views from its camera into the one global <c>_rt_WaterRefraction</c>, before its
    /// world (B711): <c>ViewDrawScene( …, VIEW_MONITOR )</c> reaches <c>DrawWorldAndEntities</c>, which carries no view-id
    /// branch (`viewrender.cpp:2649-2718`, `:3168-3238`).
    /// </summary>
    /// <remarks>
    /// The main frame is cheap water alone, so nothing else writes the target: before the monitor its centre is the opaque
    /// black it was created as. The monitor's refraction view over the red floor fills it with (255, 0, 0); the control, a
    /// monitor with no water views, leaves it black.
    /// </remarks>
    [TestCase(true, 255)]
    [TestCase(false, 0)]
    public void DrawMonitorWorld_WithItsOwnRefractionView_FillsTheGlobalTargetFirst(bool ownViews, int red)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        WaterRenderInfo cheap = new(CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: true);
        WaterRenderInfo refract = cheap with { CheapWater = false, Refract = true, OpaqueWater = false };

        target.DrawWaterWorld(
            [.. Quad(), .. RedFloor()], [new(0, 0, 6), new(1, 6, 6)], Above, WithFloor(Content.Bsp.HdrType.None),
            new WaterDraw(WaterViews.Plan(cheap, new WaterFrame(false, false, 0f, false), ViewClears.Depth), 0, 0f, [], [], null, null));

        target.WaterTargetPixel(true, 512, 512).ShouldBe((0, 0, 0), "the main frame wrote the refraction target");

        target.DrawMonitorWorld(
            Above.ToMatrix(),
            ownViews
                ? new WaterDraw(WaterViews.Plan(refract, new WaterFrame(false, false, 0f, false), ViewClears.Depth), 0, 0f, [], [], null, null)
                : null);

        target.WaterTargetPixel(true, 512, 512).ShouldBe((red, 0, 0));
    }

    /// <summary>
    /// The first map draws its water through a refraction view over a red floor; the second, a cheap-only frame over the
    /// water alone, so nothing writes the target before its water samples it. Both water pixels, at the centre.
    /// </summary>
    private static Run? TwoMaps(
        Content.Bsp.HdrType first, Content.Bsp.HdrType second)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        WaterRenderInfo refract = new(CheapWater: false, Refract: true, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: false);

        target.Clear(1f, 0f, 1f);
        target.DrawWaterWorld(
            [.. Quad(), .. RedFloor()], [new(0, 0, 6), new(1, 6, 6)], Above, WithFloor(first),
            new WaterDraw(WaterViews.Plan(refract, new WaterFrame(false, false, 0f, false), ViewClears.Depth), 0, 0f, [], [], null, null));

        (int, int, int) firstPixel = target.PixelAt(32, 32);

        WaterRenderInfo cheap = new(CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: true);

        target.Clear(1f, 0f, 1f);
        (int expensive, _, _) = target.DrawWaterWorld(
            Quad(), [new(0, 0, 6)], Above, WithFloor(second),
            new WaterDraw(WaterViews.Plan(cheap, new WaterFrame(false, false, 0f, false), ViewClears.Depth), 0, 0f, [], [], null, null));

        expensive.ShouldBe(1, "the second map's water did not take the expensive pass");

        return new Run(firstPixel, target.PixelAt(32, 32), target.WaterTargetPixel(true, 512, 512));
    }

    /// <summary>
    /// The water pixel on each map, and the refraction target's centre texel after the second. The first map's view
    /// fills it with the floor, (255, 0, 0); the water itself shows that only as 0.05 of its colour, because the view
    /// writes a full fog depth, so the texel is the instrument that sees the floor plainly and the pixel is what is drawn.
    /// </summary>
    private readonly record struct Run(
        (int Red, int Green, int Blue) First, (int Red, int Green, int Blue) Second, (int Red, int Green, int Blue) Texel);

    /// <summary>The refracting water and a lightmapped floor, under the given HDR type.</summary>
    private static MapAssets WithFloor(Content.Bsp.HdrType hdr) => MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(40), ModulateToneMapConformanceTests.Grey(200)],
        ["Water", "LightmappedGeneric"],
        [Refracting(), null],
        hdr);

    /// <summary>A red floor 5 units under the water, which only a refraction view draws into the target.</summary>
    private static List<WorldVertex> RedFloor()
    {
        const float half = 400f;
        const float z = -5f;

        return
        [
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
            new(half, -half, z, 4f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
            new(-half, -half, z, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
            new(-half, half, z, 0f, 4f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
            new(half, half, z, 4f, 4f, 0f, 0f, 0f, 1f, 0f, 0f, NormalZ: 1f),
        ];
    }

    /// <summary>0.95 of the fog colour's linear value, stored through the sRGB curve.</summary>
    private static int Stored(byte gamma)
    {
        double linear = 0.95 * WorldRenderer.Linear(gamma / 255f);
        double encoded = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;

        return (int)Math.Round(encoded * 255.0);
    }

    private static MapWater Refracting() =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$refracttexture\" \"_rt_WaterRefraction\"\n\"$refractamount\" \"0\"\n\"$abovewater\" \"1\"\n" +
            $"\"$fogcolor\" \"{{{FogColor.Red} {FogColor.Green} {FogColor.Blue}}}\"\n\"$fogstart\" \"-100\"\n\"$fogend\" \"400\"\n}}")), null);

    private static List<WorldVertex> Quad()
    {
        const float half = 400f;

        return
        [
            new(-half, -half, 0f, 0f, 0f, 0f, 0f, 0f),
            new(half, half, 0f, 4f, 4f, 0f, 0f, 0f),
            new(half, -half, 0f, 4f, 0f, 0f, 0f, 0f),
            new(-half, -half, 0f, 0f, 0f, 0f, 0f, 0f),
            new(-half, half, 0f, 0f, 4f, 0f, 0f, 0f),
            new(half, half, 0f, 4f, 4f, 0f, 0f, 0f),
        ];
    }
}
