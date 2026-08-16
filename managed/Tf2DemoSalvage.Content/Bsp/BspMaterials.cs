using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using static Tf2DemoSalvage.Content.Bsp.BspStructLayout;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One entry of a map's texture table.</summary>
/// <param name="Name">Material path relative to <c>materials/</c>, without the extension.</param>
/// <param name="Reflectivity">
/// The texture's average colour, computed by the map compiler from the texture itself.
/// </param>
/// <param name="Width">Texture width in pixels.</param>
/// <param name="Height">Texture height in pixels.</param>
public readonly record struct BspMaterial(
    string Name, (float Red, float Green, float Blue) Reflectivity, int Width, int Height);

/// <summary>
/// The material each surface is painted with, as the map states it.
/// </summary>
/// <remarks>
/// **A map names its own materials; none of this needs to be guessed.** Three lumps hold it, and
/// they are read together because none is useful alone:
///
/// | Lump | Holds |
/// |---|---|
/// | `TEXDATA` (2) | one 32-byte record per texture: reflectivity, size, and a name index |
/// | `TEXDATA_STRING_TABLE` (44) | int offsets into the string data, indexed by that name index |
/// | `TEXDATA_STRING_DATA` (43) | the names themselves, null-terminated, run together |
///
/// A face reaches this through its `texinfo`, whose last field is a texdata index.
///
/// **`reflectivity` is the useful surprise here.** It is a float3 the compiler computed by
/// averaging the texture, so it is a real colour for the surface — Valve's own number, not an
/// approximation invented by this project. It is what `vrad` bounces light off, and it is enough
/// to draw a map in something close to its real colours before a single VTF has been decoded.
/// </remarks>
public static class BspMaterials
{
    /// <summary>Reads the whole texture table.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>One entry per texture, in file order.</returns>
    /// <exception cref="InvalidDataException">A lump is malformed or an index is out of range.</exception>
    public static IReadOnlyList<BspMaterial> Read(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> texdata = BspLumpData
            .ReadStructures(file, header.Lump(BspLumpIndex.Texdata), TexdataStride, "texdata").Span;
        ReadOnlySpan<byte> table = BspLumpData
            .ReadStructures(
                file,
                header.Lump(BspLumpIndex.TexdataStringTable),
                StringTableStride,
                "texdata string table")
            .Span;
        ReadOnlySpan<byte> names = BspLumpData
            .Read(file, header.Lump(BspLumpIndex.TexdataStringData)).Span;

        int count = texdata.Length / TexdataStride;
        int nameCount = table.Length / StringTableStride;
        List<BspMaterial> materials = new(count);

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> record = texdata.Slice(index * TexdataStride, TexdataStride);

            (float, float, float) reflectivity = (
                BinaryPrimitives.ReadSingleLittleEndian(record),
                BinaryPrimitives.ReadSingleLittleEndian(record[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(record[8..]));

            int nameIndex = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
            int width = BinaryPrimitives.ReadInt32LittleEndian(record[16..]);
            int height = BinaryPrimitives.ReadInt32LittleEndian(record[20..]);

            materials.Add(new BspMaterial(
                ReadName(table, names, nameIndex, nameCount), reflectivity, width, height));
        }

        return materials;
    }

    /// <summary>Reads just the material names.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>The names, in texdata order.</returns>
    public static string[] ReadNames(ReadOnlyMemory<byte> file)
    {
        IReadOnlyList<BspMaterial> materials = Read(file);
        string[] names = new string[materials.Count];

        for (int index = 0; index < materials.Count; index++)
        {
            names[index] = materials[index].Name;
        }

        return names;
    }

    private static string ReadName(
        ReadOnlySpan<byte> table, ReadOnlySpan<byte> names, int nameIndex, int nameCount)
    {
        if (nameIndex < 0 || nameIndex >= nameCount)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A texture names string {nameIndex} of {nameCount}."));
        }

        int offset = BinaryPrimitives.ReadInt32LittleEndian(table[(nameIndex * StringTableStride)..]);

        if (offset < 0 || offset >= names.Length)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"A texture name starts at {offset} of {names.Length} bytes of name data."));
        }

        ReadOnlySpan<byte> rest = names[offset..];
        int end = rest.IndexOf((byte)0);

        // The last name in the lump is not required to be terminated.
        ReadOnlySpan<byte> text = end < 0 ? rest : rest[..end];

        // UTF-8 rather than ASCII: community maps carry non-English material paths.
        return Encoding.UTF8.GetString(text);
    }
}
