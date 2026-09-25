using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How a map's HDR lightmaps compare with its LDR ones, face by face.</summary>
/// <remarks>
/// **A map compiled for both carries two lightings**: lump 8 with the faces of lump 7, and lump 53 with the faces of lump
/// 58. TF2 draws the HDR pair at its default `mat_hdr_level 2` and the LDR pair at 0. vrad bakes them from different
/// light values (`_light` against `_lightHDR`), so the two need not be a constant factor apart. This reports each face's
/// flat-set mean in linear light (`byte · 2^exp / 255`) under both, and the ratio's spread.
/// </remarks>
public sealed class LightingLumpsProbe : IProbe
{
    /// <summary>`sizeof( dface_t )`; the layout's own constants are internal to Content.</summary>
    private const int FaceStride = 56;

    /// <summary>`dface_t.lightofs`.</summary>
    private const int FaceLightOffset = 20;

    /// <summary>`dface_t.m_LightmapTextureSizeInLuxels`.</summary>
    private const int FaceLuxelSizeOffset = 36;

    /// <inheritdoc/>
    public string Name => "lighting-lumps";

    /// <inheritdoc/>
    public string Summary => "a map's HDR lightmaps against its LDR ones, face by face: lighting-lumps [map] [face]";

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

        ReadOnlyMemory<byte> file = File.ReadAllBytes(map);
        BspHeader header = BspHeader.Parse(file.Span);
        // LUMP_FACES 7, LUMP_FACES_HDR 58, LUMP_LIGHTING 8, LUMP_LIGHTING_HDR 53 (`bspfile.h`).
        ReadOnlyMemory<byte> faces = BspLumpData.Read(file, header.Lump(7));
        ReadOnlyMemory<byte> facesHdr = BspLumpData.Read(file, header.Lump(58));
        ReadOnlyMemory<byte> ldr = BspLumpData.Read(file, header.Lump(8));
        ReadOnlyMemory<byte> hdr = BspLumpData.Read(file, header.Lump(53));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(map)}: faces {faces.Length / FaceStride}, HDR faces {facesHdr.Length / FaceStride}, lighting {ldr.Length} bytes, HDR lighting {hdr.Length} bytes"));

        // The control: two lumps that report equal can be one lump read twice.
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  lighting at {header.Lump(8).Offset}, HDR lighting at {header.Lump(53).Offset}; bytes identical: {ldr.Span.SequenceEqual(hdr.Span)}"));

        if (facesHdr.Length == 0 || hdr.Length == 0 || ldr.Length == 0)
        {
            return;
        }

        int only = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : -1;
        List<float> ratios = [];
        int count = Math.Min(faces.Length, facesHdr.Length) / FaceStride;

        for (int index = 0; index < count; index++)
        {
            if (only >= 0 && index != only)
            {
                continue;
            }

            if (Mean(faces.Span.Slice(index * FaceStride, FaceStride), ldr.Span) is not { } low ||
                Mean(facesHdr.Span.Slice(index * FaceStride, FaceStride), hdr.Span) is not { } high ||
                low <= 1e-4f)
            {
                continue;
            }

            ratios.Add(high / low);

            if (only >= 0)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  face {index}: LDR mean {low:0.0000}, HDR mean {high:0.0000}, ratio {high / low:0.000}"));
            }
        }

        ratios.Sort();

        if (ratios.Count > 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {ratios.Count} lit faces; HDR/LDR ratio p10 {ratios[ratios.Count / 10]:0.000}, median {ratios[ratios.Count / 2]:0.000}, p90 {ratios[ratios.Count * 9 / 10]:0.000}"));
        }
    }

    /// <summary>A face's flat-set mean in linear light, averaged over the three channels, or null when unlit.</summary>
    private static float? Mean(ReadOnlySpan<byte> face, ReadOnlySpan<byte> lighting)
    {
        int offset = BinaryPrimitives.ReadInt32LittleEndian(face[FaceLightOffset..]);

        if (offset < 0)
        {
            return null;
        }

        int luxels = (BinaryPrimitives.ReadInt32LittleEndian(face[FaceLuxelSizeOffset..]) + 1) *
                     (BinaryPrimitives.ReadInt32LittleEndian(face[(FaceLuxelSizeOffset + 4)..]) + 1);

        if (offset + (luxels * 4) > lighting.Length)
        {
            return null;
        }

        double sum = 0d;

        for (int luxel = 0; luxel < luxels; luxel++)
        {
            ReadOnlySpan<byte> sample = lighting.Slice(offset + (luxel * 4), 4);
            float scale = MathF.Pow(2f, (sbyte)sample[3]) / 255f;

            sum += (sample[0] + sample[1] + sample[2]) * scale / 3f;
        }

        return (float)(sum / luxels);
    }
}
