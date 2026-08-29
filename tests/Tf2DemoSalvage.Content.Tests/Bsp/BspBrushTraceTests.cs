using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <see cref="BspLeafTree.Sweep"/> against a real map's brushes, not its node planes.
/// </summary>
/// <remarks>
/// **The hand-built cases in <c>BspSweepTests</c> cannot reach this code at all.** They build a tree
/// with no collision lumps, so they exercise the FALLBACK — stopping at the node plane that bounds a
/// solid leaf. A real map carries `LUMP_BRUSHES`, `LUMP_BRUSHSIDES` and `LUMP_LEAFBRUSHES`, and the
/// sweep then clips against brushes the way `CM_ClipBoxToBrush` does. Two different paths through
/// one method, and only a real map takes the second.
///
/// **Spawn points are the instrument, because they are open space at a known height.** A map's
/// `info_player_teamspawn` entities sit where a player stands: feet on the floor, nothing solid
/// around them. That gives a prediction needing no magic constant — straight down from a spawn, the
/// floor is immediately there; straight up, it is not.
///
/// Skipping without the map rather than failing, as the neighbouring BSP suites do: this is a real
/// file the repository does not carry.
/// </remarks>
public sealed class BspBrushTraceTests
{
    private static string Map => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

    [Test]
    public void Read_ARealMap_CarriesTheCollisionLumps()
    {
        // **Without this the whole suite measures the FALLBACK and says nothing about brushes.**
        // Found by manipulation: inverting the convex enter/leave test in ClipToBrush left every
        // case below green, which can only happen if that code never runs. A trace that silently
        // degrades to the node-plane approximation is exactly the no-op this project keeps shipping,
        // and the only guard is asserting the data arrived.
        if (!File.Exists(Map))
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree.Read(File.ReadAllBytes(Map)).HasBrushes.ShouldBeTrue(
            "a compiled map carries LUMP_BRUSHES, LUMP_BRUSHSIDES and LUMP_LEAFBRUSHES; without " +
            "them Sweep falls back to stopping at node planes rather than at brushes");
    }

    [Test]
    public void Sweep_DownFromASpawnPoint_StopsAlmostAtOnce()
    {
        if (Spawns() is not { Count: > 0 } spawns)
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree tree = BspLeafTree.Read(File.ReadAllBytes(Map));

        int landed = 0;

        foreach ((float X, float Y, float Z) spawn in spawns)
        {
            // A hundred units down from a point a player stands at. The floor is under his feet, so
            // a bare ray should stop in the first few units of that.
            float reached = tree.Sweep(
                spawn.X, spawn.Y, spawn.Z + 8f,
                spawn.X, spawn.Y, spawn.Z - 92f,
                halfExtent: 0f);

            // **`> 0` as well as `< 0.25`, and the first half is the one that was missing.** A
            // fraction of zero is not "the floor is right there", it is "the sweep never started" —
            // Valve's startsolid. Counting that as a landing let an implementation that reported
            // startsolid for EVERY spawn pass this test, which is how the manipulation check below
            // came back green against a deliberately broken clip.
            if (reached is > 0f and < 0.25f)
            {
                landed++;
            }
        }

        // **Most rather than all, and the reason is the map rather than the trace.** A spawn can be
        // authored a little above the floor or inside a func_brush that is not solid, so demanding
        // every one would be asserting about level design. A large majority landing is what says the
        // trace finds real floors.
        string sample = string.Join(
            ", ",
            spawns.Take(6).Select(each => tree.Sweep(
                each.X, each.Y, each.Z + 8f, each.X, each.Y, each.Z - 92f, 0f)
                .ToString("0.###", CultureInfo.InvariantCulture)));

        landed.ShouldBeGreaterThan(
            spawns.Count / 2,
            $"only {landed} of {spawns.Count} spawns had a floor within 25 units below them; " +
            $"first fractions were {sample} (0 means the sweep reported startsolid)");
    }

    [Test]
    public void Sweep_UpFromASpawnPoint_TravelsFurtherThanDown()
    {
        if (Spawns() is not { Count: > 0 } spawns)
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree tree = BspLeafTree.Read(File.ReadAllBytes(Map));

        // **The directional control, and it is what makes the test above mean something.** A trace
        // that returned a small fraction for everything would pass that one perfectly. Ceilings are
        // metres above a spawn and floors are underfoot, so up must beat down.
        (float X, float Y, float Z) spawn = spawns[0];

        float down = tree.Sweep(
            spawn.X, spawn.Y, spawn.Z + 8f, spawn.X, spawn.Y, spawn.Z - 92f, 0f);

        float up = tree.Sweep(
            spawn.X, spawn.Y, spawn.Z + 8f, spawn.X, spawn.Y, spawn.Z + 108f, 0f);

        up.ShouldBeGreaterThan(down, "a spawn has floor below it and open air above");
    }

    [Test]
    public void Sweep_WithABox_StopsNoLaterThanARay()
    {
        if (Spawns() is not { Count: > 0 } spawns)
        {
            Assert.Ignore("the map is not installed");
            return;
        }

        BspLeafTree tree = BspLeafTree.Read(File.ReadAllBytes(Map));

        // A box cannot reach further than a point along the same line: its face meets the surface
        // first. This is the property that separates a hull trace from a ray with extra steps, and
        // it holds on real brushwork rather than on a single authored plane.
        foreach ((float X, float Y, float Z) spawn in spawns.Take(8))
        {
            float ray = tree.Sweep(
                spawn.X, spawn.Y, spawn.Z + 64f, spawn.X, spawn.Y, spawn.Z - 64f, 0f);

            float box = tree.Sweep(
                spawn.X, spawn.Y, spawn.Z + 64f, spawn.X, spawn.Y, spawn.Z - 64f, 6f);

            box.ShouldBeLessThanOrEqualTo(
                ray + 0.001f, "a six-unit box meets the floor before its centre does");
        }
    }

    /// <summary>Where players stand, from the map's own entity lump.</summary>
    private static List<(float X, float Y, float Z)>? Spawns()
    {
        if (!File.Exists(Map))
        {
            return null;
        }

        List<(float X, float Y, float Z)> found = [];

        foreach (BspEntity entity in BspEntities.ReadFrom(File.ReadAllBytes(Map)))
        {
            if (!entity.ClassName.Contains("player", StringComparison.OrdinalIgnoreCase) ||
                !entity.ClassName.Contains("spawn", StringComparison.OrdinalIgnoreCase) ||
                !entity.Values.TryGetValue("origin", out string? origin))
            {
                continue;
            }

            string[] parts = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 3 &&
                float.TryParse(parts[0], CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], CultureInfo.InvariantCulture, out float z))
            {
                found.Add((x, y, z));
            }
        }

        return found;
    }
}
