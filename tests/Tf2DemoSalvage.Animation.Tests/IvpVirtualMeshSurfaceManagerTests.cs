using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The virtual-mesh surface manager's radius query — slot 4 of <c>1800ee220</c>, <c>FUN_1800261a0</c> and <c>FUN_180025bc0</c> (B369).</summary>
/// <remarks>
/// Read from the decompiled `vphysics.dll` (`docs/findings/51`, *The virtual mesh's cache entry*). A flat power-2 displacement over a
/// 16-inch square at Source z 0; queries are in IVP metres, `Source (x, y, z)` being `IVP (x, −z, y)·0.0254`. Synthetic conformance (D38).
/// </remarks>
public sealed class IvpVirtualMeshSurfaceManagerTests
{
    private const double Metre = 0.0254d;

    /// <remarks>**At the root the query answers the hull ledges, at most two.**</remarks>
    [Test]
    public void LedgesWithin_AtTheRoot_AnswersTheFirstTwoHulls()
    {
        IvpVirtualMeshSurfaceManager manager = Manager(hulls: 3);
        List<PhysicsLedgeTreeNode> into = [];

        manager.LedgesWithin((0d, 0d, 0d), 1d, null, into);

        into.ShouldBe([manager.Mesh.Hulls[0], manager.Mesh.Hulls[1]]);
    }

    /// <remarks>
    /// **Beneath a hull, the tree's leaves in the sphere, each triangle kept when its ledge is within reach**: at Source `(7, 5)` with
    /// half an inch, the one cell's lower-right triangle holds the point and its upper-left one is 1.41 away.
    /// </remarks>
    [Test]
    public void LedgesWithin_BeneathTheHull_AnswersOnlyTheTrianglesWithinReach()
    {
        IvpVirtualMeshSurfaceManager manager = Manager(hulls: 1);
        List<PhysicsLedgeTreeNode> into = [];

        manager.LedgesWithin((7d * Metre, 0d, 5d * Metre), 0.5d * Metre, manager.Mesh.Hulls[0], into);

        into.ShouldBe([manager.Mesh.Triangles[11]]);
    }

    /// <remarks>
    /// **With two hulls the triangles are halved**: the second hull answers only from the second half, so of the centre vertex's
    /// eight triangles `10..13, 18..21` it keeps `18..21`.
    /// </remarks>
    [Test]
    public void LedgesWithin_BeneathTheSecondOfTwoHulls_AnswersFromTheSecondHalfOfTheTriangles()
    {
        IvpVirtualMeshSurfaceManager manager = Manager(hulls: 2);
        List<PhysicsLedgeTreeNode> into = [];

        manager.LedgesWithin((8d * Metre, 0d, 8d * Metre), 0.1d * Metre, manager.Mesh.Hulls[1], into);

        into.ShouldBe([manager.Mesh.Triangles[18], manager.Mesh.Triangles[19], manager.Mesh.Triangles[20], manager.Mesh.Triangles[21]]);
    }

    /// <remarks>
    /// **The mesh's radius is the tree's bounds' half-diagonal in metres** (`FUN_180025e80`, through slot 2), about their centre
    /// (`FUN_180025db0`): a 16-inch square bloated by an inch each way is 18 by 18 by 2.
    /// </remarks>
    [Test]
    public void Radius_AFlatSixteenInchSquare_IsItsBloatedBoundsHalfDiagonal()
    {
        IvpVirtualMeshSurfaceManager manager = Manager(hulls: 1);

        manager.Radius.ShouldBe((float)(System.Math.Sqrt((9d * 9d) + (9d * 9d) + 1d) * Metre), 1e-6f);
        manager.MassCenter.ShouldBe((8f * 0.0254f, -0f, 8f * 0.0254f));
    }

    private static IvpVirtualMeshSurfaceManager Manager(int hulls)
    {
        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(
            [Vector3.Zero, new Vector3(0f, 16f, 0f), new Vector3(16f, 16f, 0f), new Vector3(16f, 0f, 0f)],
            2,
            new (Vector3, float)[25]);

        List<byte> blob = [(byte)hulls, 0, 0, 0];

        for (int hull = 0; hull < hulls; hull++)
        {
            blob.AddRange([1, 0, 3, 0, 0]);
        }

        for (int hull = 0; hull < hulls; hull++)
        {
            blob.AddRange([0, 1, 2, 0, 0, 1, 1, 2, 2, 0]);
        }

        return new IvpVirtualMeshSurfaceManager(PhysicsVirtualMesh.Build(tree.Vertices, tree.Triangles, blob.ToArray()), tree);
    }
}
