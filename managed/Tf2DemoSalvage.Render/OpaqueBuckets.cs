using System;
using System.Collections.Generic;

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
/// **The thresholds are `DetectBucketedRenderGroup`'s** (`clientleafsystem.cpp:1538`), with Valve's
/// own names for what each size is:
///
/// <code>
/// float const arrThresholds[ 3 ] = {
///     200.f,  // tree size
///     80.f,   // player size
///     30.f,   // crate size
/// };
/// </code>
///
/// **The measure is the longest axis of the WORLD-space box, not the model-space one.** Valve
/// builds it with `CalcRenderableWorldSpaceAABB` → `TransformAABB`, which encloses the *rotated*
/// box — so a long prop turned forty-five degrees has a larger extent than the same prop square on,
/// and buckets larger. Taking a model's own extent once when it is packed would be systematically
/// too small for anything rotated and would draw those props later than the engine does. That
/// shortcut was nearly taken here and the owner stopped it.
/// </remarks>
public static class OpaqueBuckets
{
    /// <summary>Valve's size thresholds, largest first.</summary>
    /// <remarks>
    /// Public so the conformance suite can assert them against the SDK rather than restating them —
    /// a table checked only against itself is a table nobody checked.
    /// </remarks>
    public static ReadOnlySpan<float> Thresholds => [200f, 80f, 30f];

    /// <summary>How many buckets there are: one per threshold, plus everything below.</summary>
    public const int Count = 4;

    /// <summary>Which bucket a size falls in — zero is the largest, and drawn first.</summary>
    /// <param name="longestAxis">The longest axis of the world-space bounding box.</param>
    /// <returns>Zero through <see cref="Count"/> minus one.</returns>
    /// <remarks>
    /// **The comparisons are `&gt;=`, so a size exactly on a threshold takes the LARGER bucket.**
    /// Valve's nesting is `fDimension >= arrThresholds[0]` and so on down. Writing `&gt;` instead
    /// puts a boundary-sized object one bucket later, which almost no content would reveal because
    /// exact sizes are rare — and a crate authored at exactly thirty units would.
    /// </remarks>
    public static int BucketFor(float longestAxis)
    {
        if (longestAxis >= Thresholds[0])
        {
            return 0;
        }

        if (longestAxis >= Thresholds[1])
        {
            return 1;
        }

        return longestAxis >= Thresholds[2] ? 2 : 3;
    }

    /// <summary>The order the engine would draw these instances in.</summary>
    /// <param name="instances">What is to be drawn, in whatever order the scene produced.</param>
    /// <returns>The same instances, biggest bucket first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instances"/> is null.</exception>
    /// <remarks>
    /// **`DrawOpaqueRenderables`' own order**, minus a distinction this renderer does not have.
    /// Valve draws brush models first, then walks the buckets from biggest to smallest, drawing
    /// opaque ENTITIES then STATIC PROPS within each. This project has no static-prop render group
    /// — a map's props arrive as ordinary model instances — so the inner split has nothing to sort
    /// on and the bucket order is the whole of it.
    ///
    /// **The brush pass is already separate here**, which is why it is not in this list: brush
    /// entities are drawn by the world path with the map's own geometry, not through the model
    /// loop, so they precede every model without anything having to arrange it.
    ///
    /// **A STABLE sort, and that is load-bearing.** Two models in one bucket keep the order the
    /// scene produced, which for identical models means they stay adjacent — that is what makes a
    /// redundant-material check worth having later. An unstable sort would scatter them and the
    /// binds would come back.
    /// </remarks>
    public static IReadOnlyList<ModelInstance> InDrawOrder(IReadOnlyList<ModelInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);

        if (instances.Count < 2)
        {
            return instances;
        }

        // **Keyed once rather than inside the comparison.** A comparison sort calls its key
        // function O(n log n) times, and each call here transforms a box by a matrix; computing it
        // per instance first is the difference between hundreds of transforms a frame and
        // thousands. Valve pays none of this — its buckets are filled as renderables are collated,
        // so the sort is a bucket append rather than a comparison at all.
        (int Bucket, int Order, ModelInstance Instance)[] keyed =
            new (int, int, ModelInstance)[instances.Count];

        for (int at = 0; at < instances.Count; at++)
        {
            ModelInstance instance = instances[at];

            keyed[at] = (
                BucketFor(WorldSpaceBounds.LongestAxis(instance.Bounds, instance.Matrix)),
                at,
                instance);
        }

        Array.Sort(
            keyed,
            static (left, right) => left.Bucket != right.Bucket
                ? left.Bucket.CompareTo(right.Bucket)
                : left.Order.CompareTo(right.Order));

        ModelInstance[] ordered = new ModelInstance[keyed.Length];

        for (int at = 0; at < keyed.Length; at++)
        {
            ordered[at] = keyed[at].Instance;
        }

        return ordered;
    }
}
