using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

public sealed class HeightRangeProbe
{
    [Test]
    public void HowIsMapHeightDistributed()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tf2DemoSalvage", "maps", "cp_process_f12.bsp");

        if (!File.Exists(path))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> map = File.ReadAllBytes(path);
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(map);

        List<float> heights = [.. surfaces.SelectMany(s => s.Vertices).Select(v => v.Z)];
        heights.Sort();

        float lowest = heights[0];
        float highest = heights[^1];

        TestContext.Out.WriteLine($"HEIGHT range {lowest:F0} to {highest:F0}, span {highest - lowest:F0}");

        foreach (double q in new[] { 0.01, 0.25, 0.5, 0.75, 0.95, 0.99 })
        {
            float value = heights[(int)(q * (heights.Count - 1))];
            float depth = 1f - ((value - lowest) / (highest - lowest));

            TestContext.Out.WriteLine($"HEIGHT p{q * 100:F0} = {value:F0}  depth {depth:F3}");
        }

        // How much of the depth range does the playable part actually occupy?
        float p1 = heights[(int)(0.01 * heights.Count)];
        float p99 = heights[(int)(0.99 * heights.Count)];

        TestContext.Out.WriteLine(
            $"HEIGHT the middle 98% spans depth {(p99 - p1) / (highest - lowest):P1} of the range");

        surfaces.ShouldNotBeEmpty();
    }
}
