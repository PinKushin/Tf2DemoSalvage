using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>Hands a packed model set to whatever will draw it.</summary>
/// <remarks>
/// **The abstraction in the portable project, the implementation in the Direct3D one** — the
/// direction dependency inversion asks for, and the same arrangement D84 already chose for
/// <c>IGlyphRasteriser</c>. <c>Device3D.UploadModels</c> implements this; nothing in Scene knows
/// that Direct3D exists.
///
/// **One method, because one call is all the scene rebuild needs from a window.** Everything else in
/// <see cref="MomentScene"/> — sampling the timeline, deciding what is drawn, packing, posing, the
/// viewmodel, the diagnostics — needs no device at all. Threading the whole device through would
/// have pinned the scene to a renderer for the sake of a single upload (B188, D90).
/// </remarks>
public interface IModelUpload
{
    /// <summary>Uploads the packed vertices, replacing whatever was there.</summary>
    /// <param name="models">The packed set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
    /// <remarks>
    /// **The whole buffer is rebuilt, so this is not a one-off cost at load.** It is paid again
    /// every time a model nobody has seen yet comes into view, and it gets more expensive as the set
    /// gets bigger — which is why the caller only calls it when the set actually grew.
    /// </remarks>
    public void UploadModels(EntityModelSet models);
}
