using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Slicing the map by height, drawn offscreen and measured in pixels.
/// </summary>
/// <remarks>
/// **This project invented the height cut, so no source settles it — only a picture does.** That is
/// exactly why it went three rounds of "it does not work" with no way to tell whether the key
/// arrived, the constant reached the shader, or the depth values were wrong. Rendering offscreen
/// turns "look at the pixels" into something a test can do.
/// </remarks>
public sealed class HeightCutTests
{
    /// <summary>
    /// Real map assets, because the shader clips on texture alpha.
    /// </summary>
    /// <remarks>
    /// **Found by this test failing in a way that was itself informative.** With no textures
    /// uploaded, the albedo sample returns alpha zero and the alpha-test clip discards every
    /// fragment - so a first version of these tests drew nothing at all, and the one asserting the
    /// cut REMOVED something passed for the wrong reason. Exactly the "a test that cannot fail"
    /// case, caught only because the control drew nothing either.
    /// </remarks>
    private static MapAssets? Assets
    {
        get
        {
            string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
            string map = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            if (!System.IO.Directory.Exists(tf) || !System.IO.File.Exists(map))
            {
                return null;
            }

            return MapAssets.Load(
                System.IO.File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    /// <summary>A quad at a given depth, filling the whole view.</summary>
    private static (List<WorldVertex> Vertices, List<WorldBatch> Batches) Quad(float depth)
    {
        List<WorldVertex> vertices =
        [
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f),
            new(1f, -1f, depth, 1f, 0f, 0f, 0f, 0f),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f),
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f),
            new(-1f, 1f, depth, 0f, 1f, 0f, 0f, 0f),
        ];

        return (vertices, [new WorldBatch(0, 0, vertices.Count)]);
    }

    /// <summary>The identity camera, so world coordinates are already clip coordinates.</summary>
    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    [Test]
    public void ACutBelowTheSurface_LeavesItDrawn()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (List<WorldVertex> vertices, List<WorldBatch> batches) = Quad(0.8f);

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, batches, Identity, assets, heightCut: 0.5f);

        // The surface sits at depth 0.8 and the cut is at 0.5, so it survives - the cut removes
        // what is ABOVE it, and greater depth is lower.
        (int red, int green, int blue) = target.PixelAt(32, 32);

        (red + green + blue).ShouldBeGreaterThan(0, "a surface below the cut must still draw");
    }

    [Test]
    public void ACutAboveTheSurface_RemovesIt()
    {
        // **The measurement that matters.** Nothing else in this test file would fail if the cut
        // did nothing at all.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (List<WorldVertex> vertices, List<WorldBatch> batches) = Quad(0.2f);

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, batches, Identity, assets, heightCut: 0.5f);

        (int red, int green, int blue) = target.PixelAt(32, 32);

        (red + green + blue).ShouldBe(0, "a surface above the cut must be discarded");
    }

    [Test]
    public void NoCut_DrawsEverything()
    {
        // The control: without it, "the cut works" and "nothing ever draws" are the same result.
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        (List<WorldVertex> vertices, List<WorldBatch> batches) = Quad(0.2f);

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(vertices, batches, Identity, assets, heightCut: 0f);

        (int red, int green, int blue) = target.PixelAt(32, 32);

        (red + green + blue).ShouldBeGreaterThan(0, "with no cut, everything draws");
    }
}
