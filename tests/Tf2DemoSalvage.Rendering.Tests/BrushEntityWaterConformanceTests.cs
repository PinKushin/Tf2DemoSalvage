using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// A <c>Water</c> material on a brush ENTITY draws with the <c>Water</c> shader, sampling the refraction target the
/// world's water views filled this frame (B659).
/// </summary>
/// <remarks>
/// **What the engine does, before any code:** <c>C_BaseEntity::DrawBrushModel</c> hands a brush entity to
/// <c>render->DrawBrushModelEx</c> (<c>c_baseentity.cpp:1959</c>, <c>:1152</c>). In engine.dll (disassembly) <c>0x1800ddfa0</c>
/// batches the model's non-translucent surfaces by their own material and tests no water flag, and <c>0x1800db920</c>
/// binds each batch's material and draws it. The <c>Water</c> shader reads only its parameters (<c>water.cpp:534-584</c>)
/// and binds <c>$refracttexture</c>, <c>_rt_WaterRefraction</c> by default (<c>water.cpp:31</c>, <c>:270-278</c>).
/// So a brush face and a world face with one water material, drawn after the same views, give the same pixel.
///
/// **The subject is hand-built** (D38): one refracting <c>Water</c> material with no reflection and no cubemap, so only
/// the expensive pass applies, over a floor five units down whose colour reaches the water only through the target. The
/// water material's own base texture is a grey, so a draw that ignores the Water shader cannot follow the floor.
/// </remarks>
public sealed class BrushEntityWaterConformanceTests
{
    private const int Floor = 0;
    private const int Water = 1;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(200), ModulateToneMapConformanceTests.Grey(40)],
        ["LightmappedGeneric", "Water"],
        [null, Refracting()]);

    private static readonly FreeCamera Above = new() { Origin = (0f, 0f, 200f), Angles = (89f, 0f, 0f), Aspect = 1f };

    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    [Test]
    public void Pass_TheSyntheticWater_IsTheExpensivePassAlone()
    {
        MapWater water = Assets.Waters[Water]!;

        WaterShader.Pass(water.View.RefractTexture, water.View.ReflectTexture, hasEnvmap: false, water.View.ForceCheap,
            water.View.ForceExpensive, isDecal: false).ShouldBe(WaterPass.ReflectionRefraction);
    }

    [Test]
    public void DrawModelPose_BrushEntityWaterOverAFloor_DrawsTheWorldWatersPixel()
    {
        (int, int, int)? world = WorldWater((1f, 0f, 0f));
        (int, int, int)? brush = BrushWater((1f, 0f, 0f));

        if (world is not { } surface || brush is not { } entity)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"OVER A RED FLOOR: WORLD WATER {surface}; BRUSH-ENTITY WATER {entity}");

        entity.ShouldBe(surface, "a brush entity's water face did not draw as the world's water face does");
    }

    [Test]
    public void DrawModelPose_BrushEntityWaterOverRedAndGreenFloors_FollowsTheRefractionTarget()
    {
        (int R, int G, int B)? red = BrushWater((1f, 0f, 0f));
        (int R, int G, int B)? green = BrushWater((0f, 1f, 0f));

        if (red is not { } overRed || green is not { } overGreen)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"BRUSH-ENTITY WATER OVER RED {overRed}; OVER GREEN {overGreen}");

        // The floor reaches the brush face only through _rt_WaterRefraction: a draw with the material's base texture cannot
        // tell the two apart.
        (overRed.R - overRed.G).ShouldBeGreaterThan(overGreen.R - overGreen.G + 20,
            "the brush entity's water face did not sample the refraction target");
    }

    /// <summary>The world's water face over the floor, through the frame's refraction view.</summary>
    private static (int, int, int)? WorldWater((float, float, float) floor)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        target.Clear(0f, 0f, 0f);
        (int expensive, _, _) = target.DrawWaterWorld(
            [.. Quad(-5f, floor), .. Quad(0f, (1f, 1f, 1f))], [new(Floor, 0, 6), new(Water, 6, 6)], Above, Assets, Frame());

        expensive.ShouldBe(1, "the world's water did not take the expensive pass");

        return target.PixelAt(32, 32);
    }

    /// <summary>The same water face as a brush entity: the world (the floor) and its views first, then the model.</summary>
    private static (int, int, int)? BrushWater((float, float, float) floor)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(Quad(-5f, floor), [new(Floor, 0, 6)], Above, Assets, Frame());
        target.DrawModelPose(Quad(0f, (1f, 1f, 1f)), [new(Water, 0, 6)], Above.ToMatrix(), Identity, Assets, clearDepth: false);

        return target.PixelAt(32, 32);
    }

    /// <summary>The above-water plan with a refraction view, as <c>CAboveWaterView</c> makes it for refracting water.</summary>
    private static WaterDraw Frame()
    {
        WaterRenderInfo info = new(CheapWater: false, Refract: true, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: false);

        return new WaterDraw(WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), Water, 0f, [], [],
            null, null);
    }

    private static MapWater Refracting() =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$refracttexture\" \"_rt_WaterRefraction\"\n\"$refractamount\" \"0\"\n\"$abovewater\" \"1\"\n" +
            "\"$fogcolor\" \"{51 43 13}\"\n\"$fogstart\" \"-100\"\n\"$fogend\" \"400\"\n}")), null);

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
