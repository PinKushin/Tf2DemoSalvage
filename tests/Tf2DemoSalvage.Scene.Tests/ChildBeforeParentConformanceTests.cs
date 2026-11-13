using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A move child asks for its parent's attachment with the parent's state of THIS frame, whatever order the pass lists
/// them in (B729).
/// </summary>
/// <remarks>
/// **The engine sets every entity's state before anything asks for bones.** `OnRenderStart` runs
/// <c>C_BaseEntity::InterpolateServerEntities()</c> — every interpolated entity's origin, angles and cycle — then
/// <c>InvalidateBoneCaches()</c>, then <c>UpdateClientSideAnimations()</c>, then <c>SimulateEntities()</c>
/// (<c>cdll_client_int.cpp:2154-2205</c>). A child's placement is asked on demand: <c>GetAbsOrigin</c> →
/// <c>CalcAbsolutePosition</c> → <c>GetParentToWorldTransform</c> → <c>pMoveParent-&gt;GetAttachment</c>
/// (<c>c_baseentity.cpp:1734-1739</c>, <c>:4320-4343</c>, <c>:4350-4427</c>), whose <c>SetupBones</c> builds from the
/// parent's current sequence, cycle and <c>GetRenderOrigin()</c> (<c>c_baseanimating.cpp:2796-3033</c>). So the answer
/// does not depend on list order, and no entity is advanced twice.
/// </remarks>
public sealed class ChildBeforeParentConformanceTests
{
    private const string ChildModel = "models/props_test/child.mdl";
    private const string ParentModel = "models/props_test/parent.mdl";

    /// <remarks>
    /// The parent moves from X = 100 to 150; its muzzle is one unit along, at 151; the child hangs two units further,
    /// and its own muzzle is one more: 154, in either order. B727's control read 104 with the child first: the parent's
    /// muzzle of LAST frame (101) plus three. The parent's own muzzle is asserted too: the child's early query marked the
    /// parent's bones built for the frame.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Instances_ASkinnedChildOnAMovingParent_StandsOnThisFramesAttachmentInEitherOrder(bool childFirst)
    {
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });
        SceneProp moved = parent with { Pose = parent.Pose with { X = 150f } };

        EntityModelSet models = new();
        models.Add([child, parent], _ => MoveChildPlacementConformanceTests.WithMuzzle());
        models.Instances(childFirst ? [child, parent] : [parent, child], [], seconds: 1d);
        models.Instances(childFirst ? [child, moved] : [moved, child], [], seconds: 2d);

        models.AttachmentPosition(2, "muzzle").ShouldNotBeNull().X.ShouldBe(154f, 1e-3f);
        models.AttachmentPosition(1, "muzzle").ShouldNotBeNull().X.ShouldBe(151f, 1e-3f);
    }

    /// <remarks>
    /// **A parent that is itself a move child is placed when its child asks first** — the parent's <c>SetupBones</c> reads
    /// <c>GetRenderOrigin()</c>, which runs <c>CalcAbsolutePosition</c> on it in turn. Grandparent 100 → 150, muzzle 151;
    /// the middle entity two further, 153, muzzle 154; the grandchild two further again, 156, muzzle 157 — listed
    /// grandchild, middle, grandparent, and the reverse as the control.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Instances_AGrandchildListedFirst_StandsOnTheWholeChainOfThisFrame(bool grandchildFirst)
    {
        SceneProp grandchild = new(3, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 2, AttachmentPoint: 1);
        SceneProp middle = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp root = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });
        SceneProp moved = root with { Pose = root.Pose with { X = 150f } };

        EntityModelSet models = new();
        models.Add([grandchild, middle, root], _ => MoveChildPlacementConformanceTests.WithMuzzle());
        models.Instances(grandchildFirst ? [grandchild, middle, root] : [root, middle, grandchild], [], seconds: 1d);
        models.Instances(grandchildFirst ? [grandchild, middle, moved] : [moved, middle, grandchild], [], seconds: 2d);

        models.AttachmentPosition(3, "muzzle").ShouldNotBeNull().X.ShouldBe(157f, 1e-3f);
        models.AttachmentPosition(2, "muzzle").ShouldNotBeNull().X.ShouldBe(154f, 1e-3f);
    }

    /// <remarks>
    /// **A parent cycle ends** (B729 review): no TF2 hierarchy has one, but a demo is untrusted input, and a placement
    /// that recursed through it would overflow the stack — which no handler catches. Two entities each attached to the
    /// other, and one attached to itself, are posed and drawn.
    /// </remarks>
    [Test]
    public void Instances_AParentCycle_EndsAndDrawsEveryEntity()
    {
        SceneProp first = new(1, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 2, AttachmentPoint: 1);
        SceneProp second = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp itself = new(3, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 3, AttachmentPoint: 1);

        EntityModelSet models = new();
        models.Add([first, second, itself], _ => MoveChildPlacementConformanceTests.WithMuzzle());
        List<ModelInstance> drawn = [];
        models.Instances([first, second, itself], drawn, seconds: 1d);
        models.Instances([first, second, itself], drawn, seconds: 2d);

        drawn.Count.ShouldBe(3);
    }

    /// <remarks>
    /// **A bone-merged link in the chain is placed too** (B729 review): the merge asks its follow's bones through
    /// <c>m_pFollow-&gt;SetupBones</c> (<c>bone_merge_cache.cpp:122-170</c>), not through an attachment query, so only a
    /// walk up the whole chain places a pending follow first. G moves 100 → 150 (muzzle 151); W hangs on it two along
    /// (153, muzzle 154); M is bone-merged on W, so its muzzle is W's, 154; the child C hangs two along M's (156, muzzle
    /// 157). Listed C, M, W, G; the reverse is the control. Without the walk W is built from last frame's place and C
    /// reads 107.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Instances_AChildOnABoneMergedLink_StandsOnTheWholeChainOfThisFrame(bool childFirst)
    {
        SceneProp child = new(4, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 3, AttachmentPoint: 1);
        SceneProp merged = new(3, ChildModel, SceneModelKind.Studio, new ScenePose(), AttachedTo: 2, BoneMerged: true);
        SceneProp follow = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp root = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });
        SceneProp moved = root with { Pose = root.Pose with { X = 150f } };

        EntityModelSet models = new();
        models.Add([child, merged, follow, root], _ => MoveChildPlacementConformanceTests.WithMuzzle());
        models.Instances(childFirst ? [child, merged, follow, root] : [root, follow, merged, child], [], seconds: 1d);
        models.Instances(childFirst ? [child, merged, follow, moved] : [moved, follow, merged, child], [], seconds: 2d);

        models.AttachmentPosition(4, "muzzle").ShouldNotBeNull().X.ShouldBe(157f, 1e-3f);
    }

    /// <remarks>
    /// **A parent asked for first is advanced once, not again when the loop reaches it.** The parent plays a one-second
    /// client-animated loop with events at 0 and 0.5; the frames at 0.1, 0.6 and 0.7 s fire "zero", "half", nothing —
    /// the walk of <c>AnimationEventWalkConformanceTests</c> — in either order, two entities simulated a pass, and the
    /// parent's frame is the same in both orders.
    /// </remarks>
    [Test]
    public void Instances_AChildBeforeAnAnimatingParent_AdvancesTheParentOnce()
    {
        (string[][] childFirst, int simulatedChildFirst, (int, int, float)? frameChildFirst) = Walk(childFirst: true);
        (string[][] parentFirst, int simulatedParentFirst, (int, int, float)? frameParentFirst) = Walk(childFirst: false);

        parentFirst.ShouldBe([["zero"], ["half"], []], "the control: the walk in list order");
        childFirst.ShouldBe([["zero"], ["half"], []]);
        simulatedParentFirst.ShouldBe(2);
        simulatedChildFirst.ShouldBe(2);
        frameChildFirst.ShouldNotBeNull().ShouldBe(frameParentFirst.ShouldNotBeNull());
    }

    /// <remarks>
    /// **A parent in a LATER pass of the frame** (B729's open item): the child is drawn in the world pass and its parent is
    /// posed in the undrawn one after it — the first-person recorder carrying something on an attachment. The engine's
    /// `SimulateEntities` has run for both before any bones (`cdll_client_int.cpp:2154-2205`), so the child stands on the
    /// parent's muzzle of THIS frame: 151 + 3 = 154. Before, the world pass asked the parent's bones from last frame's
    /// state, 104. The parent listed in the same pass is the control.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Instances_AParentInALaterPass_StandsTheChildOnThisFramesAttachment(bool laterPass)
    {
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });
        SceneProp moved = parent with { Pose = parent.Pose with { X = 150f } };

        EntityModelSet models = new();
        models.Add([child, parent], _ => MoveChildPlacementConformanceTests.WithMuzzle());

        foreach ((SceneProp at, double seconds) in new[] { (parent, 1d), (moved, 2d) })
        {
            models.PlaceFrame([child, at]);

            if (laterPass)
            {
                models.Instances([child], [], seconds: seconds);
                models.Instances([at], [], seconds: seconds, pass: EntityModelSet.UndrawnPass);
            }
            else
            {
                models.Instances([child, at], [], seconds: seconds);
            }
        }

        models.AttachmentPosition(2, "muzzle").ShouldNotBeNull().X.ShouldBe(154f, 1e-3f);
        models.AttachmentPosition(1, "muzzle").ShouldNotBeNull().X.ShouldBe(151f, 1e-3f);
    }

    /// <remarks>
    /// **And it is advanced once a frame, not again when its own pass reaches it**: a spinning minigun's barrel steps its
    /// velocity by 0.1 per call (`UpdateBarrelMovement`, B347), so a parent brought up to date for its child and then
    /// simulated again by its own pass would wind up twice as fast. Eight frames a tenth of a second apart, the minigun in
    /// the undrawn pass with a child on its muzzle in the world pass before it; the same minigun with no child is the
    /// control, and the barrel angles must be equal.
    /// </remarks>
    [Test]
    public void Instances_AParentAskedBeforeItsLaterPass_IsAdvancedOnceAFrame()
    {
        static float Spun(bool withChild)
        {
            PropModels.ModelFrames minigun = MinigunBarrelWiringTests.Frames(barrel: true) with
            {
                Attachments = MoveChildPlacementConformanceTests.WithMuzzle().Attachments,
            };
            SceneProp parent = MinigunBarrelWiringTests.Minigun(3);
            SceneProp child = new(
                2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: parent.EntityIndex, AttachmentPoint: 1);

            EntityModelSet models = new() { Geometry = path => path == ChildModel ? MoveChildPlacementConformanceTests.WithMuzzle() : minigun };
            models.Add([parent, child], path => path == ChildModel ? MoveChildPlacementConformanceTests.WithMuzzle() : minigun);

            for (int frame = 0; frame < 8; frame++)
            {
                double seconds = frame * 0.1d;

                models.PlaceFrame(withChild ? [child, parent] : [parent]);
                models.Instances(withChild ? [child] : [], [], seconds: seconds);
                models.Instances([parent], [], seconds: seconds, pass: EntityModelSet.UndrawnPass);
            }

            return models.FurthestBarrelAngle;
        }

        float control = Spun(withChild: false);

        control.ShouldBeGreaterThan(0f, "the control: the barrel turned");
        Spun(withChild: true).ShouldBe(control, 1e-4f);
    }

    private static (string[][] Fired, int Simulated, (int, int, float)? Frame) Walk(bool childFirst)
    {
        PropModels.ModelFrames animated = AnimationEventWalkConformanceTests.Built() with
        {
            Attachments = MoveChildPlacementConformanceTests.WithMuzzle().Attachments,
        };
        SceneProp parent = AnimationEventWalkConformanceTests.Playing();
        SceneProp child = new(
            2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: parent.EntityIndex, AttachmentPoint: 1);

        EntityModelSet models = new() { Geometry = path => path == ChildModel ? MoveChildPlacementConformanceTests.WithMuzzle() : animated };
        models.Add([parent, child], path => path == ChildModel ? MoveChildPlacementConformanceTests.WithMuzzle() : animated);

        List<string[]> walked = [];
        int simulated = 0;

        foreach (double seconds in new[] { 0.1d, 0.6d, 0.7d })
        {
            List<FiredAnimationEvent> fired = [];
            models.Instances(childFirst ? [child, parent] : [parent, child], [], seconds: seconds);
            simulated = models.Simulated;
            models.TakeFiredEvents(fired);
            walked.Add([.. fired.Select(static one => one.Event.Options)]);
        }

        return ([.. walked], simulated, models.FrameOf(parent.EntityIndex));
    }
}
