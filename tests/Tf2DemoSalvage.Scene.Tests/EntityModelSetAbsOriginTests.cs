using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>PATTACH_ABSORIGIN_FOLLOW</c>'s control point (particle_property.cpp:633-640) from the model set (B69 g): the entity's
/// abs origin and the axes of its abs angles, composed up its parent chain.
/// </summary>
public sealed class EntityModelSetAbsOriginTests
{
    [Test]
    public void AbsOrigin_AnEntityOfTheFrame_IsItsOriginFacingItsYaw()
    {
        EntityModelSet models = new();
        models.PlaceFrame([new SceneProp(2, "models/props/crate.mdl", SceneModelKind.Studio, new ScenePose { X = 300f, Yaw = 90f })]);

        ParticleControlPoint point = ((IPosedEntities)models).AbsOrigin(2).ShouldNotBeNull();

        point.At.ShouldBe(new Vector3(300f, 0f, 0f));
        point.Forward.X.ShouldBe(0f, 1e-5f);
        point.Forward.Y.ShouldBe(1f, 1e-5f);
        ((IPosedEntities)models).AbsOrigin(3).ShouldBeNull("the control: not in the frame");
    }

    /// <summary>A merged child stands where its parent does — `MoveToAimEnt`, the base class's origin with no bone merged.</summary>
    [Test]
    public void AbsOrigin_ABoneMergedChild_IsComposedFromItsParent()
    {
        EntityModelSet models = new();
        models.PlaceFrame(
        [
            new SceneProp(2, "models/props/crate.mdl", SceneModelKind.Studio, new ScenePose { X = 300f }),
            new SceneProp(5, "models/props/hat.mdl", SceneModelKind.Studio, new ScenePose(), AttachedTo: 2, BoneMerged: true),
        ]);

        ((IPosedEntities)models).AbsOrigin(5).ShouldNotBeNull().At.ShouldBe(new Vector3(300f, 0f, 0f));
    }
}
