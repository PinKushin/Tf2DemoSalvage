using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A NEW entity in a reused slot, with the same model, starts with a new entity's state (B727).
/// </summary>
/// <remarks>
/// **A new serial is a new C++ object** — `C_BaseEntity::Init( entnum, iSerialNum )` (<c>c_baseentity.cpp:1088-1125</c>) —
/// so nothing the old occupant accumulated survives: `C_BaseAnimating`'s constructor sets the event walk to
/// <c>m_nEventSequence = -1</c> and the parities to −1, holds no IK, jiggle or bone cache, and its transitioner member is
/// empty (<c>c_baseanimating.cpp:673-748</c>); `CTFMinigun`'s `WeaponReset` zeroes the barrel
/// (<c>tf_weapon_minigun.cpp:144-147</c>). Every test plays an occupant, then a second one with the same index and model,
/// and its control keeps the SAME serial — one entity going on, which keeps everything.
/// </remarks>
public sealed class RecycledSlotConformanceTests
{
    /// <remarks>
    /// The skeleton is rebuilt and `OnNewModel` runs for the newcomer: two entities built and two reports. The control
    /// (serial kept) builds one and reports once.
    /// </remarks>
    [TestCase(0, 1)]
    [TestCase(1, 2)]
    public void Instances_ASecondOccupantWithTheSameModel_IsBuiltAndToldItsModelAfresh(int secondSerial, int expected)
    {
        int reports = 0;
        SceneProp first = new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
        EntityModelSet models = new() { ModelResolved = (_, _, _, _) => reports++ };

        models.Add([first], path => ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        models.Instances([first], [], seconds: 0d);
        models.Instances([first with { SerialNumber = secondSerial }], [], seconds: 0.1d);

        models.EntitiesBuilt.ShouldBe(expected);
        reports.ShouldBe(expected);
    }

    /// <remarks>
    /// The newcomer plays sequence 1 where the old occupant played 0. Its transitioner is empty, so nothing fades; the
    /// control — one entity changing sequence — fades the 0 it left (`SequenceTransitionTests`).
    /// </remarks>
    [TestCase(0, new[] { 0 })]
    [TestCase(1, new int[0])]
    public void Instances_ASecondOccupantOnAnotherSequence_FadesNothingOfTheFirst(int secondSerial, int[] fading)
    {
        EntityModelSet models = new() { Geometry = _ => SequenceTransitionTests.Frames() };
        List<SceneProp> drawn = [SequenceTransitionTests.Playing(sequence: 0)];

        models.Add(drawn, _ => SequenceTransitionTests.Frames());
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = SequenceTransitionTests.Playing(sequence: 1) with { SerialNumber = secondSerial };
        models.Instances(drawn, [], seconds: 0.05d);

        models.TransitionsOf(4).ShouldNotBeNull().Select(static root => root.Sequence).ShouldBe(fading);
    }

    /// <remarks>
    /// The old occupant walked to cycle 0.3. The newcomer's first frame, at 0.6, is a first frame of its sequence
    /// (<c>m_nEventSequence</c> −1 ≠ it, <c>c_baseanimating.cpp:3621-3635</c>), so it fires only the event at zero; the
    /// control walks (0.3, 0.6] and fires the one at a half.
    /// </remarks>
    [TestCase(0, "half")]
    [TestCase(1, "zero")]
    public void Instances_ASecondOccupantsFirstFrame_WalksItsEventsFromNothing(int secondSerial, string expected)
    {
        EntityModelSet models = new() { Geometry = _ => AnimationEventWalkConformanceTests.Built() };
        SceneProp first = AnimationEventWalkConformanceTests.Playing();
        List<FiredAnimationEvent> fired = [];

        models.Add([first], _ => AnimationEventWalkConformanceTests.Built());
        models.Instances([first], [], seconds: 0.1d);
        models.TakeFiredEvents(fired);
        fired.Select(static one => one.Event.Options).ShouldBe(["zero"], "the control: the first occupant's walk is live");
        fired.Clear();

        models.Instances([first], [], seconds: 0.3d);
        models.TakeFiredEvents(fired);
        fired.ShouldBeEmpty("and has walked to 0.3, short of the event at a half");

        models.Instances([first with { SerialNumber = secondSerial }], [], seconds: 0.6d);
        models.TakeFiredEvents(fired);

        fired.Select(static one => one.Event.Options).ShouldBe([expected]);
    }

    /// <remarks>
    /// The old minigun spun for three frames a tenth of a second apart: velocity 0.1, 0.2, 0.3, angle 0, 0.02, 0.05. A
    /// new minigun spinning on the next frame starts from <c>WeaponReset</c>'s zeroes — velocity 0.1, angle 0.1 × 0.1 =
    /// 0.01. The control carries on: 0.4, and 0.05 + 0.04 = 0.09.
    /// </remarks>
    [TestCase(0, 0.4f, 0.09f)]
    [TestCase(1, 0.1f, 0.01f)]
    public void Instances_ASecondMinigunInTheSlot_SpinsUpFromRest(int secondSerial, float velocity, float angle)
    {
        const int Spinning = 3;
        PropModels.ModelFrames model = MinigunBarrelWiringTests.Frames(barrel: true);
        EntityModelSet models = new() { Geometry = _ => model };
        SceneProp first = MinigunBarrelWiringTests.Minigun(Spinning);

        models.Add([first], _ => model);

        for (int frame = 0; frame < 3; frame++)
        {
            models.Instances([first], [], seconds: frame * 0.1d);
        }

        models.Instances([first with { SerialNumber = secondSerial }], [], seconds: 0.3d);

        (float Velocity, float Angle) spin = models.BarrelSpinOf(first.EntityIndex).ShouldNotBeNull();
        spin.Velocity.ShouldBe(velocity, 1e-6f);
        spin.Angle.ShouldBe(angle, 1e-6f);
    }

    /// <remarks>
    /// **The old occupant fading 0 into 1 is not the newcomer's** (B727): its queue is forgotten, not only its last
    /// sequence. The old prop left sequence 0 at 0 s; the newcomer plays 1 at 0.1 s and 0.12 s, still inside the 0.2 s
    /// window, and fades nothing. The control, one entity, still fades 0.
    /// </remarks>
    [TestCase(0, new[] { 0 })]
    [TestCase(1, new int[0])]
    public void Instances_ASecondOccupantWhileTheFirstWasFading_FadesNothingOfIt(int secondSerial, int[] fading)
    {
        EntityModelSet models = new() { Geometry = _ => SequenceTransitionTests.Frames() };
        List<SceneProp> drawn = [SequenceTransitionTests.Playing(sequence: 0)];

        models.Add(drawn, _ => SequenceTransitionTests.Frames());
        models.Instances(drawn, [], seconds: 0d);
        drawn[0] = SequenceTransitionTests.Playing(sequence: 1);
        models.Instances(drawn, [], seconds: 0.05d);
        models.TransitionsOf(4).ShouldNotBeNull().Single().Sequence.ShouldBe(0, "the control: the first occupant is fading 0");

        drawn[0] = SequenceTransitionTests.Playing(sequence: 1) with { SerialNumber = secondSerial };
        models.Instances(drawn, [], seconds: 0.1d);
        models.Instances(drawn, [], seconds: 0.12d);

        models.TransitionsOf(4).ShouldNotBeNull().Select(static root => root.Sequence).ShouldBe(fading);
    }

    /// <remarks>
    /// **A player's integrated cycle is the old player's, not the newcomer's** (B727): a player is a new `C_TFPlayer` on a
    /// new serial (`PlayerProps` carries it), and his cycle starts from his own clock. The old player ran at rate 1 to
    /// 0.25 s (cycle 0.25); the newcomer at rate 1.5 at 0.5 s is the closed form, 0.5 × 1.5 = 0.75. The control integrates
    /// on: 0.25 + 0.25 × 1.5 = 0.625.
    /// </remarks>
    [TestCase(0, 0.625f)]
    [TestCase(1, 0.75f)]
    public void Instances_ASecondPlayerInTheSlot_RunsHisCycleFromHisOwnClock(int secondSerial, float cycle)
    {
        SceneProp first = new(
            7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio,
            new ScenePose { X = 100f, Speed = 1f, PlaybackRate = 1f }, null, ClientSideAnimated: true);
        EntityModelSet models = new();

        models.Add([first], path => ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        models.Instances([first], [], seconds: 0d);
        models.Instances([first], [], seconds: 0.25d);

        SceneProp next = first with { Pose = first.Pose with { PlaybackRate = 1.5f }, SerialNumber = secondSerial };
        models.Instances([next], [], seconds: 0.5d);

        models.ShadowAnimation(next).Cycle.ShouldBe(cycle, 1e-5f);
    }

    /// <remarks>
    /// **A duck jump is the old player's** (B727): ducked in the air at 0 s, his offset ramps from 20 to 10 by 0.075 s.
    /// A newcomer ducking in the air at 0.075 s has just begun, 20. The control, one player, is at 10.
    /// </remarks>
    [TestCase(0, 10f)]
    [TestCase(1, 20f)]
    public void Instances_ASecondPlayerDuckingInTheAir_StartsHisDuckJump(int secondSerial, float offset)
    {
        SceneProp first = DuckJumpWiringTests.Cabinet(PlayerActivityState.Ducking);
        EntityModelSet models = new() { Geometry = _ => DuckJumpWiringTests.Frames() };

        models.Add([first], _ => DuckJumpWiringTests.Frames());
        models.Instances([first], [], seconds: 0d);
        models.Instances([first with { SerialNumber = secondSerial }], [], seconds: 0.075d);

        models.DuckJumpOffsetOf(first.EntityIndex).ShouldNotBeNull().ShouldBe(offset, 1e-3d);
    }

    /// <remarks>
    /// **In production `UpdateClientSideAnimations` is the frame's first per-entity pass** (`MomentScene` runs it before
    /// `Instances`), and it reads the slot's main-sequence hold, so the newcomer is forgotten there, before `Instances`:
    /// after it alone the old skeleton is gone. The control, the same entity, keeps it.
    /// </remarks>
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void UpdateClientSideAnimations_ASecondOccupant_ForgetsTheSlotBeforeInstances(int secondSerial, bool posed)
    {
        SceneProp first = new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
        EntityModelSet models = new();

        models.Add([first], path => ModelFramesFixture.OneSecondLoop(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        models.Instances([first], [], seconds: 0d);
        models.IsPosed(7).ShouldBeTrue("the control: the first occupant is posed");

        models.UpdateClientSideAnimations([first with { SerialNumber = secondSerial }]);

        models.IsPosed(7).ShouldBe(posed);
    }

    /// <remarks>
    /// **A child listed before its parent never borrows the old occupant's skeleton** (B727): the child asks for the
    /// parent's attachment from inside the simulate loop (`PlacementOf` → `PosedAttachmentMatrix`), so every slot is
    /// settled before any entity is simulated. The parent moves from X = 100 to 150; its muzzle is one unit along, the
    /// child two more and its own muzzle one more.
    ///
    /// **Neither row is the engine's 154**, and that is a separate, older divergence, filed in B727: a skinned child
    /// simulated before its parent reads the parent's skeleton before the parent's own simulate has placed it this frame,
    /// where the engine's `SetupBones` on the parent uses its current state. Kept, the parent lends last frame's skeleton
    /// (101 + 3 = 104, the control); forgotten, it has none yet, so the child composes onto the parent's origin, 150 + 3 = 153.
    /// The old occupant's 104 is what B727 removes; with the sweep inside the loop the newcomer's row read 104 too.
    /// </remarks>
    [TestCase(0, 104f)]
    [TestCase(1, 153f)]
    public void Instances_AChildListedBeforeARecycledParent_NeverBorrowsTheOldSkeleton(int secondSerial, float x)
    {
        const string ChildModel = "models/props_test/child.mdl";
        const string ParentModel = "models/props_test/parent.mdl";
        SceneProp child = new(2, ChildModel, SceneModelKind.Studio, new ScenePose { X = 2f }, AttachedTo: 1, AttachmentPoint: 1);
        SceneProp parent = new(1, ParentModel, SceneModelKind.Studio, new ScenePose { X = 100f });

        EntityModelSet models = new();
        models.Add([child, parent], _ => MoveChildPlacementConformanceTests.WithMuzzle());
        models.Instances([child, parent], [], seconds: 1d);

        SceneProp moved = parent with { Pose = parent.Pose with { X = 150f }, SerialNumber = secondSerial };
        models.Instances([child, moved], [], seconds: 2d);

        models.AttachmentPosition(2, "muzzle").ShouldNotBeNull().X.ShouldBe(x, 1e-3f);
    }

    /// <remarks>
    /// **A level shutdown destroys every entity**, so the next level's entity in the slot — even under the same serial —
    /// is a new object: the barrel above, after a shutdown, spins up from rest (0.1, 0.01). The control is the row above
    /// with the serial kept and no shutdown (0.4, 0.09).
    /// </remarks>
    [Test]
    public void LevelShutdown_TheNextLevelsMinigunInTheSlot_SpinsUpFromRest()
    {
        const int Spinning = 3;
        PropModels.ModelFrames model = MinigunBarrelWiringTests.Frames(barrel: true);
        EntityModelSet models = new() { Geometry = _ => model };
        SceneProp minigun = MinigunBarrelWiringTests.Minigun(Spinning);

        models.Add([minigun], _ => model);

        for (int frame = 0; frame < 3; frame++)
        {
            models.Instances([minigun], [], seconds: frame * 0.1d);
        }

        models.LevelShutdown();
        models.Add([minigun], _ => model);
        models.Instances([minigun], [], seconds: 0.3d);

        (float Velocity, float Angle) spin = models.BarrelSpinOf(minigun.EntityIndex).ShouldNotBeNull();
        spin.Velocity.ShouldBe(0.1f, 1e-6f);
        spin.Angle.ShouldBe(0.01f, 1e-6f);
    }
}
