using System;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><see cref="PlacedProp.InWorld()"/>: a model's own corners, placed and with their normals turned into the world.</summary>
public sealed class PlacedPropTests
{
    [Test]
    public void InWorld_APropYawedNinetyAtAnOrigin_PlacesItsCornerAndTurnsItsNormal()
    {
        PropVertex[] corners = [new(10f, 0f, 5f, 0f, 0f, 0, NormalX: 1f, NormalY: 0f, NormalZ: 0f)];

        WorldVertex[] placed = new PlacedProp(PropShape.Of(corners), new PropTransform(100f, 200f, 300f, 0f, 90f, 0f, 1f)).InWorld();

        // Yaw 90 turns model +X to world +Y: (10, 0, 5) lands at (100, 210, 305), and the normal along +Y.
        placed.Length.ShouldBe(1);
        placed[0].X.ShouldBe(100f, 1e-4f);
        placed[0].Y.ShouldBe(210f, 1e-4f);
        placed[0].Depth.ShouldBe(305f, 1e-4f);
        placed[0].NormalX.ShouldBe(0f, 1e-6f);
        placed[0].NormalY.ShouldBe(1f, 1e-6f);
    }

    /// <remarks>
    /// **Written into a reused array** (finding 78: a new world copy per decal shot on a prop was 33 MB of large-object
    /// garbage per 20 s of f12). The same corners as the allocating form, into an array a larger earlier prop left dirty,
    /// with the count of what this prop wrote, and no allocation.
    /// </remarks>
    [Test]
    public void InWorld_IntoADirtyLargerArray_WritesWhatInWorldReturnsAndAllocatesNothing()
    {
        PropVertex[] corners =
        [
            new(10f, 0f, 5f, 0f, 0f, 0, NormalX: 1f, NormalY: 0f, NormalZ: 0f),
            new(-3f, 7f, 2f, 0f, 0f, 0, NormalX: 0f, NormalY: 0f, NormalZ: 1f),
        ];
        PlacedProp prop = new(PropShape.Of(corners), new PropTransform(100f, 200f, 300f, 0f, 90f, 0f, 1f));
        WorldVertex[] into = new WorldVertex[5];
        into.AsSpan().Fill(new WorldVertex(9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f));

        _ = prop.InWorld(into);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int written = prop.InWorld(into);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        written.ShouldBe(2);
        into[..2].ShouldBe(prop.InWorld());
        allocated.ShouldBe(0);
    }
}
