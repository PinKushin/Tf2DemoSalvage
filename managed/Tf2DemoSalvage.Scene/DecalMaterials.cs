using System;
using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>Decal names to <see cref="DecalMaterial"/>s, as the material system sizes and maps them (B415).</summary>
/// <remarks>
/// **TF2's decals are nearly all Subrects**: `decals/concrete/shot1_subrect.vmt` is a window into an atlas —
///
/// <code>
/// "Subrect" { "$Material" "decals/decals_mod2x"  "$Pos" "0 0"  "$Size" "64 64"  "$decalscale" 0.16 }
/// </code>
///
/// — so the decal is SIZED by <c>$Size</c> (`GetMappingWidth`), a 64-texel hole at 0.16 being 10.24 units across,
/// and DRAWN with the atlas's material, its coordinates remapped into the window. A plain decal is sized by its base
/// texture.
///
/// *Interpolated:* the page offset and scale are <c>$Pos</c> and <c>$Size</c> over the atlas's base texture size;
/// `CMaterialSubRect` is in the closed material system and its two getters were not read. The windows TF2 ships sit
/// on texel boundaries of power-of-two atlases, so the division is the only reading that lands them there.
/// </remarks>
public sealed class DecalMaterials
{
    private readonly Func<string, byte[]?> _read;
    private readonly Func<string, (int Width, int Height)?> _textureSize;
    private readonly Dictionary<string, DecalMaterial?> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A resolver over the game's content.</summary>
    /// <param name="read">Opens a path out of the game's content, or answers null.</param>
    /// <param name="textureSize">A texture's mapping size by its <c>$basetexture</c> name, or null.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public DecalMaterials(Func<string, byte[]?> read, Func<string, (int Width, int Height)?> textureSize)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(textureSize);

        _read = read;
        _textureSize = textureSize;
    }

    /// <summary>A decal by the name the <c>decalprecache</c> table or a decal list gives, resolved once.</summary>
    /// <param name="name">Such as <c>decals/concrete/shot1_subrect</c>.</param>
    /// <returns>The material, or null when it or its texture cannot be found.</returns>
    public DecalMaterial? Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (_resolved.TryGetValue(name, out DecalMaterial? known))
        {
            return known;
        }

        DecalMaterial? resolved = Load(name);

        _resolved[name] = resolved;

        return resolved;
    }

    private DecalMaterial? Load(string name)
    {
        if (Material(name) is not { } vmt)
        {
            return null;
        }

        float scale = Number(vmt.Value("$decalscale"), 1f);

        if (vmt.Shader.Equals("Subrect", StringComparison.OrdinalIgnoreCase))
        {
            if (vmt.Value("$Material") is not { Length: > 0 } atlas ||
                Material(atlas)?.BaseTexture is not { } atlasTexture ||
                _textureSize(atlasTexture) is not { Width: > 0, Height: > 0 } atlasSize)
            {
                return null;
            }

            (float x, float y) = Pair(vmt.Value("$Pos"));
            (float width, float height) = Pair(vmt.Value("$Size"));

            return new DecalMaterial(
                name,
                (int)width,
                (int)height,
                scale,
                Paged: true,
                PageOffset: (x / atlasSize.Width, y / atlasSize.Height),
                PageScale: (width / atlasSize.Width, height / atlasSize.Height),
                Draws: atlas);
        }

        return vmt.BaseTexture is { } texture && _textureSize(texture) is { Width: > 0, Height: > 0 } size
            ? new DecalMaterial(name, size.Width, size.Height, scale, Draws: name)
            : null;
    }

    private VmtMaterial? Material(string name)
    {
        string path = name.Replace('\\', '/');

        if (!path.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            path = "materials/" + path;
        }

        if (!path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
        {
            path += ".vmt";
        }

        return _read(path) is { Length: > 0 } bytes ? VmtMaterial.Parse(bytes) : null;
    }

    private static float Number(string? text, float otherwise) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : otherwise;

    private static (float A, float B) Pair(string? text)
    {
        string[] parts = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 ? (Number(parts[0], 0f), Number(parts[1], 0f)) : (0f, 0f);
    }

    /// <summary>A resolver whose texture sizes come from each VTF's own header.</summary>
    /// <param name="read">Opens a path out of the game's content.</param>
    /// <returns>The resolver.</returns>
    public static DecalMaterials Over(Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        return new DecalMaterials(read, texture =>
        {
            string path = "materials/" + texture.Replace('\\', '/') + ".vtf";

            return read(path) is { Length: > 0 } bytes && VtfTexture.Read(bytes, 1) is { } vtf
                ? (vtf.MappingWidth, vtf.MappingHeight)
                : null;
        });
    }
}
