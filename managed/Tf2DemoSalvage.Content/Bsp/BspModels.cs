using System;
using System.IO;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One of a map's models: the world, or a piece of brushwork an entity moves.</summary>
/// <param name="Minimum">Corner of its bounding box.</param>
/// <param name="Maximum">The opposite corner.</param>
/// <param name="Origin">The model's own origin, which is nearly always zero.</param>
/// <param name="HeadNode">Its root node in the BSP tree.</param>
/// <param name="FirstFace">Where this model's faces start in the faces lump.</param>
/// <param name="FaceCount">How many faces it has.</param>
/// <remarks>
/// **Model zero is the world and every other one belongs to an entity.** A door, a lift, a payload
/// cart and any other brushwork that moves is compiled into its own model, and the entity that owns
/// it references it as <c>*N</c> in <c>m_nModelIndex</c> — which is why a demo carries model names
/// like <c>*12</c> beside <c>models/items/medkit_small.mdl</c>.
///
/// Valve's own comment on the pair of fields below says how they are meant to be used: "submodels
/// just draw faces without walking the bsp tree". There is no visibility structure to consult and
/// no leaves to gather; a submodel is a contiguous run of faces.
/// </remarks>
public readonly record struct BspModel(
    (float X, float Y, float Z) Minimum,
    (float X, float Y, float Z) Maximum,
    (float X, float Y, float Z) Origin,
    int HeadNode,
    int FirstFace,
    int FaceCount);

/// <summary>
/// Reads a map's models from lump 14.
/// </summary>
/// <remarks>
/// <c>dmodel_t</c> is mins, maxs and origin as three vectors, then <c>headnode</c>,
/// <c>firstface</c> and <c>numfaces</c> — 48 bytes, which every real map's lump length divides by
/// exactly.
///
/// **Every field is kept, including the ones nothing reads yet.** <c>headnode</c> indexes the BSP
/// tree, which is how the engine finds a submodel's faces for collision and visibility; the bounds
/// are what a culler will want. Drawing needs only the face range, but a reader that silently drops
/// the rest leaves the next person to rediscover the layout — and a field that was never read is
/// indistinguishable from one that does not exist.
/// </remarks>
public static class BspModels
{
    /// <summary>The lump models live in.</summary>
    public const int Lump = 14;

    /// <summary>Bytes per <c>dmodel_t</c>.</summary>
    private const int Stride = 48;

    /// <summary>Where <c>firstface</c> sits: after mins, maxs, origin and headnode.</summary>
    private const int FirstFaceOffset = 40;

    /// <summary>A map is untrusted input; real ones have a few hundred models at most.</summary>
    private const int Maximum = 16_384;

    /// <summary>Reads every model a map declares, world first.</summary>
    /// <param name="file">The whole BSP.</param>
    /// <returns>The models in lump order, so index N is the model <c>*N</c> names.</returns>
    /// <remarks>
    /// Ordered rather than keyed, deliberately: <c>*N</c> IS the index, so a list indexed by it is
    /// the lookup and a dictionary would only add a way to disagree with the file.
    /// </remarks>
    public static IReadOnlyList<BspModel> Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlyMemory<byte> lump;

        try
        {
            lump = BspLumpData.Read(file, BspHeader.Parse(file.Span).Lump(Lump));
        }
        catch (InvalidDataException)
        {
            return [];
        }

        ReadOnlySpan<byte> bytes = lump.Span;
        int count = bytes.Length / Stride;

        if (count <= 0 || count > Maximum)
        {
            return [];
        }

        List<BspModel> models = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> model = bytes.Slice(index * Stride, Stride);

            models.Add(new BspModel(
                Vector(model, 0),
                Vector(model, 12),
                Vector(model, 24),
                BinaryPrimitives.ReadInt32LittleEndian(model[36..]),
                BinaryPrimitives.ReadInt32LittleEndian(model[FirstFaceOffset..]),
                BinaryPrimitives.ReadInt32LittleEndian(model[(FirstFaceOffset + 4)..])));
        }

        return models;
    }

    /// <summary>Three floats at an offset.</summary>
    private static (float X, float Y, float Z) Vector(ReadOnlySpan<byte> bytes, int at) =>
    (
        BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
        BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..])
    );

    /// <summary>The model index a <c>*N</c> reference names, or −1 for anything else.</summary>
    /// <param name="modelPath">A model reference as <c>modelprecache</c> carried it.</param>
    /// <returns>The index, or −1 when this is not an inline submodel reference.</returns>
    public static int IndexOf(string? modelPath) =>
        modelPath is { Length: > 1 } path &&
        path[0] == '*' &&
        int.TryParse(path[1..], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int index)
            ? index
            : -1;
}
