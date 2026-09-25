using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which file a cubemap loads from: the HDR bake, then the LDR one.</summary>
/// <remarks>
/// **`materialsystem.dll`'s own order at TF2's default `mat_hdr_level 2`.** `CShaderSystem_LoadCubeMap` (0x180050530)
/// appends `.hdr` to the name when the HDR type is not `HDR_TYPE_NONE`, and `CTexture_ReadTextureFromFile_HdrFallback`
/// (0x18003acf0) strips it again when that file is missing. Our defaults are the game's highest quality, which is HDR.
///
/// The HDR bake is `RGBA16161616F`, linear light; the LDR one is DXT1 carrying the gamma curve. Reading the LDR bake
/// through the linear cube path — what this project did before — drew every reflection brighter than TF2 draws it.
/// </remarks>
public static class CubemapFile
{
    /// <summary>The paths to try, in order.</summary>
    /// <param name="name">The texture name as a material or the map states it, without `materials/` or an extension.</param>
    /// <returns>The HDR bake's path, then the LDR one's.</returns>
    public static IReadOnlyList<string> Candidates(string name) =>
        ["materials/" + name + ".hdr.vtf", "materials/" + name + ".vtf"];

    /// <summary>The first candidate that exists.</summary>
    /// <param name="name">The texture name.</param>
    /// <param name="find">Reads a path, or null when it is absent.</param>
    /// <returns>The file's bytes, or null when neither exists.</returns>
    public static byte[]? Find(string name, Func<string, byte[]?> find)
    {
        ArgumentNullException.ThrowIfNull(find);

        foreach (string path in Candidates(name))
        {
            if (find(path) is { } file)
            {
                return file;
            }
        }

        return null;
    }
}
