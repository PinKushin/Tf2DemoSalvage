using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Bsp;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Renders the real map offscreen and leaves the pictures behind.
/// </summary>
/// <remarks>
/// **The instrument this project spent a whole session without.** Every rendering defect here -
/// terrain culled, props unlit, foliage drawn opaque, a constant buffer bound to one stage - was
/// invisible to the test suite and visible only to a person looking at the window. That made the
/// owner the only oscilloscope in the room, and every hypothesis cost a build, a launch and a
/// screenshot.
///
/// These write PNGs into the test output folder. The assertions are deliberately weak - a map
/// should not be blank, and cutting should remove SOMETHING - because the value is the picture,
/// which a person can read in a second and no assertion can summarise.
/// </remarks>
public sealed class MapPictureTests
{
    private const int Width = 640;
    private const int Height = 360;

    private static string? MapPath
    {
        get
        {
            string map = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            return File.Exists(map) ? map : null;
        }
    }

    /// <summary>Where the pictures go, beside the test binaries.</summary>
    private static string Pictures => Path.Combine(TestContext.CurrentContext.TestDirectory, "pictures");

    [Test]
    public void DrawTheMapAtSeveralHeights()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        if (MapPath is not { } path || !Directory.Exists(tf))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        using OffscreenTarget? target = OffscreenTarget.TryCreate(Width, Height);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        MapOutline outline = MapOutline.FromFaces(BspGeometry.Read(map).Faces);
        MapAssets assets = MapAssets.Load(map, GameArchives.Open(tf), maximumTextureSize: 512);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        TopDownCamera camera = TopDownCamera.Fit(
            [
                (outline.MainBounds.MinX, outline.MainBounds.MinY),
                (outline.MainBounds.MaxX, outline.MainBounds.MaxY),
            ],
            Width,
            Height);

        BspTerrain terrain = BspTerrain.Create(map);

        MapWorld world = MapWorldBuilder.Build(
            terrain, surfaces, assets.Materials, assets.Lightmaps,
            assets.Props, camera, outline.MainBounds);

        // **A second world, because the category colours are baked into the vertices.** Flipping
        // only the shader flag draws the LIGHTING as colour, which is white for brushwork - a first
        // version of this test did exactly that and produced a blank white map. The picture said so
        // immediately; no assertion would have.
        MapWorld categorised = MapWorldBuilder.Build(
            terrain, surfaces, assets.Materials, assets.Lightmaps,
            assets.Props, camera, outline.MainBounds, categoryColours: true);

        Dictionary<string, int> lit = [];

        foreach ((string name, float cut, bool colours) in new[]
        {
            ("map", 0f, false),
            ("map-cut-30", 0.30f, false),
            ("map-cut-60", 0.60f, false),
            ("map-categories", 0f, true),
        })
        {
            MapWorld drawn = colours ? categorised : world;

            target.Clear(0.06f, 0.07f, 0.09f);
            target.DrawWorld(drawn.Vertices, drawn.Batches, camera.ToMatrix(), assets, colours, cut);

            string file = Path.Combine(Pictures, name + ".png");

            target.SavePng(file);

            lit[name] = CountLit(target);

            TestContext.Out.WriteLine($"PICTURE {file} — {lit[name]} lit pixels of {Width * Height}");
        }

        // **Weak on purpose.** The picture is the deliverable; these only catch the cases where
        // there is nothing to look at, or where a control that should change the image does not.
        lit["map"].ShouldBeGreaterThan(Width * Height / 20, "the map should not be nearly blank");
        lit["map-cut-60"].ShouldBeLessThan(lit["map"], "cutting should remove something");
        lit["map-categories"].ShouldBeGreaterThan(0, "the category view should draw");
    }

    [Test]
    public void DrawTheMapWithAndWithoutItsDetailTextures()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        if (MapPath is not { } path || !Directory.Exists(tf))
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        using OffscreenTarget? target = OffscreenTarget.TryCreate(Width, Height);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        MapOutline outline = MapOutline.FromFaces(BspGeometry.Read(map).Faces);
        MapAssets assets = MapAssets.Load(map, GameArchives.Open(tf), maximumTextureSize: 512);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        int withDetail = assets.Details.Count(detail => detail is not null);

        // **The condition has to exist before the measurement means anything.** If no material on
        // this map carries a detail texture then the two pictures below are identical for a reason
        // that has nothing to do with whether the chain works, and the comparison would be a test
        // that cannot fail.
        withDetail.ShouldBeGreaterThan(0, "the map must actually use detail textures to test them");

        TestContext.Out.WriteLine($"DETAIL {withDetail} of {assets.Details.Count} materials");

        TopDownCamera camera = TopDownCamera.Fit(
            [
                (outline.MainBounds.MinX, outline.MainBounds.MinY),
                (outline.MainBounds.MaxX, outline.MainBounds.MaxY),
            ],
            Width,
            Height);

        MapWorld world = MapWorldBuilder.Build(
            BspTerrain.Create(map), surfaces, assets.Materials, assets.Lightmaps,
            assets.Props, camera, outline.MainBounds);

        byte[] on = Render(target, world, camera, assets, detail: true, "map-detail-on");
        byte[] again = Render(target, world, camera, assets, detail: true, "map-detail-on-again");
        byte[] off = Render(target, world, camera, assets, detail: false, "map-detail-off");

        // **The control, and it is not optional.** Two renders of the same scene must be identical
        // pixel for pixel, or "the picture changed" says nothing about the switch - it could be the
        // GPU, the sampler, or an upload racing a draw.
        again.ShouldBe(on, "two identical renders must produce identical pixels");

        int changed = 0;

        for (int at = 0; at < on.Length; at++)
        {
            if (on[at] != off[at])
            {
                changed++;
            }
        }

        TestContext.Out.WriteLine(
            $"DETAIL {changed} of {on.Length} colour samples differ with detail on");

        // A detail texture is a subtle multiply, so the threshold is low deliberately - the claim
        // is that it reaches the screen at all, not that it dominates the picture.
        changed.ShouldBeGreaterThan(
            on.Length / 100, "detail textures should visibly change the surfaces that use them");
    }

    private static byte[] Render(
        OffscreenTarget target,
        MapWorld world,
        TopDownCamera camera,
        MapAssets assets,
        bool detail,
        string name)
    {
        target.Clear(0.06f, 0.07f, 0.09f);
        target.DrawWorld(
            world.Vertices, world.Batches, camera.ToMatrix(), assets, false, 0f, detail);

        string file = Path.Combine(Pictures, name + ".png");

        target.SavePng(file);

        TestContext.Out.WriteLine($"PICTURE {file}");

        List<byte> pixels = [];

        for (int y = 0; y < Height; y += 2)
        {
            for (int x = 0; x < Width; x += 2)
            {
                (int red, int green, int blue) = target.PixelAt(x, y);

                pixels.Add((byte)red);
                pixels.Add((byte)green);
                pixels.Add((byte)blue);
            }
        }

        return [.. pixels];
    }

    /// <summary>How many pixels are not the background.</summary>
    private static int CountLit(OffscreenTarget target)
    {
        int count = 0;

        for (int y = 0; y < Height; y += 2)
        {
            for (int x = 0; x < Width; x += 2)
            {
                (int red, int green, int blue) = target.PixelAt(x, y);

                if (red + green + blue > 60)
                {
                    count++;
                }
            }
        }

        return count * 4;
    }
}
