using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A static prop drawn as a model (B426, D198).</summary>
public sealed class PropModelsTests
{
    /// <remarks>
    /// The engine's static prop keeps its lump's origin, angles, scale and skin (`StaticPropLump_t`) and is
    /// drawn through the model draw `0x1800f1bd0`; each value below is one the test put in the lump.
    /// </remarks>
    [Test]
    public void StaticModel_APlacement_IsAStudioPropPlacedAsTheLumpSays()
    {
        SceneProp prop = PropModels.StaticModel(
            new BspStaticProp("models/props/crate.mdl", 10f, 20f, 30f, 5f, 90f, 2f, 1.5f, Skin: 2), 7);

        prop.ModelPath.ShouldBe("models/props/crate.mdl");
        prop.Kind.ShouldBe(SceneModelKind.Studio);
        prop.EntityIndex.ShouldBe(PropModels.FirstStaticPropEntityIndex + 7);
        (prop.Pose.X, prop.Pose.Y, prop.Pose.Z).ShouldBe((10f, 20f, 30f));
        (prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll).ShouldBe((5f, 90f, 2f));
        prop.Pose.Scale.ShouldBe(1.5f);
        prop.Pose.Skin.ShouldBe(2);
    }
}
