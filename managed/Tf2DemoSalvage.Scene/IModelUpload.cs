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

    /// <summary>Whether the last upload is still there to draw with.</summary>
    /// <remarks>
    /// **The scene used to REMEMBER this, and the memory kept going stale** (B148, B219). It held a
    /// bool saying "I have uploaded", which is a belief about the other side of the boundary — and
    /// anything that discarded the geometry made the belief wrong without touching it.
    ///
    /// Three callers of <c>ClearWorld</c> proved that. A map change was paired with a reset; the
    /// category-view toggle was not, and every model on the map vanished until the viewer was
    /// restarted; the failed-upload path in <see cref="WorldPresenter"/> was not either, and could
    /// not be, because it cannot reach the scene to tell it.
    ///
    /// **So the question is asked rather than remembered.** The side that owns the geometry is the
    /// side that knows whether it still has it, and a caller cannot forget to answer honestly. That
    /// removes the pairing entirely rather than adding a fourth one.
    /// </remarks>
    public bool HasModels { get; }
}
