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
    public void Build_InFirstPersonWithNoViewmodelSource_SaysSoRatherThanDrawingNothingQuietly()
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

        scene.Build(
            [Soldier(entity: 3)],
            [],
            Info() with { FirstPerson = true, Followed = 3, EyeCamera = Eye() });

        scene.ViewmodelCamera.ShouldBeNull();
        log.Count("no viewmodel source").ShouldBe(1);
    }

    [Test]
    public void Build_OutOfFirstPersonWithNoViewmodelSource_SaysNothing()
    {
        // **The control.** Third person does not want a viewmodel at all, so an absent source is not
        // a fault there — and a warning every frame from a viewer nobody has put into first person
        // is how a real warning stops being read.
        RecordingLogger log = new();
        MomentScene scene = new(new EntityModelSet(), new ViewmodelScene(), log);

        scene.Build([Soldier(entity: 3)], [], Info());

        log.Count("no viewmodel source").ShouldBe(0);
    }

    [Test]
    public void Build_OutOfFirstPerson_ClearsTheViewmodelCamera()
    {
        // **Dropping the camera is how "draw none" is said.** The instance list is owned by the pose
        // step and survives paused frames on purpose, so leaving it populated while first person is
        // off would keep a weapon on screen after V was pressed.
        MomentScene scene = Scene();

        scene.Build([], [], Info() with { FirstPerson = false });

        scene.ViewmodelCamera.ShouldBeNull();
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

    private static SceneProp Prop(string model, int entity = 1) =>
        new(entity, model, ScenePropTrack.Classify(model), new ScenePose(), null);

    /// <summary>Records uploads rather than making one, which needs no device.</summary>
    private sealed class Uploads : IModelUpload
    {
        public int Count { get; private set; }

        public void UploadModels(EntityModelSet models) => Count++;
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

        public string? Hands(int playerClass) =>
            playerClass == SoldierClass ? "models/weapons/c_models/c_soldier_arms.mdl" : null;
    }
}
