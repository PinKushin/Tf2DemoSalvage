using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The occlusion proxies, issued in one upload a frame (B597), leave the frame exactly as it was.</summary>
public sealed class OcclusionQueriesRenderTests
{
    /// <summary>
    /// **Byte-identical frames with and without the proxies between the world and the models**, as the viewer issues them
    /// before its viewmodels: one scene of a real map's materials — 64 world batches, then 48 skinned quads with every other
    /// one lamp-lit — hashed over every pixel. A proxy writes no colour and no depth (`$writez 0`), and every state it
    /// changes is handed back or set again by the next draw, so the models after it draw as they would with none.
    /// </summary>
    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Issue_BetweenTheWorldAndTheModels_LeavesTheFrameByteIdentical()
    {
        using OffscreenTarget? occluded = OffscreenTarget.TryCreate(128, 128);
        using OffscreenTarget? plain = OffscreenTarget.TryCreate(128, 128);

        if (occluded is null || plain is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (MapCache.TryLoad("cp_process_f12", 256) is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // 64 proxies over the view, half in front of the world (depth 0.4) and half behind it (0.6).
        List<PixelVisibilityProxy> proxies = [];

        for (int cell = 0; cell < 64; cell++)
        {
            float x = -0.875f + (cell % 8 * 0.25f);
            float y = -0.875f + (cell / 8 * 0.25f);
            float depth = cell % 2 == 0 ? 0.4f : 0.6f;

            proxies.Add(new PixelVisibilityProxy(
                cell + 1,
                new Vector3(x - 0.1f, y + 0.1f, depth),
                new Vector3(x + 0.1f, y + 0.1f, depth),
                new Vector3(x + 0.1f, y - 0.1f, depth),
                new Vector3(x - 0.1f, y - 0.1f, depth)));
        }

        IReadOnlyDictionary<int, (int Pixels, int Possible)>? counted = null;

        long Scene(OffscreenTarget target, bool occlude, float lampRed = 0.3f)
        {
            target.Seconds = 1.7;
            target.Clear(0f, 0f, 0f);

            // An 8 by 8 grid of world quads in clip space, each its own material, spread over the map's table.
            List<WorldVertex> grid = [];
            List<WorldBatch> batches = [];
            int materials = assets.Textures.Count;

            for (int cell = 0; cell < 64; cell++)
            {
                float left = -1f + (cell % 8 * 0.25f);
                float top = -1f + (cell / 8 * 0.25f);
                int first = grid.Count;

                grid.Add(new WorldVertex(left, top, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f));
                grid.Add(new WorldVertex(left + 0.25f, top + 0.25f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f));
                grid.Add(new WorldVertex(left + 0.25f, top, 0.5f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f));
                grid.Add(new WorldVertex(left, top, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f));
                grid.Add(new WorldVertex(left, top + 0.25f, 0.5f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f));
                grid.Add(new WorldVertex(left + 0.25f, top + 0.25f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f));
                batches.Add(new WorldBatch(cell * 37 % materials, first, 6));
            }

            target.DrawWorld(grid, batches, Identity(), assets, translucent: false);

            if (occlude)
            {
                counted = target.CountOcclusion(proxies, Identity());
            }

            // Small skinned quads across the view, every other one lamp-lit, each its own material, through the
            // world's depth.
            LocalLight[] lamp = [new LocalLight(0f, -150f, 0f, lampRed, 0.2f, 0.1f, 0f, 0f, 1f / (150f * 150f), 1000f)];

            for (int model = 0; model < 48; model++)
            {
                float x = -180f + (model % 8 * 50f);
                float z = -150f + (model / 8 * 50f);
                float[] bone = [0.1f, 0f, 0f, x, 0f, 1f, 0f, 0f, 0f, 0f, 0.1f, z];

                target.DrawModelPose(
                    Face(), [new WorldBatch(model * 53 % materials, 0, 6)], Camera(), Identity(), assets,
                    light: default(AmbientCube), bothSides: true, clearDepth: false,
                    locals: model % 2 == 0 ? lamp : null, bones: [bone]);
            }

            return target.ImageHash();
        }

        long withProxies = Scene(occluded, occlude: true);
        long withProxiesAgain = Scene(occluded, occlude: true);
        long without = Scene(plain, occlude: false);
        long withoutAgain = Scene(plain, occlude: false);

        // The controls: the proxies were drawn and counted — the ones in front pass, the ones behind the world do not.
        counted.ShouldNotBeNull();
        counted[1].Pixels.ShouldBeGreaterThan(0, "a proxy in front of the world passes");
        counted[2].Pixels.ShouldBe(0, "a proxy behind it does not");
        Scene(plain, occlude: false, lampRed: 0.31f).ShouldNotBe(withoutAgain, "the hash sees one lamp a hundredth brighter");

        // **The first scene differs from the second in BOTH targets** (measured: the same two hashes each; B597 files it),
        // so the two-scene sequence is compared rather than one frame against a repeat.
        withProxiesAgain.ShouldNotBe(withProxies, "the control: the first scene is not the second here either");
        (withProxies, withProxiesAgain).ShouldBe((without, withoutAgain));
    }

    private static WorldVertex[] Face() =>
    [
        Vertex(-200f, -200f),
        Vertex(200f, -200f),
        Vertex(200f, 200f),
        Vertex(-200f, -200f),
        Vertex(200f, 200f),
        Vertex(-200f, 200f),
    ];

    private static WorldVertex Vertex(float x, float z) =>
        new(x, 0f, z, 0f, 0f, 0f, 0f, 0f) { NormalY = -1f, NormalZ = 0f, WeightA = 1f };

    private static float[] Camera() =>
        new FreeCamera
        {
            Origin = (0f, -300f, 0f),
            Angles = (0f, 90f, 0f),
            Aspect = 1f,
        }.ToMatrix();

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
