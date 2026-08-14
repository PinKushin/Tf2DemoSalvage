using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Walking the BSP tree to find the leaf a point is in.
/// </summary>
/// <remarks>
/// **Transcribed from <c>PointLeafnum</c>**, and the encoding is the part worth testing: a node's
/// child is either another node index or a leaf, and a leaf is stored as <c>-(leaf + 1)</c>. Read
/// as an index it walks off into the node array and answers with confidence.
///
/// Built by hand rather than from a map, because a real BSP cannot say which answer is right
/// without already trusting this code.
/// </remarks>
public sealed class BspLeafTreeTests
{
    [Test]
    public void APointInFrontOfThePlane_TakesTheFirstChild()
    {
        // One node splitting on z = 0, with leaf 3 above and leaf 7 below. Valve's comparison sends
        // "in front" - dot product greater than the distance - to child zero.
        BspLeafTree tree = OneSplit(above: 3, below: 7);

        tree.LeafAt(0f, 0f, 100f).ShouldBe(3);
    }

    [Test]
    public void APointBehindThePlane_TakesTheSecondChild()
    {
        BspLeafTree tree = OneSplit(above: 3, below: 7);

        tree.LeafAt(0f, 0f, -100f).ShouldBe(7);
    }

    [Test]
    public void APointExactlyOnThePlane_GoesBehindIt()
    {
        // **Valve's boundary, kept deliberately.** The comparison is "<= dist", so a point on the
        // plane takes the same side as one behind it. A model standing exactly on a floor plane is
        // not a rare case, and disagreeing here would light it from the wrong leaf.
        BspLeafTree tree = OneSplit(above: 3, below: 7);

        tree.LeafAt(0f, 0f, 0f).ShouldBe(7);
    }

    [Test]
    public void AMapWithNoTree_HasNoLeaves()
    {
        // Answering leaf zero would be worse than answering nothing: leaf zero is a real leaf with
        // real light, so a map with no tree would silently light everything from it.
        BspLeafTree tree = BspLeafTree.FromLumps(default, default);

        tree.IsEmpty.ShouldBeTrue();
        tree.LeafAt(0f, 0f, 0f).ShouldBe(-1);
    }

    [Test]
    public void APointUnderSolid_CannotSeeTheSky()
    {
        // **Valve's parenthesis is the whole feature.** A sky light is a "directional light with
        // no falloff (surface must trace to SKY texture)", so without the trace the sun shines
        // through every ceiling and lights the inside of every building.
        //
        // The observer stands in leaf 0, which is empty, with solid leaf 1 above the plane.
        // Looking up must find the solid and report shade.
        BspLeafTree tree = OneSplit(above: 1, below: 0, solidLeaf: 1);

        tree.SeesSky(0f, 0f, -100f, 0f, 0f, 1f).ShouldBeFalse();
    }

    [Test]
    public void APointUnderOpenSky_CanSeeIt()
    {
        // The control: the same observer, with an EMPTY leaf above instead. Without it a test
        // could pass against a trace that always reports shade, which would darken the whole map
        // uniformly and read as a lighting choice rather than a broken trace.
        //
        // The first version of this control put the observer inside the solid leaf itself, which
        // is not "under open sky" at all - the fixture, not the trace, was wrong.
        BspLeafTree tree = OneSplit(above: 2, below: 0, solidLeaf: 1);

        tree.SeesSky(0f, 0f, -100f, 0f, 0f, 1f).ShouldBeTrue();
    }

    /// <summary>A tree of one node splitting on the z = 0 plane.</summary>
    private static BspLeafTree OneSplit(int above, int below, int solidLeaf = -1)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(12), 0f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -above - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -below - 1);

        if (solidLeaf < 0)
        {
            return BspLeafTree.FromLumps(node, plane);
        }

        // Two leaves, thirty-two bytes each, with CONTENTS_SOLID on the one named.
        byte[] leaves = new byte[128];

        BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(solidLeaf * 32), 1);

        return BspLeafTree.FromLumps(node, plane, leaves);
    }
}
