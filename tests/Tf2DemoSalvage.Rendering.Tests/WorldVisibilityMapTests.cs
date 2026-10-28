using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The visible leaf set on a real map, where the hand-built trees cannot answer.
/// </summary>
/// <remarks>
/// **`WorldVisibilityTests` proves the walk does what it was told; this proves it survives contact
/// with a compiled map.** The two catch different things. A hand-built tree of three nodes never
/// exercises a stride, a cluster count, a compressed PVS row, or a tree deep enough to matter — and
/// a walk that got any of those wrong would pass every case there.
///
/// **The assertions are self-locating rather than hard-coded coordinates.** A camera position typed
/// in by hand is a fact about one map at one moment; picking a leaf out of the map and standing in
/// the middle of it is a fact about any map. That also means these read the same way if the map is
/// ever swapped.
/// </remarks>
public sealed class WorldVisibilityMapTests
{
    private static (BspLeafTree Tree, BspVisibility Pvs) Map()
    {
        ReadOnlyMemory<byte> file = MapCache.Bytes();

        return (BspLeafTree.Read(file), BspVisibility.Read(file));
    }

    /// <summary>A leaf with a real cluster and some size to it, and where its middle is.</summary>
    /// <remarks>
    /// **Skips the tiny and the clusterless deliberately.** A leaf `vvis` gave no cluster to is
    /// solid space, and standing inside one would test the fallback rather than the PVS. Requiring
    /// 64 units across avoids the slivers a BSP produces at corners, whose centre can round to a
    /// neighbouring leaf.
    /// </remarks>
    private static (int Leaf, (float X, float Y, float Z) Middle) SomewhereInside(BspLeafTree tree)
    {
        for (int leaf = 1; leaf < tree.LeafCount; leaf++)
        {
            if (tree.Cluster(leaf) < 0 || tree.Bounds(leaf) is not { } box)
            {
                continue;
            }

            if (box.Max.X - box.Min.X < 64f ||
                box.Max.Y - box.Min.Y < 64f ||
                box.Max.Z - box.Min.Z < 64f)
            {
                continue;
            }

            (float X, float Y, float Z) middle = (
                (box.Min.X + box.Max.X) * 0.5f,
                (box.Min.Y + box.Max.Y) * 0.5f,
                (box.Min.Z + box.Max.Z) * 0.5f);

            // The tree has to agree that the middle of the box is in the box: a leaf is convex, but
            // its stored bounds are a loose cull box and a concave arrangement of neighbours can put
            // the centre elsewhere.
            if (tree.LeafAt(middle.X, middle.Y, middle.Z) == leaf)
            {
                return (leaf, middle);
            }
        }

        Skip.Because("no leaf on this map is both clustered and roomy enough to stand in");
        return default;
    }

    /// <summary>A frustum at a point, facing a direction, with a real orthonormal basis.</summary>
    /// <remarks>
    /// **The first version of this helper wrote `right = (towards.Y, −towards.X, 0)`, which is zero
    /// for a vertical direction** — and the only test that looked straight down then failed with an
    /// empty leaf list that looked like a fault in the walk. A degenerate basis produces degenerate
    /// planes, and `ViewFrustum` leaves a zero normal alone rather than dividing by nothing, so
    /// nothing throws and the frustum silently means something else.
    ///
    /// Right is `towards × up`, falling back to the world X axis when the two are parallel, and up
    /// is recovered from the pair so all three are orthogonal whatever direction is asked for.
    /// </remarks>
    private static ViewFrustum Looking(
        (float X, float Y, float Z) from, (float X, float Y, float Z) towards)
    {
        (float X, float Y, float Z) right = Cross(towards, (0f, 0f, 1f));

        if (Length(right) < 0.001f)
        {
            right = Cross(towards, (1f, 0f, 0f));
        }

        right = Unit(right);

        return ViewFrustum.PerspectiveFromAspect(
            from,
            Unit(towards),
            right,
            Unit(Cross(right, towards)),
            nearZ: 7f,
            farZ: 28_000f,
            fovX: 90f,
            aspect: 16f / 9f);
    }

    private static (float X, float Y, float Z) Cross(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        ((left.Y * right.Z) - (left.Z * right.Y),
         (left.Z * right.X) - (left.X * right.Z),
         (left.X * right.Y) - (left.Y * right.X));

    private static float Length((float X, float Y, float Z) vector) =>
        MathF.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y) + (vector.Z * vector.Z));

    private static (float X, float Y, float Z) Unit((float X, float Y, float Z) vector)
    {
        float length = Length(vector);

        return length > 0f ? (vector.X / length, vector.Y / length, vector.Z / length) : vector;
    }

    /// <summary>That the leaf the camera stands in is always returned.</summary>
    /// <remarks>
    /// **The invariant that cannot be argued with**: whichever way you face, you can see the room
    /// you are in. It fails if the PVS row is misread (a cluster is always in its own PVS), if the
    /// leaf bounds are misread, or if the walk descends the wrong child — and it needs no knowledge
    /// of the map to state.
    /// </remarks>
    [Test]
    public void Leaves_FromInsideALeaf_AlwaysIncludeThatLeaf()
    {
        (BspLeafTree tree, BspVisibility pvs) = Map();
        (int leaf, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        WorldVisibility visibility = new(tree, pvs);

        foreach ((float X, float Y, float Z) towards in Directions)
        {
            visibility.Leaves(middle.X, middle.Y, middle.Z, Looking(middle, towards))
                .ShouldContain(leaf, $"standing in leaf {leaf}, looking {towards}");
        }
    }

    /// <summary>That most of a real map is culled from any one point.</summary>
    /// <remarks>
    /// **The measurement that says the walk is doing work rather than returning everything.** A
    /// broken PVS test, an unbuilt frustum or a cull box read as huge would all show up as a leaf
    /// count near the map's total.
    ///
    /// Half is a deliberately loose ceiling. The real figure on cp_process is far below it, and
    /// pinning the real figure would make this a change detector against Valve's map — the claim
    /// worth defending is "most of the map is not drawn", not a particular percentage.
    /// </remarks>
    [Test]
    public void Leaves_FromInsideALeaf_AreASmallFractionOfTheMap()
    {
        (BspLeafTree tree, BspVisibility pvs) = Map();
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        int visible = new WorldVisibility(tree, pvs)
            .Leaves(middle.X, middle.Y, middle.Z, Looking(middle, (1f, 0f, 0f)))
            .Count;

        visible.ShouldBeGreaterThan(0, "the camera's own leaf is at least visible");

        visible.ShouldBeLessThan(
            tree.LeafCount / 2,
            $"{visible} of {tree.LeafCount} leaves survived, which is not a cull");
    }

    /// <summary>That turning around changes what is visible.</summary>
    /// <remarks>
    /// **The control for the count above.** A cull that returned a small fixed set — the first N
    /// leaves, say — would satisfy every assertion so far. What it cannot do is return a DIFFERENT
    /// small set when the camera turns.
    ///
    /// Opposite directions rather than a small turn, because the two sets legitimately overlap: the
    /// leaf you stand in is in both, and so is anything the frustum still straddles.
    /// </remarks>
    [Test]
    public void Leaves_LookingOppositeWays_DifferFromEachOther()
    {
        (BspLeafTree tree, BspVisibility pvs) = Map();
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        WorldVisibility visibility = new(tree, pvs);

        int[] forward =
            [.. visibility.Leaves(middle.X, middle.Y, middle.Z, Looking(middle, (1f, 0f, 0f)))];

        int[] backward =
            [.. visibility.Leaves(middle.X, middle.Y, middle.Z, Looking(middle, (-1f, 0f, 0f)))];

        forward.ShouldNotBe(backward);

        // **Either way round, not forward-only** (B262). Every leaf forward saw that backward did not was a SOLID leaf
        // here; the engine's walk never lists those (`0x1800e0600`), and with them gone forward's set falls inside
        // backward's from this eye. A fixed set still fails: it would differ in neither direction.
        forward.Except(backward).Concat(backward.Except(forward))
            .ShouldNotBeEmpty("the two directions see different leaves");
    }

    /// <summary>That the PVS removes leaves the frustum alone would keep.</summary>
    /// <remarks>
    /// **The two filters are separated here because either alone would pass the tests above.** The
    /// same camera is run with the map's real PVS and with none at all; the difference is exactly
    /// what `vvis` contributes, and it must be positive on a map with walls.
    ///
    /// This is also the assertion that would catch the PVS being read for the wrong cluster — a
    /// consistently wrong row still culls a lot, but it would cull the camera's own leaf, which the
    /// first test in this file forbids. The pair pins it from both sides.
    /// </remarks>
    [Test]
    public void Leaves_WithTheMapsPvs_AreFewerThanWithoutIt()
    {
        (BspLeafTree tree, BspVisibility pvs) = Map();
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        pvs.HasData.ShouldBeTrue("cp_process is compiled with visibility");

        ViewFrustum frustum = Looking(middle, (1f, 0f, 0f));

        int withPvs = new WorldVisibility(tree, pvs)
            .Leaves(middle.X, middle.Y, middle.Z, frustum).Count;

        int withoutPvs = new WorldVisibility(tree, BspVisibility.None)
            .Leaves(middle.X, middle.Y, middle.Z, frustum).Count;

        withPvs.ShouldBeLessThan(
            withoutPvs,
            $"the PVS removed nothing: {withPvs} with it, {withoutPvs} without");
    }

    /// <summary>That a camera in solid space draws rather than going blind.</summary>
    /// <remarks>
    /// **The free camera is in solid space constantly**, and Valve's `PVSCheck` treats an unplaceable
    /// point as being in the PVS rather than out of it. So a point outside the map must still
    /// produce leaves — whatever the frustum reaches — instead of an empty list.
    ///
    /// **The camera is placed just above the world rather than at a round number, and the first
    /// version of this test was wrong for exactly that reason.** It stood at z = 30,000 and looked
    /// down, which is past the 28,000-unit far plane: the frustum genuinely never reached the map
    /// and the empty answer was correct. The failure looked like the fallback being broken and was
    /// a badly chosen condition.
    ///
    /// **Node zero's cull box is the world's bounding box**, so a point a little above its top is
    /// outside every cluster and comfortably within reach. Self-locating, and it stays right if the
    /// map changes.
    /// </remarks>
    [Test]
    public void Leaves_FromOutsideTheMap_StillReturnsWhatTheFrustumReaches()
    {
        (BspLeafTree tree, BspVisibility pvs) = Map();

        BspNode world = tree.Node(0).ShouldNotBeNull("the map has a root node");

        (float X, float Y, float Z) outside = (
            (world.Min.X + world.Max.X) * 0.5f,
            (world.Min.Y + world.Max.Y) * 0.5f,
            world.Max.Z + 512f);

        tree.ClusterAt(outside.X, outside.Y, outside.Z)
            .ShouldBeLessThan(0, "a point above the world box is in no cluster");

        new WorldVisibility(tree, pvs)
            .Leaves(outside.X, outside.Y, outside.Z, Looking(outside, (0f, 0f, -1f)))
            .ShouldNotBeEmpty("an unplaceable eye must not cull the world");
    }

    /// <summary>That pruning the walk by the PVS reaches exactly the surfaces the unpruned walk did, in order.</summary>
    /// <remarks>
    /// **The output-level check on the node prune.** The reference is the walk with no PVS at all, with its PVS-hidden
    /// leaves removed afterwards by the per-leaf rule the prune replaced (<c>PVSCheck</c>: a negative cluster passes).
    /// That walk still steps through every node the frustum keeps, so it offers each node's surfaces where the pruned
    /// walk never goes. The surfaces <see cref="VisibleWorld.Surfaces"/> reaches from the two must be the same sequence,
    /// because a node surface is drawn only once a listed leaf below it has marked it.
    ///
    /// **Many eyes, two maps** (B259 review): up to 32 eyes spread over the clustered leaves, then one above the world
    /// (cluster −1), then the first eye again and again (A→B→A and A→A on ONE pruned instance), five directions each.
    /// Each view is also walked by a fresh instance, which must agree step for step with the shared one.
    ///
    /// The controls, per view: the pruned walk never takes more node steps; from the eye in no cluster it IS the open
    /// walk, step for step; and wherever the open walk entered a node whose two children are both leaves the PVS hides
    /// (or solid), the pruned walk takes strictly fewer — that node cannot be stamped.
    /// </remarks>
    /// <param name="mapName">The map.</param>
    [TestCase("cp_process_final")]
    [TestCase("cp_badlands")]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
    public void Surfaces_ForAPvsPrunedWalk_AreTheUnprunedWalksInTheSameOrder(string mapName)
    {
        (MapLevel level, MapWorld world) = WorldCullingMapTests.Built(mapName);

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("the map has a tree");
        BspLeafFaces leafFaces = level.LeafFaces.ShouldNotBeNull("the map has leaf faces");
        BspVisibility pvs = BspVisibility.Read(MapCache.Bytes(mapName));

        pvs.HasData.ShouldBeTrue("the map is compiled with visibility, so the PVS applies");

        List<(float X, float Y, float Z)> eyes = [.. Inside(tree, 32)];

        eyes.Count.ShouldBeGreaterThan(1, "too few eyes for a cluster change");

        BspNode root = tree.Node(0).ShouldNotBeNull("the map has a root node");
        (float X, float Y, float Z) outside = (
            (root.Min.X + root.Max.X) * 0.5f, (root.Min.Y + root.Max.Y) * 0.5f, root.Max.Z + 512f);

        tree.ClusterAt(outside.X, outside.Y, outside.Z).ShouldBeLessThan(0, "the control: this eye is in no cluster");
        eyes.AddRange([outside, eyes[0], eyes[0]]);

        WorldVisibility pruned = new(tree, pvs);
        WorldVisibility open = new(tree, BspVisibility.None);
        VisibleWorld prunedWorld = new(world.FaceSpans, tree, leafFaces);
        VisibleWorld openWorld = new(world.FaceSpans, tree, leafFaces);
        (int Views, int PrunedNodes, int OpenNodes, int Surfaces, int MustShrink) total = default;

        foreach ((float X, float Y, float Z) eye in eyes)
        {
            int from = tree.ClusterAt(eye.X, eye.Y, eye.Z);

            bool Stampable(int leaf) =>
                tree.Contents(leaf) != BspLeafTree.ContentsSolid &&
                (from < 0 || tree.Cluster(leaf) < 0 || pvs.Visible(from, tree.Cluster(leaf)));

            foreach ((float X, float Y, float Z) towards in Directions.Append((0f, 0f, -1f)))
            {
                string view = $"{mapName} from {eye} (cluster {from}) looking {towards}";
                ViewFrustum frustum = Looking(eye, towards);

                pruned.Leaves(eye.X, eye.Y, eye.Z, frustum);
                open.Leaves(eye.X, eye.Y, eye.Z, frustum);

                WorldVisibility fresh = new(tree, pvs);
                fresh.Leaves(eye.X, eye.Y, eye.Z, frustum);
                pruned.Walk.ShouldBe(fresh.Walk, $"{view}: the shared instance walked unlike a fresh one");

                List<WorldWalkStep> reference = [.. open.Walk.Where(step => step.IsNode || Stampable(step.Index))];
                int prunedNodes = pruned.Walk.Count(step => step.IsNode);
                int openNodes = open.Walk.Count(step => step.IsNode);

                pruned.Walk.Where(step => !step.IsNode).ShouldBe(
                    reference.Where(step => !step.IsNode), $"{view}: the leaves differ");

                prunedNodes.ShouldBeLessThanOrEqualTo(openNodes, $"{view}: the prune added node steps");

                if (from < 0)
                {
                    pruned.Walk.ShouldBe(open.Walk, $"{view}: an eye in no cluster stamps everything");
                }

                bool mustShrink = open.Walk.Any(step =>
                    step.IsNode && tree.Node(step.Index) is { Front: < 0, Back: < 0 } split &&
                    !Stampable(-split.Front - 1) && !Stampable(-split.Back - 1));

                if (mustShrink)
                {
                    prunedNodes.ShouldBeLessThan(openNodes, $"{view}: a node with two hidden leaves was walked");
                    total.MustShrink++;
                }

                List<int> expected = [.. openWorld.Surfaces(reference, eye.X, eye.Y, eye.Z, frustum)];

                prunedWorld.Surfaces(pruned.Walk, eye.X, eye.Y, eye.Z, frustum)
                    .ShouldBe(expected, $"{view}: the reached surfaces differ");

                total = (total.Views + 1, total.PrunedNodes + prunedNodes, total.OpenNodes + openNodes,
                    total.Surfaces + expected.Count, total.MustShrink);
            }
        }

        TestContext.Out.WriteLine(
            $"{mapName}: {eyes.Count} eyes, {total.Views} views, node steps {total.PrunedNodes} pruned vs " +
            $"{total.OpenNodes} open, {total.Surfaces} surfaces compared, {total.MustShrink} views required to shrink");

        total.Surfaces.ShouldBeGreaterThan(0, "nothing is reached from any view, so this cannot fail");
        total.MustShrink.ShouldBeGreaterThan(0, "no view had a node the PVS must prune, so the prune is untested");
    }

    /// <summary>Up to <paramref name="count"/> eyes, each the middle of a clustered, roomy leaf, spread over the map.</summary>
    private static IEnumerable<(float X, float Y, float Z)> Inside(BspLeafTree tree, int count)
    {
        List<(float X, float Y, float Z)> all = [];

        for (int leaf = 1; leaf < tree.LeafCount; leaf++)
        {
            if (tree.Cluster(leaf) < 0 || tree.Bounds(leaf) is not { } box ||
                box.Max.X - box.Min.X < 64f || box.Max.Y - box.Min.Y < 64f || box.Max.Z - box.Min.Z < 64f)
            {
                continue;
            }

            (float X, float Y, float Z) middle = (
                (box.Min.X + box.Max.X) * 0.5f, (box.Min.Y + box.Max.Y) * 0.5f, (box.Min.Z + box.Max.Z) * 0.5f);

            if (tree.LeafAt(middle.X, middle.Y, middle.Z) == leaf)
            {
                all.Add(middle);
            }
        }

        int step = Math.Max(1, all.Count / count);

        return all.Where((_, at) => at % step == 0).Take(count);
    }

    private static IEnumerable<(float X, float Y, float Z)> Directions =>
    [
        (1f, 0f, 0f),
        (-1f, 0f, 0f),
        (0f, 1f, 0f),
        (0f, -1f, 0f),
    ];
}
