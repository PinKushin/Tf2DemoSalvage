using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The leaf box the camera stands in — <c>mat_leafvis</c>.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.LeafBoxLines</c> and had no test at all** (B188, D90). Reaching it meant
/// constructing a form and a device, so the only thing that ever exercised it was a launch someone
/// happened to look at — which is how its projection shipped wrong once already.
///
/// **The projection is gone from this type entirely** (D95). It used to multiply eight corners
/// through the view matrix here and return clip-space pairs; the engine transforms debug lines on
/// the GPU and so do we now. What is left is arithmetic about a box, which needs no camera — and
/// that makes these tests describe geometry rather than a transform.
/// </remarks>
public sealed class LeafVisTests
{
    [Test]
    public void WhyNothing_WithNoMapLoaded_SaysSoRatherThanBlamingTheTree()
    {
        // **This was `MainForm.WhyNoLeafBox`** (B208). Three cases and not two, deliberately: "no
        // map" and "a map with no tree" are different problems with different fixes — one is a demo
        // whose map could not be found, the other is a map whose tree we failed to read. Collapsing
        // them sends the reader to the wrong half.
        LeafVis.WhyNothing(mapLoaded: false, tree: null)
            .ShouldContain("no map loaded");
    }

    [Test]
    public void WhyNothing_WithAMapButNoTree_BlamesTheTree()
    {
        LeafVis.WhyNothing(mapLoaded: true, tree: null)
            .ShouldContain("no BSP tree");
    }

    [Test]
    public void WhyNothing_WithATreeThatHasNoBounds_BlamesTheLeaf()
    {
        // **The case the other two cannot reach, and the reason `mapLoaded` is a separate argument.**
        // A tree built without the leaf lump still answers WHICH leaf a point is in — the walk needs
        // only nodes and planes — so this is a real state, not a hypothetical.
        // No `box` argument, so the tree has nodes and planes but no leaf bounds — which is exactly
        // the state `Lines` returns empty for.
        BspLeafTree tree = OneSplit(above: 1, below: 2);

        LeafVis.WhyNothing(mapLoaded: true, tree)
            .ShouldContain("no bounds");
    }

    [Test]
    public void Edges_ABox_AreItsTwelve()
    {
        // A box has twelve edges: every pair of corners differing in exactly one axis. A builder
        // that emitted each edge from both ends would give 24, and one that walked the six faces
        // would give 24 as well — so the count alone separates three plausible implementations.
        LeafVis.Edges(((-64f, -64f, 16f), (64f, 64f, 128f))).Count.ShouldBe(12);
    }

    [Test]
    public void Edges_ABox_UseOnlyItsOwnCorners()
    {
        // Every endpoint must be one of the eight corners: each coordinate is either the minimum or
        // the maximum on its axis, never an average or an off-by-one neighbour. A transposed axis
        // would still give twelve edges and would fail here.
        ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) box =
            ((-64f, -32f, 16f), (64f, 32f, 128f));

        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in
            LeafVis.Edges(box))
        {
            foreach ((float X, float Y, float Z) corner in new[] { from, to })
            {
                corner.X.ShouldBeOneOf(box.Min.X, box.Max.X);
                corner.Y.ShouldBeOneOf(box.Min.Y, box.Max.Y);
                corner.Z.ShouldBeOneOf(box.Min.Z, box.Max.Z);
            }
        }
    }

    [Test]
    public void Edges_EveryOne_JoinsCornersDifferingOnASingleAxis()
    {
        // **This is what makes them EDGES rather than diagonals.** A box's twelve edges each change
        // one coordinate; a face diagonal changes two and a body diagonal three. Without this, a
        // builder that paired every corner with every other would pass the count test by accident
        // if it also happened to stop at twelve.
        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in
            LeafVis.Edges(((-64f, -32f, 16f), (64f, 32f, 128f))))
        {
            int changed =
                (Same(from.X, to.X) ? 0 : 1) +
                (Same(from.Y, to.Y) ? 0 : 1) +
                (Same(from.Z, to.Z) ? 0 : 1);

            changed.ShouldBe(1);
        }
    }

    [Test]
    public void Edges_TheTwelve_AreFourAlongEachAxis()
    {
        // A box has four edges parallel to each axis. This is the control for the test above: that
        // one proves each edge changes ONE coordinate, this proves they are not all the same one.
        List<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> edges =
            [.. LeafVis.Edges(((-64f, -32f, 16f), (64f, 32f, 128f)))];

        edges.Count(edge => !Same(edge.From.X, edge.To.X)).ShouldBe(4);
        edges.Count(edge => !Same(edge.From.Y, edge.To.Y)).ShouldBe(4);
        edges.Count(edge => !Same(edge.From.Z, edge.To.Z)).ShouldBe(4);
    }

    [Test]
    public void Edges_ABoxBehindTheCamera_AreStillAllTwelve()
    {
        // **The old version dropped these, and that was a consequence of projecting on the CPU.**
        // Dividing by a w at or below zero mirrors a point through the camera, so an edge with an
        // end behind the eye had to be discarded whole. The GPU clips properly, so a box behind the
        // viewer is simply not drawn and one crossing the near plane is drawn up to it.
        LeafVis.Edges(((-64f, -64f, -128f), (64f, 64f, -16f))).Count.ShouldBe(12);
    }

    [Test]
    public void Lines_WithNoTree_AreNone()
    {
        // The control for the two below: a map that carried no tree, which is every map until one
        // is loaded. Not an error — there is simply no leaf to be in.
        LeafVis.Lines(tree: null, (0f, 0f, 64f)).ShouldBeEmpty();
    }

    [Test]
    public void Lines_ALeafWithNoBoundsLump_AreNone()
    {
        // A tree built without the leaf lump can still say WHICH leaf a point is in — the walk only
        // needs nodes and planes — but it cannot say how big that leaf is. Answering with a box at
        // the origin would draw a lie; answering with nothing draws no annotation.
        LeafVis.Lines(OneSplit(above: 1, below: 2), (0f, 0f, 64f)).ShouldBeEmpty();
    }

    [Test]
    public void Lines_AnEyeAboveTheSplit_AreTheBoxOfTheLeafItIsIn()
    {
        // The whole path: walk the tree to a leaf, read that leaf's box, emit its edges. The eye is
        // at z = +64, so the walk must reach leaf 1 rather than leaf 2 — and leaf 2 is given a
        // DIFFERENT box, so a walk that picked the wrong side produces different coordinates rather
        // than the same ones.
        BspLeafTree tree = OneSplit(
            above: 1,
            below: 2,
            box: (1, (-64, -64, 16), (64, 64, 128)),
            other: (2, (-8, -8, -128), (8, 8, -16)));

        IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> lines =
            LeafVis.Lines(tree, (0f, 0f, 64f));

        lines.Count.ShouldBe(12);

        // Corner 0 is the minimum corner; corner 1 differs in X only.
        lines.ShouldContain(((-64f, -64f, 16f), (64f, -64f, 16f)));
    }

    [Test]
    public void Lines_AnEyeBelowTheSplit_AreTheOtherLeafsBox()
    {
        // The bystander for the test above. Same tree, same call, the other side of the plane — and
        // the box that comes back must be leaf 2's, not leaf 1's. Without this, "walked the tree"
        // and "always answered with the first leaf" are indistinguishable.
        BspLeafTree tree = OneSplit(
            above: 1,
            below: 2,
            box: (1, (-64, -64, 16), (64, 64, 128)),
            other: (2, (-8, -8, -128), (8, 8, -16)));

        LeafVis.Lines(tree, (0f, 0f, -64f))
            .ShouldContain(((-8f, -8f, -128f), (8f, -8f, -128f)));
    }

    /// <summary>Whether two coordinates are bit-for-bit the same copied value.</summary>
    /// <remarks>
    /// **Compared as BITS rather than as floats, and that is the honest form of this question.**
    /// These coordinates are not computed: <c>Corner</c> copies them straight out of the box's
    /// minimum or maximum, so two endpoints on one axis either came from the same corner or they did
    /// not. That is an identity question, not a numeric one.
    ///
    /// It also satisfies S1244 without a suppression. The analyzer objects to `==` and to
    /// `float.Equals` alike, and it is right to in general — a tolerance would be wrong HERE, since
    /// a genuinely thin leaf would read as degenerate and thin leaves are ordinary in a BSP.
    /// </remarks>
    private static bool Same(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    /// <summary>A tree of one node splitting on the z = 0 plane, optionally with leaf boxes.</summary>
    /// <remarks>
    /// The same shape as <c>BspLeafTreeTests.OneSplit</c> and <c>LevelLightingTests.OneSplit</c>: a
    /// real BSP cannot say which leaf is the right answer without already trusting the walk, so the
    /// fixture has to be small enough to reason about by hand.
    ///
    /// Bounds are three shorts at offset 8 and three more at 14, in a 32-byte leaf — the offsets
    /// `dleaf_t` and `dleaf_version_0_t` share, which is why one reader serves both.
    /// </remarks>
    private static BspLeafTree OneSplit(
        int above,
        int below,
        (int Leaf, (short X, short Y, short Z) Min, (short X, short Y, short Z) Max)? box = null,
        (int Leaf, (short X, short Y, short Z) Min, (short X, short Y, short Z) Max)? other = null)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -above - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -below - 1);

        if (box is null)
        {
            return BspLeafTree.FromLumps(node, plane);
        }

        byte[] leaves = new byte[128];

        foreach ((int leaf, (short X, short Y, short Z) min, (short X, short Y, short Z) max) in
            new[] { box.Value, other }.Where(entry => entry is not null).Select(entry => entry!.Value))
        {
            int at = leaf * 32;

            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 8), min.X);
            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 10), min.Y);
            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 12), min.Z);
            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 14), max.X);
            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 16), max.Y);
            BinaryPrimitives.WriteInt16LittleEndian(leaves.AsSpan(at + 18), max.Z);
        }

        return BspLeafTree.FromLumps(node, plane, leaves);
    }
}
