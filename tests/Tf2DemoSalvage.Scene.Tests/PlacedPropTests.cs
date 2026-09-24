using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary><see cref="PlacedProp.InWorld"/>: a model's own corners, placed and with their normals turned into the world.</summary>
public sealed class PlacedPropTests
{
    [Test]
    public void InWorld_APropYawedNinetyAtAnOrigin_PlacesItsCornerAndTurnsItsNormal()
    {
        PropVertex[] corners = [new(10f, 0f, 5f, 0f, 0f, 0, NormalX: 1f, NormalY: 0f, NormalZ: 0f)];

        WorldVertex[] placed = new PlacedProp(corners, new PropTransform(100f, 200f, 300f, 0f, 90f, 0f, 1f)).InWorld();

        // Yaw 90 turns model +X to world +Y: (10, 0, 5) lands at (100, 210, 305), and the normal along +Y.
        placed.Length.ShouldBe(1);
        placed[0].X.ShouldBe(100f, 1e-4f);
        placed[0].Y.ShouldBe(210f, 1e-4f);
        placed[0].Depth.ShouldBe(305f, 1e-4f);
        placed[0].NormalX.ShouldBe(0f, 1e-6f);
        placed[0].NormalY.ShouldBe(1f, 1e-6f);
    }
}
