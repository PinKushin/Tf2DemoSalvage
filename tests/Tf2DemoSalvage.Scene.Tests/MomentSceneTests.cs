using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Rebuilding the scene for one moment: what is drawn, what is packed, and in which order.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.ShowMoment</c> and the four members it drove**, so none of it had a test —
/// reaching it meant constructing a <c>MainForm</c>, which needs an STA thread, a Direct3D device
/// and the desktop lock (B188, B184).
///
/// The builder is told what it needs through <see cref="MomentInfo"/> rather than reaching for it,
/// which is <c>SetupRenderInfo_t</c>'s arrangement (<c>clientleafsystem.h:75</c>).
///
/// **What is NOT covered here, said rather than left to look covered: the phase ORDER.**
/// <c>cdll_client_int.cpp:2188-2210</c> runs <c>UpdateClientSideAnimations()</c> →
/// <c>SimulateEntities()</c> → <c>ThreadedBoneSetup()</c>, so sequence selection happens before
/// simulation and before any bone is built — and getting it wrong does not fail, it draws the
/// previous frame's pose. Nothing below would notice if the two were swapped.
///
/// It is not covered because the distinguishing input is expensive: with an empty model set
/// <c>SequenceFor</c> answers -1 and neither order does anything, so the test needs a packed model
/// carrying a real sequence table. That is the same trap
/// <c>APropWithNoSpeed_LeavesItsSequenceAlone</c> fell into once — it passed with its guard removed.
/// <c>UpdateClientSideAnimationsTests</c> covers the pass itself; only its POSITION here is open.
/// </remarks>
public sealed class MomentSceneTests
{
    [Test]
    public void Build_WithProps_DrawsThem()
    {
        // The baseline every other case here is measured against. Without it, a test asserting that
        // something is FILTERED cannot tell filtering from "nothing is ever drawn".
        MomentScene scene = Scene();

        scene.Build([], [Prop("models/props/crate.mdl", entity: 5)], Info());

        scene.Drawn.Select(prop => prop.EntityIndex).ShouldBe([5]);
    }

    [Test]
    public void Build_WithPlayers_TurnsThemIntoProps()
    {
        // **A player is a model at a pose, which is what the prop path already draws.** Ours needs
        // this step only because `DemoTimeline` splits `PlayerTracks` from `Props`; Valve has no
        // equivalent, since a player is already a `C_BaseAnimating` in the renderables list.
        MomentScene scene = Scene();

        scene.Build([Soldier(entity: 3)], [], Info());

        scene.Drawn.Select(prop => prop.EntityIndex).ShouldContain(3);
    }

    [Test]
    public void Build_WithAnAppearance_GivesAPlayerTheirWeaponsAnimationSlot()
    {
        // **This is the assertion for the thing that was nearly lost.** `EnsureWeaponRoles` is what
        // populates the roles, `GameAppearance` captures them, and `WeaponSuffix` lands on the pose's
        // `Slot` (`PlayerProps.cs:154`) — which is what picks a soldier's rocket-launcher animation
        // over the generic primary one.
        //
        // **Dropping any link in that chain does not fail.** Every suffix answers null, the pose
        // falls back, and a green suite says nothing. Only reading the value out the far end can
        // tell, which is why the observable here is `Pose.Slot` rather than "did it run".
        MomentScene scene = Scene();

        scene.Build([Soldier(entity: 3) with { WeaponClass = "tf_weapon_rocketlauncher" }], [], Info());

        scene.Drawn.Single(prop => prop.EntityIndex == 3).Pose.Slot.ShouldBe("PRIMARY");
    }

    [Test]
    public void Build_WhenTheWeaponRolesWereNeverRead_DrawsThePlayerWithNoSlot()
    {
        // **This is the exact regression, reproduced.** Dropping `EnsureWeaponRoles` does NOT leave
        // the appearance null — the class models are read when the archives open, so `ModelOf` keeps
        // working and the player is still drawn. Only `WeaponSuffix` goes null, and the animation
        // falls back to the generic primary form.
        //
        // So the distinguishing input is an appearance that knows the models and not the roles,
        // which is a state the null object cannot express — asserting against THAT would have been
        // measuring a different, louder failure.
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), new RecordingLogger())
        {
            Appearance = new Appearance { RolesKnown = false },
        };

        scene.Build([Soldier(entity: 3) with { WeaponClass = "tf_weapon_rocketlauncher" }], [], Info());

        scene.Drawn.Single(prop => prop.EntityIndex == 3).Pose.Slot.ShouldBeNull();
    }

    [Test]
    public void Build_WithNoAppearanceSet_DrawsNoPlayersAndSaysSo()
    {
        // **A null object is the right default and it hides a missed wiring, so it has to say so.**
        // It answers null to everything, including the model — so nobody is drawn at all, which is
        // silent in a viewer that may legitimately have no TF2 installed. That is the shape
        // `docs/memory/a-null-object-default-hides-a-missed-wiring.md` records, where 193 converted
        // call sites lost 202 log lines and the suite stayed green.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build([Soldier(entity: 3) with { WeaponClass = "tf_weapon_rocketlauncher" }], [], Info());

        scene.Drawn.ShouldBeEmpty();
        log.Count("no player appearance").ShouldBe(1);
    }

    [Test]
    public void Build_WithNoAppearanceOverManyFrames_SaysSoOnce()
    {
        // A wiring warning repeated sixty times a second is how a real warning stops being read —
        // which is B191 from the other direction.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        for (int frame = 0; frame < 5; frame++)
        {
            scene.Build([Soldier(entity: 3)], [], Info() with { Tick = frame });
        }

        log.Count("no player appearance").ShouldBe(1);
    }

    [Test]
    public void Build_WithAnAppearance_SaysNothingAboutIt()
    {
        // The other half of the pair: a correctly wired scene must be silent, or the line is noise
        // that gets filtered out and stops being read.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log)
        {
            Appearance = new Appearance(),
        };

        scene.Build([Soldier(entity: 3)], [], Info());

        log.Count("no player appearance").ShouldBe(0);
    }

    [Test]
    public void Build_WithNoPlayersAndNoAppearance_SaysNothing()
    {
        // A viewer with no demo open draws no players, so an unwired appearance is not yet a fault —
        // reporting it every frame from startup is how a real warning stops being noticed.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build([], [Prop("models/props/crate.mdl")], Info());

        log.Count("no player appearance").ShouldBe(0);
    }

    [Test]
    public void Build_InFirstPerson_DropsThePlayerBeingLookedThrough()
    {
        // **The engine does not draw the player whose eyes you are using.** Without this the view is
        // the inside of the recorder's own model — which is exactly what the first capture showed.
        MomentScene scene = Scene();

        scene.Build(
            [Soldier(entity: 3), Soldier(entity: 4)],
            [],
            Info() with { FirstPerson = true, Followed = 3, EyeCamera = Eye() });

        // The FOLLOWED player is the one dropped; everyone else is still drawn. Getting this
        // backwards was my first version of the test, and the code was right.
        scene.Drawn.Select(prop => prop.EntityIndex).ShouldNotContain(3);
        scene.Drawn.Select(prop => prop.EntityIndex).ShouldContain(4);
    }

    [Test]
    public void Build_InThirdPerson_DrawsEveryPlayer()
    {
        // **The control for the pair**, and it is what makes the case above a measurement of
        // first-person filtering rather than of a draw list that drops players generally.
        MomentScene scene = Scene();

        scene.Build([Soldier(entity: 3), Soldier(entity: 4)], [], Info());

        scene.Drawn.Select(prop => prop.EntityIndex).ShouldContain(3);
        scene.Drawn.Select(prop => prop.EntityIndex).ShouldContain(4);
    }

    [Test]
    public void Build_WhenTheModelSetGrows_UploadsOnce()
    {
        // **Packing is not uploading**, and conflating them is a bug this project has already had:
        // the arms were packed, posed, instanced, transformed correctly and submitted against
        // geometry the renderer did not have.
        Uploads uploads = new();
        MomentScene scene = Scene(uploads);

        scene.Build([], [Prop("models/props/crate.mdl")], Info());

        uploads.Count.ShouldBe(1);
    }

    [Test]
    public void Build_WhenTheModelSetIsUnchanged_DoesNotUploadAgain()
    {
        // **The control, and the reason it matters is cost.** The whole vertex buffer is rebuilt on
        // every upload, so doing it per frame is a rebuild per frame — measured at 193-231 ms each
        // when this was last wrong. A demo shows most of its models early and none of them twice.
        Uploads uploads = new();
        MomentScene scene = Scene(uploads);

        scene.Build([], [Prop("models/props/crate.mdl")], Info());
        scene.Build([], [Prop("models/props/crate.mdl")], Info() with { Tick = 2d });

        uploads.Count.ShouldBe(1);
    }

    /// <summary>
    /// That a device reporting no geometry gets it sent again (B219).
    /// </summary>
    /// <remarks>
    /// **The owner, looking at the viewer**: *"setting surface colors on removes the viewmodel and
    /// doesnt bring it back, so i need you to restart the program"*. It was every model, not just
    /// the viewmodel, and the log had said so 444,242 times in seven seconds —
    /// `*N was posed before its geometry was uploaded`, 617,923 lines between the two toggles.
    ///
    /// `SetSurfaceColours` ends in `Device3D.ClearWorld`, which disposes the world and empties
    /// `_packedModels` along with the buffers they feed. The map returns because `Invalidate`
    /// re-projects it. The models did not, because the test above is the rule the whole time: an
    /// upload happens when the set GREW, and after a clear it has not — it is the GPU-side copy
    /// that went.
    ///
    /// **B148 is this same bug on the map-change path** and built the mechanism this asserts: the
    /// packed set survived a second demo while its buffer did not, so `Pack` guards on
    /// <c>if (!grew &amp;&amp; Uploaded) return;</c> and level shutdown clears the flag. Surface colours was
    /// simply the one caller of `ClearWorld` never paired with that reset.
    ///
    /// **This is the pair to the test above and neither means much alone.** That one says an
    /// unchanged set costs nothing; this one says an unchanged set whose buffer is gone still gets
    /// sent. A fix that uploaded every frame passes this and fails that.
    /// </remarks>
    [Test]
    public void Build_AfterTheRenderersCopyWasDiscarded_UploadsAgain()
    {
        Uploads uploads = new();
        MomentScene scene = Scene(uploads);

        scene.Build([], [Prop("models/props/crate.mdl")], Info());

        uploads.Count.ShouldBe(1, "the first build has to upload before a second can be measured");

        // **What `ClearWorld` leaves behind**: the same props on this side, and a device holding
        // nothing. Said by the DEVICE rather than by resetting a flag on the scene, because that is
        // the change — the scene asks instead of remembering, so no caller can forget to tell it.
        uploads.HasModels = false;

        scene.Build([], [Prop("models/props/crate.mdl")], Info() with { Tick = 2d });

        uploads.Count.ShouldBe(2);
    }

    [Test]
    public void Build_WithNoModelUpload_StillDrawsRatherThanThrowing()
    {
        // Every frame before the device exists takes this path, and the viewer pumps frames from
        // the moment the window opens.
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), new RecordingLogger());

        Should.NotThrow(() => scene.Build([], [Prop("models/props/crate.mdl")], Info()));
    }

    [Test]
    public void Build_WithGeometryToPackAndNoUpload_SaysNothingEverReachedTheDevice()
    {
        // **The worst of the three wiring regressions, and it shipped** (B193). Nothing assigned
        // `Upload`, so `Pack` returned before uploading and NO entity geometry ever reached the GPU:
        // every model packed, posed, transformed correctly and submitted against a vertex buffer the
        // renderer never received. That is B148's symptom, and B148 took a 37 MB log to find.
        //
        // The guard is right — a viewer whose viewport has no handle yet has no device — but it must
        // not be silent once there is geometry to hand over.
        RecordingLogger log = new();
        MomentScene scene = new(
            new EntityModelSet { Geometry = OneTriangle }, new ViewmodelScene(), log);

        scene.Build([], [Prop("models/props/crate.mdl")], Info());

        log.Count("no model upload").ShouldBe(1);
    }

    [Test]
    public void Build_WithNothingToPackAndNoUpload_SaysNothing()
    {
        // **The control.** Before a demo is open there is nothing to upload, so an absent device is
        // not yet a fault — and a warning per frame from an idle viewer is how a real warning stops
        // being read.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build([], [], Info());

        log.Count("no model upload").ShouldBe(0);
    }

    [Test]
    public void Build_WithGeometryAndAnUpload_SaysNothing()
    {
        // The other control: a correctly wired scene must be silent, or the line is noise.
        RecordingLogger log = new();
        MomentScene scene = new(
            new EntityModelSet { Geometry = OneTriangle },
            new ViewmodelScene(),
            log)
        {
            Upload = new Uploads(),
        };

        scene.Build([], [Prop("models/props/crate.mdl")], Info());

        log.Count("no model upload").ShouldBe(0);
    }

    [Test]
    public void Build_ReportsThePhasesItSpentTimeIn()
    {
        // **A ledger with a residual, not a threshold on one event.** Every direct column being
        // small while the remainder is large is what says the cost is in something unmeasured — the
        // pattern that found B191, where `reports` held 129 ms of a 133 ms pose.
        MomentScene scene = Scene();

        MomentPhases phases = scene.Build([], [Prop("models/props/crate.mdl")], Info());

        phases.Total.ShouldBeGreaterThan(0L);
        phases.Drawn.ShouldBe(1);
    }

    /// <remarks>
    /// **The engine's order is view, then visibility, then bones** — `CViewRender::RenderView` calls
    /// `SetUpView` before `BuildWorldLists` and `BuildRenderablesList`, and `SetupBones` is reached
    /// only from the draw of what survived (B255). This project posed inside `Simulate`, which
    /// `FrameSequence.Run` executes BEFORE `PlaceCamera`, so the only frustum available at pose time
    /// was the previous frame's — and culling against a stale view pops entities in at the screen
    /// edge, which is a visible defect bought with a frame rate.
    ///
    /// **Simulation staying before the view is NOT the same question and is already correct**:
    /// `UpdateAllSystems` runs from `CHLClient::HudUpdate` before the view is built at all, which
    /// B203 established deliberately. Only the pose moves.
    ///
    /// So `Build` selects what is drawable and `Pose` turns it into instances, and this test is what
    /// stops them being quietly recombined.
    /// </remarks>
    /// <remarks>
    /// **The first version of this test passed before the split existed and was worthless.** The
    /// default fixture has no geometry source, so `Instances` produced nothing whichever side of the
    /// split it ran on — an input for which a correct implementation and the one being replaced
    /// predict the same observation. The model set here is given geometry so that posing has
    /// something to produce, which is what makes the assertion able to fail.
    /// </remarks>
    [Test]
    public void Build_WithoutPose_SelectsPropsButProducesNoInstances()
    {
        MomentScene scene = Posable();

        scene.Build([], [Prop("models/props/crate.mdl", entity: 5)], Info());

        scene.Drawn.Select(prop => prop.EntityIndex).ShouldBe([5]);
        scene.Instances.ShouldBeEmpty("the pose has not run yet");
    }

    /// <remarks>
    /// The control for the test above: with the same fixture, posing DOES produce an instance. One
    /// without the other cannot distinguish "the pose moved" from "the pose stopped working".
    /// </remarks>
    [Test]
    public void Pose_AfterBuild_ProducesTheInstances()
    {
        MomentScene scene = Posable();

        scene.Build([], [Prop("models/props/crate.mdl", entity: 5)], Info());
        scene.Pose(Info());

        scene.Instances.Count.ShouldBe(1);
    }


    /// <summary>
    /// One weapon with a display model, so <c>Weapons.For</c> can answer in a unit test.
    /// </summary>
    /// <remarks>
    /// **Written because its absence is what made B257 untestable.** `WeaponModels.For` returns null
    /// with no schema, so the first-person weapon never became a prop and the arms could not be told
    /// apart from it by counting. Synthetic rather than the real `items_game.txt` for the reason
    /// `CLAUDE.md` gives: eight megabytes read per test, and no ground truth — here the test put the
    /// model there and knows what to predict.
    /// </remarks>
    private const string WeaponSchema = """
        "items_game"
        {
            "items"
            {
                "205"
                {
                    "name" "Rocket Launcher"
                    "model_player" "models/weapons/w_models/w_rocketlauncher.mdl"
                }
            }
        }
        """;

    /// <remarks>
    /// **The players have to survive the gap between `Build` and `Pose`, and nothing tested it**
    /// (B257). Splitting the two put the player list in a field, and emptying that field on purpose
    /// left all 290 tests green — so the one piece of state the split introduced was unverified.
    ///
    /// `players` is how `AddViewmodel` finds the followed player, and from that player come the
    /// class ARMS (`Appearance.Hands`, which is B242's sleeve) and the first-person WEAPON
    /// (`Weapons.For`). Losing the list draws a first-person view with neither, silently.
    ///
    /// **Two earlier attempts were deleted for being insensitive**, and both failures are the same
    /// shape: an input where a working list and a lost one predict the same observation. A `v_` model
    /// gives one prop either way, because `AttachesToHands` compares the viewmodel's own path to the
    /// hands path and only the `c_` scheme separates them; and without a schema `Weapons.For`
    /// answers null whether or not the player was found. This setup fixes both — hands that match,
    /// and a schema that resolves — so the arms and the weapon are two props when the list survives
    /// and fewer when it does not.
    /// </remarks>
    [Test]
    public void Pose_AfterBuild_StillKnowsThePlayersBuildWasGiven()
    {
        MomentScene scene = Posable();

        scene.Weapons = new WeaponModels(
            _ => System.Text.Encoding.UTF8.GetBytes(WeaponSchema),
            new RecordingLogger());

        // **The viewmodel's own model IS the arms**, which is what `AttachesToHands` tests for and
        // what the `c_` scheme actually networks. The weapon then hangs off it as a second prop.
        scene.Viewmodels = new FakeViewmodels
        {
            MainHand = new SceneViewmodel(
                "models/weapons/c_models/c_soldier_arms.mdl",
                Sequence: 0,
                PlaybackRate: 1f,
                OwnerEntityIndex: 3,
                Slot: SceneViewmodel.MainHand),
        };

        MomentInfo info = Info() with
        {
            FirstPerson = true,
            Followed = 3,
            EyeCamera = Eye(),
            DrawViewmodel = true,
        };

        scene.Build(
            [Soldier(entity: 3) with { WeaponItem = 205 }],
            [],
            info);

        scene.Pose(info);

        scene.ViewmodelInstances.Count.ShouldBe(
            2,
            "arms and weapon — the followed player must still be findable when the pose runs");
    }

    /// <summary>A scene whose model set can actually produce an instance.</summary>
    private static MomentScene Posable()
    {
        EntityModelSet models = new()
        {
            Geometry = _ => new PropModels.ModelFrames(
                [
                    new PropVertex[]
                    {
                        new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                        new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                        new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                    },
                ],
                new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
                {
                    [0] = (0, 1, 0f),
                },
                [0],
                [true]),
        };

        return new MomentScene(models, new ViewmodelScene(), new RecordingLogger())
        {
            Upload = new Uploads(),
            Appearance = new Appearance(),
        };
    }

    [Test]
    public void Build_WithNoPlayersOrProps_DrawsNothingAndDoesNotThrow()
    {
        // A demo seeks to a tick before anyone has spawned, and every frame of a freshly opened
        // viewer looks like this.
        MomentScene scene = Scene();

        Should.NotThrow(() => scene.Build([], [], Info()));

        scene.Drawn.ShouldBeEmpty();
        scene.Instances.ShouldBeEmpty();
    }

    [Test]
    public void Pose_InFirstPersonWithNoViewmodelSource_SaysSoRatherThanDrawingNothingQuietly()
    {
        // **This is a regression that shipped, found two commits later.** When the scene rebuild
        // moved out of the form, nothing assigned `Viewmodels` — so `AddViewmodel` returned on its
        // first guard and the first-person weapon never drew, with the viewer suite green at 620/620
        // throughout. Exactly B193's shape, for the second time in three commits.
        //
        // The guard is right: a viewer with no demo open has no viewmodel source and must not throw.
        // What was wrong is that it was SILENT, so an unset source and a demo that genuinely carries
        // no viewmodel looked identical.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log)
        {
            Appearance = new Appearance(),
        };

        MomentInfo info = Info() with { FirstPerson = true, Followed = 3, EyeCamera = Eye() };

        scene.Build([Soldier(entity: 3)], [], info);
        scene.Pose(info);

        scene.ViewmodelCamera.ShouldBeNull();
        log.Count("no viewmodel source").ShouldBe(1);
    }

    [Test]
    public void Pose_OutOfFirstPersonWithNoViewmodelSource_SaysNothing()
    {
        // **The control.** Third person does not want a viewmodel at all, so an absent source is not
        // a fault there — and a warning every frame from a viewer nobody has put into first person
        // is how a real warning stops being read.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build([Soldier(entity: 3)], [], Info());
        scene.Pose(Info());

        log.Count("no viewmodel source").ShouldBe(0);
    }

    [Test]
    public void Pose_OutOfFirstPerson_ClearsTheViewmodelCamera()
    {
        // **Dropping the camera is how "draw none" is said.** The instance list is owned by the pose
        // step and survives paused frames on purpose, so leaving it populated while first person is
        // off would keep a weapon on screen after V was pressed.
        MomentScene scene = Scene();

        scene.Build([], [], Info() with { FirstPerson = false });
        scene.Pose(Info() with { FirstPerson = false });

        scene.ViewmodelCamera.ShouldBeNull();
    }

    [Test]
    public void Pose_InFirstPersonWithDrawViewmodelOff_DrawsNoWeaponEvenWithASource()
    {
        // **The assertion that actually covers the gate**, and the first attempt did not. That one
        // built a scene with NO viewmodel source and asserted the camera was null with the switch
        // off — which is true with the switch on as well, because an absent source drops the camera
        // by itself. Correct and broken predicted the same observation, so removing the
        // `!info.DrawViewmodel` clause reddened nothing. The fix is a bigger setup, not a sharper
        // assertion: a source that WOULD draw, so the switch is the only thing left deciding.
        MomentScene source = Scene();
        source.Viewmodels = new FakeViewmodels
        {
            MainHand = new SceneViewmodel(
                "models/weapons/v_models/v_rocketlauncher_soldier.mdl",
                Sequence: 0,
                PlaybackRate: 1f,
                OwnerEntityIndex: 3,
                Slot: SceneViewmodel.MainHand),
        };

        MomentInfo info = Info() with
        {
            FirstPerson = true,
            Followed = 3,
            EyeCamera = Eye(),
            DrawViewmodel = false,
        };

        source.Build([Soldier(entity: 3)], [], info);
        source.Pose(info);

        source.ViewmodelCamera.ShouldBeNull(
            "r_drawviewmodel 0 means no weapon in hand, however much of one is available");
    }

    [Test]
    public void Pose_InFirstPersonWithDrawViewmodelOn_DrawsTheWeapon()
    {
        // **The control for the case above**, identical but for the switch. Without it, "the switch
        // turned it off" is indistinguishable from "this setup never draws anything" — which is
        // exactly the trap the first attempt fell into.
        MomentScene source = Scene();
        source.Viewmodels = new FakeViewmodels
        {
            MainHand = new SceneViewmodel(
                "models/weapons/v_models/v_rocketlauncher_soldier.mdl",
                Sequence: 0,
                PlaybackRate: 1f,
                OwnerEntityIndex: 3,
                Slot: SceneViewmodel.MainHand),
        };

        MomentInfo info = Info() with
        {
            FirstPerson = true,
            Followed = 3,
            EyeCamera = Eye(),
            DrawViewmodel = true,
        };

        source.Build([Soldier(entity: 3)], [], info);
        source.Pose(info);

        source.ViewmodelCamera.ShouldNotBeNull(
            "the same setup with the switch on must reach the viewmodel");
    }

    /// <summary>A viewmodel source that hands back whatever it was given.</summary>
    private sealed class FakeViewmodels : IViewmodelSource
    {
        public SceneViewmodel? MainHand { get; init; }

        public SceneViewmodel? MainHandAt(int tick, int player) => MainHand;

        public SceneViewmodel? OffHandAt(int tick, int player) => null;
    }

    [Test]
    public void Pose_InFirstPersonWithDrawViewmodelOff_SaysNothingAboutAMissingSource()
    {
        // **With the viewmodel switched off, an absent source is not a wiring fault** — nothing was
        // going to be drawn. Warning anyway would be the same mistake the third-person clause above
        // already avoids, and it would fire every frame for anyone running `r_drawviewmodel 0`.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build(
            [Soldier(entity: 3)],
            [],
            Info() with { FirstPerson = true, DrawViewmodel = false });

        log.Count("no viewmodel source").ShouldBe(0);
    }

    [Test]
    public void Pose_InFirstPersonWithDrawViewmodelOn_StillReportsAMissingSource()
    {
        // **The control, and it is the one that matters here.** Without it the assertion above is
        // satisfied by a scene that stopped reporting missing sources altogether — which is exactly
        // the alarm B193 was filed to add, so silencing it by accident would undo that.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        MomentInfo info = Info() with { FirstPerson = true, DrawViewmodel = true };

        scene.Build([Soldier(entity: 3)], [], info);
        scene.Pose(info);

        log.Count("no viewmodel source").ShouldBe(1, "the switch is on, so a missing source is a fault");
    }

    [Test]
    public void Build_WithNoPlayers_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => Scene().Build(null!, [], Info()));
    }

    [Test]
    public void Build_WithNoProps_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => Scene().Build([], null!, Info()));
    }

    /// <summary>A scene with an empty model set and somewhere to report.</summary>
    private static MomentScene Scene(IModelUpload? upload = null) =>
        new(new EntityModelSet(), new ViewmodelScene(), new RecordingLogger())
        {
            Upload = upload ?? new Uploads(),
            Appearance = new Appearance(),
        };

    /// <summary>The ordinary third-person moment, which every case varies from.</summary>
    private static MomentInfo Info() =>
        new(
            Tick: 1d,
            CurrentTick: 1,
            FirstPerson: false,
            Followed: null,
            EyeCamera: null,
            IntervalPerTick: 0.015f,
            ViewmodelFieldOfView: 54f);

    /// <summary>A camera at the origin, for the first-person cases.</summary>
    private static FreeCamera Eye() =>
        new() { Origin = (0f, 0f, 64f), Angles = (0f, 0f, 0f), Aspect = 16f / 9f };

    private static ScenePlayer Soldier(int entity) =>
        new(
            EntityIndex: entity,
            X: entity * 100f,
            Y: 20f,
            Z: 30f,
            Team: SceneTeams.Red,
            Health: 200,
            PlayerClass: SoldierClass);

    private const int SoldierClass = 3;

    /// <summary>Geometry for any path, so a prop actually packs to something.</summary>
    /// <remarks>
    /// Needed by the upload cases: with no geometry the set never grows, so "did not upload" and
    /// "had nothing to upload" are the same observation.
    /// </remarks>
    private static PropModels.ModelFrames? OneTriangle(string path) =>
        ModelFramesFixture.OneTriangle(path);

    private static SceneProp Prop(string model, int entity = 1) =>
        new(entity, model, ScenePropTrack.Classify(model), new ScenePose(), null);

    /// <summary>Records uploads rather than making one, which needs no device.</summary>
    private sealed class Uploads : IModelUpload
    {
        public int Count { get; private set; }

        /// <summary>What a real device answers from its packed set (B219).</summary>
        /// <remarks>
        /// **Settable, because discarding the geometry is the condition worth testing.** A real
        /// device answers false after `ClearWorld`; this lets a test say the same thing without one.
        /// </remarks>
        public bool HasModels { get; set; }

        public void UploadModels(EntityModelSet models)
        {
            Count++;
            HasModels = true;
        }
    }

    /// <summary>A stand-in for the installed game, so this needs no TF2 and no window.</summary>
    private sealed class Appearance : IPlayerAppearance
    {
        /// <summary>Whether the weapon scripts have been read, which is a separate step.</summary>
        /// <remarks>
        /// **The two halves arrive at different times, and that is the whole point of the flag.**
        /// Class models are read when the archives open; weapon roles need the demo as well, so
        /// they are read lazily on the first rebuild. A viewer that skipped the second still draws
        /// every player — it just draws them with the wrong weapon animation.
        /// </remarks>
        public bool RolesKnown { get; init; } = true;

        public string? ModelOf(int playerClass) =>
            playerClass == SoldierClass ? "models/player/soldier.mdl" : null;

        public string? WeaponSuffix(string? weaponClass, int? playerClass) =>
            RolesKnown && weaponClass is not null ? "PRIMARY" : null;

        public bool Airwalks(int playerClass) => true;

        /// <inheritdoc/>
        public bool Lands(int playerClass) => true;

        public string? Hands(int playerClass) =>
            playerClass == SoldierClass ? "models/weapons/c_models/c_soldier_arms.mdl" : null;
    }
}
