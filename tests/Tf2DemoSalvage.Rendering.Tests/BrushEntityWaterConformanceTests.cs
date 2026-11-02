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
    private const int Additive = 2;
    private const int PlainWater = 3;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(200), ModulateToneMapConformanceTests.Grey(40),
            ModulateToneMapConformanceTests.Grey(200, additive: true), null],
        ["LightmappedGeneric", "Water", "UnlitGeneric", "Water"],
        [null, Refracting(), null, PlainWaterMaterial()]);

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

    /// <remarks>
    /// <c>DrawReflectionRefraction</c>'s shadow state enables no blending (<c>water.cpp:182-266</c>), and the material system
    /// applies a material's whole shadow state on bind, so the expensive pass is opaque whatever drew before it. An additive
    /// batch earlier in the same model must not leave its blend on the water.
    /// </remarks>
    [Test]
    public void DrawModelPose_BrushWaterAfterAnAdditiveBatch_DrawsTheWorldWatersPixel()
    {
        (int, int, int)? world = WorldWater((1f, 0f, 0f));
        (int, int, int)? brush = BrushWaterAfterAdditive((1f, 0f, 0f));

        if (world is not { } surface || brush is not { } entity)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"WORLD WATER {surface}; BRUSH WATER AFTER ADDITIVE {entity}");

        entity.ShouldBe(surface, "the water pass drew under the previous batch's additive blend");
    }

    /// <summary>A brush entity placed by its model matrix: the water face's world position, not its stored one, is drawn.</summary>
    [Test]
    public void DrawModelPose_BrushWaterMovedByItsMatrix_DrawsTheWorldWatersPixel()
    {
        (int, int, int)? world = WorldWater((1f, 0f, 0f));
        (int, int, int)? brush = BrushWaterMoved((1f, 0f, 0f));

        if (world is not { } surface || brush is not { } entity)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"WORLD WATER {surface}; MOVED BRUSH WATER {entity}");

        entity.ShouldBe(surface, "a brush water face placed by a non-identity matrix did not draw the world water's pixel");
    }

    /// <summary>
    /// A Water material with no pass (no refraction, reflection or cubemap) is Valve's bare <c>Draw()</c>
    /// (<c>water.cpp:579-584</c>), on a brush entity as on the world: the same untextured white.
    /// </summary>
    [Test]
    public void DrawModelPose_PlainWaterOnABrushEntity_DrawsTheWorldsPlainPixel()
    {
        (int, int, int)? world = null;
        (int, int, int)? brush = null;

        using (OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64))
        {
            if (target is not null)
            {
                target.Clear(0f, 0f, 0f);
                (_, _, int plain) = target.DrawWaterWorld(
                    [.. Quad(-5f, (1f, 0f, 0f)), .. Quad(0f, (1f, 1f, 1f))], [new(Floor, 0, 6), new(PlainWater, 6, 6)], Above, Assets,
                    Frame());

                plain.ShouldBe(1, "the world's face did not take the Plain fallback");
                world = target.PixelAt(32, 32);
            }
        }

        using (OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64))
        {
            if (target is not null)
            {
                target.Clear(0f, 0f, 0f);
                target.DrawWaterWorld(Quad(-5f, (1f, 0f, 0f)), [new(Floor, 0, 6)], Above, Assets, Frame());
                target.DrawModelPose(Quad(0f, (1f, 1f, 1f)), [new(PlainWater, 0, 6)], Above.ToMatrix(), Identity, Assets, clearDepth: false);
                brush = target.PixelAt(32, 32);
            }
        }

        if (world is not { } surface || brush is not { } entity)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"PLAIN WATER: WORLD {surface}; BRUSH ENTITY {entity}");

        entity.ShouldBe(surface, "a brush entity's Plain water drew differently from the world's");
    }

    /// <summary>The B597 vertex cap of zero suppresses a water batch too: the frame keeps the floor beneath it.</summary>
    [Test]
    public void DrawModelPose_BrushWaterUnderAZeroVertexCap_DrawsNothing()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(Quad(-5f, (1f, 0f, 0f)), [new(Floor, 0, 6)], Above, Assets, Frame());

        (int, int, int) floor = target.PixelAt(32, 32);

        target.WorldWaterDraws.ShouldBe((0, 0, 0), "the floor alone drew a water pass");

        target.ModelVertexCap = 0;
        target.DrawModelPose(Quad(0f, (1f, 1f, 1f)), [new(Water, 0, 6)], Above.ToMatrix(), Identity, Assets, clearDepth: false);

        target.PixelAt(32, 32).ShouldBe(floor, "a water batch drew under a vertex cap of zero");
        target.WorldWaterDraws.ShouldBe((0, 0, 0), "the Water passes ran under a vertex cap of zero");
    }

    /// <remarks>
    /// **NOT PARITY: what this pins is an interpolation** (RISKS B663). The engine draws a brush entity's water face
    /// inside the refraction view (DF_DRAW_ENTITITES, <c>viewrender.cpp:6033</c>) and binds the live target it is drawing
    /// into (shaderapidx9 <c>0x1800149b0</c>, no guard), which D3D9 leaves undefined. The lead picked option (b): the face
    /// samples a copy of the target as it stands at the view's first water bind. Here that is the red floor the view drew
    /// before its entities, so the face is red-dominant, where the nulled input it replaced gave black.
    /// </remarks>
    [Test]
    public void DrawWaterWorld_BrushWaterInsideTheRefractionView_SamplesTheViewsSnapshot()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);
        using OffscreenTarget? control = OffscreenTarget.TryCreate(64, 64);

        if (target is null || control is null)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        List<WorldVertex> face = Shifted(Quad(-2f, (1f, 1f, 1f)), 0.15f, 0f);
        List<WorldVertex> world = [.. Quad(-5f, (1f, 0f, 0f)), .. Quad(0f, (1f, 1f, 1f))];
        WorldBatch[] batches = [new(Floor, 0, 6), new(Water, 6, 6)];

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(world, batches, Above, Assets, Frame() with
        {
            DrawEntities = view =>
            {
                if (view.Kind == WaterViewKind.Refraction)
                {
                    target.DrawModelInCurrentView(face, [new(Water, 0, 6)], Identity);
                }
            },
        });

        control.Clear(0f, 0f, 0f);
        control.DrawWaterWorld(world, batches, Above, Assets, Frame());

        (int R, int G, int B) inside = target.WaterTargetPixel(refraction: true, 512, 512);
        (int R, int G, int B) edge = target.WaterTargetPixel(refraction: true, 40, 40);
        (int R, int G, int B) controlEdge = control.WaterTargetPixel(refraction: true, 40, 40);

        TestContext.Out.WriteLine(
            $"REFRACTION TARGET: BRUSH FACE {inside}; EDGE {edge}; CONTROL EDGE {controlEdge}; SNAPSHOTS {target.WorldViewSnapshots}");

        (inside.R - inside.G).ShouldBeGreaterThan(20, "the brush water face inside the view did not sample the view's own content");
        edge.ShouldBe(controlEdge, "the view's image changed outside the brush water face");
        target.WorldViewSnapshots.ShouldBe(1, "one water face in one view is one snapshot");
        control.WorldViewSnapshots.ShouldBe(0, "a view with no water face took a snapshot");
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

    /// <summary>The water face as a brush entity drawn after an additive batch of the same model, off to the side.</summary>
    private static (int, int, int)? BrushWaterAfterAdditive((float, float, float) floor)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        // A small additive square in view (the frame spans ±200 units at this height), well left of the sampled centre.
        List<WorldVertex> vertices = [.. Shifted(Quad(0f, (1f, 1f, 1f)), 0.05f, -150f), .. Quad(0f, (1f, 1f, 1f))];

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(Quad(-5f, floor), [new(Floor, 0, 6)], Above, Assets, Frame());
        target.DrawModelPose(vertices, [new(Additive, 0, 6), new(Water, 6, 6)], Above.ToMatrix(), Identity, Assets, clearDepth: false);

        return target.PixelAt(32, 32);
    }

    /// <summary>The water face as a brush entity whose model matrix moves it: the face is built 300 units off and moved back.</summary>
    private static (int, int, int)? BrushWaterMoved((float, float, float) floor)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        // Row-vector convention: the translation is row three. The vertices sit 1000 units along x, wholly out of the ±200
        // the frame sees, so a draw that ignored the matrix leaves the floor; the matrix puts them back.
        float[] back = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, -1000f, 0f, 0f, 1f];

        target.Clear(0f, 0f, 0f);
        target.DrawWaterWorld(Quad(-5f, floor), [new(Floor, 0, 6)], Above, Assets, Frame());
        target.DrawModelPose(Shifted(Quad(0f, (1f, 1f, 1f)), 1f, 1000f), [new(Water, 0, 6)], Above.ToMatrix(), back, Assets,
            clearDepth: false);

        return target.PixelAt(32, 32);
    }

    /// <summary>A quad scaled about its centre and moved along x.</summary>
    private static List<WorldVertex> Shifted(List<WorldVertex> quad, float scale, float x) =>
        [.. quad.ConvertAll(vertex => vertex with { X = (vertex.X * scale) + x, Y = vertex.Y * scale })];

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

    private static MapWater PlainWaterMaterial() =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$abovewater\" \"1\"\n\"$fogcolor\" \"{51 43 13}\"\n}")), null);

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
