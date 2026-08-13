using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Tf2DemoSalvage.Core.Diagnostics;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// The light arriving at a point from each of six directions.
/// </summary>
/// <param name="PositiveX">Light arriving from the east.</param>
/// <param name="NegativeX">Light arriving from the west.</param>
/// <param name="PositiveY">Light arriving from the north.</param>
/// <param name="NegativeY">Light arriving from the south.</param>
/// <param name="PositiveZ">Light arriving from above.</param>
/// <param name="NegativeZ">Light arriving from below.</param>
/// <remarks>
/// **Valve's <c>CompressedLightCube</c>**, six <c>ColorRGBExp32</c> samples — the same encoding as
/// a lightmap luxel, which this project already decodes.
///
/// **The order is the shader's, not a choice.** <c>VertexShaderAmbientLight</c> indexes it as
/// <c>cAmbientCube[isNegative.x]</c>, <c>[isNegative.y + 2]</c>, <c>[isNegative.z + 4]</c>, so the
/// pairs are (+X, −X), (+Y, −Y), (+Z, −Z) in that order. Storing them any other way lights a model
/// from the wrong side, which looks like a lighting bug rather than an indexing one.
/// </remarks>
public readonly record struct AmbientCube(
    (float Red, float Green, float Blue) PositiveX,
    (float Red, float Green, float Blue) NegativeX,
    (float Red, float Green, float Blue) PositiveY,
    (float Red, float Green, float Blue) NegativeY,
    (float Red, float Green, float Blue) PositiveZ,
    (float Red, float Green, float Blue) NegativeZ)
{
    /// <summary>Evaluates the cube for a surface facing a direction.</summary>
    /// <param name="normalX">World normal, east-west.</param>
    /// <param name="normalY">World normal, north-south.</param>
    /// <param name="normalZ">World normal, vertically.</param>
    /// <returns>The light reaching a surface with that normal.</returns>
    /// <remarks>
    /// **Transcribed from <c>VertexShaderAmbientLight</c>** in
    /// <c>common_vertexlitgeneric_dx9.h</c>:
    ///
    /// <code>
    /// float3 nSquared = worldNormal * worldNormal;
    /// int3 isNegative = ( worldNormal &lt; 0.0 );
    /// linearColor = nSquared.x * cAmbientCube[isNegative.x] +
    ///               nSquared.y * cAmbientCube[isNegative.y+2] +
    ///               nSquared.z * cAmbientCube[isNegative.z+4];
    /// </code>
    ///
    /// Squaring the normal is what makes the three axes sum to one for a unit normal, so a surface
    /// facing exactly along an axis takes that face's colour and nothing else.
    /// </remarks>
    public (float Red, float Green, float Blue) Light(float normalX, float normalY, float normalZ)
    {
        (float Red, float Green, float Blue) x = normalX < 0f ? NegativeX : PositiveX;
        (float Red, float Green, float Blue) y = normalY < 0f ? NegativeY : PositiveY;
        (float Red, float Green, float Blue) z = normalZ < 0f ? NegativeZ : PositiveZ;

        float squaredX = normalX * normalX;
        float squaredY = normalY * normalY;
        float squaredZ = normalZ * normalZ;

        return (
            (squaredX * x.Red) + (squaredY * y.Red) + (squaredZ * z.Red),
            (squaredX * x.Green) + (squaredY * y.Green) + (squaredZ * z.Green),
            (squaredX * x.Blue) + (squaredY * y.Blue) + (squaredZ * z.Blue));
    }
}

/// <summary>One ambient cube, and where inside its leaf it was measured.</summary>
/// <param name="Cube">The light arriving at that point.</param>
/// <param name="X">Position across the leaf, from 0 at the minimum to 1 at the maximum.</param>
/// <param name="Y">Position along the leaf.</param>
/// <param name="Z">Position up the leaf.</param>
/// <remarks>
/// **The position is a fixed-point fraction of the leaf's bounding box**, stored as a byte per
/// axis — <c>bspfile.h</c> calls it "a 0.8 fraction (mins=0,maxs=255) of the leaf's bounding box".
/// It exists because a leaf is a volume, and the light at one end of a long corridor is not the
/// light at the other.
/// </remarks>
public readonly record struct AmbientSample(AmbientCube Cube, float X, float Y, float Z);

/// <summary>Every ambient sample one leaf holds, and the box they sit in.</summary>
/// <param name="Samples">The samples, in the order the map stored them.</param>
/// <param name="Bounds">The leaf's bounding box, which the sample positions are fractions of.</param>
public readonly record struct AmbientSamples(
    IReadOnlyList<AmbientSample> Samples,
    (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Bounds)
{
    /// <summary>The sample taken closest to a world position.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The nearest sample's cube, or an unlit one when the leaf holds none.</returns>
    /// <remarks>
    /// **Nearest rather than blended, and that is a limit of the evidence rather than a shortcut.**
    /// The engine blends a leaf's samples in <c>LightcacheGetDynamic</c>, which is in the closed
    /// engine — the weighting is not in <c>source-sdk-2013</c> and cannot be transcribed. Choosing
    /// the nearest is a decision this project can defend and state; inventing a blend would be a
    /// guess wearing parity's clothes.
    ///
    /// Distances are compared squared, since only the ordering matters.
    /// </remarks>
    public AmbientCube Nearest(float x, float y, float z)
    {
        if (Samples is not { Count: > 0 })
        {
            return default;
        }

        if (Samples.Count == 1)
        {
            return Samples[0].Cube;
        }

        float width = Bounds.MaxX - Bounds.MinX;
        float depth = Bounds.MaxY - Bounds.MinY;
        float height = Bounds.MaxZ - Bounds.MinZ;

        AmbientCube best = Samples[0].Cube;
        float nearest = float.MaxValue;

        foreach (AmbientSample sample in Samples)
        {
            float dx = Bounds.MinX + (sample.X * width) - x;
            float dy = Bounds.MinY + (sample.Y * depth) - y;
            float dz = Bounds.MinZ + (sample.Z * height) - z;

            float distance = (dx * dx) + (dy * dy) + (dz * dz);

            if (distance < nearest)
            {
                nearest = distance;
                best = sample.Cube;
            }
        }

        return best;
    }
}

/// <summary>
/// The ambient light a map baked for the things that move through it.
/// </summary>
/// <remarks>
/// **This is how the engine lights anything that is not a brush.** A lightmap belongs to a surface,
/// and a model has no lightmap coordinates — so <c>vrad</c> also samples the light arriving at
/// points inside each leaf and stores a cube per sample. The client looks up the leaf a model
/// stands in and lights it from there; <c>LightingState_t</c> carries exactly this as
/// <c>m_vecAmbientCube[6]</c>.
///
/// **The lump moved, and old maps keep it inline.** <c>bspfile.h</c> notes that the ambient cube
/// was removed from <c>dleaf_t</c> for version 1 and moved to <c>LUMP_LEAF_AMBIENT_LIGHTING</c>,
/// which makes a leaf 32 bytes instead of 56. A reader that assumes one size silently misreads
/// every leaf on maps of the other era — and this project's whole purpose is the other era.
/// </remarks>
public static class BspAmbientLight
{
    /// <summary>Leaves, whose bounds place the samples.</summary>
    private const int LumpLeafs = 10;

    /// <summary>Where each leaf's samples start, and how many it has.</summary>
    private const int LumpLeafAmbientIndex = 52;

    /// <summary>The samples themselves.</summary>
    private const int LumpLeafAmbientLighting = 56;

    /// <summary>A leaf with the cube moved out, as every modern map has it.</summary>
    private const int LeafStride = 32;

    /// <summary>A leaf with the cube still inline, as maps before version 1 have it.</summary>
    private const int LeafStrideWithCube = 56;

    private const int AmbientSampleStride = 28;
    private const int AmbientIndexStride = 4;

    /// <summary>Reads every ambient sample, grouped by the leaf that holds it.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>The samples per leaf, in leaf order; empty when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or a lump is malformed.</exception>
    /// <remarks>
    /// **Every sample, not the first.** A leaf holds several cubes at different points inside it —
    /// that is the whole reason the format stores a position with each one, as a fixed-point
    /// fraction of the leaf's bounding box. Keeping only the first throws away the variation the
    /// samples exist to describe, and a large leaf is exactly where that variation matters.
    ///
    /// **How the engine weights them is not published.** <c>LightcacheGetDynamic</c> lives in the
    /// closed engine, so the blend between samples cannot be transcribed. This project therefore
    /// takes the nearest sample by position, which is defensible and stated rather than a guess
    /// dressed as parity — see <see cref="AmbientSamples.Nearest"/>.
    /// </remarks>
    public static IReadOnlyList<AmbientSamples> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> samples = BspLumpData
            .Read(file, header.Lump(LumpLeafAmbientLighting)).Span;

        ReadOnlySpan<byte> indices = BspLumpData
            .Read(file, header.Lump(LumpLeafAmbientIndex)).Span;

        if (samples.IsEmpty)
        {
            // **Named, because an unlit map and an unread lump look the same on screen.** Every
            // model drawn at full brightness is the symptom of both, and only this line separates
            // them.
            DecodeLog.Lost(
                "assets",
                "the map carries no leaf ambient lighting, so models will draw unlit");

            return [];
        }

        ReadOnlySpan<byte> leaves = BspLumpData.Read(file, header.Lump(LumpLeafs)).Span;
        int leafStride = header.Lump(LumpLeafs).Version >= 1 ? LeafStride : LeafStrideWithCube;

        // **No index lump means one sample per leaf, in leaf order.** Some maps carry the lighting
        // without the index; the engine treats the samples as leaf-ordered, and so does this.
        if (indices.IsEmpty)
        {
            List<AmbientSamples> direct = new(samples.Length / AmbientSampleStride);

            for (int leaf = 0; (leaf + 1) * AmbientSampleStride <= samples.Length; leaf++)
            {
                direct.Add(
                    new AmbientSamples(
                        [ReadSample(samples[(leaf * AmbientSampleStride)..])],
                        Bounds(leaves, leafStride, leaf)));
            }

            DecodeLog.Note(
                "assets",
                $"{direct.Count} leaf ambient samples, one per leaf, with no index lump");

            return direct;
        }

        int leafCount = indices.Length / AmbientIndexStride;
        List<AmbientSamples> perLeaf = new(leafCount);

        for (int leaf = 0; leaf < leafCount; leaf++)
        {
            ReadOnlySpan<byte> entry = indices[(leaf * AmbientIndexStride)..];

            int count = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            int first = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);

            AmbientSample[] taken = new AmbientSample[Math.Max(0, count)];

            for (int index = 0; index < taken.Length; index++)
            {
                int offset = (first + index) * AmbientSampleStride;

                if (offset + AmbientSampleStride > samples.Length)
                {
                    taken = taken[..index];
                    break;
                }

                taken[index] = ReadSample(samples[offset..]);
            }

            // A leaf with no samples is solid or outside the map, and takes no light. Kept in the
            // list so it stays indexed by leaf.
            perLeaf.Add(new AmbientSamples(taken, Bounds(leaves, leafStride, leaf)));
        }

        int total = perLeaf.Sum(leaf => leaf.Samples.Count);
        int lit = perLeaf.Count(leaf => leaf.Samples.Count > 0);

        DecodeLog.Note(
            "assets",
            $"{total} ambient samples across {lit} of {perLeaf.Count} leaves");

        return perLeaf;
    }

    /// <summary>A leaf's bounding box, which is what the sample positions are a fraction of.</summary>
    /// <remarks>
    /// <c>dleaf_t</c> stores mins and maxs as three shorts each, at byte 8 and byte 14 - after
    /// contents, cluster and the area/flags bitfield. They are the same in both leaf shapes, since
    /// the ambient cube that was removed sat at the end.
    /// </remarks>
    private static (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) Bounds(
        ReadOnlySpan<byte> leaves, int stride, int leaf)
    {
        int at = leaf * stride;

        if (at + stride > leaves.Length)
        {
            return default;
        }

        return (
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 8)..]),
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 10)..]),
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 12)..]),
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 14)..]),
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 16)..]),
            BinaryPrimitives.ReadInt16LittleEndian(leaves[(at + 18)..]));
    }

    /// <summary>One sample: a cube and where in the leaf it was taken.</summary>
    private static AmbientSample ReadSample(ReadOnlySpan<byte> sample) =>
        new(
            ReadCube(sample),
            sample[24] / 255f,
            sample[25] / 255f,
            sample[26] / 255f);

    /// <summary>How many bytes a leaf occupies in this map.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>32 when the ambient cube was moved out, 56 when it is still inline.</returns>
    /// <exception cref="InvalidDataException">The header or the leaf lump is malformed.</exception>
    /// <remarks>
    /// **Decided by the lump's own version, which is what states it.** <c>bspfile.h</c> says the
    /// cube was removed from <c>dleaf_t</c> "for version 1", so version 0 leaves are the larger
    /// shape. Guessing from the lump length instead would work until a map's leaf count happened to
    /// divide both ways.
    /// </remarks>
    public static int LeafSize(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return header.Lump(LumpLeafs).Version >= 1 ? LeafStride : LeafStrideWithCube;
    }

    /// <summary>Reads a <c>CompressedLightCube</c>: six ColorRGBExp32 in shader order.</summary>
    private static AmbientCube ReadCube(ReadOnlySpan<byte> sample) =>
        new(
            Colour(sample),
            Colour(sample[4..]),
            Colour(sample[8..]),
            Colour(sample[12..]),
            Colour(sample[16..]),
            Colour(sample[20..]));

    /// <summary>One <c>ColorRGBExp32</c>, in linear light.</summary>
    /// <remarks>
    /// **The exponent is signed and routinely negative**, so the stored bytes are not a colour:
    /// a sample is <c>channel * 2^exponent</c>. Reading them directly gives light at full
    /// brightness everywhere, which is a picture rather than an error — the same trap the lightmap
    /// reader documents, and the same one that made these models white in the first place.
    ///
    /// **Taken into display space, exactly as the lightmap is.** This is multiplied against a
    /// texture in the same shader slot the lightmap occupies, and <c>BspLightmaps</c> puts its
    /// samples through <c>SourceGamma</c> before upload — so a cube left in linear light is being
    /// compared against display-space values and comes out far too dark.
    ///
    /// Measured: the first version left it linear, on the reasoning that both arrived "the same
    /// way". They do not, and a medkit in daylight rendered nearly black.
    /// </remarks>
    private static (float Red, float Green, float Blue) Colour(ReadOnlySpan<byte> sample)
    {
        float scale = MathF.Pow(2f, (sbyte)sample[3]);

        return (
            SourceGamma.ToDisplay(sample[0] * scale / 255f),
            SourceGamma.ToDisplay(sample[1] * scale / 255f),
            SourceGamma.ToDisplay(sample[2] * scale / 255f));
    }
}
