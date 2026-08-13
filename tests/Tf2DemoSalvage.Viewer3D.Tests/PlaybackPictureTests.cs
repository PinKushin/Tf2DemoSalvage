using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Renders a real demo's players standing on a real map.
/// </summary>
/// <remarks>
/// **The first picture in this project that shows the actual product.** Everything before it drew
/// a map; this draws a match. It is also the only check available for the thing that matters
/// most — that a player's world position lands where that player was standing — because no
/// assertion knows what a scout at mid looks like and a person does.
/// </remarks>
public sealed class PlaybackPictureTests
{
    private const int Width = 900;
    private const int Height = 520;

    private static string Pictures =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "pictures");

    private static string? DemoPath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "source", "repos", "PinKushin", "Tf2DemoSalvage", "tools", "corpus", "local");

            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*process*.dem").FirstOrDefault()
                : null;
        }
    }

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

    [Test]
    public void DrawTheMatchAtSeveralMoments()
    {
        string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

        if (DemoPath is not { } demoPath || MapPath is not { } mapPath || !Directory.Exists(tf))
        {
            Assert.Ignore("the demo, the map or the game is not installed");
            return;
        }

        using OffscreenTarget? target = OffscreenTarget.TryCreate(Width, Height);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demoPath));

        timeline.Frames.Count.ShouldBeGreaterThan(0, "the demo must carry positions");

        ReadOnlyMemory<byte> map = File.ReadAllBytes(mapPath);
        MapOutline outline = MapOutline.FromFaces(BspGeometry.Read(map).Faces);
        MapAssets assets = MapAssets.Load(map, GameArchives.Open(tf), maximumTextureSize: 512);

        TopDownCamera camera = TopDownCamera.Fit(
            [
                (outline.MainBounds.MinX, outline.MainBounds.MinY),
                (outline.MainBounds.MaxX, outline.MainBounds.MaxY),
            ],
            Width,
            Height);

        MapWorld world = MapWorldBuilder.Build(
            BspTerrain.Create(map),
            BspSurfaces.Read(map),
            assets.Materials,
            assets.Lightmaps,
            assets.Props,
            camera,
            outline.MainBounds,
            categoryColours: false,
            overlays: BspOverlays.Read(map));

        // Four moments spread through the match, so the pictures show players in different places
        // rather than one arrangement that might happen to look right.
        int[] moments =
        [
            timeline.Frames[timeline.Frames.Count / 5].Tick,
            timeline.Frames[timeline.Frames.Count * 2 / 5].Tick,
            timeline.Frames[timeline.Frames.Count * 3 / 5].Tick,
            timeline.Frames[timeline.Frames.Count * 4 / 5].Tick,
        ];

        int drawn = 0;

        for (int index = 0; index < moments.Length; index++)
        {
            IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(moments[index]);

            List<ScenePoint> points = [];

            foreach (ScenePlayer player in players)
            {
                (float x, float y) = camera.Project(player.X, player.Y);

                (float red, float green, float blue) = player.Team switch
                {
                    2 => (0.90f, 0.31f, 0.27f),
                    3 => (0.34f, 0.60f, 0.78f),
                    _ => (0.62f, 0.62f, 0.62f),
                };

                points.Add(new ScenePoint(x, y, red, green, blue));
            }

            if (index == 0)
            {
                // Diagnostic: the same points on an empty target. If they appear here and not over
                // the map, the world is covering them; if they appear in neither, the projection
                // is putting them off screen.
                target.Clear(0.06f, 0.07f, 0.09f);
                target.Draw(points);
                target.SavePng(Path.Combine(Pictures, "playback-points-only.png"));

                TestContext.Out.WriteLine(
                    $"PLAYBACK first point at {points[0].X:N3}, {points[0].Y:N3} " +
                    $"(clip space runs -1 to 1)");
            }

            target.Clear(0.06f, 0.07f, 0.09f);
            target.DrawWorld(
                world.Vertices, world.Batches, camera.ToMatrix(), assets, false, 0f, true, true,
                world.Decals);
            target.Draw(points);

            string file = Path.Combine(Pictures, $"playback-{index + 1}.png");

            target.SavePng(file);

            drawn += points.Count;

            TestContext.Out.WriteLine(
                $"PLAYBACK tick {moments[index]}: {points.Count} players, " +
                $"{points.Count(p => p.Red > 0.8f)} red, {points.Count(p => p.Blue > 0.7f)} blu -> {file}");
        }

        drawn.ShouldBeGreaterThan(0, "the pictures must actually contain players");
    }
}
