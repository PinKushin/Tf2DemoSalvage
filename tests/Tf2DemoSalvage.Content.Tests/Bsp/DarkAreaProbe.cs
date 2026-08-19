using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Which materials cover the most ground, and how dark each one draws.
/// </summary>
/// <remarks>
/// **Area, not count.** A blob is a large dark region, so the material responsible has to cover
/// real ground - and the earlier diagnostic ranked by texture brightness alone, which surfaced a
/// 4,096 pixel tool texture at the top and told nobody anything. Weighting by the world area a
/// material actually paints is what turns a list into a suspect.
/// </remarks>
public sealed class DarkAreaProbe
{
    [Test]
    public void DarkestMaterials_WeightedByArea_AreReported()
    {
        string path = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/maps/cp_process_final.bsp";

        if (!File.Exists(path))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);
        IReadOnlyList<BspMaterial> materials = BspMaterials.Read(map);
        IReadOnlyList<BspLightmap> lightmaps = BspLightmaps.Read(map);

        Dictionary<int, double> areaByMaterial = [];
        Dictionary<int, double> lightByMaterial = [];
        Dictionary<int, int> litFaces = [];

        foreach (BspSurface surface in surfaces)
        {
            if (surface.Vertices.Count < 3)
            {
                continue;
            }

            double area = FlatArea(surface);
            areaByMaterial[surface.MaterialIndex] =
                areaByMaterial.GetValueOrDefault(surface.MaterialIndex) + area;

            if (surface.FaceIndex >= lightmaps.Count || lightmaps[surface.FaceIndex].IsEmpty)
            {
                continue;
            }

            ReadOnlySpan<byte> pixels = lightmaps[surface.FaceIndex].Pixels.Span;
            double total = 0;
            int counted = 0;

            for (int at = 0; at + 3 < pixels.Length; at += 4)
            {
                total += (0.2126 * pixels[at]) + (0.7152 * pixels[at + 1]) + (0.0722 * pixels[at + 2]);
                counted++;
            }

            if (counted > 0)
            {
                lightByMaterial[surface.MaterialIndex] =
                    lightByMaterial.GetValueOrDefault(surface.MaterialIndex) + (total / counted / 255d);
                litFaces[surface.MaterialIndex] = litFaces.GetValueOrDefault(surface.MaterialIndex) + 1;
            }
        }

        TestContext.Out.WriteLine("AREA  darkest lighting among the twenty largest materials");

        foreach ((int index, double area) in areaByMaterial.OrderByDescending(entry => entry.Value).Take(20)
            .OrderBy(entry => litFaces.GetValueOrDefault(entry.Key) == 0
                ? 9d
                : lightByMaterial.GetValueOrDefault(entry.Key) / litFaces[entry.Key]))
        {
            int faces = litFaces.GetValueOrDefault(index);
            double light = faces == 0 ? -1 : lightByMaterial.GetValueOrDefault(index) / faces;
            string name = index >= 0 && index < materials.Count ? materials[index].Name : "?";

            TestContext.Out.WriteLine(
                $"AREA  light {light,7:F3}  area {area,14:N0}  faces {faces,5}  {name}");
        }

        areaByMaterial.ShouldNotBeEmpty();
    }

    /// <summary>Rough ground-plane area of a face, which is what an overhead view shows.</summary>
    private static double FlatArea(BspSurface surface)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (SurfaceVertex vertex in surface.Vertices)
        {
            minX = Math.Min(minX, vertex.X);
            minY = Math.Min(minY, vertex.Y);
            maxX = Math.Max(maxX, vertex.X);
            maxY = Math.Max(maxY, vertex.Y);
        }

        return (maxX - minX) * (maxY - minY);
    }
}
