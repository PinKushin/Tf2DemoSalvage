using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>cp_process_f12's sunlight survives into the lightmaps the renderer uploads (B514).</summary>
/// <remarks>
/// **The output-level half of <see cref="HdrLightmapRangeConformanceTests"/>**: the decoded lightmaps of the parity map
/// keep the luxels above 2.0 that the byte atlas clipped. `lightmap-range` measured 8.91% of the HDR lump's luxels with
/// a channel above 2 and 0.01% above 4; a clipped decode has none above 2.
/// </remarks>
public sealed class HdrLightmapRangeOnF12Tests
{
    [Test]
    public void Read_CpProcessF12_KeepsItsSunlitLuxelsAboveTwo()
    {
        string tf = GameInstall.Require();
        string map = Path.Combine(tf, "maps", "cp_process_f12.bsp");

        if (!File.Exists(map))
        {
            Assert.Ignore("cp_process_f12 is not installed");
            return;
        }

        long luxels = 0;
        long aboveTwo = 0;

        foreach (BspLightmap lightmap in BspLightmaps.Read(File.ReadAllBytes(map)))
        {
            for (int at = 0; at + 4 <= lightmap.Pixels.Length; at += 4)
            {
                (float red, float green, float blue) = BspLightmaps.Load(lightmap.Pixels.Span.Slice(at, 4));
                luxels++;
                aboveTwo += System.MathF.Max(red, System.MathF.Max(green, blue)) > 2.05f * 255f ? 1 : 0;
            }
        }

        TestContext.Out.WriteLine($"F12 LIGHTMAPS {aboveTwo} of {luxels} flat luxels above 2.0");

        luxels.ShouldBeGreaterThan(0, "the control: the map has lighting");
        ((double)aboveTwo / luxels).ShouldBeGreaterThan(0.02);
    }
}
