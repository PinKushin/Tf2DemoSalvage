using System;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// Which size bucket an opaque model draws in, and therefore how early it is drawn.
/// </summary>
/// <remarks>
/// **Valve draws opaque models biggest first, and the reason is occlusion bought with a sort.**
/// A large object drawn early fills the depth buffer, so everything behind it that comes later
/// fails the depth test before its pixels are shaded. `CRendering3dView::DrawOpaqueRenderables`
/// (`viewrender.cpp:4059`) does brush models first, then walks the buckets under its own comment —
/// *"Draw static props + opaque entities from the biggest bucket to the smallest"*.
///
/// **The thresholds are `DetectBucketedRenderGroup`'s** (`clientleafsystem.cpp:1538`) and live on
/// <see cref="ClientLeafSystem"/>, which fills the buckets at collation as the engine does (B262). This
/// type is what the device and the conformance suite name them by.
///
/// **The measure is the longest axis of the WORLD-space box, not the model-space one.** Valve
/// builds it with `CalcRenderableWorldSpaceAABB` → `TransformAABB`, which encloses the *rotated*
/// box — so a long prop turned forty-five degrees has a larger extent than the same prop square on,
/// and buckets larger.
///
/// **`InDrawOrder` was here until B262**: a per-frame cull and comparison sort over the whole drawn
/// list. Its cull was measured rejecting nothing (152 of 152) because the scene had already culled
/// against the same frustum; its sort reconstructed what collation produces as an append. Both are
/// gone — the buckets now arrive filled from <see cref="ClientLeafSystem.BuildRenderablesList"/>.
/// </remarks>
public static class OpaqueBuckets
{
    /// <summary>Valve's size thresholds, largest first.</summary>
    public static ReadOnlySpan<float> Thresholds => ClientLeafSystem.Thresholds;

    /// <summary>How many buckets there are: one per threshold, plus everything below.</summary>
    public const int Count = ClientLeafSystem.BucketCount;

    /// <summary>Which bucket a size falls in — zero is the largest, and drawn first.</summary>
    /// <param name="longestAxis">The longest axis of the world-space bounding box.</param>
    /// <returns>Zero through <see cref="Count"/> minus one.</returns>
    /// <remarks>`&gt;=`, so a size exactly on a threshold takes the LARGER bucket, as Valve's nesting does.</remarks>
    public static int BucketFor(float longestAxis) => ClientLeafSystem.BucketFor(longestAxis);
}
