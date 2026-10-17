using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Each VGUI material's texture size, resolved once per name — `ISurface::DrawSetTextureFile`'s once-per-name load.</summary>
/// <remarks>
/// **Per frame it opened a file.** Every spectator card's `ScalableImageBorder` asks `DrawGetTextureSize` when it paints,
/// and the answer was resolved out of the VPK each time. The engine keys a texture by its name and loads it once, so the
/// size is read once; a material that does not resolve is not asked again either, as the engine binds its error texture
/// once. Lives as long as the surface host it serves, which is rebuilt with the HUD.
/// </remarks>
public sealed class VguiTextureSizes(Func<string, (int Wide, int Tall)> resolve)
{
    private readonly Dictionary<string, (int Wide, int Tall)> _sizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The material's texture size, resolving it on the first ask.</summary>
    /// <param name="material">The VGUI material path.</param>
    /// <returns>The size, (0, 0) for a material that does not resolve.</returns>
    public (int Wide, int Tall) Size(string material)
    {
        if (!_sizes.TryGetValue(material, out (int Wide, int Tall) size))
        {
            size = resolve(material);
            _sizes[material] = size;
        }

        return size;
    }
}
