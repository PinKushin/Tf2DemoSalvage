using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order opaque models are handed to the device, and what decides it.
/// </summary>
/// <remarks>
/// **`OpaqueDrawOrderConformanceTests` asserts the thresholds against the SDK; this asserts the
/// ORDER.** A correct threshold table that nothing sorts by is the shape this project has shipped
/// three times — decoded, retained, unit-tested and read by no production code. So the subject here
/// is the sequence that comes out, not the bucket a size maps to.
///
/// Every box below is chosen so its longest axis lands unambiguously inside one of Valve's four
/// bands rather than on a boundary: a boundary case is
/// <see cref="OpaqueDrawOrderConformanceTests"/>' subject, and reusing one here would make a failure
/// ambiguous between the two.
/// </remarks>
public sealed class OpaqueDrawOrderTests
{
    /// <summary>A cube of a given side, centred on the origin.</summary>
    private static StudioBox Cube(float side) =>
        new(-side / 2f, -side / 2f, -side / 2f, side / 2f, side / 2f, side / 2f);

    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private static ModelInstance Sized(string name, float side) =>
        new(name, Identity(), null, null, Bounds: Cube(side));

    /// <summary>That the biggest bucket is drawn first and the smallest last.</summary>
    /// <remarks>
    /// **Handed in reverse, so list order cannot produce this answer.** If the sort were absent the
    /// result would be exactly the input, which is the opposite of what is asserted — that is what
    /// makes this test sensitive to the wiring rather than to the comparison alone.
    ///
    /// The sides are 400, 100, 50 and 10: comfortably inside "tree", "player", "crate" and
    /// everything-below, with no value near 200, 80 or 30.
    /// </remarks>
    [Test]
    public void InDrawOrder_WithAModelInEveryBucket_DrawsTheBiggestFirst()
    {
        ModelInstance[] scene =
        [
            Sized("tiny", 10f),
            Sized("crate", 50f),
            Sized("player", 100f),
            Sized("tree", 400f),
        ];

        OpaqueBuckets.InDrawOrder(scene)
            .Select(instance => instance.ModelPath)
            .ShouldBe(["tree", "player", "crate", "tiny"]);
    }

    /// <summary>That models sharing a bucket keep the order the scene produced.</summary>
    /// <remarks>
    /// **Stability is load-bearing rather than incidental.** Identical models arrive adjacent from
    /// the scene, and adjacency is what a redundant-material-bind check would later depend on; an
    /// unstable sort would scatter them and put the binds back. `Array.Sort` is introsort and is NOT
    /// stable, which is why the comparison carries the original index.
    ///
    /// **Twenty-four models, and the number is the whole experiment.** A first version used six and
    /// could not fail: deleting the tiebreak left it green, because .NET's introsort hands any
    /// partition of sixteen or fewer to insertion sort, which is stable by accident
    /// (`IntrosortSizeThreshold = 16` in `ArraySortHelper`). The assertion was right and the
    /// CONDITION was wrong — six models cannot distinguish a stable sort from an unstable one.
    ///
    /// Above the threshold, `PickPivotAndPartition` swaps the middle element to `hi - 1` before it
    /// compares anything, so an all-equal run is reordered on the first partition. Measured: the
    /// tiebreak removed, this fails.
    /// </remarks>
    [Test]
    public void InDrawOrder_ForModelsSharingABucket_KeepsTheOrderTheSceneGave()
    {
        string[] names = [.. Enumerable.Range(0, 24).Select(at => $"model{at:00}")];

        ModelInstance[] scene = [.. names.Select(name => Sized(name, 100f))];

        OpaqueBuckets.InDrawOrder(scene)
            .Select(instance => instance.ModelPath)
            .ShouldBe(names);
    }

    /// <summary>That a model's placement decides its bucket, not the box it was authored with.</summary>
    /// <remarks>
    /// **The whole reason bounds are transformed per instance rather than measured once at pack
    /// time.** Two instances of ONE model — same box, same path — take different buckets because one
    /// is scaled up. A packed-once implementation gives them the same bucket and this is the only
    /// test here that can tell the difference.
    ///
    /// A 60-unit cube is "crate" (bucket 2); tripled it spans 180, still short of the 200 that would
    /// make it a tree, so it is "player" (bucket 1) and is drawn first.
    /// </remarks>
    [Test]
    public void InDrawOrder_ForOneModelAtTwoScales_BucketsThemApart()
    {
        float[] tripled = Identity();

        tripled[0] = 3f;
        tripled[5] = 3f;
        tripled[10] = 3f;

        ModelInstance[] scene =
        [
            new("crate", Identity(), null, null, Bounds: Cube(60f)),
            new("crate", tripled, null, null, Bounds: Cube(60f)),
        ];

        OpaqueBuckets.InDrawOrder(scene)
            .Select(instance => instance.Matrix[0])
            .ShouldBe([3f, 1f]);
    }

    /// <summary>That a scene whose bounds were never filled in is left exactly as it came.</summary>
    /// <remarks>
    /// **This documents the no-op, which is the failure mode worth naming.** An unset
    /// <see cref="ModelInstance.Bounds"/> is a zero-sized box, so every model lands in the smallest
    /// bucket and the sort returns the input order — green, silent, and drawing in exactly the order
    /// it did before. Nothing in this file could catch that, which is why
    /// <see cref="ModelBoundsWiringTests"/> asserts on a model loaded the way the viewer loads one.
    /// </remarks>
    [Test]
    public void InDrawOrder_WhenNoBoundsWereSet_LeavesTheOrderAlone()
    {
        ModelInstance[] scene =
        [
            new("first", Identity(), null, null),
            new("second", Identity(), null, null),
            new("third", Identity(), null, null),
        ];

        OpaqueBuckets.InDrawOrder(scene)
            .Select(instance => instance.ModelPath)
            .ShouldBe(["first", "second", "third"]);
    }

    /// <summary>That a model outside the view is dropped before it is bucketed.</summary>
    /// <remarks>
    /// **This replaced a shortcut that returned a one-model list unchanged.** That was correct while
    /// this method only sorted — one item is already in order — and became wrong the moment it also
    /// culled, because a single model can be off screen. The old test asserted reference equality,
    /// so it would have kept passing against an implementation that skipped the cull for small
    /// scenes, which is exactly the sort of special case that survives because nothing exercises it.
    ///
    /// The frustum looks down +X from the origin; the two models sit 200 units ahead and behind.
    /// </remarks>
    [Test]
    public void InDrawOrder_WithAModelBehindTheCamera_DropsIt()
    {
        ModelInstance[] scene =
        [
            Placed("ahead", 200f),
            Placed("behind", -200f),
        ];

        OpaqueBuckets.InDrawOrder(scene, Looking())
            .Select(instance => instance.ModelPath)
            .ShouldBe(["ahead"]);
    }

    /// <summary>That an unbuilt frustum culls nothing, so the sort alone still works.</summary>
    /// <remarks>
    /// The control for the case above: the same two models, no frustum, both drawn. Without it,
    /// "the cull works" and "the list was emptied for some other reason" look identical.
    /// </remarks>
    [Test]
    public void InDrawOrder_WithNoFrustum_KeepsWhatIsBehindTheCamera()
    {
        ModelInstance[] scene =
        [
            Placed("ahead", 200f),
            Placed("behind", -200f),
        ];

        OpaqueBuckets.InDrawOrder(scene).Count.ShouldBe(2);
    }

    /// <summary>A camera at the origin looking down +X.</summary>
    private static ViewFrustum Looking() =>
        ViewFrustum.PerspectiveFromAspect(
            origin: (0f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 1f);

    /// <summary>A crate-sized model standing at a given distance along +X.</summary>
    private static ModelInstance Placed(string name, float x)
    {
        float[] matrix = Identity();

        matrix[12] = x;

        return new ModelInstance(name, matrix, null, null, Bounds: Cube(50f));
    }

    [Test]
    public void InDrawOrder_ForNull_Throws()
    {
        Should.Throw<ArgumentNullException>(() => OpaqueBuckets.InDrawOrder(null!));
    }
}
