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

    /// <summary>
    /// **A seek's corpse replay does not touch the frame.** `AdvanceCorpsesAlone` simulates at the corpses' own tick
    /// between `PlaceFrame` and the frame's first pass; the parent handed over must still carry its child.
    /// </summary>
    [Test]
    public void Instances_AfterACorpseReplayAtAnotherTick_KeepsTheFramesParent()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);
        models.AdvanceCorpsesAlone([], 5d);

        List<ModelInstance> world = [];
        models.Instances([child], world, seconds: 1d);

        world.Single(model => model.ModelPath == ChildModel).Matrix[12].ShouldBe(201f, 1e-3f);
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
    /// **The instance carries the box the collation filed it by** — `CalcRenderableWorldSpaceAABB`, placed once. Two props
    /// of one model 199 units apart in X: every bound of the far one is the near one's, X moved by exactly 199. A carry that
    /// swapped two elements, or dropped the placement, cannot pass.
    /// </summary>
    [Test]
    public void Instances_TwoPlacedProps_CarryTheirCollatedBoxes()
    {
        SceneProp near = new(1, ParentModel, SceneModelKind.Studio, new ScenePose());
        SceneProp far = new(2, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });

        EntityModelSet models = new();
        models.Add([near, far], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.PlaceFrame([near, far]);

        List<ModelInstance> world = [];
        models.Instances([near, far], world, seconds: 1d);

        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) a =
            world.Single(model => model.EntityIndex == 1).WorldBounds;
        (float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ) b =
            world.Single(model => model.EntityIndex == 2).WorldBounds;

        (a.MaxX > a.MinX).ShouldBeTrue("the control: the model has a box at all");
        b.MinX.ShouldBe(a.MinX + 199f, 1e-3f);
        b.MaxX.ShouldBe(a.MaxX + 199f, 1e-3f);
        (b.MinY, b.MinZ, b.MaxY, b.MaxZ).ShouldBe((a.MinY, a.MinZ, a.MaxY, a.MaxZ));
    }

    /// <summary>
    /// **A static prop is filed once, and filed again the moment its placement differs** — `CStaticPropMgr` never re-links
    /// one, so a reuse is the engine's; but a reuse that outlived a change would draw and cull it where it was. The same
    /// static prop, at 0 then at 199: its box follows. The control is the first frame's box.
    /// </summary>
    [Test]
    public void Instances_AStaticPropWhosePlacementChanges_IsFiledAgain()
    {
        const int Static = PropModels.FirstStaticPropEntityIndex + 7;
        SceneProp at0 = new(Static, ParentModel, SceneModelKind.Studio, new ScenePose());
        SceneProp at199 = at0 with { Pose = new ScenePose { X = 199f } };

        EntityModelSet models = new();
        models.Add([at0], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });

        List<ModelInstance> first = [];
        models.Instances([at0], first, seconds: 1d);
        List<ModelInstance> again = [];
        models.Instances([at0], again, seconds: 2d);
        List<ModelInstance> moved = [];
        models.Instances([at199], moved, seconds: 3d);

        first.Single().WorldBounds.MinX.ShouldBe(-1f, 1e-3f, "the control: filed at its placement");
        again.Single().WorldBounds.ShouldBe(first.Single().WorldBounds, "unmoved, the filed box is the box");
        moved.Single().WorldBounds.MinX.ShouldBe(198f, 1e-3f, "moved, it is filed again");
    }

    /// <summary>
    /// **The reuse half, and every field it compares** (`FiledPlacement`): an unchanged frame re-links nothing; a change to
    /// any one input of `WorldBoxFor` re-links exactly once. Counted at the `RenderableChanged` call.
    /// </summary>
    [Test]
    public void Instances_AnUnparentedPropChangingAnyPlacementField_IsRelinked()
    {
        ScenePose start = new() { X = 1f, Y = 2f, Z = 3f, Pitch = 4f, Yaw = 5f, Roll = 6f, Scale = 1f, Sequence = 0 };
        SceneProp prop = new(1, ParentModel, SceneModelKind.Studio, start);

        EntityModelSet models = new();
        models.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.Instances([prop], [], seconds: 1d);

        int before = models.Relinked;
        models.Instances([prop], [], seconds: 2d);
        models.Relinked.ShouldBe(before, "unmoved: the filed box is reused, nothing re-linked");

        ScenePose[] changed =
        [
            start with { X = 9f }, start with { Y = 9f }, start with { Z = 9f }, start with { Pitch = 9f },
            start with { Yaw = 9f }, start with { Roll = 9f }, start with { Scale = 2f }, start with { Sequence = 1 },
        ];

        foreach (ScenePose pose in changed)
        {
            models.Instances([prop], [], seconds: 3d);
            int at = models.Relinked;
            models.Instances([prop with { Pose = pose }], [], seconds: 4d);
            models.Relinked.ShouldBe(at + 1, $"changed {pose}");
        }
    }

    /// <summary>
    /// **The handle lives with the object, as the engine's lives on the entity** (finding 82): `C_BaseEntity` keeps its
    /// `m_hRender` and is touched by `PreRender` only when it raised `RenderableChanged` (`clientleafsystem.cpp:543`). An
    /// unparented prop that reaches the collation as the SAME object it was last frame is unchanged — every field is
    /// init-only — so it is filed by identity, with no keyed lookup. An equal object that is a different instance takes
    /// the keyed path, and is still not re-linked, which is the control that the two paths agree.
    /// </summary>
    [Test]
    public void Instances_TheSameUnparentedObjectNextFrame_IsFiledByIdentityAndNotRelinked()
    {
        SceneProp prop = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 1f });

        EntityModelSet models = new();
        models.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.Instances([prop], [], seconds: 1d);

        int byIdentity = models.CollatedByIdentity;
        int relinked = models.Relinked;
        models.Instances([prop], [], seconds: 2d);

        models.CollatedByIdentity.ShouldBe(byIdentity + 1);
        models.Relinked.ShouldBe(relinked);

        models.Instances([prop with { }], [], seconds: 3d);

        models.CollatedByIdentity.ShouldBe(byIdentity + 1, "an equal but new object takes the keyed path");
        models.Relinked.ShouldBe(relinked, "and the keyed path agrees nothing moved");
    }

    /// <summary>
    /// **A model is resolved when the entity takes an update, not every frame** (finding 82): the engine's `SetModel` runs
    /// from `PostDataUpdate`, and the same object is the not-updated case. Asked again, the same object resolves nothing;
    /// a new object (an update) and a level whose models were read again resolve once each. Counted at the resolution.
    /// </summary>
    [Test]
    public void Instances_TheSameObjectNextFrame_ResolvesItsModelOnce()
    {
        SceneProp prop = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 1f });

        EntityModelSet models = new();
        models.Add([prop], _ => Baked());
        models.Instances([prop], [], seconds: 1d);
        int resolved = models.ModelsResolved;

        models.Instances([prop], [], seconds: 2d);
        models.ModelsResolved.ShouldBe(resolved, "the same object: nothing resolved again");

        models.Instances([prop with { Pose = new ScenePose { X = 5f } }], [], seconds: 3d);
        models.ModelsResolved.ShouldBe(resolved + 1, "an update resolves once");

        models.Add([new SceneProp(3, ChildModel, SceneModelKind.Studio, new ScenePose())], _ => Baked());
        models.Instances([prop], [], seconds: 4d);
        models.ModelsResolved.ShouldBe(resolved + 2, "models read since: resolved again");

        models.LevelShutdown();
        models.Instances([prop], [], seconds: 5d);
        models.ModelsResolved.ShouldBe(resolved + 3, "a level shut down: nothing it resolved survives, before any model is read");
    }

    /// <summary>
    /// **A resolution is dropped once its object is no longer asked for** (finding 82): every prop update is a new object,
    /// so a resolution kept for ever would grow without bound over a match — and one kept long promotes a frame's garbage
    /// into the older generations, which measured as a higher GC pause. Held for one world pass.
    /// </summary>
    [Test]
    public void Instances_AnObjectNotAskedForThroughASweep_IsForgotten()
    {
        SceneProp gone = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 1f });
        SceneProp stays = new(2, ParentModel, SceneModelKind.Studio, new ScenePose { X = 2f });

        EntityModelSet models = new();
        models.Add([gone, stays], _ => Baked());
        models.Instances([gone, stays], [], seconds: 1d);
        models.ResolutionsHeld.ShouldBe(2, "the control: both resolved");

        for (int pass = 0; pass < 300; pass++)
        {
            models.Instances([stays], [], seconds: 2d + pass);
        }

        models.ResolutionsHeld.ShouldBe(1);
    }

    /// <summary>
    /// **A model added after a prop was first seen is drawn the frame it arrives** (finding 82) — the same object, resolved
    /// while its model was missing, must not keep the miss. Its box too: the engine's `SetModelPointer` reaches
    /// `RenderableChanged` through `UpdateVisibility` (`c_baseentity.cpp:1790`, `:3950`), so the object is filed again with
    /// the model's box, the box a set that had the model from the start files it with.
    /// </summary>
    [Test]
    public void Instances_AModelAddedAfterThePropWasSeen_IsDrawnThatFrame()
    {
        SceneProp prop = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 7f });

        EntityModelSet models = new();
        List<ModelInstance> before = [];
        models.Instances([prop], before, seconds: 1d);

        models.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        List<ModelInstance> after = [];
        models.Instances([prop], after, seconds: 2d);

        EntityModelSet fresh = new();
        fresh.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        List<ModelInstance> expected = [];
        fresh.Instances([prop], expected, seconds: 2d);

        before.ShouldBeEmpty("the control: no model yet");
        after.Count.ShouldBe(1);
        expected[0].WorldBounds.MinX.ShouldBe(6f, "the control: the fresh set files the model's box");
        after[0].WorldBounds.ShouldBe(expected[0].WorldBounds);
    }

    /// <summary>
    /// **Two props sharing entity 0 and a model keep their own handles when they arrive in the other order** (finding 82) —
    /// the engine's handle is the entity's, so the order it is enumerated in decides nothing. The keyed path paired the
    /// first-seen with occurrence 0, so a swap re-linked both; this is the one place the two paths differ, and why the
    /// differential below keeps the twins in order. f12 has no such pair (0 repeats a collation, measured).
    /// </summary>
    [Test]
    public void Instances_TwoEntityZeroTwinsSwapped_KeepTheirFilingAndAreNotRelinked()
    {
        SceneProp twinA = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = 40f });
        SceneProp twinB = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = -40f });

        EntityModelSet models = new();
        models.Add([twinA], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.Instances([twinA, twinB], [], seconds: 1d);
        int relinked = models.Relinked;

        List<ModelInstance> swapped = [];
        models.Instances([twinB, twinA], swapped, seconds: 2d);

        models.Relinked.ShouldBe(relinked);
        swapped.Select(static model => model.WorldBounds.MinY).Order().ShouldBe([-42f, 38f]);
    }

    /// <summary>
    /// **A handle another prop claimed first this frame is not reused by identity** (finding 82): a new entity-0 twin met
    /// before the old one takes the first unclaimed occurrence, which is the old one's, so the old one is filed by key
    /// instead — and both draw. Without the claim check the old one would share the handle and one of the two would vanish.
    /// </summary>
    [Test]
    public void Instances_ANewTwinBeforeAnOldOne_BothDraw()
    {
        SceneProp old = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = 40f });
        SceneProp born = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = -40f });

        EntityModelSet models = new();
        models.Add([old], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.Instances([old], [], seconds: 1d);

        List<ModelInstance> both = [];
        models.Instances([born, old], both, seconds: 2d);

        both.Select(static model => model.WorldBounds.MinY).Order().ShouldBe([-42f, 38f]);
    }

    /// <summary>
    /// **A differential: filing by identity draws exactly what the keyed path draws** (finding 82). Two sets see the same
    /// frames; one is handed the same objects whenever a prop is unchanged, the other a fresh copy of every prop every
    /// frame, which can never hit the identity path. Through moves, a parenting, a parented child that stays the same
    /// object while its parent moves (it must follow), an unparenting at the same pose, two
    /// props sharing entity 0 and a model, a prop that leaves for a frame and returns as the same object, and a death, every
    /// frame's instances must be the same, in the same order, at the same boxes.
    /// </summary>
    [Test]
    public void Instances_SameObjectsAgainstFreshCopies_DrawTheSameEveryFrame()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp alone = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f });
        SceneProp parented = alone with { AttachedTo = 1 };
        SceneProp twinA = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = 40f });
        SceneProp twinB = new(0, ChildModel, SceneModelKind.Studio, new ScenePose { Y = -40f });
        SceneProp moved = parent with { Pose = new ScenePose { X = 150f } };

        SceneProp[][] frames =
        [
            [parent, alone, twinA, twinB],
            [parent, alone, twinA, twinB],
            [parent, parented, twinA, twinB],
            [moved, parented, twinA, twinB],
            [parent, alone, twinA],
            [moved, alone, twinA, twinB],
            [moved, twinA],
            [moved, alone, twinA, twinB],
        ];

        EntityModelSet same = new();
        EntityModelSet fresh = new();
        // Skinned, so the animation pass places each one too (`PlacementOf`), not only the collation.
        PropModels.ModelFrames model = WithMuzzle() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) };
        same.Add([parent, alone, twinA], _ => model);
        fresh.Add([parent, alone, twinA], _ => model);

        for (int at = 0; at < frames.Length; at++)
        {
            SceneProp[] copies = [.. frames[at].Select(static prop => prop with { })];
            same.PlaceFrame(frames[at]);
            fresh.PlaceFrame(copies);

            List<ModelInstance> drawnSame = [];
            List<ModelInstance> drawnFresh = [];
            same.Instances(frames[at], drawnSame, seconds: at + 1d);
            fresh.Instances(copies, drawnFresh, seconds: at + 1d);

            drawnSame.Select(Drawn).ShouldBe(drawnFresh.Select(Drawn), $"frame {at}");
        }

        same.CollatedByIdentity.ShouldBeGreaterThan(0, "the control: the identity path ran");
        fresh.CollatedByIdentity.ShouldBe(0, "and the keyed path alone ran on the other");
    }

    /// <summary>
    /// **Parenting forgets the filing** — red in review: unparented, then parented (placed every frame), then unparented at
    /// the SAME pose, which must be re-linked, because the box it was last filed with was the parented one's.
    /// </summary>
    [Test]
    public void Instances_UnparentedParentedThenUnparentedAtTheSamePose_IsFiledAgain()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp alone = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f });
        SceneProp parented = alone with { AttachedTo = 1 };

        EntityModelSet models = new();
        models.Add([parent, alone], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        models.PlaceFrame([parent, alone]);

        models.Instances([parent, alone], [], seconds: 1d);
        models.PlaceFrame([parent, parented]);
        models.Instances([parent, parented], [], seconds: 2d);
        models.PlaceFrame([parent, alone]);
        int at = models.Relinked;

        List<ModelInstance> back = [];
        models.Instances([parent, alone], back, seconds: 3d);

        models.Relinked.ShouldBe(at + 1, "filed again on leaving its parent");
        back.Single(model => model.EntityIndex == 2).WorldBounds.MinX.ShouldBe(1f, 1e-3f);
    }

    /// <summary>
    /// **A new level's model, at the same pose, is filed from the new model** — `LevelShutdown` forgets every filing, and
    /// the frames are compared by reference besides, so a model read again (a swap of `ModelFrames`) cannot keep the old
    /// box. The control is the first level's box.
    /// </summary>
    [Test]
    public void Instances_AfterLevelShutdownWithAnotherModel_FilesFromTheNewOne()
    {
        SceneProp prop = new(1, ParentModel, SceneModelKind.Studio, new ScenePose());

        EntityModelSet models = new();
        models.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-1f, -2f, -3f, 4f, 5f, 6f) });
        List<ModelInstance> first = [];
        models.Instances([prop], first, seconds: 1d);

        models.LevelShutdown();
        models.Add([prop], _ => Baked() with { HeaderBounds = new StudioBox(-7f, -2f, -3f, 4f, 5f, 6f) });
        List<ModelInstance> after = [];
        models.Instances([prop], after, seconds: 2d);

        first.Single().WorldBounds.MinX.ShouldBe(-1f, 1e-3f, "the control");
        after.Single().WorldBounds.MinX.ShouldBe(-7f, 1e-3f);
    }

    /// <summary>
    /// **The undrawn pass builds nothing when nothing will draw it** — `C_BasePlayer::ShouldDraw` is false for the local player
    /// in first person, so the engine reaches his `SetupBones` only from a monitor's `ForceTempForceDraw` or `AddDecal`, and
    /// both of ours ask on demand. The control is the same call with posing on, which does build the instance; and the parent
    /// still carries its child either way, because the frame, not the pass, places it.
    /// </summary>
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void Instances_UndrawnPassWhenPosingIs_BuildsThatManyInstances(bool poses, int built)
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new() { PosesUndrawn = poses };
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);

        List<ModelInstance> undrawn = [];
        List<ModelInstance> world = [];
        models.Instances([parent], undrawn, seconds: 1d, pass: EntityModelSet.UndrawnPass);
        models.Instances([child], world, seconds: 1d);

        undrawn.Count.ShouldBe(built);
        world.Single(model => model.ModelPath == ChildModel).Matrix[12].ShouldBe(201f, 1e-3f);
    }

    /// <summary>
    /// **A frame's props live until the next frame is handed over**, and no longer: once the next `PlaceFrame` lacks the
    /// parent, the parent has left, so the child is not composed onto it — <c>HierarchySetParent( NULL )</c>.
    /// </summary>
    [Test]
    public void Instances_AfterTheNextPlaceFrameWithoutTheParent_ForgetsIt()
    {
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 199f });
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1);

        EntityModelSet models = new();
        models.Add([parent, child], _ => Baked());
        models.PlaceFrame([parent, child]);
        models.Instances([child], [], seconds: 1d);

        models.PlaceFrame([child]);
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

    /// <summary>Everything a drawn instance says about where it is: identity, box, placement and every bone, as text.</summary>
    private static string Drawn(ModelInstance model) =>
        $"{model.EntityIndex} {model.ModelPath} {model.WorldBounds} [{string.Join(",", model.Matrix)}] " +
        string.Join(" | ", (model.Bones ?? []).Select(static bone => string.Join(",", bone)));

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
