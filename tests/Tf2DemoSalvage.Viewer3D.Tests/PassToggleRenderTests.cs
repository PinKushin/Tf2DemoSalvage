using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// r_drawworld and r_drawentities each remove exactly their own pass.
/// </summary>
/// <remarks>
/// **The one thing a pass toggle must not do is remove the wrong pass**, and that is invisible in a
/// picture unless both are measured against each other. A toggle wired to the same flag as its
/// neighbour, or to the whole draw, still "works" when you flip it — the world goes away, and the
/// question of WHICH pass owns a surface goes unanswered while looking answered.
///
/// So the assertion is a partition: world-only and entities-only must each show something, and must
/// not show the same thing.
/// </remarks>
public sealed class PassToggleRenderTests
{
    private static MapAssets? Assets
    {
        get
        {
            string tf = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";
            string map = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

            return !Directory.Exists(tf) || !File.Exists(map)
                ? null
                : MapAssets.Load(
                    File.ReadAllBytes(map), GameArchives.Open(tf), maximumTextureSize: 256);
        }
    }

    [Test]
    public void PassToggles_WorldAndEntities_RemoveOnlyTheirOwnPass()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return;
        }

        if (Assets is not { } assets)
        {
            Assert.Ignore("the map or the game is not installed");
            return;
        }

        // **A vertex tint can only show what the texture has**, which the first version of this got
        // wrong: a blue quad over a yellowish material came out at (0,0,8) and failed the "did it
        // draw" check while drawing perfectly well. White for the world and green for the prop are
        // both carried by this material, and stay far apart.
        (List<WorldVertex> wall, WorldBatch wallBatch) = Quad(0.9f, 0, (1f, 1f, 1f));
        (List<WorldVertex> prop, WorldBatch propBatch) = Quad(0.5f, 6, (0f, 1f, 0f));

        List<WorldVertex> all = [.. wall, .. prop];

        (int Red, int Green, int Blue) Draw(bool world, bool entities)
        {
            // **Both lists are always passed, and the FLAGS are what varies.** Passing an empty
            // world list instead would not isolate the entity pass: Draw returns early when there
            // are no world batches, so the frame comes out black and the test would have measured
            // its own fixture rather than the toggle.
            target.Clear(0f, 0f, 0f);
            target.DrawWorld(
                all,
                [wallBatch],
                Identity,
                assets,
                props: [propBatch],
                drawWorld: world,
                drawEntities: entities);

            return target.PixelAt(32, 32);
        }

        (int Red, int Green, int Blue) worldOnly = Draw(world: true, entities: false);
        (int Red, int Green, int Blue) entitiesOnly = Draw(world: false, entities: true);
        (int Red, int Green, int Blue) neither = Draw(world: false, entities: false);

        TestContext.Out.WriteLine(
            $"PASSES world {worldOnly} / entities {entitiesOnly} / neither {neither}");

        // Each pass draws something on its own...
        (worldOnly.Red + worldOnly.Green + worldOnly.Blue).ShouldBeGreaterThan(
            10, "r_drawworld 1 with no entities should still show the map");

        (entitiesOnly.Red + entitiesOnly.Green + entitiesOnly.Blue).ShouldBeGreaterThan(
            10, "r_drawentities 1 with no world should still show the props");

        // ...and they are not the same something, which is what a mis-wired toggle would produce.
        worldOnly.ShouldNotBe(
            entitiesOnly, "the two toggles are removing the same pass rather than their own");

        // The control: with both off the frame is the clear colour, so the assertions above
        // measured drawing rather than a background that happened to be lit.
        (neither.Red + neither.Green + neither.Blue).ShouldBeLessThan(
            10, "with both passes off nothing should be drawn");
    }

    private static (List<WorldVertex> Vertices, WorldBatch Batch) Quad(
        float depth, int firstVertex, (float Red, float Green, float Blue) colour)
    {
        (float r, float g, float b) = colour;

        List<WorldVertex> vertices =
        [
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
            new(1f, -1f, depth, 1f, 0f, 0f, 0f, 0f, r, g, b),
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, r, g, b),
            new(-1f, 1f, depth, 0f, 1f, 0f, 0f, 0f, r, g, b),
            new(1f, 1f, depth, 1f, 1f, 0f, 0f, 0f, r, g, b),
        ];

        return (vertices, new WorldBatch(0, firstVertex, vertices.Count));
    }

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
