using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the translucent world, the detail sprites and the translucent entities are drawn in, leaf by leaf
/// (B426, B434).
/// </summary>
/// <remarks>
/// <c>CRendering3dView::DrawTranslucentRenderables</c> (<c>viewrender.cpp:4554-4698</c>): the world list's
/// leaves are front to back, the translucent entities are in that list's leaf order, and both are walked
/// BACKWARDS. Before an entity, every leaf from the last one drawn down to and including the entity's is
/// drawn (<c>:4583</c>, <c>DrawTranslucentWorldAndDetailPropsInLeaves</c> at <c>:4302</c>); after the loop,
/// the rest down to leaf zero (<c>:4695</c>). "L3" is leaf position 3's world surfaces, "E2" entity 2,
/// "D3" leaf 3's remaining detail sprites, "B2" the sprites of entity 2's leaf farther than it (<c>:4607</c>).
/// </remarks>
public sealed class TranslucentInterleaveTests
{
    [Test]
    public void Plan_EntitiesInTwoLeaves_DrawsEachAfterItsLeafAndBeforeTheNearerLeaves()
    {
        Steps(4, [1, 1, 3]).ShouldBe(["L3", "E2", "L2", "L1", "E1", "E0", "L0"]);
    }

    [Test]
    public void Plan_WithNoEntities_DrawsEveryLeafFarthestFirst()
    {
        Steps(3, []).ShouldBe(["L2", "L1", "L0"]);
    }

    [Test]
    public void Plan_AnEntityInTheNearestLeaf_IsDrawnAfterThatLeafsSurfaces()
    {
        Steps(2, [0]).ShouldBe(["L1", "L0", "E0"]);
    }

    [Test]
    public void Plan_AnEntityInTheFarthestLeaf_IsDrawnBeforeEveryNearerLeaf()
    {
        Steps(3, [2]).ShouldBe(["L2", "E0", "L1", "L0"]);
    }

    /// <remarks>
    /// Every leaf has sprites. A leaf's sprites follow its surfaces (queued at <c>:4319</c>, flushed before the next
    /// leaf's surfaces at <c>:4309</c>); in an entity's leaf the sprites farther than each entity go before it
    /// (<c>:4607</c>) and the rest after the last (<c>:4639</c>).
    /// </remarks>
    [Test]
    public void Plan_DetailInEveryLeaf_SplitsTheEntityLeafAroundEachEntity()
    {
        Steps(3, [1, 1], detail: [0, 1, 2])
            .ShouldBe(["L2", "D2", "L1", "B1", "E1", "B0", "E0", "D1", "L0", "D0"]);
    }

    /// <remarks>
    /// An entity leaf with no sprites takes the else branch (<c>:4643</c>): no split, and the farther leaf's queued
    /// sprites have already gone before it.
    /// </remarks>
    [Test]
    public void Plan_DetailOnlyInAFartherLeaf_DrawsItBeforeTheEntityWithNoSplit()
    {
        Steps(2, [0], detail: [1]).ShouldBe(["L1", "D1", "L0", "E0"]);
    }

    private static string[] Steps(int leaves, int[] entityLeaves, int[]? detail = null)
    {
        List<InterleaveStep> steps = [];
        HashSet<int> withDetail = [.. detail ?? []];

        TranslucentInterleave.Plan(leaves, entityLeaves, withDetail.Contains, steps);

        return [.. steps.Select(step => step.Kind switch
        {
            InterleaveKind.Entity => "E",
            InterleaveKind.Detail => "D",
            InterleaveKind.DetailBeyond => "B",
            _ => "L",
        } + step.Index.ToString(CultureInfo.InvariantCulture))];
    }
}
