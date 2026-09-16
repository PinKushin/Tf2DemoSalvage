using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A displacement's virtual-mesh cache entry as vphysics lays it out — <c>FUN_180025f10</c>, the triangle ledge
/// <c>FUN_180003f70</c> and the hull unpacker <c>FUN_180004d90</c>/<c>FUN_1800048d0</c> (B369).
/// </summary>
/// <remarks>
/// Read from the decompiled `vphysics.dll` (`docs/findings/51`, *The virtual mesh's cache entry*). Each prediction is the bytes' own
/// arithmetic carried by hand. Synthetic conformance (D38).
/// </remarks>
public sealed class PhysicsVirtualMeshConformanceTests
{
    private const float Metre = 0.0254f;

    /// <remarks>
    /// **Every triangle is a two-triangle ledge of 48 bytes**: the front `(i0, i1, i2)` with pierce 1 and edge hops `6, 4, 2`, the back
    /// `(i0, i2, i1)` with pierce 0 and hops `−2, −4, −6`; no child flag. The vertices go into IVP as `(x, −z, y)·0.0254f`.
    /// </remarks>
    [Test]
    public void Build_ATriangle_IsATwoSidedLedgeOverTheConvertedVertices()
    {
        PhysicsVirtualMesh mesh = PhysicsVirtualMesh.Build(
            [new Vector3(10f, 20f, 30f), new Vector3(40f, 20f, 30f), new Vector3(10f, 60f, 30f)],
            [(0, 1, 2)],
            []);

        PhysicsLedgeTreeNode node = mesh.Triangles.ShouldHaveSingleItem();
        PhysicsLedge ledge = node.Ledge.ShouldNotBeNull();

        node.LedgeChildren.ShouldBe(0);
        ledge.Triangles.ShouldBe([(0, 1, 2), (0, 2, 1)]);
        ledge.PierceTriangles.ShouldBe([1, 0]);
        ledge.EdgeOffsets.ShouldBe([(6, 4, 2), (-2, -4, -6)]);
        ledge.VirtualTriangles.ShouldBe([false, false]);
        ledge.Points[0].X.ShouldBe(10f * Metre);
        ledge.Points[0].Y.ShouldBe(-(30f * Metre));
        ledge.Points[0].Z.ShouldBe(20f * Metre);
        mesh.Hulls.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A hull is packed as triangles of edge ids and edges of vertex bytes** (`FUN_1800048d0`): a triangle takes an edge's first
    /// vertex byte the first time the edge is met and its second the next, the two words then hopping to each other; a triangle
    /// under the virtual-triangle count and an edge under the virtual-edge count set bit 31; the byte after the edge ids is the
    /// pierce triangle; the ledge carries the child flag. Two triangles sharing one edge, over vertices 0..3 at base 0.
    /// </remarks>
    [Test]
    public void Build_AHull_UnpacksItsTrianglesEdgesAndFlags()
    {
        byte[] hull =
        [
            1, 0, 0, 0,          // one hull
            2, 1, 5, 2, 0,       // 2 triangles, 1 virtual, 5 edges, 2 virtual edges, base vertex 0
            0, 1, 2, 1,          // triangle 0: edges 0, 1, 2, pierce 1
            2, 3, 4, 0,          // triangle 1: edges 2, 3, 4, pierce 0
            0, 1,                // edge 0: vertex 0 then 1
            1, 2,                // edge 1
            2, 0,                // edge 2: first met as 2, then as 0
            2, 3,                // edge 3
            3, 0,                // edge 4
        ];

        PhysicsVirtualMesh mesh = PhysicsVirtualMesh.Build(
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ], [], hull);

        PhysicsLedgeTreeNode node = mesh.Hulls.ShouldHaveSingleItem();
        PhysicsLedge ledge = node.Ledge.ShouldNotBeNull();

        node.LedgeChildren.ShouldBe(1);
        ledge.Triangles.ShouldBe([(0, 1, 2), (0, 2, 3)], "the shared edge's second vertex byte on its second meeting");
        ledge.PierceTriangles.ShouldBe([1, 0]);
        ledge.VirtualTriangles.ShouldBe([true, false]);
        ledge.VirtualEdges.ShouldBe([(true, true, false), (false, false, false)]);
        ledge.EdgeOffsets.ShouldBe([(0, 0, 2), (-2, 0, 0)], "the shared edge's two words hop to each other; the rest were never met twice");
    }
}
