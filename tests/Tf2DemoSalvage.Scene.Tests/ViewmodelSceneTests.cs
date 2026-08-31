using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What the first-person view contains at a tick.
/// </summary>
/// <remarks>
/// **These tests exist because <c>AddViewmodel</c> had none and could not have any** (B188). It was
/// 319 lines inside a 7,263-line form, reachable only by constructing a <c>MainForm</c> — which
/// needs the STA, a Direct3D device and the desktop lock — so the whole first-person path was
/// verifiable only by launching the viewer and looking.
///
/// That is why <c>viewmodel pass skipped</c> has been in every log without anybody able to assert on
/// it, and it is why three open bugs against this path (B170, B186, B187) have no regression tests
/// between them.
///
/// **The subject moved to Scene so its tests could be plain <c>net10.0</c>** — B184's half of the
/// same job. They run on the Linux measurement boxes and under Stryker, neither of which the
/// viewer's suite can do.
/// </remarks>
public sealed class ViewmodelSceneTests
{
    private const int Player = 3;
    private const int Tick = 100;

    [Test]
    public void Build_ABluPlayersViewmodel_DrawsInTheBlueSkinFamily()
    {
        // **The owner, watching his own demos:** *"the 1st person pov always showing a red player
        // viewmodel"* (B242). `CEconItemView::GetSkin( iTeam, bViewmodel )` takes the owner's team;
        // nothing here was passing one, so every viewmodel drew family 0 — and family 0 is RED on
        // every two-family `c_` model. Measured on the shipped files:
        //
        //   c_medigun.mdl     skin0 'c_medigun'       skin1 'c_medigun_blue'
        //   c_medic_arms.mdl  skin0 'medic_red'       skin1 'medic_blue'
        //
        // The player's own BODY has taken its skin from its team since `PlayerProps` was written.
        // The hands in front of the camera never did.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") with { OwnerEntityIndex = Player } },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null,
            teamOf: _ => SceneTeams.Blu);

        scene.Props.ShouldNotBeEmpty();
        scene.Props[0].Pose.Skin.ShouldBe(1);
    }

    [Test]
    public void Build_ARedPlayersViewmodel_DrawsInTheRedSkinFamily()
    {
        // **The control, and it is the half that was accidentally right.** RED is family 0, which
        // is also what an unset skin gives — so a test with only the BLU case could be satisfied by
        // a rule that always returned 1, and one with only the RED case by changing nothing at all.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") with { OwnerEntityIndex = Player } },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null,
            teamOf: _ => SceneTeams.Red);

        scene.Props.ShouldNotBeEmpty();
        scene.Props[0].Pose.Skin.ShouldBe(0);
    }

    [Test]
    public void Build_AViewmodelThatNamesNoOwner_DrawsInTheFirstFamily()
    {
        // **An era demo does not always send `m_hOwner`** — `DemoTimeline._viewmodelsNameOwners`
        // exists for exactly that — so the team cannot be asked for and family 0 is the honest
        // answer. The same rule `PlayerSkin.ForTeam` applies to a player whose team is not yet
        // known, rather than flashing them blue for a tick.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null);

        scene.Props.ShouldNotBeEmpty();
        scene.Props[0].Pose.Skin.ShouldBe(0);
    }

    [Test]
    public void Build_APlayerHoldingNothing_DrawsNothing()
    {
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels(), Tick, Player, At, hands: null, heldWeapon: null);

        // **Empty is a state, not a failure.** The caller drops its camera to say "draw none", and
        // the instance list survives paused frames on purpose — so leaving it populated would keep
        // a weapon on screen after first person was turned off.
        scene.Props.ShouldBeEmpty();
    }

    [Test]
    public void Build_AWeaponThatIsItsOwnViewmodel_DrawsOneModel()
    {
        // **The one-model scheme.** CTFWeaponBase::GetViewModel asks the item whether it
        // ShouldAttachToHands(); when it does not, the networked viewmodel IS the weapon and there
        // is no second model. Drawing one anyway is how one weapon becomes two on screen.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/v_rocketlauncher.mdl") },
            Tick,
            Player,
            At,
            hands: "models/weapons/c_models/c_soldier_arms.mdl",
            heldWeapon: "models/weapons/w_rocketlauncher.mdl");

        scene.Props.Count.ShouldBe(1);
        scene.Props[0].ModelPath.ShouldBe("models/weapons/v_rocketlauncher.mdl");
    }

    [Test]
    public void Build_ArmsAsTheViewmodel_DrawsTheWeaponAsASecondModelParentedToThem()
    {
        // **The two-model scheme**, which modern TF2 uses: the networked viewmodel carries the
        // player's ARMS and the gun is a separate C_ViewmodelAttachmentModel the client creates and
        // parents to them (econ_entity.cpp:1153). It is not networked, so no demo carries it and it
        // has to be rebuilt from the item the player is holding.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/c_models/c_soldier_arms.mdl") },
            Tick,
            Player,
            At,
            hands: "models/weapons/c_models/c_soldier_arms.mdl",
            heldWeapon: "models/weapons/c_models/c_rocketlauncher.mdl");

        scene.Props.Count.ShouldBe(2);
        scene.Props[1].ModelPath.ShouldBe("models/weapons/c_models/c_rocketlauncher.mdl");

        // Parented to the arms, not standing on its own: the attachment is created with
        // SetLocalOrigin( vec3_origin ) and bone-merged, so its bones take the arms' outright.
        scene.Props[1].AttachedTo.ShouldBe(ViewmodelScene.ArmsEntityIndex);
    }

    [Test]
    public void Build_ArmsWithNoWeaponToHold_DrawsOnlyTheArms()
    {
        // The control for the scheme above: it is the PAIR of conditions that adds a model, and a
        // version keying on either alone would draw an empty attachment or none at all.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/c_models/c_soldier_arms.mdl") },
            Tick,
            Player,
            At,
            hands: "models/weapons/c_models/c_soldier_arms.mdl",
            heldWeapon: null);

        scene.Props.Count.ShouldBe(1);
    }

    [Test]
    public void AttachesToHands_PathsDifferingOnlyBySeparator_AreTheSameModel()
    {
        // **The comparison decides which of two exclusive schemes applies, so getting it wrong does
        // not throw — it draws the wrong number of models.** One name comes off the wire and one
        // from the class schema, and they disagree on slashes.
        ViewmodelScene.AttachesToHands(
            @"models\weapons\c_models\c_soldier_arms.mdl",
            "models/weapons/c_models/c_soldier_arms.mdl")
            .ShouldBeTrue();

        // And the control, or a comparison that always said true would pass the line above.
        ViewmodelScene.AttachesToHands(
            "models/weapons/v_rocketlauncher.mdl",
            "models/weapons/c_models/c_soldier_arms.mdl")
            .ShouldBeFalse();
    }

    [Test]
    public void AttachesToHands_AClassWithNoHandModel_IsNeverTheTwoModelScheme()
    {
        ViewmodelScene.AttachesToHands("models/weapons/v_rocketlauncher.mdl", null).ShouldBeFalse();
        ViewmodelScene.AttachesToHands("models/weapons/v_rocketlauncher.mdl", string.Empty).ShouldBeFalse();
    }

    [Test]
    public void Build_ASpysWatch_IsDrawnAsWellAsTheWeaponRatherThanInsteadOfIt()
    {
        // **The owner, who has played the class**: "main viewmodel doesnt get hidden when a spy goes
        // invis, the watch just comes up and everything goes transparent". A viewer answering only
        // with the main hand is one model short of what that player saw.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels
            {
                MainHand = Weapon("models/weapons/v_knife.mdl"),
                OffHand = Weapon("models/weapons/v_watch.mdl"),
            },
            Tick,
            Player,
            At,
            hands: null,
            heldWeapon: null);

        scene.Props.Select(prop => prop.ModelPath)
            .ShouldBe(["models/weapons/v_knife.mdl", "models/weapons/v_watch.mdl"]);
    }

    [Test]
    public void Build_EveryProp_IsPlacedAtTheCameraPlayingTheDemosSequence()
    {
        // **The demo's sequence is played, never one chosen here.** Substituting VM_IDLE was tried
        // and cost the placement: on a spy it replaced the recorded 34 with 3, posing the arms for
        // a weapon they were not holding. The owner's rule — "we shouldnt be forcing any sequence
        // only stuff from the demo or how valve does it" — and the engine agrees, since nothing in
        // its viewmodel path picks an idle.
        ViewmodelPlacement at = new(10f, 20f, 30f, 1f, 2f, 3f);

        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels { MainHand = Weapon("models/weapons/v_knife.mdl", sequence: 34) },
            Tick,
            Player,
            at,
            hands: null,
            heldWeapon: null);

        ScenePose pose = scene.Props[0].Pose;

        pose.Sequence.ShouldBe(34);
        (pose.X, pose.Y, pose.Z).ShouldBe((10f, 20f, 30f));
        (pose.Pitch, pose.Yaw, pose.Roll).ShouldBe((1f, 2f, 3f));
    }

    [Test]
    public void Build_TheSameWeaponTwice_ReportsChangedOnceRatherThanEveryFrame()
    {
        // **Once per weapon, not once per frame.** Measured 2026-08-24: the viewmodel lines printed
        // 6,588 times each in two minutes and the log reached 8.2 MB at roughly 1,280 writes a
        // second (B163). What they answer is "which model, playing what" — a question about a
        // WEAPON, which changes when the player switches.
        ViewmodelScene scene = new();
        FakeViewmodels source = new() { MainHand = Weapon("models/weapons/v_knife.mdl") };

        scene.Build(source, Tick, Player, At, null, null).Changed.ShouldBeTrue();
        scene.Build(source, Tick + 1, Player, At, null, null).Changed.ShouldBeFalse();
    }

    [Test]
    public void Build_ADifferentWeapon_ReportsChangedAgain()
    {
        // The control for the test above. A flag that latched false would silence the line for the
        // rest of the demo, which is the failure the dedupe is meant to avoid in the other
        // direction — a log that says nothing when the thing it describes changes.
        ViewmodelScene scene = new();
        FakeViewmodels source = new() { MainHand = Weapon("models/weapons/v_knife.mdl") };

        scene.Build(source, Tick, Player, At, null, null);

        source.MainHand = Weapon("models/weapons/v_revolver.mdl");

        scene.Build(source, Tick + 1, Player, At, null, null).Changed.ShouldBeTrue();
    }

    [Test]
    public void Build_TheThreeProps_CarryDistinctEntityIndices()
    {
        // They are this project's own numbering rather than anything the engine has, and two props
        // sharing an index would make one overwrite the other's bones in the entity registry.
        ViewmodelSceneResult scene = new ViewmodelScene().Build(
            new FakeViewmodels
            {
                MainHand = Weapon("models/weapons/c_models/c_soldier_arms.mdl"),
                OffHand = Weapon("models/weapons/v_watch.mdl"),
            },
            Tick,
            Player,
            At,
            hands: "models/weapons/c_models/c_soldier_arms.mdl",
            heldWeapon: "models/weapons/c_models/c_rocketlauncher.mdl");

        scene.Props.Select(prop => prop.EntityIndex).Distinct().Count().ShouldBe(3);
    }

    private static ViewmodelPlacement At => new(0f, 0f, 0f, 0f, 0f, 0f);

    private static SceneViewmodel Weapon(string path, int sequence = 0) =>
        new(path, sequence, 1f, OwnerEntityIndex: Player, Slot: 0);

    /// <summary>A viewmodel source that answers whatever the test set.</summary>
    /// <remarks>
    /// Two lines of state, which is the point of <see cref="IViewmodelSource"/> existing: a
    /// <c>DemoTimeline</c> is constructible only from a real demo file, so a scene that depended on
    /// one could be exercised only by opening a demo.
    /// </remarks>
    private sealed class FakeViewmodels : IViewmodelSource
    {
        public SceneViewmodel? MainHand { get; set; }

        public SceneViewmodel? OffHand { get; set; }

        public SceneViewmodel? MainHandAt(int tick, int player) => MainHand;

        public SceneViewmodel? OffHandAt(int tick, int player) => OffHand;
    }
}
