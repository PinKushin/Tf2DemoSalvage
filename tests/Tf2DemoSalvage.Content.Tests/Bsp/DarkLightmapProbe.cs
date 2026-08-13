using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class DarkLightmapProbe
{
    [Test]
    public void CompareLightingOnDisplacementsAgainstBrushwork()
    {
        string path = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/maps/cp_process_final.bsp";

        if (!File.Exists(path))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);
        IReadOnlyList<BspLightmap> lightmaps = BspLightmaps.Read(map);

        List<double> displacement = [];
        List<double> brush = [];
        int darkDisplacements = 0;
        int darkBrushes = 0;

        foreach (BspSurface surface in surfaces)
        {
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

            if (counted == 0)
            {
                continue;
            }

            double mean = total / counted / 255d;

            if (surface.IsDisplacement)
            {
                displacement.Add(mean);

                if (mean < 0.15)
                {
                    darkDisplacements++;
                }
            }
            else
            {
                brush.Add(mean);

                if (mean < 0.15)
                {
                    darkBrushes++;
                }
            }
        }

        TestContext.Out.WriteLine(
            $"LIGHT displacements n={displacement.Count,5} mean={displacement.Average():F3} dark={darkDisplacements} ({darkDisplacements * 100.0 / Math.Max(1, displacement.Count):F1}%)");
        TestContext.Out.WriteLine(
            $"LIGHT brushwork     n={brush.Count,5} mean={brush.Average():F3} dark={darkBrushes} ({darkBrushes * 100.0 / Math.Max(1, brush.Count):F1}%)");

        surfaces.ShouldNotBeEmpty();
    }
}
