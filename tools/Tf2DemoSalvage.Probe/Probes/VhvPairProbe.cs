using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A map's HDR static prop vertex lighting against its LDR, placement by placement.</summary>
/// <remarks>
/// **Written to test a claim in `StudioVertexLighting.PathsFor`**: that `sp_hdr_&lt;n&gt;.vhv` holds brighter values than
/// `sp_&lt;n&gt;.vhv` and wants a tonemap. The engine reads the HDR one at TF2's default HDR level when the map's flags
/// declare an HDR bake (`engine.dll` 0x1800f4760, lump 59). This reports each pair's mean colour byte, the ratio's
/// spread, and how many pairs stamp different model checksums — the engine refuses a file whose stamp disagrees.
/// </remarks>
public sealed class VhvPairProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vhv-pair";

    /// <inheritdoc/>
    public string Summary => "a map's sp_hdr_<n>.vhv against sp_<n>.vhv, mean colour and checksum: vhv-pair [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "koth_harvest_final") is not { } map)
        {
            output.WriteLine("Map not found.");
            return;
        }

        PakFile pak = PakFile.ReadFrom(File.ReadAllBytes(map));
        List<float> ratios = [];
        int pairs = 0;
        int stampsDiffer = 0;

        int placements = pak.Paths.Count(path => path.EndsWith(".vhv", StringComparison.OrdinalIgnoreCase));

        for (int index = 0; index < placements; index++)
        {
            byte[]? low = pak.ReadFile($"sp_{index}.vhv");
            byte[]? high = pak.ReadFile($"sp_hdr_{index}.vhv");

            if (low is null || high is null)
            {
                continue;
            }

            pairs++;

            int lowStamp = BinaryPrimitives.ReadInt32LittleEndian(low.AsSpan(4));
            int highStamp = BinaryPrimitives.ReadInt32LittleEndian(high.AsSpan(4));

            if (lowStamp != highStamp)
            {
                stampsDiffer++;
            }

            float lowMean = Mean(low, lowStamp);
            float highMean = Mean(high, highStamp);

            if (lowMean > 0.5f)
            {
                ratios.Add(highMean / lowMean);
            }
        }

        ratios.Sort();

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(map)}: {pairs} pairs, {stampsDiffer} stamp different checksums"));

        if (ratios.Count > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  HDR/LDR mean colour: p10 {ratios[ratios.Count / 10]:0.000}, median {ratios[ratios.Count / 2]:0.000}, p90 {ratios[ratios.Count * 9 / 10]:0.000} over {ratios.Count}"));
        }
    }

    /// <summary>The mean colour byte over every vertex of every mesh, read as the viewer reads it.</summary>
    private static float Mean(byte[] file, int stamp)
    {
        IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>> meshes = StudioVertexLighting.Read(file, stamp);
        List<(byte Red, byte Green, byte Blue)> all = [.. meshes.SelectMany(mesh => mesh)];

        return all.Count > 0 ? (float)all.Average(colour => (colour.Red + colour.Green + colour.Blue) / 3.0) : 0f;
    }
}
