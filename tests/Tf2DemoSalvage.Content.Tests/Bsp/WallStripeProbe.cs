using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// What the red and blue wall stripes in cp_process actually are.
/// </summary>
/// <remarks>
/// A probe, not a test. They draw off the walls they belong to, and three explanations have been
/// proposed and killed by measurement: a decal offset (overlay origins measure a median of 0.00
/// units from their plane), a depth bias (a bias cannot move geometry, and zero bias produced no
/// z-fighting), and entity brushwork placed without its origin (no model in the map carries a
/// non-zero origin).
///
/// So stop guessing at the mechanism and ask the map what the things ARE. The BSP names every
/// material, says which faces use them, and says which overlays use them; whichever of those the
/// stripes turn out to be decides where to look.
/// </remarks>
public sealed class WallStripeProbe
{
    [Test]
    public void WallStripes_TheirMaterials_AreReported()
    {
        string? path = null;

        foreach (string? root in new[]
        {
            Environment.GetEnvironmentVariable("TF2_FOLDER"),
            @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
        })
        {
            foreach (string name in new[] { "cp_process_f12.bsp", "cp_process_final.bsp" })
            {
                if (!string.IsNullOrWhiteSpace(root) &&
                    File.Exists(Path.Combine(root, "maps", name)))
                {
                    path ??= Path.Combine(root, "maps", name);
                }
            }
        }

        if (path is null)
        {
            Assert.Ignore("No map available; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);

        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(map);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);
        IReadOnlyList<BspOverlay> overlays = BspOverlays.Read(map);
        IReadOnlyList<BspModel> models = BspModels.Read(map);

        TestContext.Out.WriteLine(
            $"STRIPE {Path.GetFileName(path)}: {materials.Count} materials, " +
            $"{surfaces.Count} surfaces, {overlays.Count} overlays, {models.Count} models");

        // **Named by what a mapper would call them.** TF2's team-coloured trim is authored as a
        // material, so the name is the handle: anything with a team name or a stripe-like word in
        // it is a candidate, and printing the matches is cheaper than guessing which it is.
        string[] wanted = ["stripe", "team", "trim", "border", "band", "red", "blu"];

        List<int> candidates = [];

        for (int index = 0; index < materials.Count; index++)
        {
            if (wanted.Any(word =>
                materials[index].Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(index);
            }
        }

        int worldFaces = models.Count > 0 ? models[0].FaceCount : int.MaxValue;

        foreach (int material in candidates.Take(20))
        {
            int onWorld = 0;
            int onEntity = 0;

            foreach (BspSurface surface in surfaces)
            {
                if (surface.MaterialIndex != material)
                {
                    continue;
                }

                _ = surface.FaceIndex < worldFaces ? onWorld++ : onEntity++;
            }

            int asOverlay = overlays.Count(overlay => overlay.MaterialIndex == material);

            // Only report materials something actually uses, or the list is mostly noise.
            if (onWorld + onEntity + asOverlay == 0)
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"STRIPE   {materials[material].Name}: {onWorld} world faces, " +
                $"{onEntity} entity faces, {asOverlay} overlays");
        }

        // And the overlays by material, most used first — whatever the stripes are, if they are
        // overlays they will be near the top of this.
        Dictionary<int, int> byMaterial = [];

        foreach (BspOverlay overlay in overlays)
        {
            byMaterial[overlay.MaterialIndex] =
                byMaterial.TryGetValue(overlay.MaterialIndex, out int seen) ? seen + 1 : 1;
        }

        TestContext.Out.WriteLine(
            "STRIPE overlays by material: " + string.Join(
                ", ",
                byMaterial
                    .OrderByDescending(entry => entry.Value)
                    .Take(10)
                    .Select(entry =>
                        $"{entry.Value}x " +
                        $"{(entry.Key >= 0 && entry.Key < materials.Count ? materials[entry.Key].Name : "?")}")));

        // **How many faces each overlay names**, which is the difference between a sign and a
        // stripe: a sign sits on one wall, a stripe wraps a building and crosses many.
        foreach (string name in new[] { "overlays/stripe_red", "signs/redstone", "signs/sign067" })
        {
            int material = -1;

            for (int index = 0; index < materials.Count; index++)
            {
                if (string.Equals(materials[index].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    material = index;
                    break;
                }
            }

            int[] counts =
            [
                .. overlays.Where(overlay => overlay.MaterialIndex == material)
                    .Select(overlay => overlay.Faces.Count),
            ];

            if (counts.Length == 0)
            {
                continue;
            }

            TestContext.Out.WriteLine(
                $"STRIPE   {name} names {counts.Min()} to {counts.Max()} faces, " +
                $"median {counts.Order().ElementAt(counts.Length / 2)}");
        }

        Assert.Pass();
    }
}
