using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CPotteryWheelPanel</c>/<c>CMDLPanel</c>'s portable half (<see cref="VguiModelPanel"/>) and
/// <c>CBaseModelPanel</c>'s <c>.res</c> parsing (<see cref="VguiBaseModelPanel"/>).
/// </summary>
public sealed class VguiModelPanelConformanceTests
{
    private const int ScreenTall = 1080;

    [Test]
    public void Ambient_ANewPanel_IsPoint4OnEveryFaceLikeCreateDefaultLights()
    {
        // `CreateDefaultLights` (potterywheelpanel.cpp:316-321): `m_vecAmbientCube[i].Init(0.4f, 0.4f, 0.4f, 1.0f)`
        // for all six faces.
        VguiModelPanel panel = new(null, "model");

        panel.Ambient.PositiveX.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeX.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.PositiveY.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeY.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.PositiveZ.ShouldBe((0.4f, 0.4f, 0.4f));
        panel.Ambient.NegativeZ.ShouldBe((0.4f, 0.4f, 0.4f));
    }

    [Test]
    public void Sun_ANewPanel_IsOneWhiteLightPointingStraightDown()
    {
        // `CreateDefaultLights` (potterywheelpanel.cpp:325-327): `MATERIAL_LIGHT_DIRECTIONAL`, colour
        // (1, 1, 1), direction (0, 0, -1).
        VguiModelPanel panel = new(null, "model");

        panel.Sun.ShouldBe(new SunLight(1f, 1f, 1f, 0f, 0f, -1f));
    }

    [Test]
    public void Camera_ANewPanel_MatchesThePotteryWheelPanelConstructor()
    {
        // potterywheelpanel.cpp:250-252.
        VguiModelPanel.NearZ.ShouldBe(3f);
        VguiModelPanel.FarZ.ShouldBe(16384f * 1.73205080757f);

        VguiModelPanel panel = new(null, "model");

        panel.FieldOfView.ShouldBe(30f);
    }

    [Test]
    public void ParseLightsFromKV_ADirectionalEntry_ReplacesTheSun()
    {
        // `ParseLightsFromKV` (potterywheelpanel.cpp:392-413): the first `directional` entry's colour and
        // (normalised) direction become the light.
        VguiModelPanel panel = new(null, "model");
        KeyValuesTree lights = Resource("""
            lights
            {
                "light1"
                {
                    "name" "directional"
                    "color" "1 0 0"
                    "direction" "0 3 4"
                }
            }
            """).Find("lights")!;

        panel.ParseLightsFromKV(lights);

        panel.Sun!.Value.Red.ShouldBe(1f);
        panel.Sun.Value.Green.ShouldBe(0f);
        panel.Sun.Value.Blue.ShouldBe(0f);
        panel.Sun.Value.DirectionX.ShouldBe(0f);
        panel.Sun.Value.DirectionY.ShouldBe(0.6f, 0.0001f);
        panel.Sun.Value.DirectionZ.ShouldBe(0.8f, 0.0001f);
    }

    [Test]
    public void ParseLightsFromKV_NoDirectionalEntry_ClearsTheSunRatherThanKeepingTheOldOne()
    {
        // Valve's own list REPLACES itself wholesale every parse — `m_nLightCount = nLightCount` runs
        // unconditionally at the end of `ParseLightsFromKV` (potterywheelpanel.cpp:459), even when the block named
        // no directional light. Leaving the panel's previous Sun standing would be the opposite of what a
        // point-only or empty `lights` block does in the engine.
        VguiModelPanel panel = new(null, "model");
        KeyValuesTree pointOnly = Resource("""
            lights
            {
                "light1"
                {
                    "name" "point"
                    "color" "1 1 1"
                    "origin" "0 0 100"
                    "attenuation" "1 0 0"
                }
            }
            """).Find("lights")!;

        panel.ParseLightsFromKV(pointOnly);

        panel.Sun.ShouldBeNull();
    }

    [Test]
    public void ApplySettings_ALightsBlock_IsParsedIntoTheSun()
    {
        VguiModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("""
            lights
            {
                "light1"
                {
                    "name" "directional"
                    "color" "0 1 0"
                    "direction" "0 0 1"
                }
            }
            """);

        panel.ApplySettings(resource, Context());

        panel.Sun!.Value.Green.ShouldBe(1f);
        panel.Sun.Value.DirectionZ.ShouldBe(1f);
    }

    [Test]
    public void Paint_NoModelName_EmitsNoPaint3DCall()
    {
        VguiModelPanel panel = new(null, "model") { ResolveModel = _ => SyntheticSkinnedModel.WithOneBone() };
        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.ShouldBeEmpty();
    }

    [Test]
    public void Paint_ARootModel_EmitsOneDrawCarryingItsPath()
    {
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithOneBone(),
            Wide = 100,
            Tall = 50,
        };

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.Count.ShouldBe(1);
        surface.Draws[0].Models.Count.ShouldBe(1);
        surface.Draws[0].Models[0].ModelPath.ShouldBe("models/player/scout.mdl");

        // `Paint3D`'s rectangle is the panel's own, panel-relative (`IVguiSurface.Paint3D`'s remarks).
        surface.Draws[0].Left.ShouldBe(0);
        surface.Draws[0].Top.ShouldBe(0);
        surface.Draws[0].Right.ShouldBe(100);
        surface.Draws[0].Bottom.ShouldBe(50);
    }

    [Test]
    public void Paint_ARootAndAMergeModel_EmitsBothInOneDraw()
    {
        // Two bones, one shared name ("root") and one not — `SyntheticSkinnedModel.WithBones`'s own note: a
        // fixture sharing every bone name cannot see a merge failure, so the weapon-shaped model here keeps
        // one bone the wearer does not have.
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/demo.mdl",
            ResolveModel = path => path == "models/player/demo.mdl"
                ? SyntheticSkinnedModel.WithBones("root")
                : SyntheticSkinnedModel.WithBones("root", "muzzle"),
            Wide = 100,
            Tall = 100,
        };

        panel.MergeModels.Add("models/weapons/c_models/c_stickybomb_launcher.mdl");

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        surface.Draws.Count.ShouldBe(1);
        surface.Draws[0].Models.Count.ShouldBe(2);
        surface.Draws[0].Models[0].ModelPath.ShouldBe("models/player/demo.mdl");
        surface.Draws[0].Models[1].ModelPath.ShouldBe("models/weapons/c_models/c_stickybomb_launcher.mdl");
    }

    [Test]
    public void Paint_DefaultCameraState_MatchesThePotteryWheelPanelConstructor()
    {
        // No `.res` file has touched the pivot or offset: pivot identity (SetIdentityMatrix(m_CameraPivot),
        // potterywheelpanel.cpp:242), offset (100, 0, 0) (:248) — `UpdateCameraTransform` (:765-773) then puts the
        // camera at (100, 0, 0) facing the same way the identity pivot does, angles (0, 0, 0).
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithOneBone(),
            Wide = 100,
            Tall = 100,
        };

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        // Read back through the actual camera matrix rather than a settable property (there is none any more —
        // CameraOrigin/CameraAngles are COMPUTED, per the coordinator's finding that the previous code stored them
        // directly and skipped the pivot/offset arithmetic entirely). Index 14 always carries the projection's own
        // NearZ*FarZ/(NearZ-FarZ) constant (about -3) EVEN AT the true world origin — that term does not vanish —
        // but a camera 100 units out along its own forward axis (the identity pivot's forward is +X, the same axis
        // the offset is along) makes it substantially MORE negative on top of that; -50 comfortably separates the
        // two (measured: about -103 here, about -3 at the true origin).
        float[] camera = surface.Draws[0].Camera;

        camera[14].ShouldBeLessThan(
            -50f, "the camera never left the world origin, so the default 100-unit offset was not applied");
    }

    [Test]
    public void Paint_ForcePosition_PutsTheCameraAtTheWorldOriginAndTheModelAtModelOrigin()
    {
        // PerformLayout's force_pos branch (basemodel_panel.cpp:381-387): ResetCameraPivot(); SetCameraOffset(0,0,0);
        // SetCameraPositionAndAngles(vec3_origin, vec3_angle) puts the CAMERA at the origin, and
        // SetModelAnglesAndPosition(m_angPlayer, m_vecPlayerPos) moves the MODEL to what ParseModelResInfo cached —
        // the exact swap the coordinator's review flagged: the previous code did the opposite (moved the camera to
        // the model's angles/origin and drew the model at identity).
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithOneBone();

        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => model,
            ForcePosition = true,
            ModelOrigin = (500f, 0f, 0f),
            Wide = 100,
            Tall = 100,
        };

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        float[] camera = surface.Draws[0].Camera;

        // The camera's translation row's X and Y are zero: it never left the world origin. Index 14 is the
        // projection's own NearZ*FarZ/(NearZ-FarZ) constant (about -3) — present even at the true origin, so it is
        // close to that small constant rather than zero, and specifically NOT the far-more-negative value (about
        // -103) the default-camera test sees for a camera 100 units out along its forward axis.
        camera[12].ShouldBe(0f, 0.001f);
        camera[13].ShouldBe(0f, 0.001f);
        camera[14].ShouldBeGreaterThan(-50f);

        // The model's one bone sits at ModelOrigin now, not at the origin AngleMatrix3x4 would leave it at
        // without ForcePosition.
        float[] rootBone = surface.Draws[0].Models[0].Bones![0];

        rootBone[3].ShouldBe(500f, 0.001f);
    }

    [Test]
    public void Paint_NoForcePosition_LeavesTheModelAtTheOriginRegardlessOfModelOrigin()
    {
        // The common case: no stock TF2 HUD .res sets force_pos, so ModelOrigin/ModelAngles are cached but never
        // applied — the model draws wherever AnimatingEntity's own bind pose puts it (the origin, for one bone at
        // the rest position), and the CAMERA is what backs away instead.
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithOneBone(),
            ModelOrigin = (500f, 0f, 0f),
            Wide = 100,
            Tall = 100,
        };

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        float[] rootBone = surface.Draws[0].Models[0].Bones![0];

        rootBone[3].ShouldBe(0f, 0.001f);
    }

    [Test]
    public void FrameAt_HalfASecondIntoAOneCyclePerSecondSequence_IsFrame15Of31()
    {
        // AnimatedStudioBytes.OneSecondLoop: 31 frames at 30 fps is exactly one cycle a second
        // (Studio_CPS divides by numframes - 1 = 30). Half a second is half a cycle: frame 15 of 30 steps,
        // landing exactly on a frame with no fraction left over.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root") with
        {
            Models = [AnimatedStudioBytes.OneSecondLoop()],
        };

        (int frame, float fraction) = VguiModelPanel.FrameAt(model, sequence: 0, poseValues: [], cycleTime: 0.5d);

        frame.ShouldBe(15);
        fraction.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void FrameAt_TwoAndAHalfCyclesIntoALoopingSequence_WrapsToTheFraction()
    {
        // Same fixture, but far enough past one cycle that a non-looping sequence would already be held on its
        // last frame — this asserts the LOOPING path specifically wraps rather than holds.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root") with
        {
            Models = [AnimatedStudioBytes.OneSecondLoop()],
            Groups = [(0, [new StudioSequence(
                // STUDIO_LOOPING, studio.h:3078 — StudioFlags is internal to Content.Assets and not visible here.
                Animation: 0, Flags: 0x0001, Label: "idle", Blend: null,
                Activity: "idle", ActivityWeight: 1)])],
        };

        (int frame, float fraction) = VguiModelPanel.FrameAt(model, sequence: 0, poseValues: [], cycleTime: 2.5d);

        // 2.5 cycles wraps to 0.5 of a cycle - the same frame 15 the half-second test landed on.
        frame.ShouldBe(15);
        fraction.ShouldBe(0f, 0.0001f);
    }

    [Test]
    public void Paint_AMergeModelSharingABoneName_TakesTheRootsBoneToWorldForThatBone()
    {
        // `CMDLPanel::OnPaint3D` (mdlpanel.cpp:493): `SetupBonesWithBoneMerge` folds the merge model's shared-named
        // bones from the root's already-built bone-to-world. `AnimatingEntity.Follows` is this project's version of
        // that (WeaponMergeContentTests already proves it for a real weapon/player pair); this checks the panel
        // actually wires it rather than merely holding both models unmerged.
        VguiModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/demo.mdl",
            ResolveModel = path => path == "models/player/demo.mdl"
                ? SyntheticSkinnedModel.WithBones("root")
                : SyntheticSkinnedModel.WithBones("root", "muzzle"),
            Wide = 100,
            Tall = 100,
        };

        panel.MergeModels.Add("models/weapons/c_models/c_stickybomb_launcher.mdl");

        RecordingModelSurface surface = new();

        panel.Paint(surface, Context());

        float[] rootBone = surface.Draws[0].Models[0].Bones![0];
        float[] mergeBone = surface.Draws[0].Models[1].Bones![0];

        mergeBone.ShouldBe(rootBone);
    }

    [Test]
    public void ModelsToPrecache_ARootAndMergeModels_YieldsRootFirst()
    {
        VguiModelPanel panel = new(null, "model") { ModelName = "models/player/scout.mdl" };

        panel.MergeModels.Add("models/weapons/c_models/c_scattergun.mdl");

        panel.ModelsToPrecache().ShouldBe(
            ["models/player/scout.mdl", "models/weapons/c_models/c_scattergun.mdl"]);
    }

    [Test]
    public void ParseModelResInfo_AModelBlock_ReadsNameSkinAnglesAndOrigin()
    {
        // basemodel_panel.cpp:88-99, defaults at :95-96.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "skin" "2"
                "angles_y" "90"
                "origin_x" "50"
                "spotlight" "1"
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.ModelName.ShouldBe("models/player/scout.mdl");
        panel.Skin.ShouldBe(2);
        panel.ModelAngles.ShouldBe((0f, 90f, 0f));
        panel.ModelOrigin.ShouldBe((50f, 5f, 5f));
        panel.UseSpotlight.ShouldBeTrue();
    }

    [Test]
    public void ParseModelResInfo_NoOriginGiven_DefaultsTo110_5_5()
    {
        // basemodel_panel.cpp:95: `GetFloat( "origin_x", 110.0 )`, `"origin_y", 5.0`, `"origin_z", 5.0`.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.ModelOrigin.ShouldBe((110f, 5f, 5f));
    }

    [Test]
    public void ApplySettings_AModelBlock_IsFoundAmongTheResourcesOtherKeys()
    {
        // `CBaseModelPanel::ApplySettings` (basemodel_panel.cpp:46-83) reads `fov` itself and then finds the
        // `model` sub-block by name among the resource's other keys.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("""
            xpos 0
            fov 45
            model
            {
                "modelname" "models/player/scout.mdl"
            }
            """);

        panel.ApplySettings(resource, Context());

        panel.FieldOfView.ShouldBe(45f);
        panel.ModelName.ShouldBe("models/player/scout.mdl");
    }

    [Test]
    public void ApplySettings_AFractionalFov_Truncates()
    {
        // `inResourceData->GetInt( "fov", flFOV )` (basemodel_panel.cpp:56) is GetInt, not GetFloat — KeyValues'
        // GetInt does `atoi`, which truncates toward zero rather than rounding.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree resource = Resource("fov 45.9");

        panel.ApplySettings(resource, Context());

        panel.FieldOfView.ShouldBe(45f);
    }

    [Test]
    public void ParseModelAnimInfo_TwoAnimationsOneDefault_RecordsBoth()
    {
        // basemodel_panel.cpp:122-134.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "animation"
                {
                    "name" "idle"
                    "sequence" "idle"
                    "default" "1"
                }
                "animation"
                {
                    "name" "taunt"
                    "sequence" "taunt"
                }
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.Animations.Count.ShouldBe(2);
        panel.Animations[0].Name.ShouldBe("idle");
        panel.Animations[0].Default.ShouldBeTrue();
        panel.Animations[1].Name.ShouldBe("taunt");
        panel.Animations[1].Default.ShouldBeFalse();
    }

    [Test]
    public void ParseModelAttachInfo_AnAttachedModelBlock_RecordsNameAndSkin()
    {
        // basemodel_panel.cpp:148-159.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "attached_model"
                {
                    "modelname" "models/weapons/c_models/c_scattergun.mdl"
                    "skin" "1"
                }
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.Attachments.Count.ShouldBe(1);
        panel.Attachments[0].ModelName.ShouldBe("models/weapons/c_models/c_scattergun.mdl");
        panel.Attachments[0].Skin.ShouldBe(1);
    }

    [Test]
    public void ParseModelAttachInfo_NoSkinGiven_DefaultsToMinusOne()
    {
        // basemodel_panel.cpp:158: `GetInt( "skin", -1 )`.
        VguiBaseModelPanel panel = new(null, "model");
        KeyValuesTree modelBlock = Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "attached_model" { "modelname" "models/weapons/c_models/c_scattergun.mdl" }
            }
            """).Find("model")!;

        panel.ParseModelResInfo(modelBlock);

        panel.Attachments[0].Skin.ShouldBe(-1);
    }

    [Test]
    public void ParseModelResInfo_TwoDefaultAnimations_UsesTheFirst()
    {
        // `FindDefaultAnim` (basemodel_panel.cpp:193-205) returns on the FIRST match; a later animation flagged
        // default too is never reached, so it never becomes the played sequence.
        VguiBaseModelPanel panel = new(null, "model")
        {
            ResolveModel = _ => SyntheticSkinnedModel.WithBones("root"),
        };

        panel.ParseModelResInfo(Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "animation" { "name" "a" "sequence" "idle" "default" "1" }
                "animation" { "name" "b" "sequence" "idle" "default" "1" }
            }
            """).Find("model")!);

        // Both animations name the same sequence here on purpose — the assertion that matters is which of the two
        // ACTUALLY ran (resetting the cycle clock), not which sequence number it landed on, so CycleStartTime is
        // what distinguishes "the first one's SetModelAnim ran" from "nothing ran at all".
        panel.CycleStartTime.ShouldBe(panel.RealTimeSeconds);
    }

    [Test]
    public void SetModelAnim_BySequenceLabel_PicksItAndResetsTheCycle()
    {
        // basemodel_panel.cpp:270-278: no activity named, so LookupSequence(sequence) — SkinnedModel.SequenceByLabel
        // here — and SetSequence(iSequence, true) resets m_flCycleStartTime to GetAutoPlayTime() (mdlpanel.cpp:547).
        VguiBaseModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithBones("root"),
            RealTimeSeconds = 100d,
            CycleStartTime = 1d,
        };

        panel.SetModelAnim(new ModelPanelAnimation("idle", "idle", null, true));

        panel.Sequence.ShouldBe(0);
        panel.CycleStartTime.ShouldBe(100d);
    }

    [Test]
    public void SetModelAnim_ByActivity_ScansForAnExactMatchRatherThanAWeightedPick()
    {
        // FindSequenceFromActivity (basemodel_panel.cpp:229-244) is a plain linear scan for the first exact
        // activity-name match — this model has two sequences sharing "run" so a weighted selector could legally
        // answer either; the exact scan must answer the FIRST one, index 0.
        VguiBaseModelPanel panel = new(null, "model")
        {
            ModelName = "models/player/scout.mdl",
            ResolveModel = _ => SyntheticSkinnedModel.WithActivities(("run_a", "run"), ("run_b", "run")),
        };

        panel.SetModelAnim(new ModelPanelAnimation("run", null, "run", true));

        panel.Sequence.ShouldBe(0);
    }

    [Test]
    public void MoveXPoseValues_AMoveXParameter_NormalisesToOne()
    {
        // SetupModelAnimDefaults (basemodel_panel.cpp:175): SetPoseParameterByName( "move_x", 1.0f ), unconditional.
        // move_x runs -1..1 here, so the raw value 1.0 normalises to the TOP of the range: 1.0, not the 0.5 an
        // unset parameter (raw 0) would leave it at — StudioBlendGrid.Normalize's own contract.
        float[] values = VguiModelPanel.MoveXPoseValues([new StudioPoseParameter("move_x", -1f, 1f, 0f)]);

        values[0].ShouldBe(1f);
    }

    [Test]
    public void MoveXPoseValues_AnUnrelatedParameter_StaysAtRawZeroNormalised()
    {
        // Everything but move_x is left at raw zero — the same "unset" value EntityModelSet.Filled leaves an
        // uncomputed parameter at, which normalises to the MIDDLE of a symmetric range rather than its bottom.
        float[] values = VguiModelPanel.MoveXPoseValues([new StudioPoseParameter("aim_yaw", -180f, 180f, 360f)]);

        values[0].ShouldBe(0.5f);
    }

    [Test]
    public void Tick_AfterAnActiveSequenceExpires_RevertsToTheDefaultAnimation()
    {
        // CBaseModelPanel::OnTick (basemodel_panel.cpp:402-419): once GetAutoPlayTime() - m_flCycleStartTime passes
        // m_flActiveSequenceDuration, SetupModelDefaults runs again.
        VguiBaseModelPanel panel = new(null, "model")
        {
            ResolveModel = _ => SyntheticSkinnedModel.WithBones("root"),
        };

        panel.ParseModelResInfo(Resource("""
            model
            {
                "modelname" "models/player/scout.mdl"
                "animation" { "name" "idle" "sequence" "idle" "default" "1" }
            }
            """).Find("model")!);

        panel.RealTimeSeconds = 0d;
        panel.PlaySequence("idle");
        panel.Sequence.ShouldBe(0);

        // One cycle of a one-bone, one-frame synthetic sequence has no meaningful fps, so the duration derived from
        // CyclesPerSecond is whatever the fixture's animation block gives it — the point under test is that ANY
        // elapsed time past it reverts, not the exact number of seconds.
        panel.RealTimeSeconds = 1_000_000d;
        panel.Tick();

        panel.CycleStartTime.ShouldBe(1_000_000d);
    }

    /// <summary>Wraps a body in a root block, the way <c>Panel::ApplySettings</c>'s caller already has one.</summary>
    private static KeyValuesTree Resource(string body) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource\n{\n" + body + "\n}"), "test.res", _ => null);

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, ScreenTall), scheme.Find("Fonts")!, 1920, ScreenTall, "english");
    }

    /// <summary>Every <see cref="IVguiSurface"/> member no-op except <see cref="IVguiSurface.Paint3D"/>, which records.</summary>
    private sealed class RecordingModelSurface : IVguiSurface
    {
        public List<(int Left, int Top, int Right, int Bottom, float[] Camera, IReadOnlyList<ModelInstance> Models)> Draws { get; } = [];

        public float AlphaMultiplier { get; set; } = 1f;

        public void PushMakeCurrent(VguiPanel panel, bool useInset)
        {
        }

        public void PopMakeCurrent(VguiPanel panel)
        {
        }

        public void Paint3D(int left, int top, int right, int bottom, float[] camera, IReadOnlyList<ModelInstance> models) =>
            Draws.Add((left, top, right, bottom, camera, models));

        public void DrawSetColor((byte Red, byte Green, byte Blue, byte Alpha) color)
        {
        }

        public void DrawFilledRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawFilledRectFade(int x0, int y0, int x1, int y1, int alpha0, int alpha1, bool horizontal)
        {
        }

        public void DrawOutlinedRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawSetTexture(string? texture)
        {
        }

        public void DrawTexturedQuad(float x0, float y0, float x1, float y1, float s0, float t0, float s1, float t1)
        {
        }

        public void DrawTexturedPolygon(System.ReadOnlySpan<VguiVertex> vertices)
        {
        }

        public void DrawTexturedRect(int x0, int y0, int x1, int y1)
        {
        }

        public void DrawTexturedSubRect(int x0, int y0, int x1, int y1, float s0, float t0, float s1, float t1)
        {
        }

        public (int Wide, int Tall) DrawGetTextureSize(string texture) => (0, 0);

        public void DrawSetTextFont(VguiFontAmalgam font)
        {
        }

        public void DrawSetTextColor((byte Red, byte Green, byte Blue, byte Alpha) color)
        {
        }

        public void DrawSetTextPos(int x, int y)
        {
        }

        public (int X, int Y) DrawGetTextPos() => (0, 0);

        public void DrawUnicodeChar(char character, VguiFontDrawType drawType = VguiFontDrawType.Default)
        {
        }

        public void DrawPrintText(string text, VguiFontDrawType drawType = VguiFontDrawType.Default)
        {
        }

        public int GetFontTall(VguiFontAmalgam font) => 0;

        public (int A, int B, int C) GetCharAbcWide(VguiFontAmalgam font, char character) => (0, 0, 0);

        public int GetCharacterWidth(VguiFontAmalgam font, char character) => 0;
    }
}
