using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How much of a map's HDR lighting lies above what the lightmap atlas can hold (B514).</summary>
/// <remarks>
/// Every <c>ColorRGBExp32</c> in the HDR lighting lump, as linear light (<c>c · 2^e / 255</c>), binned by its
/// brightest channel. The atlas stores <c>linear / 2</c> in a byte, so anything above 2.0 is clipped; TF2 under
/// <c>HDR_TYPE_INTEGER</c> multiplies its lightmap by <c>LIGHT_MAP_SCALE</c> = 16 (<c>shaderapidx9.dll</c>
/// <c>0x18001be90</c>), so it holds up to 16. A measurement, not a test (D38).
/// </remarks>
public sealed class LightmapRangeProbe : IProbe
{
    /// <summary><c>LUMP_LIGHTING_HDR</c> (bspfile.h), whose enum is internal to Content.</summary>
    private const int LightingHdrLump = 53;

    /// <inheritdoc/>
    public string Name => "lightmap-range";

    /// <inheritdoc/>
    public string Summary => "share of a map's HDR luxels above 1, 2, 4 and 8 in linear light (B514): lightmap-range <map.bsp>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] file = File.ReadAllBytes(arguments[0]);
        ReadOnlySpan<byte> lump = BspLumpData.Read(file, BspHeader.Parse(file).Lump(LightingHdrLump)).Span;
        float[] edges = [1f, 2f, 4f, 8f, 16f];
        long[] above = new long[edges.Length];
        long count = lump.Length / 4;
        double sumAll = 0;
        double sumClipped = 0;

        for (int i = 0; i + 4 <= lump.Length; i += 4)
        {
            float scale = MathF.Pow(2f, (sbyte)lump[i + 3]) / 255f;
            float peak = Math.Max(lump[i], Math.Max(lump[i + 1], lump[i + 2])) * scale;

            for (int e = 0; e < edges.Length; e++)
            {
                above[e] += peak > edges[e] ? 1 : 0;
            }

            for (int c = 0; c < 3; c++)
            {
                float v = lump[i + c] * scale;
                sumAll += v;
                sumClipped += Math.Min(v, 2f);
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{count} luxels; mean channel {sumAll / (count * 3):0.000}, clipped at 2: {sumClipped / (count * 3):0.000}"));

        for (int e = 0; e < edges.Length; e++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  brightest channel > {edges[e],2}: {100.0 * above[e] / count:0.00}%"));
        }
    }
}
