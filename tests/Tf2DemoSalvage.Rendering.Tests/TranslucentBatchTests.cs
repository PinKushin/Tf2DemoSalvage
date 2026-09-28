using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which world runs the translucent pass is given, and in what order.
/// </summary>
/// <remarks>
/// `DrawOpaqueBatches` skips every translucent material — it has to, or a window would be opaque — so
/// this list is the only place a blended world run is issued. Static prop runs were in it too (B362)
/// until static props became model draws (B426, D198).
/// </remarks>
public sealed class TranslucentBatchTests
{
    /// <remarks>
    /// Farthest first, because a blended fragment is combined with whatever is already in the
    /// frame buffer. The middle run is listed last, so a list kept in input order fails.
    /// </remarks>
    [Test]
    public void SortTranslucent_ThreeRunsAtThreeDepths_AreOrderedFarthestFirst()
    {
        WorldBatch near = new(MaterialIndex: 7, FirstVertex: 0, VertexCount: 3);
        WorldBatch far = new(MaterialIndex: 8, FirstVertex: 3, VertexCount: 3);
        WorldBatch middle = new(MaterialIndex: 9, FirstVertex: 6, VertexCount: 3);

        List<WorldVertex> corners = [];

        Append(corners, 10f);
        Append(corners, 900f);
        Append(corners, 500f);

        WorldRenderer.SortTranslucent(corners, [near, far, middle], new HashSet<int> { 7, 8, 9 })
            .ShouldBe([far, middle, near]);
    }

    /// <remarks>The control: an opaque run is the opaque pass's, and must not be issued twice.</remarks>
    [Test]
    public void SortTranslucent_AnOpaqueRun_IsNotIssued()
    {
        WorldBatch glass = new(MaterialIndex: 7, FirstVertex: 0, VertexCount: 3);
        WorldBatch rock = new(MaterialIndex: 4, FirstVertex: 3, VertexCount: 3);

        List<WorldVertex> corners = [];

        Append(corners, 10f);
        Append(corners, 20f);

        WorldRenderer.SortTranslucent(corners, [glass, rock], new HashSet<int> { 7 })
            .ShouldBe([glass]);
    }

    private static void Append(List<WorldVertex> corners, float depth)
    {
        for (int at = 0; at < 3; at++)
        {
            corners.Add(new WorldVertex(0f, 0f, depth, 0f, 0f, 0f, 0f, 1f));
        }
    }
}
