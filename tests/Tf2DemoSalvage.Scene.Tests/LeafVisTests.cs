using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The leaf box the camera stands in, projected — <c>mat_leafvis</c>.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.LeafBoxLines</c> and had no test at all** (B188, D90). Reaching it meant
/// constructing a form and a device, so the only thing that ever exercised it was a launch someone
/// happened to look at — which is exactly how its projection shipped wrong once already.
///
/// **The matrices here are hand-built rather than taken from <see cref="FreeCamera"/>**, because a
/// camera's matrix is sixteen numbers nobody can predict by reading. These are chosen so the
/// expected clip coordinate is one division, which is what makes an exact prediction possible.
/// </remarks>
public sealed class LeafVisTests
{
    [Test]
    public void Edges_ABoxWhollyInFront_AreItsTwelve()
    {
        // A box has twelve edges: every pair of corners differing in exactly one axis. A projector
        // that emitted each edge from both ends would give 24, and one that walked faces would give
        // 24 as well — so the count alone separates three plausible implementations.
        LeafVis.Edges(((-64f, -64f, 16f), (64f, 64f, 128f)), WIsZ).Count.ShouldBe(12);
    }

    [Test]
    public void Edges_AMatrixTakingWFromElementEleven_ProjectAsARowVector()
    {
        // **THE TEST THIS TYPE EXISTS FOR.** The original indexed the matrix as a column-vector
        // transform, taking w from elements 12-15. That does not fail — it produces A projection,
        // and the owner saw the box as "a dot that gets kinda triangular", which is a room-sized
        // box collapsed through the wrong transform.
        //
        // `WIsZ` is built so the two readings are DECIDABLE: w comes from element 11, and element
        // 15 is zero. Read as a row vector every corner has w = z and projects; read as a column
        // vector every corner has w = 0 and is dropped as behind the eye. Correct gives twelve
        // segments with known coordinates, broken gives none.
        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> edges =
            LeafVis.Edges(((2f, 4f, 2f), (6f, 8f, 10f)), WIsZ);

        edges.Count.ShouldBe(12);

        // Corner 0 is (2,4,2) -> (2/2, 4/2); corner 1 differs in X only, (6,4,2) -> (6/2, 4/2).
        edges.ShouldContain(((1f, 2f), (3f, 2f)));
    }

    [Test]
    public void Edges_CornersBehindTheEye_AreDroppedWithTheEdgesTouchingThem()
    {
        // Dividing by a w at or below zero MIRRORS the point through the camera, and the edge then
        // streaks across the screen from somewhere it is not. Dropping is the only safe answer.
        //
        // The prediction is exact rather than "fewer": with w = z and the box straddling z = 0, the
        // four corners at z = -32 go and the four at z = +32 stay. Of the twelve edges, the four on
        // the far face have both ends kept, the four on the near face have both dropped, and the
        // four running along z have one each. FOUR survive.
        LeafVis.Edges(((-64f, -64f, -32f), (64f, 64f, 32f)), WIsZ).Count.ShouldBe(4);
    }

    [Test]
    public void Edges_ABoxWhollyBehindTheEye_AreNone()
    {
        LeafVis.Edges(((-64f, -64f, -128f), (64f, 64f, -16f)), WIsZ).ShouldBeEmpty();
    }

    [Test]
    public void Edges_WithNoMatrix_Refuses()
    {
        Should.Throw<ArgumentNullException>(() =>
            LeafVis.Edges(((0f, 0f, 0f), (1f, 1f, 1f)), viewProjection: null!));
    }

    [Test]
    public void Edges_WithTooFewElements_Refuses()
    {
        // A caller that hands over a 3x3 or a partially-filled buffer gets told, rather than an
        // IndexOutOfRangeException from inside the projection with no clue whose fault it was.
        Should.Throw<ArgumentException>(() =>
            LeafVis.Edges(((0f, 0f, 0f), (1f, 1f, 1f)), new float[15]));
    }

    [Test]
    public void Lines_WithNoTree_AreNone()
    {
        // The control for the two below: a map that carried no tree, which is every map until one
        // is loaded. Not an error — there is simply no leaf to be in.
        LeafVis.Lines(tree: null, (0f, 0f, 64f), WIsZ).ShouldBeEmpty();
    }

    [Test]
    public void Lines_ALeafWithNoBoundsLump_AreNone()
    {
        // A tree built without the leaf lump can still say WHICH leaf a point is in — the walk only
        // needs nodes and planes — but it cannot say how big that leaf is. Answering with a box at
        // the origin would draw a lie; answering with nothing draws no annotation.
        LeafVis.Lines(OneSplit(above: 1, below: 2), (0f, 0f, 64f), WIsZ).ShouldBeEmpty();
    }

    [Test]
    public void Lines_AnEyeAboveTheSplit_AreTheBoxOfTheLeafItIsIn()
    {
        // The whole path: walk the tree to a leaf, read that leaf's box, project it. The eye is at
        // z = +64, so the walk must reach leaf 1 rather than leaf 2 — and leaf 2 is given a
        // DIFFERENT box, so a walk that picked the wrong side produces different coordinates rather
        // than the same ones.
        BspLeafTree tree = OneSplit(
            above: 1,
            below: 2,
            box: (1, (-64, -64, 16), (64, 64, 128)),
            other: (2, (-8, -8, -128), (8, 8, -16)));

        IReadOnlyList<((float X, float Y) From, (float X, float Y) To)> lines =
            LeafVis.Lines(tree, (0f, 0f, 64f), WIsZ);

        lines.Count.ShouldBe(12);

        // Corner 0 is (-64,-64,16) -> (-64/16, -64/16); corner 1 differs in X, (64,-64,16).
        lines.ShouldContain(((-4f, -4f), (4f, -4f)));
    }

    [Test]
    public void Lines_AnEyeBelowTheSplit_AreTheOtherLeafsBox()
    {
        // The bystander for the test above. Leaf 2 sits entirely behind the eye under `WIsZ`, so
        // its box projects to nothing — which is a different observation from leaf 1's twelve, and
        // therefore proves the walk chose a side rather than always answering the same way.
        BspLeafTree tree = OneSplit(
            above: 1,
            below: 2,
            box: (1, (-64, -64, 16), (64, 64, 128)),
            other: (2, (-8, -8, -128), (8, 8, -16)));

        LeafVis.Lines(tree, (0f, 0f, -64f), WIsZ).ShouldBeEmpty();
    }

    [Test]
    public void Lines_WithNoMatrix_Refuses()
    {
        Should.Throw<ArgumentNullException>(() =>
            LeafVis.Lines(tree: null, (0f, 0f, 0f), viewProjection: null!));
    }

    /// <summary>A view-projection whose w is the point's z, and whose x and y pass through.</summary>
    /// <remarks>
    /// Read as a ROW vector — which is what the shader does, `mul(world, viewProjection)` with the
    /// matrix declared `row_major` — element 11 supplies w. `FreeCamera.ToMatrix` sets
    /// `projection[11] = 1` for exactly that reason, and element 15 stays zero, which is the
    /// giveaway that separates the two conventions.
    /// </remarks>
    private static float[] WIsZ
    {
        get
        {
            float[] matrix = new float[16];

            matrix[0] = 1f;   // x' <- x
            matrix[5] = 1f;   // y' <- y
            matrix[11] = 1f;  // w  <- z

            return matrix;
        }
    }

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
