using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CClientLeafSystem</c>'s structure (source-sdk-2013 <c>src/game/client/clientleafsystem.cpp</c>), each test
/// citing the lines it states (B262).
/// </summary>
/// <remarks>
/// The tree is a row of leaves along +X, leaf <c>n</c> spanning <c>[100n, 100n+100)</c>, so which leaves a box is in
/// is arithmetic the test can see. Every box is a cube 20 units high and deep unless the test says otherwise.
/// </remarks>
public sealed class ClientLeafSystemConformanceTests
{
    private const int Leaves = 8;

    private static ClientLeafSystem Row() =>
        new(Leaves, static (min, max, into) =>
        {
            for (int leaf = (int)(min.X / 100f); leaf <= (int)(max.X / 100f) && leaf < Leaves; leaf++)
            {
                into.Add(leaf);
            }
        });

    private static (float, float, float, float, float, float) Span(float fromX, float toX) =>
        (fromX, -10f, -10f, toX, 10f, 10f);

    private static readonly int[] AllLeaves = [0, 1, 2, 3, 4, 5, 6, 7];

    private static CollatedRenderables Build(
        ClientLeafSystem system, IReadOnlyList<int> leaves, System.Func<int, LeafRenderGroup>? groupOf = null,
        ViewFrustum frustum = default)
    {
        CollatedRenderables into = new();

        system.BuildRenderablesList(leaves, frustum, groupOf ?? (static _ => LeafRenderGroup.Opaque), into);

        return into;
    }

    /// <summary>
    /// <c>PreRender</c> inserts the dirty list BACKWARDS (<c>:557-560</c>) and each insert goes to the head of the
    /// leaf (<c>utlbidirectionalset.h:205-208</c>), so two renderables added in one frame are walked in add order.
    /// </summary>
    [Test]
    public void RenderablesInLeaf_AfterTwoAddsInOneFrame_AreInAddOrder()
    {
        ClientLeafSystem system = Row();
        int first = system.AddRenderable(Span(10f, 20f));
        int second = system.AddRenderable(Span(30f, 40f));

        system.PreRender();

        system.RenderablesInLeaf(0).ShouldBe([first, second]);
    }

    /// <summary>A renderable that moves is re-linked at the HEAD of its new leaf (<c>:1274</c>, <c>:550</c>, <c>:559</c>).</summary>
    [Test]
    public void RenderablesInLeaf_AfterTheSecondMovesAwayAndBack_HoldsItAtTheHead()
    {
        ClientLeafSystem system = Row();
        int first = system.AddRenderable(Span(10f, 20f));
        int second = system.AddRenderable(Span(30f, 40f));
        system.PreRender();

        system.RenderableChanged(second, Span(130f, 140f));
        system.PreRender();
        system.RenderableChanged(second, Span(50f, 60f));
        system.PreRender();

        system.RenderablesInLeaf(0).ShouldBe([second, first]);
        system.RenderablesInLeaf(1).ShouldBeEmpty();
    }

    /// <summary><c>RenderableChanged</c> is what dirties (<c>:1281-1285</c>); an unmoved renderable is not re-linked.</summary>
    [Test]
    public void PreRender_ForAnUnmovedRenderable_RelinksNothing()
    {
        ClientLeafSystem system = Row();
        int one = system.AddRenderable(Span(10f, 20f));
        system.PreRender();
        system.Relinked.ShouldBe(1);

        system.RenderableChanged(one, Span(10f, 20f));
        system.PreRender();

        system.Relinked.ShouldBe(0);
    }

    /// <summary>A box over a leaf boundary is in both leaves — <c>EnumerateLeavesInBox</c> (<c>:1239</c>).</summary>
    [Test]
    public void RenderablesInLeaf_ForABoxAcrossABoundary_HoldItInBoth()
    {
        ClientLeafSystem system = Row();
        int straddling = system.AddRenderable(Span(90f, 110f));
        system.PreRender();

        system.RenderablesInLeaf(0).ShouldBe([straddling]);
        system.RenderablesInLeaf(1).ShouldBe([straddling]);
    }

    /// <summary>
    /// <c>m_RenderFrame2</c> (<c>:1607-1613</c>): an opaque renderable in two listed leaves is collated once, at the
    /// first of them in LIST order — not the lower leaf number.
    /// </summary>
    [Test]
    public void BuildRenderablesList_ForAnOpaqueRenderableInTwoListedLeaves_CollatesItOnceAtTheFirstListed()
    {
        ClientLeafSystem system = Row();
        int straddling = system.AddRenderable(Span(90f, 110f));
        system.PreRender();

        CollatedRenderables collated = Build(system, [1, 0]);

        collated.Opaque(ClientLeafSystem.BucketFor(20f)).ShouldBe([(straddling, 0)]);
        collated.Collated.ShouldBe([straddling]);
    }

    /// <summary>
    /// Leaves are collated in the list's order (<c>:1822-1827</c>), so within a bucket a renderable in an earlier
    /// listed leaf comes first whatever order it was added in.
    /// </summary>
    [Test]
    public void BuildRenderablesList_ForTwoLeavesListedFarFirst_CollatesTheFarLeafFirst()
    {
        ClientLeafSystem system = Row();
        int near = system.AddRenderable(Span(10f, 20f));
        int far = system.AddRenderable(Span(210f, 220f));
        system.PreRender();

        Build(system, [2, 0]).Opaque(3).ShouldBe([(far, 0), (near, 1)]);
    }

    /// <summary>A leaf not in the list contributes nothing — the list IS the visibility (<c>:1822</c>).</summary>
    [Test]
    public void BuildRenderablesList_ForARenderableOnlyInAnUnlistedLeaf_CollatesNothing()
    {
        ClientLeafSystem system = Row();
        _ = system.AddRenderable(Span(310f, 320f));
        int listed = system.AddRenderable(Span(10f, 20f));
        system.PreRender();

        Build(system, [0, 1, 2]).Collated.ShouldBe([listed]);
    }

    /// <summary>
    /// <c>ComputeTranslucentRenderLeaf</c> (<c>:1444-1455</c>) gives a translucent renderable the first listed leaf it
    /// is in, and <c>CollateRenderablesInLeaf</c> collates it only there (<c>:1620</c>).
    /// </summary>
    [Test]
    public void BuildRenderablesList_ForATranslucentRenderableInTwoListedLeaves_CollatesItAtTheNearest()
    {
        ClientLeafSystem system = Row();
        int glass = system.AddRenderable(Span(150f, 250f));
        system.PreRender();

        CollatedRenderables collated = Build(system, [0, 2, 1], static _ => LeafRenderGroup.Translucent);

        collated.Translucent.ShouldBe([(glass, 1, false)]);
        Enumerable.Range(0, ClientLeafSystem.BucketCount).SelectMany(collated.Opaque).ShouldBeEmpty();
    }

    /// <summary>
    /// A two-pass renderable joins its translucent list AND <c>RENDER_GROUP_OPAQUE_ENTITY</c> (<c>:1710-1713</c>), which
    /// is the smallest bucket whatever its size — <c>DetectBucketedRenderGroup</c> is not asked for it.
    /// </summary>
    [Test]
    public void BuildRenderablesList_ForATreeSizedTwoPassRenderable_AddsItToTheLastOpaqueBucket()
    {
        ClientLeafSystem system = Row();
        int big = system.AddRenderable((10f, -150f, -150f, 90f, 150f, 150f));
        system.PreRender();

        CollatedRenderables collated = Build(system, [0], static _ => LeafRenderGroup.TwoPass);

        collated.Translucent.ShouldBe([(big, 0, true)]);
        collated.Opaque(0).ShouldBeEmpty();
        collated.Opaque(3).ShouldBe([(big, 0)]);
    }

    /// <summary>
    /// The bucket is the WORLD box's longest axis (<c>:1687-1691</c>) against 200, 80 and 30 (<c>:1540-1544</c>),
    /// <c>&gt;=</c> taking the larger.
    /// </summary>
    [TestCase(200f, 0)]
    [TestCase(199f, 1)]
    [TestCase(80f, 1)]
    [TestCase(30f, 2)]
    [TestCase(29f, 3)]
    public void BuildRenderablesList_ForAnOpaqueBox_BucketsByItsLongestAxis(float height, int bucket)
    {
        ClientLeafSystem system = Row();
        int one = system.AddRenderable((10f, -10f, 0f, 20f, 10f, height));
        system.PreRender();

        Build(system, [0]).Opaque(bucket).ShouldBe([(one, 0)]);
    }

    /// <summary><c>engine->CullBox</c> (<c>:1647</c>): a box outside the frustum is not collated.</summary>
    [Test]
    public void BuildRenderablesList_ForABoxBehindTheCamera_DropsIt()
    {
        ClientLeafSystem system = Row();
        _ = system.AddRenderable(Span(10f, 20f));
        int ahead = system.AddRenderable(Span(410f, 420f));
        system.PreRender();

        ViewFrustum looking = ViewFrustum.PerspectiveFromAspect(
            origin: (300f, 0f, 0f), forward: (1f, 0f, 0f), right: (0f, -1f, 0f), up: (0f, 0f, 1f),
            nearZ: 7f, farZ: 1000f, fovX: 90f, aspect: 1f);

        Build(system, AllLeaves, frustum: looking).Collated.ShouldBe([ahead]);
    }

    /// <summary>An invisible renderable — alpha 0 — is skipped (<c>:1630-1632</c>).</summary>
    [Test]
    public void BuildRenderablesList_ForAnInvisibleRenderable_DropsIt()
    {
        ClientLeafSystem system = Row();
        int hidden = system.AddRenderable(Span(10f, 20f));
        int shown = system.AddRenderable(Span(30f, 40f));
        system.PreRender();

        Build(system, [0], handle => handle == hidden ? LeafRenderGroup.None : LeafRenderGroup.Opaque)
            .Collated.ShouldBe([shown]);
    }

    /// <summary><c>RemoveRenderable</c> (<c>:724-752</c>) takes it out of every leaf.</summary>
    [Test]
    public void RemoveRenderable_ForALinkedRenderable_LeavesEveryLeaf()
    {
        ClientLeafSystem system = Row();
        int gone = system.AddRenderable(Span(90f, 110f));
        system.PreRender();

        system.RemoveRenderable(gone);

        system.RenderablesInLeaf(0).ShouldBeEmpty();
        system.RenderablesInLeaf(1).ShouldBeEmpty();
    }

    /// <summary>
    /// Ours: a box with no extent is never linked and never culled, collated at place 0 — an empty box says nothing
    /// about where its model is.
    /// </summary>
    [Test]
    public void BuildRenderablesList_ForAnUnplacedBox_CollatesItAtPlaceZeroUnculled()
    {
        ClientLeafSystem system = Row();
        int bare = system.AddRenderable(default);
        system.PreRender();

        ViewFrustum awayFromOrigin = ViewFrustum.PerspectiveFromAspect(
            origin: (300f, 0f, 0f), forward: (1f, 0f, 0f), right: (0f, -1f, 0f), up: (0f, 0f, 1f),
            nearZ: 7f, farZ: 1000f, fovX: 90f, aspect: 1f);

        Build(system, [3], frustum: awayFromOrigin).Opaque(3).ShouldBe([(bare, 0)]);
    }
}
