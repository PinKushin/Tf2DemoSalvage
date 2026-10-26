using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The constant ring's windows (B597): D3D11.1's offset rules, and never a window the GPU may still read.</summary>
public sealed class ConstantRingTests
{
    /// <summary>A window is a multiple of 16 constants, so 256 bytes: `VSSetConstantBuffers1`'s rule for its count.</summary>
    [TestCase(1, 16)]
    [TestCase(16, 16)]
    [TestCase(17, 32)]
    [TestCase(34, 48)]
    [TestCase(384, 384)]
    [TestCase(4096, 4096)]
    public void WindowFor_AConstantCount_RoundsUpToSixteen(int constants, int window) =>
        ConstantRingCursor.WindowFor(constants).ShouldBe(window);

    /// <summary>Nothing, or more than 4096 constants, is no window at all.</summary>
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(4097)]
    public void WindowFor_OutsideOneToFourThousandNinetySix_Throws(int constants) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ConstantRingCursor.WindowFor(constants));

    /// <summary>A ring that is not whole windows cannot hand out aligned ones.</summary>
    [TestCase(0)]
    [TestCase(8)]
    [TestCase(40)]
    public void Constructor_ACapacityNotAMultipleOfSixteen_Throws(int capacity) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new ConstantRingCursor(capacity));

    /// <summary>The first map of a ring discards (its memory is undefined until then); the next ones do not.</summary>
    [Test]
    public void Reserve_FirstAndThen_DiscardsOnlyTheFirst()
    {
        ConstantRingCursor cursor = new(256);

        cursor.Reserve(34).ShouldBe((0, 48, true));
        cursor.Reserve(384 / 4).ShouldBe((48, 96, false));
        cursor.Reserve(1).ShouldBe((144, 16, false));
    }

    /// <summary>The reservation that would pass the end starts at zero again and discards; nothing is written over.</summary>
    [Test]
    public void Reserve_PastTheEnd_StartsAtZeroAndDiscards()
    {
        ConstantRingCursor cursor = new(64);

        cursor.Reserve(32).ShouldBe((0, 32, true));
        cursor.Reserve(16).ShouldBe((32, 16, false));
        cursor.Reserve(32).ShouldBe((0, 32, true), "16 left, 32 asked: the ring starts again");
        cursor.Reserve(32).ShouldBe((32, 32, false), "exactly to the end is not past it");
        cursor.Reserve(1).ShouldBe((0, 16, true));
    }

    /// <summary>A window larger than the whole ring can never fit, so it is refused rather than wrapped forever.</summary>
    [Test]
    public void Reserve_LargerThanTheRing_Throws()
    {
        ConstantRingCursor cursor = new(32);

        Should.Throw<ArgumentOutOfRangeException>(() => cursor.Reserve(33));
    }

    /// <summary>
    /// The NO_OVERWRITE discipline over a long mixed sequence: between two discards no two windows overlap, every window
    /// is aligned and inside the ring.
    /// </summary>
    [Test]
    public void Reserve_AMixedSequence_NeverOverlapsBetweenDiscards()
    {
        ConstantRingCursor cursor = new(1024);
        List<(int First, int Count)> epoch = [];
        int discards = 0;

        for (int i = 0; i < 5000; i++)
        {
            // Sizes 1 to 400 in a scrambled order (97 is prime to 400), so windows of every rounding mix.
            (int first, int count, bool discard) = cursor.Reserve(1 + (i * 97 % 400));

            if (discard)
            {
                epoch.Clear();
                discards++;
            }

            (first % ConstantRingCursor.Alignment).ShouldBe(0);
            (count % ConstantRingCursor.Alignment).ShouldBe(0);
            (first + count).ShouldBeLessThanOrEqualTo(1024);

            foreach ((int otherFirst, int otherCount) in epoch)
            {
                bool apart = first >= otherFirst + otherCount || otherFirst >= first + count;

                apart.ShouldBeTrue($"window {i} at {first}+{count} overlaps {otherFirst}+{otherCount} since the last discard");
            }

            epoch.Add((first, count));
        }

        discards.ShouldBeGreaterThan(100, "the control: the sequence wrapped many times");
    }

    /// <summary>
    /// The device side across every ring's wrap: a skinned, lamp-lit quad drawn alternately in view and out of it, read
    /// back after each draw. A slot bound at a stale offset, or into a renamed buffer, reads the OTHER draw's bones or
    /// lamp, or nothing — the in-view pixel changes or the out-of-view one lights.
    /// </summary>
    /// <remarks>
    /// **Blind to a missing discard, by construction**: each read-back waits for the GPU, so no window is ever still in
    /// flight when it is written again. Sabotaged (wrap without discard), this stayed green and the cursor tests above went
    /// red; they are what hold the NO_OVERWRITE discipline. Sabotaged with every vertex binding at offset zero, this went
    /// red at its control.
    /// </remarks>
    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void DrawModelPose_AcrossEveryRingsWrap_DrawsEachPoseAsTheFirstDid()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (GameInstall.Root is not { } tf ||
            !File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp")))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        MapAssets assets = MapCache.With().Assets;

        float[] inView = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        float[] away = [1f, 0f, 0f, 100000f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        LocalLight[] lamp = [new LocalLight(0f, -150f, 0f, 0.3f, 0f, 0f, 0f, 0f, 1f / (150f * 150f), 1000f)];

        // A grey static light from a vertex stream, not a constant, so the quad shows wherever it is drawn: a pose
        // read from the wrong window puts it in view (or out of it) whatever the lamp says.
        float[] dim = [.. System.Linq.Enumerable.Repeat(0.1f, 18)];

        (int Red, int Green, int Blue) Draw(bool seen, bool lamped)
        {
            target.Clear(0f, 0f, 0f);
            target.DrawModelPose(
                Face(), [new WorldBatch(0, 0, 6)], Camera(), Identity(), assets,
                light: default(AmbientCube), bothSides: true, debug: new DebugModes(DrawFlat: true),
                bakedColours: dim, locals: lamped ? lamp : null, bones: [seen ? inView : away]);

            return target.PixelAt(32, 32);
        }

        (int Red, int Green, int Blue) lit = Draw(seen: true, lamped: true);

        target.UsesConstantRing.ShouldBeTrue("the control: this device writes through the rings");
        Draw(seen: false, lamped: false).ShouldBe((0, 0, 0), "the control: the quad away from the camera leaves the pixel black");
        Draw(seen: true, lamped: false).ShouldNotBe((0, 0, 0), "the control: the stream lights the quad without the lamp");
        Draw(seen: true, lamped: false).ShouldNotBe(lit, "the control: the lamp changes the pixel");

        // Each draw takes one window from each ring: 384 constants of bones, 48 of model and 32 of material. Past the
        // material ring's 2048th window, every ring has started again at least once.
        int draws = (WorldRenderer.RingConstants / 32) + 64;

        for (int i = 2; i < draws; i++)
        {
            bool seen = i % 2 == 0;

            Draw(seen, lamped: seen).ShouldBe(seen ? lit : (0, 0, 0), $"draw {i}");
        }
    }

    /// <summary>
    /// **Byte-identical frames, rings against discard-per-draw**: one scene of a real map's materials — 64 world batches,
    /// then 48 posed models skinned and every other one lamp-lit, through their materials' proxies — drawn by two renderers that differ only in
    /// how the constants reach the GPU, hashed over every pixel.
    /// </summary>
    [Test]
    [Category(TestCategories.MutationHeavy)]
    public void Draw_AWorldAndModelsThroughTheRings_IsByteIdenticalToDiscardPerDraw()
    {
        using OffscreenTarget? ring = OffscreenTarget.TryCreate(128, 128);
        using OffscreenTarget? discard = OffscreenTarget.TryCreate(128, 128);

        if (ring is null || discard is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (MapCache.TryLoad("cp_process_f12", 256) is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        discard.WithoutConstantRing();

        long Scene(OffscreenTarget target, float lampRed = 0.3f)
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

            // Small skinned quads across the view, every other one lamp-lit, each its own material.
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

        long viaRing = Scene(ring);
        long again = Scene(ring);
        long viaDiscard = Scene(discard);
        long discardAgain = Scene(discard);

        TestContext.Out.WriteLine($"ring {viaRing:x16} {again:x16} discard {viaDiscard:x16} {discardAgain:x16}");

        ring.UsesConstantRing.ShouldBeTrue("the control: one renderer writes through the rings");
        discard.UsesConstantRing.ShouldBeFalse("the control: the other discards per draw");
        Scene(ring, lampRed: 0.31f).ShouldNotBe(again, "the control: the hash sees one lamp a hundredth brighter");

        // **The first scene differs from the second in BOTH renderers** (measured: the same two hashes each), so the
        // sequence is compared, not one frame against a repeat: state carried from one draw to the next — the model
        // path's frame chosen before its proxies run is the candidate (B343's order) — is the same either way.
        again.ShouldNotBe(viaRing, "the control: the first scene is not the second, in this renderer too");
        (viaRing, again).ShouldBe((viaDiscard, discardAgain));
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
