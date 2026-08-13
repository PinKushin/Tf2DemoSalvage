using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;
using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Assets;

/// <summary>
/// Prop vertex lighting against lightmap lighting, on the same map.
/// </summary>
/// <remarks>
/// **Settles a colour-space question by measurement rather than by reading.** Both come from
/// ColorRGBExp32 and both end up as bytes the shader multiplies; the lightmap path is taken through
/// a gamma curve into display space by BspLightmaps, and the .vhv path is not. If the .vhv bytes
/// are linear, props are being drawn far darker than the ground they stand on, and the ratio
/// between the two distributions is the curve.
///
/// Reports rather than asserts: there is no correct number here, only a comparison.
/// </remarks>
public sealed class PropLightingSpaceProbe
{
    [Test]
    public void ComparePropLightingWithLightmapLighting()
    {
        string map = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf/maps/cp_process_final.bsp";

        if (!File.Exists(map))
        {
            Assert.Ignore("map missing");
            return;
        }

        ReadOnlyMemory<byte> bytes = File.ReadAllBytes(map);
        PakFile pak = PakFile.ReadFrom(bytes);
        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(bytes);

        List<double> propLight = [];
        int read = 0;

        for (int index = 0; index < props.Count && read < 150; index++)
        {
            foreach (string path in StudioVertexLighting.PathsFor(index))
            {
                if (pak.ReadFile(path) is not { } file || file.Length < 40)
                {
                    continue;
                }

                // The checksum is not known here, so read past the guard by taking the stored one.
                int stamped = BitConverter.ToInt32(file, 4);

                foreach (IReadOnlyList<(byte Red, byte Green, byte Blue)> mesh in
                    StudioVertexLighting.Read(file, stamped))
                {
                    foreach ((byte red, byte green, byte blue) in mesh)
                    {
                        propLight.Add(((0.2126 * red) + (0.7152 * green) + (0.0722 * blue)) / 255d);
                    }
                }

                read++;
                break;
            }
        }

        IReadOnlyList<BspLightmap> lightmaps = BspLightmaps.Read(bytes);
        List<double> worldLight = [];

        foreach (BspLightmap lightmap in lightmaps.Take(4000))
        {
            ReadOnlySpan<byte> pixels = lightmap.Pixels.Span;

            for (int at = 0; at + 3 < pixels.Length; at += 64)
            {
                worldLight.Add(
                    ((0.2126 * pixels[at]) + (0.7152 * pixels[at + 1]) + (0.0722 * pixels[at + 2])) / 255d);
            }
        }

        TestContext.Out.WriteLine($"SPACE props   n={propLight.Count,8}  mean={Mean(propLight):F4}  median={Median(propLight):F4}");
        TestContext.Out.WriteLine($"SPACE world   n={worldLight.Count,8}  mean={Mean(worldLight):F4}  median={Median(worldLight):F4}");

        // If the prop bytes are linear and the world bytes are gamma, then applying the gamma curve
        // to the prop values should bring the two distributions together.
        List<double> corrected = [.. propLight.Select(value => Math.Pow(value, 1d / 2.2d))];

        TestContext.Out.WriteLine($"SPACE props^(1/2.2)  mean={Mean(corrected):F4}  median={Median(corrected):F4}");

        propLight.Count.ShouldBeGreaterThan(0);
    }

    private static double Mean(List<double> values) => values.Count == 0 ? 0 : values.Average();

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = [.. values.OrderBy(value => value)];

        return sorted[sorted.Count / 2];
    }
}
