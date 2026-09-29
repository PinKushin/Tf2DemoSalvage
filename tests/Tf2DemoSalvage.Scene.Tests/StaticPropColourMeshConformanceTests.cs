using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Which of a `.vhv`'s meshes reach a static prop's colour mesh, and what the rest take (B436).
/// </summary>
/// <remarks>
/// `engine.dll` `FUN_1800f1550` walks the root-LOD mesh headers in order, uploads each only while its vertex count
/// equals its colour mesh's (the strip group's <c>numVerts</c>) and breaks at the first that does not, so that mesh
/// and every later one are never written. The engine leaves them uninitialised; this project reads them as the CPU
/// bake (`FUN_1800ee4a0`, B429) — never white.
/// </remarks>
public sealed class StaticPropColourMeshConformanceTests
{
    private static readonly IReadOnlyList<IReadOnlyList<(byte, byte, byte)>> ThreeMeshes =
    [
        [(255, 0, 0), (0, 255, 0)],
        [(0, 0, 255)],
        [(10, 20, 30)],
    ];

    /// <summary>One corner per mesh: mesh 0 vertex 1, mesh 1 vertex 0, mesh 2 vertex 0.</summary>
    private static readonly int[] Meshes = [0, 1, 2];

    private static readonly int[] Vertices = [1, 0, 0];

    [Test]
    public void ColourMesh_EveryCountMatching_UsesEachVhvByte()
    {
        float[] colours = PropModels.ColourMesh(ThreeMeshes, Meshes, Vertices, [2, 1, 1]);

        colours.ShouldBe(
        [
            0f, PropModels.FromVertexByte(255), 0f,
            0f, 0f, PropModels.FromVertexByte(255),
            PropModels.FromVertexByte(10), PropModels.FromVertexByte(20), PropModels.FromVertexByte(30),
        ]);
    }

    [Test]
    public void ColourMesh_TheMiddleMeshLonger_BreaksThereAndLeavesItAndTheLastUnfilled()
    {
        // The second strip group has two vertices and its header one — a mismatch in the direction the old per-vertex
        // check let through, and the third mesh, which matches, is still never reached.
        float[] colours = PropModels.ColourMesh(ThreeMeshes, Meshes, Vertices, [2, 2, 1]);

        colours[1].ShouldBe(PropModels.FromVertexByte(255));
        colours[3..].ShouldAllBe(value => float.IsNaN(value));
    }

    [Test]
    public void FillUnfilled_WithACpuBake_TakesItOnlyWhereTheVhvWasNotWritten()
    {
        float[] baked = [0.5f, 0.5f, 0.5f, float.NaN, float.NaN, float.NaN];

        PropModels.FillUnfilled(baked, [9f, 9f, 9f, 0.25f, 0.125f, 0.0625f]).ShouldBeTrue();

        baked.ShouldBe([0.5f, 0.5f, 0.5f, 0.25f, 0.125f, 0.0625f]);
    }

    [Test]
    public void FillUnfilled_WithNoCpuBake_RefusesSoThePropIsLitPerDraw() =>
        PropModels.FillUnfilled([0.5f, float.NaN, 0.5f], null).ShouldBeFalse();
}
