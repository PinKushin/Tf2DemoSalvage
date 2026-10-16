using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A move child that is NOT bone-merged is drawn and lit at its parent's world transform times its own LOCAL one.
/// </summary>
/// <remarks>
/// **The wire's origin is local.** <c>SendProxy_Origin</c> sends <c>entity-&gt;GetLocalOrigin()</c>
/// (<c>server/baseentity.cpp:199</c>), so <c>DemoTimeline.PropsAt</c> — which answers what the wire stated — hands a child
/// its offset from the parent, and composing it is this layer's job. <c>C_BaseEntity::CalcAbsolutePosition</c>
/// (<c>client/c_baseentity.cpp:4393-4401</c>):
///
/// <code>
///   AngleMatrix( GetLocalAngles(), matEntityToParent );
///   MatrixSetColumn( GetLocalOrigin(), 3, matEntityToParent );
///   ConcatTransforms( GetParentToWorldTransform( scratchMatrix ), matEntityToParent, m_rgflCoordinateFrame );
/// </code>
///
/// and <c>GetParentToWorldTransform</c> (<c>:4342</c>) returns <c>pMoveParent-&gt;EntityToWorldTransform()</c>, which
/// runs the same function on the parent — so the WHOLE chain composes, not one level of it.
///
/// **The lighting origin is the same absolute place.** Everything that samples light, a cubemap or a distance fade
/// for an entity asks <c>GetAbsOrigin()</c> / the render origin, never the local one.
///
/// Subjects: a parent at (199, 0, 0) and a child 2 units along it — the fixture the timeline test that found this
/// authored — and the same child one level deeper, under a parent that is itself hung 99 units off a grandparent
/// at (100, 0, 0). Both must land at X = 201.
/// </remarks>
public sealed class MoveChildPlacementConformanceTests
{
    private const string ChildModel = "models/props_test/child.mdl";
    private const string ParentModel = "models/props_test/parent.mdl";

    [Test]
    public void Instances_ChildOfAPlacedParent_IsDrawnAtParentPlusLocal()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f }),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1));

        child.Matrix[12].ShouldBe(201f, 1e-3f);
    }

    [Test]
    public void Instances_ChildOfAPlacedParent_IsLitAtParentPlusLocal()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f }),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1));

        child.Origin.ShouldNotBeNull().X.ShouldBe(201f, 1e-3f);
    }

    [Test]
    public void Instances_ChildOfAParentedParent_ComposesTheWholeChain()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f }),
            new SceneProp(3, ParentModel, SceneModelKind.Studio, new ScenePose { X = 99f }, AttachedTo: 1),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 3));

        child.Matrix[12].ShouldBe(201f, 1e-3f);
        child.Origin.ShouldNotBeNull().X.ShouldBe(201f, 1e-3f);
    }

    /// <summary>The control: a child with no parent is drawn at its own origin, so the fixture is not composing by accident.</summary>
    [Test]
    public void Instances_UnparentedProp_IsDrawnAtItsOwnOrigin()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f }),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }));

        child.Matrix[12].ShouldBe(2f, 1e-3f);
        child.Origin.ShouldNotBeNull().X.ShouldBe(2f, 1e-3f);
    }

    private static ModelInstance Child(params SceneProp[] props)
    {
        EntityModelSet models = new();
        models.Add(props, _ => Baked());

        List<ModelInstance> drawn = [];
        models.Instances(props, drawn, seconds: 0d);

        return drawn.Single(instance => instance.ModelPath == ChildModel);
    }

    /// <summary>One baked triangle with no illumination offset, so the lighting origin is the placement itself.</summary>
    private static PropModels.ModelFrames Baked() =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true]);
}
