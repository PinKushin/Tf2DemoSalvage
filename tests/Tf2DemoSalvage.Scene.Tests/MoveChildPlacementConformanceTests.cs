using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
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

    /// <summary>
    /// **The parent in ANOTHER pass.** A frame runs drawn, undrawn and viewmodel passes, and a parent filtered out of
    /// the child's pass — an undrawn player, an invisible door — is still the parent: <c>CalcAbsolutePosition</c> never
    /// asks whether it is drawn. The frame's props are handed over once, before the passes.
    /// </summary>
    [Test]
    public void Instances_ParentOnlyInTheFrameNotThePass_StillComposes()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);

        List<ModelInstance> drawn = [];
        models.Instances([child], drawn, seconds: 0d);

        ModelInstance instance = drawn.Single(model => model.ModelPath == ChildModel);
        instance.Matrix[12].ShouldBe(201f, 1e-3f);
        instance.Origin.ShouldNotBeNull().X.ShouldBe(201f, 1e-3f);
    }

    /// <summary>
    /// **Through the parent's ATTACHMENT when one is named** — <c>GetParentToWorldTransform</c>
    /// (<c>c_baseentity.cpp:4330-4338</c>): <c>GetAttachment( m_iParentAttachment, … )</c>, then
    /// <c>AngleMatrix( vAngles, vOrigin, tempMatrix )</c>, then the local transform on top.
    ///
    /// The parent stands at (100, 200, 300) turned yaw 90, so forward is +Y; its attachment is one unit forward, at
    /// (100, 201, 300), and the child two units further along the attachment's own forward: (100, 203, 300). Composing
    /// onto the parent's own origin instead would answer (100, 202, 300).
    /// </summary>
    [Test]
    public void Instances_ChildOnAParentAttachment_IsAttachmentTimesLocal()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f });
        SceneProp child = new(
            2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);

        EntityModelSet models = new();
        models.Add([parent, child], path => path == ParentModel ? WithMuzzle() : Baked());

        List<ModelInstance> drawn = [];
        models.Instances([parent, child], drawn, seconds: 0d);

        ModelInstance instance = drawn.Single(model => model.ModelPath == ChildModel);
        (float X, float Y, float Z) lit = instance.Origin.ShouldNotBeNull();
        instance.Matrix[12].ShouldBe(100f, 1e-3f);
        instance.Matrix[13].ShouldBe(203f, 1e-3f);
        instance.Matrix[14].ShouldBe(300f, 1e-3f);
        lit.Y.ShouldBe(203f, 1e-3f);
    }

    /// <summary>
    /// The same for a SKINNED child, which is placed by its bones: its own muzzle, one unit along its forward, must stand
    /// at (100, 204, 300). Placing it at the bare attachment, as a bone-merged item's fallback does, would answer 202.
    /// </summary>
    [Test]
    public void Instances_SkinnedChildOnAParentAttachment_KeepsItsLocalOffset()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f, Y = 200f, Z = 300f, Yaw = 90f });
        SceneProp child = new(
            2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => WithMuzzle());
        models.Instances([parent, child], [], seconds: 0d);

        (float X, float Y, float Z) muzzle = models.AttachmentPosition(2, "muzzle").ShouldNotBeNull();
        muzzle.X.ShouldBe(100f, 1e-3f, $"{muzzle}");
        muzzle.Y.ShouldBe(204f, 1e-3f, $"{muzzle}");
        muzzle.Z.ShouldBe(300f, 1e-3f);
    }

    /// <summary>
    /// **A parent's model scale does not reach its children.** The coordinate frame is built from angles and origin
    /// alone — <c>AngleMatrix( GetLocalAngles(), GetLocalOrigin(), m_rgflCoordinateFrame )</c>
    /// (<c>c_baseentity.cpp:4380</c>) — so a parent at scale 2 still holds a child 2 units out at 2 units.
    /// </summary>
    [Test]
    public void Instances_ChildOfAScaledParent_IsNotScaledOut()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f, Scale = 2f }),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1));

        child.Matrix[12].ShouldBe(201f, 1e-3f);
        child.Origin.ShouldNotBeNull().X.ShouldBe(201f, 1e-3f);
    }

    /// <summary>
    /// **Through the scene, where the parent is removed before any pass sees it.** A respawn wall is not drawn once the
    /// round is won (<c>C_FuncRespawnRoomVisualizer::DrawModel</c>), and a prop hung from it is still hung from it.
    /// </summary>
    [Test]
    public void Pose_ParentRemovedByAVisibilityRule_StillCarriesItsChild()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f }, ClassName: "CFuncRespawnRoomVisualizer");
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());
        MomentInfo info = new(
            Tick: 1d, CurrentTick: 1, FirstPerson: false, Followed: null, EyeCamera: null, IntervalPerTick: 0.015f,
            ViewmodelFieldOfView: 54f, RoundState: RespawnRoomVisibility.TeamWin);

        scene.Build([], [parent, child], info);
        scene.Pose(info);

        scene.Drawn.Select(prop => prop.EntityIndex).ShouldBe([2], "the control: the wall itself is gone");
        scene.Instances.Single(model => model.ModelPath == ChildModel).Matrix[12].ShouldBe(201f, 1e-3f);
    }

    /// <summary>A parent posed only in the UNDRAWN pass still carries a child the world pass draws.</summary>
    [Test]
    public void Instances_ParentOnlyInTheUndrawnPass_CarriesAVisibleChild()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);

        List<ModelInstance> world = [];
        models.Instances([parent], [], seconds: 1d, pass: EntityModelSet.UndrawnPass);
        models.Instances([child], world, seconds: 1d);

        world.Single(model => model.ModelPath == ChildModel).Matrix[12].ShouldBe(201f, 1e-3f);
    }

    /// <summary>
    /// **A frame's props do not outlive the frame.** A caller that handed them over once and then simulates a LATER
    /// moment without the parent must not find it: the parent has left, so the child stands at its own offset, as
    /// <c>HierarchySetParent( NULL )</c> leaves it.
    /// </summary>
    [Test]
    public void Instances_AtALaterMomentWithoutPlaceFrame_ForgetsTheOldParent()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);
        models.Instances([child], [], seconds: 1d);

        List<ModelInstance> later = [];
        models.Instances([child], later, seconds: 2d);

        // The child of a parent nobody has now is not composed: the bone path refuses it, as an unknown wearer.
        later.ShouldNotContain(model => model.ModelPath == ChildModel && System.MathF.Abs(model.Matrix[12] - 201f) < 1e-3f);
    }

    /// <summary>
    /// **<see cref="PropTransform.FromMatrix"/> against both other routes, with a rotation that is not symmetric**, so a
    /// transpose cannot pass: the Euler constructor's own matrix read back through it, and a real attachment matrix
    /// whose child, hung at zero offset, must face exactly as the attachment does.
    /// </summary>
    [Test]
    public void FromMatrix_ANonSymmetricRotation_MatchesTheEulerAndTheAttachment()
    {
        PropTransform euler = new(5f, 6f, 7f, 20f, 50f, 70f, 1f);
        float[] valve = MatrixConvention.ToBoneMatrix(euler.ToMatrix());

        PropTransform.FromMatrix(valve).ToMatrix().ShouldBe(euler.ToMatrix(), 1e-5f);

        SceneProp parent = new(
            1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f, Y = 200f, Z = 300f, Pitch = 20f, Yaw = 50f, Roll = 70f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose(), AttachedTo: 1, AttachmentPoint: 1);

        EntityModelSet models = new();
        models.Add([parent, child], path => path == ParentModel ? WithMuzzle() : Baked());
        List<ModelInstance> drawn = [];
        models.Instances([parent, child], drawn, seconds: 0d);

        ParticleControlPoint muzzle = models.AttachmentPoint(1, 1).ShouldNotBeNull();
        float[] matrix = drawn.Single(model => model.ModelPath == ChildModel).Matrix;

        // ToMatrix's first row is the child's forward (column 0 of its rotation), the third its up.
        float[] expected =
        [
            muzzle.Forward.X, muzzle.Forward.Y, muzzle.Forward.Z,
            muzzle.Up.X, muzzle.Up.Y, muzzle.Up.Z,
            muzzle.At.X, muzzle.At.Y, muzzle.At.Z,
        ];
        float[] actual = [matrix[0], matrix[1], matrix[2], matrix[8], matrix[9], matrix[10], matrix[12], matrix[13], matrix[14]];
        actual.ShouldBe(expected, 1e-3f);
    }

    /// <summary>
    /// **An attachment asked before its parent is updated is not kept.** The child is listed FIRST, so while the pass is
    /// simulated it asks for the parent's attachment before the parent's own pose has moved to this frame; the draw must
    /// use the attachment of the frame it draws. The parent moves from X = 100 to X = 150; its muzzle is one unit along
    /// its forward, so the child hung two units further sits at 153.
    /// </summary>
    [Test]
    public void Instances_ChildListedBeforeAMovingParent_UsesThisFramesAttachment()
    {
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp before = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });
        SceneProp after = before with { Pose = before.Pose with { X = 150f } };

        EntityModelSet models = new();
        models.Add([child, before], path => path == ParentModel ? WithMuzzle() : Baked());
        models.Instances([child, before], [], seconds: 1d);

        List<ModelInstance> drawn = [];
        models.Instances([child, after], drawn, seconds: 2d);

        drawn.Single(model => model.ModelPath == ChildModel).Matrix[12].ShouldBe(153f, 1e-3f);
    }

    /// <summary>
    /// **A child's size is its own model scale and nothing of its parent's** — the frame carries none (<c>:4380</c>).
    /// A parent at 2 holding a child at 3 draws the child at 3.
    /// </summary>
    [Test]
    public void Instances_ScaledChildOfAScaledParent_IsSizedByItsOwnScaleOnly()
    {
        ModelInstance child = Child(
            new SceneProp(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f, Scale = 2f }),
            new SceneProp(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f, Scale = 3f }, AttachedTo: 1));

        new System.Numerics.Vector3(child.Matrix[0], child.Matrix[1], child.Matrix[2]).Length().ShouldBe(3f, 1e-4f);
    }

    private static ModelInstance Child(params SceneProp[] props)
    {
        EntityModelSet models = new();
        models.Add(props, _ => Baked());

        List<ModelInstance> drawn = [];
        models.Instances(props, drawn, seconds: 0d);

        return drawn.Single(instance => instance.ModelPath == ChildModel);
    }

    /// <summary>One bone and one attachment, <c>muzzle</c>, a unit along the bone's X with no turn of its own.</summary>
    private static PropModels.ModelFrames WithMuzzle() =>
        Baked() with
        {
            Skinned = SyntheticSkinnedModel.WithBones("root"),
            Attachments = [new StudioAttachment("muzzle", 0, 0, [1f, 0f, 0f, 1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f])],
        };

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
