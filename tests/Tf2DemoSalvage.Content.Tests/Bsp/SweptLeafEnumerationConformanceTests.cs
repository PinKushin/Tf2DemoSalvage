using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// <c>CEngineBSPTree::EnumerateLeavesAlongRay</c> for a swept box — `engine.dll` vtable `0x1803907a8` slot 4,
/// `FUN_1800daa70`, walking `FUN_1800dec70` — the leaves a render-to-texture shadow's volume crosses (finding 107).
/// </summary>
/// <remarks>
/// **The fixture is one node**: the plane x = 0, front child leaf 0 (x &gt; 0), back child leaf 1 (x &lt; 0), both empty
/// unless a test makes leaf 1 solid. The walk pushes each plane out by the box's reach plus 1/32 (`0.03125`), visits the
/// side the sweep starts on first when it crosses, and both sides front first when it runs parallel.
/// </remarks>
public sealed class SweptLeafEnumerationConformanceTests
{
    private static BspLeafTree Wall(bool backSolid = false)
    {
        byte[] plane = new byte[20];
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(0), 1f);

        byte[] node = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        byte[] leaves = new byte[64];

        if (backSolid)
        {
            BinaryPrimitives.WriteInt32LittleEndian(leaves.AsSpan(32), 1);
        }

        return BspLeafTree.FromLumps(node, plane, leaves);
    }

    private static List<int> Sweep(BspLeafTree tree, (float, float, float) start, (float, float, float) delta, float extent)
    {
        List<int> leaves = [];
        tree.EnumerateLeavesAlongSweptBox(start, delta, (extent, extent, extent), leaves);
        return leaves;
    }

    [Test]
    public void EnumerateLeavesAlongSweptBox_FarBehindThePlane_VisitsOnlyTheBack() =>
        Sweep(Wall(), (-100f, 0f, 0f), (0f, 0f, -100f), 10f).ShouldBe([1]);

    /// <remarks>Within the box's reach plus 1/32 of the plane and parallel to it: both sides, front first (`|d| &lt; 1/32`).</remarks>
    [Test]
    public void EnumerateLeavesAlongSweptBox_ParallelAndWithinReach_VisitsFrontThenBack() =>
        Sweep(Wall(), (-5f, 0f, 0f), (0f, 0f, -100f), 10f).ShouldBe([0, 1]);

    /// <remarks>The 1/32 counts: a box reaching 10 starting 10.02 behind the plane still touches it.</remarks>
    [Test]
    public void EnumerateLeavesAlongSweptBox_JustPastTheReach_StillTouchesWithinTheEpsilon() =>
        Sweep(Wall(), (-10.02f, 0f, 0f), (0f, 0f, -100f), 10f).ShouldBe([0, 1]);

    [Test]
    public void EnumerateLeavesAlongSweptBox_PastTheReachAndTheEpsilon_VisitsOnlyItsSide() =>
        Sweep(Wall(), (-10.05f, 0f, 0f), (0f, 0f, -100f), 10f).ShouldBe([1]);

    /// <remarks>Crossing from behind to in front: the side it starts on first, the back, then the front.</remarks>
    [Test]
    public void EnumerateLeavesAlongSweptBox_CrossingFromBehind_VisitsTheBackFirst() =>
        Sweep(Wall(), (-50f, 0f, 0f), (100f, 0f, 0f), 1f).ShouldBe([1, 0]);

    [Test]
    public void EnumerateLeavesAlongSweptBox_CrossingFromTheFront_VisitsTheFrontFirst() =>
        Sweep(Wall(), (50f, 0f, 0f), (-100f, 0f, 0f), 1f).ShouldBe([0, 1]);

    /// <remarks>A solid leaf (contents 1) is never reported (`if ( contents == CONTENTS_SOLID ) return true`).</remarks>
    [Test]
    public void EnumerateLeavesAlongSweptBox_ASolidLeaf_IsSkipped() =>
        Sweep(Wall(backSolid: true), (-50f, 0f, 0f), (100f, 0f, 0f), 1f).ShouldBe([0]);
}
