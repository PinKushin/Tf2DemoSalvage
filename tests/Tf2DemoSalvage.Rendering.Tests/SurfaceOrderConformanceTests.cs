using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the world walk reaches surfaces in — what the overlay queue is built from (B457).
/// </summary>
/// <remarks>
/// **Read from engine.dll (x64, live), in disassembly.** <c>R_RecursiveWorldNode</c> (<c>0x1800e0600</c>) walks the
/// child on the eye's side first (<c>dot − dist &lt; 0</c> picks the back child), then each surface on the node's
/// plane that is MARKED this frame and whose plane-back flag (bit 6) equals that side, then the other child.
/// <c>R_DrawLeaf</c> (<c>0x1800df9d0</c>) only MARKS a leaf's node surfaces, and marks and draws each of its other
/// surfaces not already marked whose plane the eye is in front of (<c>dot − dist ≥ −0.01</c>). Both draws go to
/// <c>R_DrawSurface</c> (<c>0x1800dfbb0</c>), which is the order returned here.
///
/// The tree is the plane x = 0: leaf 1 on the +x side, leaf 0 on the −x side. Faces 5 and 6 lie on the node, 6
/// facing −x; 7, 8 and 9 are leaf faces, 9 facing −x. Leaf 0 lists 6 and 8, leaf 1 lists 5, 7 and 9.
/// </remarks>
public sealed class SurfaceOrderConformanceTests
{
    private static WorldCulling Culling()
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

        byte[] leaves = new byte[64];

        BitConverter.TryWriteBytes(leaves.AsSpan(20), (ushort)0);
        BitConverter.TryWriteBytes(leaves.AsSpan(22), (ushort)2);
        BitConverter.TryWriteBytes(leaves.AsSpan(32 + 20), (ushort)2);
        BitConverter.TryWriteBytes(leaves.AsSpan(32 + 22), (ushort)3);

        int[] listed = [6, 8, 5, 7, 9];
        byte[] leafFaces = new byte[listed.Length * 2];

        for (int at = 0; at < listed.Length; at++)
        {
            BitConverter.TryWriteBytes(leafFaces.AsSpan(at * 2), (ushort)listed[at]);
        }

        WorldFaceSpan Span(int face, (float X, float Y, float Z, float Distance) plane, bool back, bool onNode) =>
            new(face, face * 3, 3, 0, SurfaceCategory.Brush, Plane: plane, PlaneBack: back, OnNode: onNode);

        return new WorldCulling(
            BspLeafTree.FromLumps(node, plane, leaves),
            BspVisibility.None,
            BspLeafFaces.FromLump(leafFaces),
            [
                Span(5, (1f, 0f, 0f, 0f), back: false, onNode: true),
                Span(6, (1f, 0f, 0f, 0f), back: true, onNode: true),
                Span(7, (1f, 0f, 0f, 100f), back: false, onNode: false),
                Span(8, (1f, 0f, 0f, -100f), back: false, onNode: false),
                Span(9, (-1f, 0f, 0f, 200f), back: false, onNode: false),
            ]);
    }

    private static IReadOnlyList<int>? From(float x)
    {
        WorldCulling culling = Culling();

        culling.Batches(x, 0f, 0f, default);

        return culling.Surfaces;
    }

    [Test]
    public void Surfaces_FromTheFrontSide_AreNearLeafThenNodeThenFarLeaf() =>
        From(1000f).ShouldBe([7, 5, 8]);

    [Test]
    public void Surfaces_FromTheBackSide_DrawThePlaneBackNodeFaceAndSkipFacesTurnedAway() =>
        From(-1000f).ShouldBe([6, 9]);
}
