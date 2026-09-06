using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which runs the translucent pass is given (B362).
/// </summary>
/// <remarks>
/// **A translucent PROP run was drawn by nothing at all.** `DrawOpaqueBatches` skips every
/// translucent material — it has to, or a window would be opaque — and the sorted translucent list
/// was built from the world's own batches alone. A prop batch whose material blends therefore fell
/// between the two passes and was never issued.
///
/// **The engine draws them together**: `DrawTranslucentRenderables` walks the world's translucent
/// surfaces and the renderables in one back-to-front pass.
///
/// **Found by two correct counts either side of the gap.** Detail sprites (B360) are
/// `detail/detailsprites`, which is `$translucent 1`: 20,117 quads were built, `world:` reported
/// all 398,595 prop triangles drawn, and the screenshot had no grass in it. Measured on
/// `koth_harvest_final`, 5 of 11 translucent batches are prop runs.
/// </remarks>
public sealed class TranslucentBatchTests
{
    /// <remarks>
    /// **The control is the opaque prop run beside it**, without which "included every prop" and
    /// "included the translucent one" are the same observation.
    /// </remarks>
    [Test]
    public void SortTranslucent_ATranslucentPropRun_IsIssuedLikeAWorldOne()
    {
        WorldBatch worldGlass = new(MaterialIndex: 7, FirstVertex: 0, VertexCount: 3);
        WorldBatch propGrass = new(
            MaterialIndex: 9, FirstVertex: 3, VertexCount: 3, Category: SurfaceCategory.Prop);
        WorldBatch propRock = new(
            MaterialIndex: 4, FirstVertex: 6, VertexCount: 3, Category: SurfaceCategory.Prop);

        IReadOnlyList<WorldBatch> sorted = WorldRenderer.SortTranslucent(
            Corners(9),
            [worldGlass],
            [propGrass, propRock],
            new HashSet<int> { 7, 9 });

        sorted.ShouldContain(propGrass);
        sorted.ShouldContain(worldGlass);
        sorted.ShouldNotContain(propRock);
    }

    /// <remarks>
    /// Farthest first, because a blended fragment is combined with whatever is already in the
    /// frame buffer. The depths below are chosen so the prop run must be ordered BETWEEN two world
    /// runs — a list that merely appended the props after the world's would pass a test where the
    /// prop was nearest or farthest, and fail this one.
    /// </remarks>
    [Test]
    public void SortTranslucent_ARunBetweenTwoWorldRuns_IsOrderedByDepthRatherThanBySource()
    {
        WorldBatch near = new(MaterialIndex: 7, FirstVertex: 0, VertexCount: 3);
        WorldBatch far = new(MaterialIndex: 8, FirstVertex: 3, VertexCount: 3);
        WorldBatch middle = new(
            MaterialIndex: 9, FirstVertex: 6, VertexCount: 3, Category: SurfaceCategory.Prop);

        List<WorldVertex> corners = [];

        Append(corners, 10f);   // the near world run
        Append(corners, 900f);  // the far world run
        Append(corners, 500f);  // the prop run, between them

        IReadOnlyList<WorldBatch> sorted = WorldRenderer.SortTranslucent(
            corners, [near, far], [middle], new HashSet<int> { 7, 8, 9 });

        sorted.ShouldBe([far, middle, near]);
    }

    /// <remarks>
    /// A map whose props are all opaque must be unchanged by this, which is the other half of the
    /// control: the fix adds prop runs to a list, and a list that grew for an opaque map would mean
    /// the material test had stopped being applied.
    /// </remarks>
    [Test]
    public void SortTranslucent_AMapWithNoTranslucentProps_IssuesTheWorldRunsAlone()
    {
        WorldBatch worldGlass = new(MaterialIndex: 7, FirstVertex: 0, VertexCount: 3);
        WorldBatch propRock = new(
            MaterialIndex: 4, FirstVertex: 3, VertexCount: 3, Category: SurfaceCategory.Prop);

        WorldRenderer.SortTranslucent(
                Corners(6), [worldGlass], [propRock], new HashSet<int> { 7 })
            .ShouldBe([worldGlass]);
    }

    private static List<WorldVertex> Corners(int count)
    {
        List<WorldVertex> corners = [];

        for (int at = 0; at < count; at++)
        {
            corners.Add(new WorldVertex(0f, 0f, at, 0f, 0f, 0f, 0f, 1f));
        }

        return corners;
    }

    private static void Append(List<WorldVertex> corners, float depth)
    {
        for (int at = 0; at < 3; at++)
        {
            corners.Add(new WorldVertex(0f, 0f, depth, 0f, 0f, 0f, 0f, 1f));
        }
    }
}
