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
