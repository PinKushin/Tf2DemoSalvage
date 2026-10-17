using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Each VGUI material's texture size, resolved once per name — `ISurface::DrawSetTextureFile`'s once-per-name load.</summary>
/// <param name="resolve">Reads a material's size from the files the HUD reads.</param>
/// <param name="files">Those files as they stand now, or null before an install is open.</param>
/// <remarks>
/// **Per frame it opened a file.** Every spectator card's `ScalableImageBorder` asks `DrawGetTextureSize` when it paints,
/// and the answer was resolved out of the VPK each time. The engine keys a texture by its name and loads it once, so the
/// size is read once; a material that does not resolve is not asked again either, as the engine binds its error texture
/// once.
///
/// **Once per name per set of files**: the sizes are forgotten when the files change — a HUD switch or a new install remakes
/// them — and nothing is kept while there are none, since "no files yet" is not "the material does not exist".
/// </remarks>
public sealed class VguiTextureSizes(Func<string, (int Wide, int Tall)> resolve, Func<object?> files)
{
    private readonly Dictionary<string, (int Wide, int Tall)> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private object? _of;

    /// <summary>The material's texture size, resolving it on the first ask against the current files.</summary>
    /// <param name="material">The VGUI material path.</param>
    /// <returns>The size, (0, 0) for a material that does not resolve.</returns>
    public (int Wide, int Tall) Size(string material)
    {
        object? now = files();

        if (now is null)
        {
            return resolve(material);
        }

        if (!ReferenceEquals(now, _of))
        {
            _sizes.Clear();
            _of = now;
        }

        if (!_sizes.TryGetValue(material, out (int Wide, int Tall) size))
        {
            size = resolve(material);
            _sizes[material] = size;
        }

        return size;
    }
}
