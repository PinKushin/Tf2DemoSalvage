using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <see cref="BoxCollation"/> asks <c>BuildRenderablesList</c>'s two questions of one box (`clientleafsystem.cpp:1574-1716`):
/// a listed leaf, then the frustum; the sky view as a second chance. A map with no tree files everything in leaf 0.
/// </summary>
public sealed class BoxCollationConformanceTests
{
    private static readonly Vector3 Low = new(-1f);
    private static readonly Vector3 High = new(1f);

    /// <summary>An unbuilt frustum culls nothing; <see cref="ViewFrustum.Nothing"/> culls everything.</summary>
    private static CollationView View(int[]? main, ViewFrustum frustum, int[]? sky = null, ViewFrustum sky3D = default) =>
        new(frustum, new RenderableViews(main, sky, sky3D), Tree: null);

    [Test]
    public void Sees_ABoxInAListedLeafInsideTheFrustum_IsTrue() =>
        new BoxCollation().Sees(View([0], default), Low, High).ShouldBeTrue();

    [Test]
    public void Sees_ABoxInNoListedLeaf_IsFalse() =>
        new BoxCollation().Sees(View([], default), Low, High).ShouldBeFalse();

    [Test]
    public void Sees_ABoxOutsideTheFrustum_IsFalse() =>
        new BoxCollation().Sees(View([0], ViewFrustum.Nothing), Low, High).ShouldBeFalse();

    [Test]
    public void Sees_WithNoViewWalk_LetsTheFrustumAloneDecide() =>
        new BoxCollation().Sees(View(null, default), Low, High).ShouldBeTrue();

    [Test]
    public void Sees_ABoxOnlyTheSkyViewLists_IsTrueInsideTheSkyFrustumAndFalseOutside()
    {
        new BoxCollation().Sees(View([], default, sky: [0]), Low, High).ShouldBeTrue();
        new BoxCollation().Sees(View([], default, sky: [0], sky3D: ViewFrustum.Nothing), Low, High).ShouldBeFalse();
    }

    [Test]
    public void Sees_ABoxTheMainFrustumCullsButTheSkyViewSees_IsTrue() =>
        new BoxCollation().Sees(View([0], ViewFrustum.Nothing, sky: [0]), Low, High).ShouldBeTrue();

    /// <remarks>
    /// **A box with no extent is unplaced** (`WorldSpaceBounds.IsPlaced`): linked to no leaf and collated by the main view
    /// without a cull, so even a view that lists nothing and a frustum that culls everything collate it. The control is the
    /// same view with a box that has extent.
    /// </remarks>
    [Test]
    public void Sees_ABoxWithNoExtent_IsCollatedUnculled()
    {
        new BoxCollation().Sees(View([], ViewFrustum.Nothing), Vector3.Zero, Vector3.Zero).ShouldBeTrue();
        new BoxCollation().Sees(View([], ViewFrustum.Nothing), Low, High).ShouldBeFalse();
    }

    /// <remarks>The leaf sets are cached by view: a second view must be answered by its own leaves, not the first's.</remarks>
    [Test]
    public void Sees_ANewView_IsAnsweredByItsOwnLeaves()
    {
        BoxCollation collation = new();

        collation.Sees(View([0], default), Low, High).ShouldBeTrue();
        collation.Sees(View([], default), Low, High).ShouldBeFalse();
    }
}
