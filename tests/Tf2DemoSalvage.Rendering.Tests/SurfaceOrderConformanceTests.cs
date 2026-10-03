using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the world walk reaches surfaces in — what the overlay queue is built from (B457).
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), in disassembly.** <c>R_RecursiveWorldNode</c> (<c>0x1800e0600</c>) walks the
/// child on the eye's side first (<c>dot − dist &lt; 0</c> picks the back child), then each surface on the node's
/// plane that is MARKED this frame and either carries flag 0x20000 or has a plane-back flag (bit 6) equal to that
/// side, then the other child. 0x20000 is set by <c>0x180100b60</c> on every surface of a leaf whose water data ID
/// (leaf +0x42, <c>dleaf_t.leafWaterDataID</c>) is not −1 — 0x40000 otherwise — skipping displacements and warp
/// faces (0x10800). <c>R_DrawLeaf</c> (<c>0x1800df9d0</c>) first reaches the leaf's displacements
/// (<c>0x1800db7b0</c>), then only MARKS its node surfaces, and marks and draws each other surface not already marked
/// that is NOCULL (0x200, set by <c>0x1800fa3d0</c> from the material's vtable +0x108, <c>$nocull</c>) or whose
/// plane the eye is in front of (<c>dot − dist ≥ −0.01</c>).
///
/// **A leaf's displacements** are the collision tree's list, copied by <c>Mod_LoadLeafs</c> (<c>0x180102300</c>,
/// collision leaf +0xc/+0xe): <c>0x18016fa20</c> visits every displacement in index order, <c>0x18016f470</c> walks
/// its box down the tree (axial plane: back when <c>min &lt; dist</c>, front when <c>dist &lt; max</c> or
/// <c>min ≥ dist</c>), and <c>0x180170210</c> files each into every leaf reached — so a leaf lists its displacements
/// in index order.
///
/// The tree is the plane x = 0: leaf 1 on the +x side, leaf 0 on the −x side. Faces 5 and 6 lie on the node, 6
/// facing −x; 7, 8 and 9 are leaf faces, 9 facing −x. Leaf 0 lists 6 and 8, leaf 1 lists 5, 7 and 9.
/// </remarks>
public sealed class SurfaceOrderConformanceTests
{
    private static WorldCulling Culling(int[]? leaf0Faces = null, bool leaf0Water = false, bool displacements = false)
    {
        byte[] node = new byte[32];

        BitConverter.TryWriteBytes(node.AsSpan(4), -2);
        BitConverter.TryWriteBytes(node.AsSpan(8), -1);
        BitConverter.TryWriteBytes(node.AsSpan(12), (short)-512);
        BitConverter.TryWriteBytes(node.AsSpan(14), (short)-512);
        BitConverter.TryWriteBytes(node.AsSpan(16), (short)-512);
        BitConverter.TryWriteBytes(node.AsSpan(18), (short)512);
        BitConverter.TryWriteBytes(node.AsSpan(20), (short)512);
        BitConverter.TryWriteBytes(node.AsSpan(22), (short)512);
        BitConverter.TryWriteBytes(node.AsSpan(24), (ushort)5);
        BitConverter.TryWriteBytes(node.AsSpan(26), (ushort)2);

        byte[] plane = new byte[20];

        BitConverter.TryWriteBytes(plane.AsSpan(0), 1f);

        int[] first = leaf0Faces ?? [6, 8];
        int[] listed = [.. first, 5, 7, 9];

        byte[] leaves = new byte[64];

        BitConverter.TryWriteBytes(leaves.AsSpan(20), (ushort)0);
        BitConverter.TryWriteBytes(leaves.AsSpan(22), (ushort)first.Length);
        BitConverter.TryWriteBytes(leaves.AsSpan(28), (short)(leaf0Water ? 0 : -1));
        BitConverter.TryWriteBytes(leaves.AsSpan(32 + 20), (ushort)first.Length);
        BitConverter.TryWriteBytes(leaves.AsSpan(32 + 22), (ushort)3);
        BitConverter.TryWriteBytes(leaves.AsSpan(32 + 28), (short)-1);

        byte[] leafFaces = new byte[listed.Length * 2];

        for (int at = 0; at < listed.Length; at++)
        {
            BitConverter.TryWriteBytes(leafFaces.AsSpan(at * 2), (ushort)listed[at]);
        }

        WorldFaceSpan Span(int face, (float X, float Y, float Z, float Distance) plane, bool back, bool onNode) =>
            new(face, face * 3, 3, face, SurfaceCategory.Brush, Plane: plane, PlaneBack: back, OnNode: onNode);

        WorldFaceSpan Displacement(int face, int index, float low, float high) =>
            new(face, face * 3, 3, face, SurfaceCategory.Terrain, (low, low, low), (high, high, high), Displacement: index);

        List<WorldFaceSpan> spans =
        [
            Span(5, (1f, 0f, 0f, 0f), back: false, onNode: true),
            Span(6, (1f, 0f, 0f, 0f), back: true, onNode: true),
            Span(7, (1f, 0f, 0f, 100f), back: false, onNode: false),
            Span(8, (1f, 0f, 0f, -100f), back: false, onNode: false),
            Span(9, (-1f, 0f, 0f, 200f), back: false, onNode: false),
        ];

        if (displacements)
        {
            // Face 20 is displacement 1 and straddles the plane; face 21 is displacement 0, on the +x side only.
            spans.Add(Displacement(20, 1, -10f, 10f));
            spans.Add(Displacement(21, 0, 100f, 120f));
        }

        return new WorldCulling(
            BspLeafTree.FromLumps(node, plane, leaves),
            BspVisibility.None,
            BspLeafFaces.FromLump(leafFaces),
            spans);
    }

    private static IReadOnlyList<int>? From(float x, WorldCulling? culling = null, Func<int, bool>? twoSided = null)
    {
        culling ??= Culling();

        culling.Batches(x, 0f, 0f, default, twoSided);

        return culling.Surfaces;
    }

    [Test]
    public void Surfaces_FromTheFrontSide_AreNearLeafThenNodeThenFarLeaf() =>
        From(1000f).ShouldBe([7, 5, 8]);

    [Test]
    public void Surfaces_FromTheBackSide_DrawThePlaneBackNodeFaceAndSkipFacesTurnedAway() =>
        From(-1000f).ShouldBe([6, 9]);

    [Test]
    public void Surfaces_ANodeFaceInAWaterLeaf_IsDrawnWhicheverSideItFaces() =>
        From(-1000f, Culling(leaf0Faces: [6, 8, 5], leaf0Water: true)).ShouldBe([5, 6, 9]);

    [Test]
    public void Surfaces_ANoCullLeafFace_IsDrawnFacingAway() =>
        From(1000f, twoSided: material => material == 9).ShouldBe([7, 9, 5, 8]);

    [Test]
    public void BlendedRuns_ATranslucentFaceWithOverlays_IsItsOwnRunNamingIt()
    {
        // 0x1800e4fd0 draws a translucent surface's overlays straight after that surface, so it cannot share a run.
        WorldCulling culling = Culling();

        culling.Batches(1000f, 0f, 0f, default);

        TranslucentLeafRuns runs = culling.BlendedRuns(_ => true, face => face == 7).ShouldNotBeNull();

        // Leaf 1 first, its faces last-listed first (9, 7, 5); then leaf 0 (8, 6).
        runs.RunFaces.ShouldBe([-1, 7, -1, -1, -1]);
    }

    [Test]
    public void Displacements_FromEitherSide_AreReachedAtTheirFirstLeafInIndexOrder()
    {
        WorldCulling culling = Culling(displacements: true);

        culling.Batches(1000f, 0f, 0f, default);
        culling.Displacements.Select(reached => reached.Face).ShouldBe([21, 20]);

        culling.Batches(-1000f, 0f, 0f, default);
        culling.Displacements.Select(reached => reached.Face).ShouldBe([20, 21]);
    }
}
