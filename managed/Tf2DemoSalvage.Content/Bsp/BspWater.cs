using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One water volume: <c>dleafwaterdata_t</c>, <c>public/bspfile.h:936</c>.</summary>
/// <param name="SurfaceZ">The water's surface height.</param>
/// <param name="MinZ">The volume's floor.</param>
/// <param name="SurfaceTexinfo">The texinfo of the surface, whose material is the water's.</param>
public readonly record struct BspWaterVolume(float SurfaceZ, float MinZ, int SurfaceTexinfo);

/// <summary>The map's water volumes and each leaf's distance to the nearest of them.</summary>
/// <param name="Volumes">Indexed by a leaf's <c>leafWaterDataID</c>.</param>
/// <param name="LeafDistances">
/// <c>LUMP_LEAFMINDISTTOWATER</c>, one per leaf — the <c>m_flDistanceToWater</c> <c>R_GetVisibleFogVolume</c> reports
/// for the eye's leaf (engine.dll <c>0x1800e031f</c>). Empty when the map carries none, and the engine then reports 0.
/// </param>
public sealed record BspWater(IReadOnlyList<BspWaterVolume> Volumes, IReadOnlyList<ushort> LeafDistances)
{
    /// <summary><c>sizeof( dleafwaterdata_t )</c>: two floats and a short, padded to 12 — and the engine's stride.</summary>
    private const int Stride = 12;

    /// <summary>The distance the engine reports for a leaf.</summary>
    /// <param name="leaf">The leaf index.</param>
    /// <returns>The distance, 0 when the map carries no table or the leaf is out of it.</returns>
    public float DistanceToWater(int leaf) =>
        leaf >= 0 && leaf < LeafDistances.Count ? LeafDistances[leaf] : 0f;

    /// <summary>Reads both lumps.</summary>
    /// <param name="file">The whole BSP.</param>
    /// <returns>The water, empty for a dry map.</returns>
    public static BspWater Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);
        ReadOnlySpan<byte> volumes = BspLumpData.Read(file, header.Lump(BspLumpIndex.LeafWaterData)).Span;
        ReadOnlySpan<byte> distances = BspLumpData.Read(file, header.Lump(BspLumpIndex.LeafMinDistToWater)).Span;

        List<BspWaterVolume> read = new(volumes.Length / Stride);

        for (int at = 0; at + Stride <= volumes.Length; at += Stride)
        {
            read.Add(new(
                BinaryPrimitives.ReadSingleLittleEndian(volumes[at..]),
                BinaryPrimitives.ReadSingleLittleEndian(volumes[(at + 4)..]),
                BinaryPrimitives.ReadInt16LittleEndian(volumes[(at + 8)..])));
        }

        ushort[] perLeaf = new ushort[distances.Length / 2];

        for (int leaf = 0; leaf < perLeaf.Length; leaf++)
        {
            perLeaf[leaf] = BinaryPrimitives.ReadUInt16LittleEndian(distances[(leaf * 2)..]);
        }

        return new(read, perLeaf);
    }
}
