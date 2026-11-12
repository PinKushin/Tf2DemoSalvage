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
    /// <remarks>
    /// **Never throws for bad content.** A malformed pak entry, an unreadable archive or a VMT that will not parse each
    /// answer null, so the caller keeps the texdata's copy for that one material; the texdata read it replaces could not
    /// fail a map load, and this must not start to.
    /// </remarks>
    public static Vector3? Of(string materialName, Func<string, byte[]?> find)
    {
        ArgumentNullException.ThrowIfNull(materialName);
        ArgumentNullException.ThrowIfNull(find);

        try
        {
            if (find($"materials/{materialName.Replace('\\', '/')}.vmt") is not { } vmt ||
                VmtMaterial.Load(vmt, find).BaseTexture is not { } texture ||
                find($"materials/{texture.Replace('\\', '/')}.vtf") is not { Length: >= HeaderOffset + 12 } vtf ||
                !vtf.AsSpan(0, 4).SequenceEqual("VTF\0"u8))
            {
                return null;
            }

            Vector3 reflectivity = new(
                BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset)),
                BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset + 4)),
                BinaryPrimitives.ReadSingleLittleEndian(vtf.AsSpan(HeaderOffset + 8)));

            return float.IsFinite(reflectivity.X) && float.IsFinite(reflectivity.Y) && float.IsFinite(reflectivity.Z)
                ? reflectivity
                : null;
        }
        catch (Exception failure) when (failure is InvalidDataException or IOException)
        {
            // Handled, not swallowed: null is the documented answer, and the caller falls back to the texdata's copy.
            return null;
        }
    }
}
