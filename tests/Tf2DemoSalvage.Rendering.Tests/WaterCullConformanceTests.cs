using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The <c>Water</c> shader's back-face cull on a water material that lands on its <c>Draw()</c> fallback (B657 review).
/// </summary>
/// <remarks>
/// **The fallback still runs under the shader's shadow state.** <c>SHADER_DRAW</c>'s last branch is a bare
/// <c>Draw()</c> when neither pass applies (<c>water.cpp:579-584</c>, "draw something so that we won't go into
/// wireframe-land"), and the cull the shadow state leaves on (off only in the editor, <c>water.cpp:393-397</c>) is part
/// of that state. So a water surface's downward face (<c>$bottommaterial</c>, <c>vbsp/faces.cpp:1255-1289</c>) is culled
/// from above on this path too.
///
/// **The subject is hand-built** (D38): two <c>Water</c> materials with no <c>$refracttexture</c>, no reflection and no
/// cubemap, so <see cref="WaterShader.Pass"/> answers <see cref="WaterPass.Plain"/> for both, each with a flat grey base
/// so the two faces draw different pixels. Expected, Valve's arithmetic: from above, the pixel with both coplanar faces
/// drawn, the downward one first, is exactly the upward one's alone.
/// </remarks>
public sealed class WaterCullConformanceTests
{
    private const int Top = 0;
    private const int Bottom = 1;

    private static readonly MapAssets Assets = MapAssets.ForMaterials(
        [ModulateToneMapConformanceTests.Grey(200), ModulateToneMapConformanceTests.Grey(40)],
        ["Water", "Water"],
        [Plain(above: true), Plain(above: false)]);

    private static readonly FreeCamera Above = new() { Origin = (0f, 0f, 200f), Angles = (89f, 0f, 0f), Aspect = 1f };

    [Test]
    public void Pass_TheSyntheticMaterials_AreThePlainFallback()
    {
        // The condition: neither pass applies, so the batch reaches the opaque loop's draw.
        foreach (MapWater? water in Assets.Waters)
        {
            WaterShader.Pass(water!.View.RefractTexture, water.View.ReflectTexture, hasEnvmap: false, water.View.ForceCheap,
                water.View.ForceExpensive, isDecal: false).ShouldBe(WaterPass.Plain);
        }
    }

    [Test]
    public void DrawWaterWorld_PlainFallbackSurfaceWithItsBottomFaceDrawnFirst_DrawsOnlyTheSurfaceFromAbove()
    {
        (int, int, int)? alone = Draw([(Top, Up())]);
        (int, int, int)? bottomUp = Draw([(Bottom, Up())]);
        (int, int, int)? both = Draw([(Bottom, Down()), (Top, Up())]);

        if (alone is not { } surface || bottomUp is not { } other || both is not { } drawn)
        {
            Assert.Ignore("no Direct3D device on this machine");
            return;
        }

        TestContext.Out.WriteLine($"PLAIN WATER FROM ABOVE: SURFACE {surface}; BOTTOM MATERIAL FACING UP {other}; BOTH {drawn}");

        // The control: the bottom material draws a different pixel wherever it faces the eye, so equality below is the cull.
        other.ShouldNotBe(surface, "the two materials draw the same pixel, so the cull cannot be seen");
        drawn.ShouldBe(surface, "the plain-fallback bottom face drew from above");
    }

    private static (int, int, int)? Draw(IReadOnlyList<(int Material, List<WorldVertex> Face)> faces)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            return null;
        }

        List<WorldVertex> vertices = [];
        List<WorldBatch> batches = [];

        foreach ((int material, List<WorldVertex> face) in faces)
        {
            batches.Add(new(material, vertices.Count, face.Count));
            vertices.AddRange(face);
        }

        // No refraction, so the simple view: CSimpleWorldView, which draws the water surface's sort group.
        WaterRenderInfo info = new(CheapWater: true, Refract: false, Reflect: false, ReflectEntities: false,
            DrawWaterSurface: true, OpaqueWater: true);

        target.Clear(0f, 0f, 0f);
        (_, _, int plain) = target.DrawWaterWorld(vertices, batches, Above, Assets,
            new WaterDraw(WaterViews.Plan(info, new WaterFrame(false, false, 0f, false), ViewClears.Depth), Top, 0f, [], [], null, null));

        plain.ShouldBe(faces.Count, "a face did not take the Plain fallback");

        return target.PixelAt(32, 32);
    }

    private static MapWater Plain(bool above) =>
        MapWater.From(VmtMaterial.Parse(Encoding.UTF8.GetBytes(
            "\"Water\"\n{\n\"$abovewater\" " + (above ? "1" : "0") + "\n\"$fogcolor\" \"{51 43 13}\"\n}")), null);

    private static List<WorldVertex> Up()
    {
        const float half = 400f;

        return
        [
            new(-half, -half, 0f, 0f, 0f, 0f, 0f, 1f),
            new(half, half, 0f, 1f, 1f, 0f, 0f, 1f),
            new(half, -half, 0f, 1f, 0f, 0f, 0f, 1f),
            new(-half, -half, 0f, 0f, 0f, 0f, 0f, 1f),
            new(-half, half, 0f, 0f, 1f, 0f, 0f, 1f),
            new(half, half, 0f, 1f, 1f, 0f, 0f, 1f),
        ];
    }

    private static List<WorldVertex> Down()
    {
        List<WorldVertex> up = Up();
        List<WorldVertex> down = [];

        for (int at = 0; at < up.Count; at += 3)
        {
            down.Add(up[at] with { NormalZ = -1f });
            down.Add(up[at + 2] with { NormalZ = -1f });
            down.Add(up[at + 1] with { NormalZ = -1f });
        }

        return down;
    }
}
