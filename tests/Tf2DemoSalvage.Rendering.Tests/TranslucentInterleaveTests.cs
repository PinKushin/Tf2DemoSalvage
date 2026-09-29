using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the translucent world and the translucent entities are drawn in, leaf by leaf (B426).
/// </summary>
/// <remarks>
/// <c>CRendering3dView::DrawTranslucentRenderables</c> (<c>viewrender.cpp:4554-4695</c>): the world list's
/// leaves are front to back, the translucent entities are in that list's leaf order, and both are walked
/// BACKWARDS. Before an entity, every leaf from the last one drawn down to and including the entity's is
/// drawn (<c>:4583</c>, <c>DrawTranslucentWorldAndDetailPropsInLeaves</c> at <c>:4302</c>); after the loop,
/// the rest down to leaf zero (<c>:4695</c>). "L3" is leaf position 3's world surfaces, "E2" entity 2.
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

    private static string[] Steps(int leaves, int[] entityLeaves)
    {
        List<InterleaveStep> steps = [];

        TranslucentInterleave.Plan(leaves, entityLeaves, steps);

        return [.. steps.Select(step => (step.IsEntity ? "E" : "L") + step.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))];
    }
}
