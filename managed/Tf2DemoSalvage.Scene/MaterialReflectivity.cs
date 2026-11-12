using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>A material's reflectivity as the material system holds it: its base texture's VTF header (B683).</summary>
/// <remarks>
/// `materialsystem.dll` `FUN_180006a00` (build 11191711) fills a material's reflectivity from its `$baseTexture` through
/// `ITexture::GetReflectivity` (vtable `+0x120`), which is the `reflectivity` vector the VTF header stores at offset 32. The
/// engine's static prop cube reads that (`0x1801b8e30`, `IMaterial` vtable `+0xf8`), not the copy vbsp wrote into texdata.
/// </remarks>
public static class MaterialReflectivity
{
    /// <summary>The header's reflectivity at offset 32, three floats.</summary>
    private const int HeaderOffset = 32;

    /// <summary>A material's reflectivity, or null when the material, its base texture or the header would not read.</summary>
    /// <param name="materialName">The material's name under <c>materials/</c>, without the extension.</param>
    /// <param name="find">The level's file lookup, the pakfile ahead of the game's archives.</param>
    /// <returns>The reflectivity, or null.</returns>
    public static Vector3? Of(string materialName, Func<string, byte[]?> find)
    {
        ArgumentNullException.ThrowIfNull(materialName);
        ArgumentNullException.ThrowIfNull(find);

        string bare = materialName.Replace('\\', '/');

        if (find($"materials/{bare}.vmt") is not { } vmt)
        {
            return null;
        }

        string? texture;

        try
        {
            texture = VmtMaterial.Load(vmt, find).BaseTexture;
        }
        catch (InvalidDataException)
        {
            // A VMT that will not parse has no base texture to ask; the caller keeps its own value and says so.
            return null;
        }

        if (texture is null || find($"materials/{texture.Replace('\\', '/')}.vtf") is not { Length: >= HeaderOffset + 12 } vtf)
        {
            return null;
        }

        return new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset)),
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset + 8)));
    }
}
