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

    /// <remarks>
    /// **The 3D skybox view collates and draws its own** (`CSkyboxView::DrawInternal`, `viewrender.cpp:4869-5030`): a box only
    /// the sky view lists is the sky view's, inside its frustum, and not the main view's.
    /// </remarks>
    [Test]
    public void SeesInSky_ABoxOnlyTheSkyViewLists_IsTrueInsideTheSkyFrustumAndFalseOutside()
    {
        new BoxCollation().SeesInSky(View([], default, sky: [0]), Low, High).ShouldBeTrue();
        new BoxCollation().SeesInSky(View([], default, sky: [0], sky3D: ViewFrustum.Nothing), Low, High).ShouldBeFalse();
        new BoxCollation().Sees(View([], default, sky: [0]), Low, High).ShouldBeFalse("the main view lists no leaf of it");
    }

    /// <remarks>
    /// **The monitor collates its own** (B711): a box only the monitor's list holds is the monitor's, inside its frustum, and
    /// not the main view's; with no monitor, nothing is.
    /// </remarks>
    [Test]
    public void SeesInMonitor_ABoxOnlyTheMonitorLists_IsTrueInsideItsFrustumAndFalseOutside()
    {
        CollationView seen = new(default, new RenderableViews([], null, default, Monitor: new RenderableView([0], default)), Tree: null);
        CollationView culled = new(
            default, new RenderableViews([], null, default, Monitor: new RenderableView([0], ViewFrustum.Nothing)), Tree: null);

        new BoxCollation().SeesInMonitor(seen, Low, High).ShouldBeTrue();
        new BoxCollation().SeesInMonitor(culled, Low, High).ShouldBeFalse();
        new BoxCollation().SeesInMonitor(View([0], default), Low, High).ShouldBeFalse("no monitor this frame");
        new BoxCollation().Sees(seen, Low, High).ShouldBeFalse("the main view lists no leaf of it");
    }

    /// <remarks>The sky view never collates an unplaced box (`includeUnplaced: false`), which the main view collates unculled.</remarks>
    [Test]
    public void SeesInSky_ABoxWithNoExtent_IsFalse() =>
        new BoxCollation().SeesInSky(View([], default, sky: [0]), Vector3.Zero, Vector3.Zero).ShouldBeFalse();

    /// <remarks>
    /// **A water view collates its own** (B711): `DrawSetup` builds each view's renderable lists over its own pruned leaves
    /// under its own frustum (`viewrender.cpp:5411-5440`), and the view draws them. So a box only a water view lists is that
    /// view's, inside its frustum and not outside it, and the main and sky views do not draw it.
    /// </remarks>
    [Test]
    public void SeesInWater_ABoxOnlyAWaterViewLists_IsTrueInsideItsFrustumAndFalseOutside()
    {
        CollationView seen = new(default, new RenderableViews([], null, default, [new RenderableView([0], default)]), Tree: null);
        CollationView culled = new(
            default, new RenderableViews([], null, default, [new RenderableView([0], ViewFrustum.Nothing)]), Tree: null);

        new BoxCollation().SeesInWater(seen, 0, Low, High).ShouldBeTrue();
        new BoxCollation().SeesInWater(culled, 0, Low, High).ShouldBeFalse();
        new BoxCollation().Sees(seen, Low, High).ShouldBeFalse("the main and sky views list no leaf of it");
    }

    /// <remarks>A water view never collates an unplaced box (`includeUnplaced: false`), which the main view collates unculled.</remarks>
    [Test]
    public void SeesInWater_ABoxWithNoExtent_IsFalse()
    {
        CollationView view = new(default, new RenderableViews([], null, default, [new RenderableView([0], default)]), Tree: null);

        new BoxCollation().SeesInWater(view, 0, Vector3.Zero, Vector3.Zero).ShouldBeFalse();
        new BoxCollation().SeesInWater(view, 1, Low, High).ShouldBeFalse("no second water view");
    }

    [Test]
    public void Sees_ABoxTheMainFrustumCullsButTheSkyViewSees_IsFalse() =>
        new BoxCollation().Sees(View([0], ViewFrustum.Nothing, sky: [0]), Low, High).ShouldBeFalse("the sky view draws it");

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
