using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

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

    /// <summary>Reads one ambient cube per leaf.</summary>
    /// <param name="file">The whole map file.</param>
    /// <returns>A cube per leaf, in leaf order; empty when the map carries none.</returns>
    /// <exception cref="InvalidDataException">The header or a lump is malformed.</exception>
    /// <remarks>
    /// **One cube per leaf, not one per sample.** A leaf can carry several samples at different
    /// points inside it, and the engine interpolates between them by position. This takes the
    /// first, which is what a viewer drawing a whole map at once can justify: the difference
    /// between two samples in one leaf is smaller than the difference between leaves, and the thing
    /// being fixed is models drawn at full brightness.
    ///
    /// Recorded rather than hidden, because it is a simplification of the engine's behaviour and
    /// the next person should know it is there.
    /// </remarks>
    public static IReadOnlyList<AmbientCube> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> samples = BspLumpData
            .Read(file, header.Lump(LumpLeafAmbientLighting)).Span;

        ReadOnlySpan<byte> indices = BspLumpData
            .Read(file, header.Lump(LumpLeafAmbientIndex)).Span;

        if (samples.IsEmpty)
        {
            return [];
        }

        // **No index lump means one sample per leaf, in leaf order.** Some maps carry the lighting
        // without the index; the engine treats the samples as leaf-ordered, and so does this.
        if (indices.IsEmpty)
        {
            List<AmbientCube> direct = new(samples.Length / AmbientSampleStride);

            for (int at = 0; at + AmbientSampleStride <= samples.Length; at += AmbientSampleStride)
            {
                direct.Add(ReadCube(samples[at..]));
            }

            return direct;
        }

        int leafCount = indices.Length / AmbientIndexStride;
        List<AmbientCube> cubes = new(leafCount);

        for (int leaf = 0; leaf < leafCount; leaf++)
        {
            ReadOnlySpan<byte> entry = indices[(leaf * AmbientIndexStride)..];

            int count = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            int first = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            int offset = first * AmbientSampleStride;

            // A leaf with no samples is solid or outside the map, and takes no light. Black rather
            // than skipped, so the list stays indexed by leaf.
            cubes.Add(
                count > 0 && offset + AmbientSampleStride <= samples.Length
                    ? ReadCube(samples[offset..])
                    : default);
        }

        return cubes;
    }

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
    /// Left in LINEAR light rather than gamma-corrected, unlike the lightmap path: this is
    /// multiplied against a texture in the shader, where the lightmap's own value arrives the same
    /// way. Converting here would apply the curve twice.
    /// </remarks>
    private static (float Red, float Green, float Blue) Colour(ReadOnlySpan<byte> sample)
    {
        float scale = MathF.Pow(2f, (sbyte)sample[3]) / 255f;

        return (sample[0] * scale, sample[1] * scale, sample[2] * scale);
    }
}
