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
