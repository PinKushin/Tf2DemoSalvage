using System;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// How much decal and reflection work a real map actually contains.
/// </summary>
/// <remarks>
/// **Both remaining features are gated on data this project does not read yet**, so the first
/// question is how much of it there is. Decals arrive as overlays in lump 45, and reflections need
/// the cubemap positions in lump 42 plus the cubemap images packed into the map's own pakfile.
///
/// Sizes rather than a full parse: a lump's length divided by its structure's stride counts the
/// entries without committing to a reader, and if the division is not exact then the stride is
/// wrong and nothing built on it would be trustworthy.
/// </remarks>
public sealed class OverlayAndCubemapProbe
{
    /// <summary><c>doverlay_t</c>: id, texinfo, count, 64 faces, 4 floats, 4 vectors, 2 vectors.</summary>
    private const int OverlayStride = 352;

    /// <summary><c>dcubemapsample_t</c>: three ints and a size byte, padded.</summary>
    private const int CubemapStride = 16;

    private static string? MapFile
    {
        get
        {
            foreach (string? root in new[]
            {
                Environment.GetEnvironmentVariable("TF2_FOLDER"),
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string map = Path.Combine(root, "maps", "cp_process_final.bsp");

                if (File.Exists(map))
                {
                    return map;
                }
            }

            return null;
        }
    }

    [Test]
    public void OverlaysAndCubemaps_TheirCounts_AreReported()
    {
        if (MapFile is not { } path)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        ReadOnlyMemory<byte> bytes = File.ReadAllBytes(path);
        BspHeader header = BspHeader.Parse(bytes.Span);

        foreach ((string name, int lump, int stride) in new[]
        {
            ("overlays", 45, OverlayStride),
            ("cubemap samples", 42, CubemapStride),
        })
        {
            // **Decompressed, not the directory's length.** Every BSP lump is LZMA packed and the
            // directory never says so, so header.Lump(n).Length is the PACKED size. Dividing that
            // by a stride gives a fractional entry count that reads as a wrong stride - measured
            // here as 18.46 overlays and 16.88 cubemaps before this line existed.
            int length = BspLumpData.Read(bytes, header.Lump(lump)).Length;

            TestContext.Out.WriteLine(
                $"LUMP {name}: {length} bytes, {length / (double)stride:N2} entries at {stride} each");
        }

        // The packed cubemap images, which is what a reflection would actually sample.
        int packed = 0;
        int patched = 0;

        foreach (string file in PakFile.ReadFrom(bytes).Paths)
        {
            if (file.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase) &&
                file.Contains("/c", StringComparison.OrdinalIgnoreCase))
            {
                packed++;
            }

            if (file.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
            {
                patched++;
            }
        }

        TestContext.Out.WriteLine($"LUMP {packed} packed cubemap-shaped textures, {patched} packed materials");
    }
}
