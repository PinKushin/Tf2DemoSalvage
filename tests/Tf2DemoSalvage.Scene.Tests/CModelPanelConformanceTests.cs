using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// `CModelPanel` (basemodelpanel.cpp) and the two `CTFHudMatchStatus` uses of it: the match doors (`ShowMatchStartDoors`,
/// tf_hud_match_status.cpp:646) and the round sign (`ShowRoundSign`, :726).
/// </summary>
/// <remarks>
/// Every model here has the sequences ref, open, close, intro and outro (0-4) and one bone; its "logos" bodygroup is index 3,
/// and <c>SetBodygroup</c> answers <c>body + value * 10</c>.
/// </remarks>
public sealed class CModelPanelConformanceTests
{
    private const string DoorsBlock = """
        "MatchDoors"
        {
            "fieldName" "MatchDoors" "wide" "640" "tall" "480" "visible" "0" "fov" "70"
            "model"
            {
                "modelname" "models/vgui/versus_doors.mdl" "skin" "0" "origin_x" "120" "origin_y" "0" "origin_z" "-77"
                "animation" { "name" "ref" "sequence" "ref" "default" "1" }
                "animation" { "name" "open" "sequence" "open" }
                "animation" { "name" "close" "sequence" "close" }
            }
        }
        "RoundSignModel"
        {
            "fieldName" "RoundSignModel" "wide" "640" "tall" "480" "visible" "0" "fov" "70"
            "model"
            {
                "modelname" "models/props_ui/banner.mdl" "skin" "0" "angles_x" "30" "angles_y" "180" "origin_x" "150" "origin_z" "62" "spotlight" "1"
                "animation" { "name" "ref" "sequence" "ref" "default" "1" }
                "animation" { "name" "intro" "sequence" "intro" }
            }
        }
        """;

    [Test]
    public void ApplySettings_AModelBlock_ParsesItsInfoAndDefaultAnimation()
    {
        TfHudMatchStatus status = Built();
        VguiModelPanel sign = status.RoundSignModel;

        sign.FieldOfView.ShouldBe(70);
        VguiModelPanelModelInfo info = sign.ModelInfo.ShouldNotBeNull();
        (info.ModelName, info.Skin, info.AbsAngles, info.OriginOffset, info.UseSpotlight)
            .ShouldBe(("models/props_ui/banner.mdl", 0, (30f, 180f, 0f), (150f, 5f, 62f), true), "origin_y defaults to 5 (:131)");
        info.Animations.Count.ShouldBe(2);

        sign.UpdateModel();
        sign.Sequence.ShouldBe(0, "the default animation's sequence, ref (:431-446)");
    }

    [Test]
    public void OnCommand_AnimationOpen_PlaysThatSequenceFromCycleZero()
    {
        VguiModelPanel doors = Built().MatchStartModelPanel;

        doors.OnCommand("animation open");

        (doors.HasModel, doors.Sequence, doors.Cycle).ShouldBe((true, 1, 0f), "UpdateModel then SetSequence( command + 10 ) (:100-104)");
    }

    [Test]
    public void UpdateAnimations_AFireCommandInAChildEvent_ReachesTheModelPanelsOnCommand()
    {
        TfHudMatchStatus status = Built();
        HudViewport viewport = (HudViewport)status.Parent!;

        viewport.Animations.StartAnimationSequence(status, "Close");
        viewport.Animations.UpdateAnimations(1f, viewport.Context!);

        status.MatchStartModelPanel.Sequence.ShouldBe(2, "FireCommand → msg.parent->OnCommand (AnimationController.cpp:763)");
    }

    [Test]
    public void HandleCountdown_TenSecondsBeforeTheFirstCasualRound_ShutsTheCasualDoors()
    {
        TfHudMatchStatus status = Built();
        List<string> sounds = [];

        status.SoundEmitter = sounds.Add;
        status.HandleGameEvent(Event("restart_timer_time", new SceneGameRules(false, 0, false) { MatchGroup = 7 }, ("time", 10)));

        VguiModelPanel doors = status.MatchStartModelPanel;
        doors.UpdateModel();
        (doors.ModelSkin, doors.ModelBody).ShouldBe((3, 10), "casual: skin 3, logos 1 (tf_match_description_casual.cpp:73-78)");
        sounds.ShouldBe(["MatchMaking.RoundStartCasual"]);
        ((HudViewport)status.Parent!).Animations.ActiveAnimationCount.ShouldBe(1, "HudMatchStatus_ShowMatchStartDoors");
    }

    [Test]
    public void HandleCountdown_MannVsMachine_ShowsNoDoors()
    {
        TfHudMatchStatus status = Built();
        List<string> sounds = [];

        status.SoundEmitter = sounds.Add;
        status.HandleGameEvent(Event("restart_timer_time", new SceneGameRules(false, 0, false) { MatchGroup = 0 }, ("time", 10)));

        sounds.ShouldBeEmpty("\"Don't show in MvM...for now\" (tf_match_description_mvm.cpp:52)");
        status.MatchStartModelPanel.HasModel.ShouldBeFalse();
    }

    [Test]
    public void RoundStart_ALadderMatchPastItsFirstRound_DropsTheRoundSign()
    {
        TfHudMatchStatus status = Built();

        status.HandleGameEvent(Event("teamplay_round_start", new SceneGameRules(false, 0, false) { MatchGroup = 2, RoundsPlayed = 2 }));

        VguiModelPanel sign = status.RoundSignModel;
        sign.UpdateModel();
        (sign.ModelSkin, sign.ModelBody).ShouldBe((10, 10), "\"The comp skins start at skin 8\", logos 1 (tf_match_description_comp.cpp:94-100)");
    }

    [Test]
    public void RoundStart_TheFirstRound_DropsNoSign()
    {
        TfHudMatchStatus status = Built();

        status.HandleGameEvent(Event("teamplay_round_start", new SceneGameRules(false, 0, false) { MatchGroup = 2 }));

        status.RoundSignModel.HasModel.ShouldBeFalse("only rounds > 1 (:555)");
    }

    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void ShowMatchSummary_ALadderMatch_OpensTheDoorsOnlyWithAStage(bool stage, int animations)
    {
        TfHudMatchStatus status = Built();

        status.HandleGameEvent(Event("show_match_summary", new SceneGameRules(false, 0, false) { MatchGroup = 2, MapHasMatchSummaryStage = stage }));

        ((HudViewport)status.Parent!).Animations.ActiveAnimationCount.ShouldBe(animations, "ShowMatchWinDoors or _NoOpen (:602-609)");
    }

    [Test]
    public void Paint_TheRoundSign_DrawsUnderItsOwnCameraWithTheSpotlight()
    {
        TfHudMatchStatus status = Built();
        HudViewport viewport = (HudViewport)status.Parent!;
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        status.RoundSignModel.Paint(surface, viewport.Context!);

        (int Left, int Top, int Right, int Bottom, float[] Camera, IReadOnlyList<ModelInstance> Models) draw = surface.Draws.ShouldHaveSingleItem();
        (draw.Left, draw.Top, draw.Right, draw.Bottom).ShouldBe((0, 0, 640, 480));
        ModelInstance sign = draw.Models.ShouldHaveSingleItem();
        sign.Sun.ShouldBeNull("SetLocalLights( 0 ) then the spotlight alone (:649-657)");
        sign.Light.ShouldNotBeNull().PositiveX.ShouldBe((0.4f, 0.4f, 0.4f));

        // From (0, 0, 200) at the origin offset (150, 5, 62) plus three quarters of the bounds' height: the fixture's are empty.
        LocalLight spot = sign.Locals.ShouldHaveSingleItem();
        (spot.Spot, spot.X, spot.Y, spot.Z, spot.SpotExponent).ShouldBe((true, 0f, 0f, 200f, 5f));
        float length = MathF.Sqrt((150f * 150f) + (5f * 5f) + (138f * 138f));
        spot.Direction.X.ShouldBe(150f / length, 1e-5);
        spot.Direction.Z.ShouldBe(-138f / length, 1e-5);
        spot.SpotOuter.ShouldBe(MathF.Cos(0.873f));
        VguiModelPanel.ScaleFovByWidthRatio(70f, 1f).ShouldBe(70f, 1e-4, "a 4:3 panel keeps its field of view");
    }

    [Test]
    public void ShowMatchStartDoors_TheResource_FillsEachTeamsListByIndexDescending()
    {
        TfHudMatchStatus status = Built();
        HudState state = ((HudViewport)status.Parent!).State with
        {
            ScoreboardPlayers =
            [
                new SceneScoreboardPlayer(1) { Connected = true, Team = 2 },
                new SceneScoreboardPlayer(2) { Connected = true, Team = 3 },
                new SceneScoreboardPlayer(4) { Connected = false, Team = 2 },
                new SceneScoreboardPlayer(5) { Connected = true, Team = 2 },
                new SceneScoreboardPlayer(6) { Connected = true, Team = 1 },
            ],
            Names = new Dictionary<int, string> { [1] = "One", [2] = "Two", [5] = "Five" },
            Teams = [new SceneTeam(2), new SceneTeam(3)],
        };

        status.UpdatePlayerList(state);

        VguiSectionedListPanel red = status.PlayerListRed;
        IReadOnlyList<int> order = red.ItemsInPaintOrder();
        order.Count.ShouldBe(2, "connected RED only (:818-832)");
        red.GetItemData(order[0])!["name"].ShouldBe("Five", "no \"score\" is ever set, so the higher index first (:766-776)");
        red.GetItemData(order[1])!["name"].ShouldBe("One");
        red.GetItemFgColor(order[0]).ShouldBe(((byte)255, (byte)64, (byte)64, (byte)255), "COLOR_RED");
        red.GetItemBgColor(order[0]).ShouldBe(((byte)120, (byte)120, (byte)120, (byte)80));
        status.PlayerListBlue.ItemCount.ShouldBe(1);
    }

    [TestCase(3, 7, true)]
    [TestCase(3, 0, false)]
    public void UpdateTeamInfo_PremadeParties_ShowTheLeaderAvatarsInsteadOfTheTeamImages(int red, int blue, bool avatars)
    {
        TfHudMatchStatus status = Built();
        HudState state = ((HudViewport)status.Parent!).State with
        {
            ScoreboardPlayers = [],
            Rules = new SceneGameRules(false, 0, false) { PartyLeaderRed = red, PartyLeaderBlue = blue },
        };

        status.UpdateTeamInfo(state);

        (status.RedLeaderAvatarImage.Visible, status.BlueTeamName.Visible, status.RedTeamImage.Visible).ShouldBe((avatars, avatars, !avatars));
    }

    [Test]
    public void Localized_ACompetitiveTournamentWithParties_NamesTheLeadersTeam()
    {
        HudState state = new HudState(true, true, 0, 100, true) with
        {
            ConVars = new HudConVars(name => name == "mp_tournament" ? "1" : null),
            Rules = new SceneGameRules(false, 0, false) { MatchGroup = 2, PartyLeaderRed = 3, PartyLeaderBlue = 7 },
            ScoreboardPlayers = [new SceneScoreboardPlayer(3) { Connected = true }],
            Names = new Dictionary<int, string> { [3] = "#50%&co" },
        };
        Func<string, string?> find = token => token == "#TF_Team_PartyLeader" ? "Team %s" : null;

        TfTeamNames.Localized(2, state, find).ShouldBe("Team *50*&&co", "UTIL_SafeName (cdll_util.cpp:801-837)");
        TfTeamNames.Localized(3, state, find).ShouldBe("BLU", "leader 7 is not connected, so the localized name");
        TfTeamNames.Localized(3, state with { Rules = state.Rules with { EventTeamStatus = 1 } }, token => token == "#TF_Pyro" ? "Pyro" : null)
            .ShouldBe("Pyro", "INVADERS_ARE_PYRO: BLU is the pyros (:138-140)");
    }

    /// <remarks>
    /// `CParticleCollection::Init` gives an unseeded collection `(int)this + Plat_MSTime()` (B469), so two panels' slams
    /// never draw alike; D136's stand-in is a distinct, reproducible, non-zero seed per created collection (B490).
    /// </remarks>
    [Test]
    public void ParticlePanel_TwoEffectsOfOneSystem_AreSeededDistinctlyAndNonZero()
    {
        Dictionary<string, ParticleSystem> systems = new(StringComparer.OrdinalIgnoreCase) { ["versus_door_slam"] = Slam() };
        TfParticlePanel.Effect first = new("versus_door_slam");
        TfParticlePanel.Effect second = new("versus_door_slam");

        first.SetParticleSystem("versus_door_slam", systems, _ => null);
        second.SetParticleSystem("versus_door_slam", systems, _ => null);

        int a = first.System.ShouldNotBeNull().Particles.Seed;
        int b = second.System.ShouldNotBeNull().Particles.Seed;

        (a != 0, b != 0, a != b).ShouldBe((true, true, true));
    }

    [Test]
    public void ParticlePanel_TheResBlock_PlacesTheSlamAtTheCentreUnstartedUntilStart0()
    {
        TfHudMatchStatus status = Built();
        HudViewport viewport = (HudViewport)status.Parent!;
        TfParticlePanel slam = (TfParticlePanel)status.FindChildByName("FrontParticlePanel")!;

        // "c0": the panel's centre; y from the bottom (:421-422); `start_activated` 0, `loop` 0.
        TfParticlePanel.Effect effect = slam.Effects.ShouldHaveSingleItem();
        (effect.X, effect.Y, effect.Scale, effect.Loop, effect.Started).ShouldBe((320, 240, 2f, false, false));

        viewport.ParticleSystems = new Dictionary<string, ParticleSystem>(StringComparer.OrdinalIgnoreCase) { ["versus_door_slam"] = Slam() };

        // `RunEventChild FrontParticlePanel PlayDoorSlamParticles` → `FireCommand 0 "start0"` (hudanimations_tf.txt).
        slam.OnCommand("start0");
        effect.Started.ShouldBeTrue();

        viewport.Think(viewport.State with { RealTime = 10f });
        slam.Think();
        viewport.Think(viewport.State with { RealTime = 10.5f });
        slam.Think();

        // Half a second on engine->Time() is five 0.1 sub-steps (B492); each adds ( curtime − ( curtime − dt ) ) · 66 in
        // float, and the five sum to a hair under 33, so the floor emits 32 — the engine's own float arithmetic.
        effect.System.ShouldNotBeNull().Particles.Count.ShouldBe(32, "66 a second for half a second, in five sub-steps");
    }

    [Test]
    public void OrthoCamera_APointLeftOfAndAboveTheOrigin_LandsWhereTheTranslateAndScalePutIt()
    {
        // View x is world −y, view y world z (ComputeViewMatrix); Translate( 320, 240 ) then Scale( 2 ) in a 640×480 ortho.
        float[] m = TfParticlePanel.OrthoCamera(320, 240, 2f, 640, 480);
        (float x, float y, float z) world = (0f, -10f, 5f);

        float clipX = (world.x * m[0]) + (world.y * m[4]) + (world.z * m[8]) + m[12];
        float clipY = (world.x * m[1]) + (world.y * m[5]) + (world.z * m[9]) + m[13];

        clipX.ShouldBe(((320f + 20f) * 2f / 640f) - 1f, 1e-6);
        clipY.ShouldBe(((240f + 10f) * 2f / 480f) - 1f, 1e-6);
    }

    private static ParticleSystem Slam() => new(
        "versus_door_slam",
        [
            new ParticleFunction("emit_continuously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 66d),
            }),
        ],
        [
            new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 5d),
                ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 5d),
            }),
        ],
        [],
        [new ParticleFunction("render_animated_sprites", "draw", new Dictionary<string, DmxValue>(StringComparer.Ordinal))],
        [],
        new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    [Test]
    public void SetupModel_StartFramed_FitsTheHeaderBoundsIntoTheFieldOfView()
    {
        // Bounds ±10 × ±20 × 0..40, fov 54 on 640×480: the widest corner's `fabs( z / tanY − x )` is 63.73, ×1.1 is 70.110,
        // less the framed origin's x of 110 (:862); y and z are minus the framed origin's (:863-864). Symmetric, so centred.
        VguiModelPanel framed = Framed(startFramed: true);

        framed.UpdateModel();

        VguiModelPanelModelInfo info = framed.ModelInfo.ShouldNotBeNull();
        info.OriginOffset.X.ShouldBe(-39.88959f, 1e-3);
        (info.OriginOffset.Y, info.OriginOffset.Z).ShouldBe((-5f, -25f));
        info.ViewportOffset.X.ShouldBe(0f, 1e-3);
        info.ViewportOffset.Y.ShouldBe(0f, 1e-3);
    }

    [Test]
    public void SetupModel_NotStartFramed_KeepsTheResOrigin()
    {
        VguiModelPanel framed = Framed(startFramed: false);

        framed.UpdateModel();

        framed.ModelInfo!.OriginOffset.ShouldBe((110f, 5f, 5f));
    }

    [Test]
    public void DrawnModelName_AnHwmModelNamed_IsThePlainOneBecauseUseHWMorphModelsIsFalse() =>
        Framed(startFramed: false).DrawnModelName.ShouldBe("models/plain.mdl", "UseHWMorphModels returns false (baseplayer_shared.cpp:104)");

    /// <remarks>
    /// **A <c>CModelPanelModel</c> is a C_BaseFlex whose <c>m_viewtarget</c> is never set** (<c>basemodelpanel.h:23</c>;
    /// zeroed only in a debug build, <c>c_baseflex.cpp:129-131</c>), and with no controllers it never reaches
    /// <c>SetViewTarget</c>'s clamp: its eye looks at (0, 0, 0). The model unrotated at the res origin (110, 5, 5), with an eye
    /// on bone 0 at (10, 0, 0), up +z: the eye E is at (120, 5, 5) and looks back at the world origin, so `$irisu` is minus
    /// the right of −E with +z up. A second paint rewrites the same face (B725 review).
    /// </remarks>
    [Test]
    public void Paint_AModelWithAnEyeAndNoControllers_LooksAtTheOriginIntoOneKeptFace()
    {
        VguiModelPanel panel = Framed(startFramed: false, eyed: true);
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        HudViewport viewport = (HudViewport)panel.Parent!;
        VguiContext context = viewport.Context!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, context);
        panel.Paint(surface, context);

        surface.Draws.Count.ShouldBe(2);
        StudioFace first = surface.Draws[0].Models.ShouldHaveSingleItem().Face.ShouldNotBeNull();
        StudioFace second = surface.Draws[1].Models.ShouldHaveSingleItem().Face.ShouldNotBeNull();
        second.ShouldBeSameAs(first);

        EyeShading eye = second.Eyes.ShouldHaveSingleItem();
        (eye.Origin.X, eye.Origin.Y, eye.Origin.Z).ShouldBe((120f, 5f, 5f));

        // Forward f = −E/|E|; right = f × +z = (f.y, −f.x, 0), normalised; `$irisu` = −right, offset 0.5 − E · `$irisu`.
        float length = MathF.Sqrt((120f * 120f) + 25f + 25f);
        (float X, float Y) f = (-120f / length, -5f / length);
        float across = MathF.Sqrt((f.X * f.X) + (f.Y * f.Y));
        (float X, float Y) u = (-f.Y / across, f.X / across);

        eye.IrisU.X.ShouldBe(u.X, 1e-4);
        eye.IrisU.Y.ShouldBe(u.Y, 1e-4);
        eye.IrisU.Z.ShouldBe(0f, 1e-4);
        eye.IrisU.W.ShouldBe(0.5f - ((120f * u.X) + (5f * u.Y)), 1e-3);
    }

    /// <remarks>
    /// **A <c>CModelPanelModel</c>'s mouth is set up from its rules at rest** (B725 5f): a C_BaseFlex nothing drives runs its
    /// rules on each controller's minimum (`m_flexWeight` zero rescaled, `c_baseflex.cpp:1220-1222`), and the mesh draw's
    /// `R_MouthSetupVertexShader` reads descriptor 0. The rule `jaw + 0.25` over a controller ranged 0.5..1 gives 0.75 — a
    /// CMDL's zero controls would give 0.25. The mouth's forward (1, 0, 0) on the unrotated bone, negated: (−1, 0, 0).
    /// </remarks>
    [Test]
    public void Paint_AModelWithTeethAndAMouth_SetsTheMouthUpAtItsRestingWeights()
    {
        VguiModelPanel panel = Framed(startFramed: false, mouthed: true);
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        HudViewport viewport = (HudViewport)panel.Parent!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);

        MouthShading mouth = surface.Draws.ShouldHaveSingleItem().Models.ShouldHaveSingleItem().Face.ShouldNotBeNull().Mouth.ShouldNotBeNull();
        mouth.IllumFactor.ShouldBe(0.75f, 1e-6f);
        mouth.Forward.X.ShouldBe(-1f, 1e-5f);
        mouth.Forward.Y.ShouldBe(0f, 1e-5f);
        mouth.Forward.Z.ShouldBe(0f, 1e-5f);
    }

    /// <remarks>
    /// **A <c>CModelPanel</c>'s models draw their meshes flexed by the weights they draw with** (B779), as a world model
    /// does: the <c>CModelPanelModel</c>, a C_BaseFlex nothing drives, at its rules over each controller's minimum
    /// (`c_baseflex.cpp:1220-1222`) — `jaw + 0.25` over 0.5..1 is 0.75, the vertex (1.5, 0, 0); an attached model, a plain
    /// C_BaseAnimating (`new C_BaseAnimating`, `basemodelpanel.cpp:468`), at the zero weights `LockFlexWeights` leaves
    /// (studiorender `0x1800584b0`, B513) — 0, which a ramp from −0.5 to 0.5 takes to 0.5, the vertex (1, 0, 0), where the
    /// C_BaseFlex's 0.75 would be the ramp's full 1. Neither has teeth.
    /// </remarks>
    [TestCase(false)]
    [TestCase(true)]
    public void Paint_AModelWithAVertexFlexAndNoTeeth_DrawsItsMeshFlexedByTheWeightsItDrawsWith(bool asAttached)
    {
        PropModels.ModelFrames attached = PanelMeshFlexFixture.Flexed(Frames(eyed: false, mouthed: false, flexed: false) with
        {
            Flex = Frames(eyed: false, mouthed: true).Flex,
        }, target0: -0.5f);
        VguiModelPanel panel = asAttached ? Framed(startFramed: false, attached: attached) : Framed(startFramed: false, flexed: true);
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        HudViewport viewport = (HudViewport)panel.Parent!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);

        IReadOnlyList<ModelInstance> models = surface.Draws.ShouldHaveSingleItem().Models;
        PanelMeshFlexFixture.ShouldBeFlexedBy(models[asAttached ? 1 : 0], asAttached ? 0.5f : 0.75f);
    }

    /// <remarks>
    /// **A <c>CModelPanelModel</c>'s mesh reads its delayed weights, which rise from zero** (B779 review 1 item 7): a
    /// C_BaseFlex with `flex_smooth` on hands the mesh `m_flFlexDelayedWeight` (`c_baseflex.cpp:1123-1126`, `:1308-1311`),
    /// zeroed when the model is set (`:203-213`), and `RunFlexDelay` (`:1265-1279`) blends nothing on the first draw
    /// (`m_flFlexDelayTime` still 0) and then `delayed · d + weight · (1 − d)`, `d = ExponentialDecay( 0.8, 0.033,
    /// frametime )`. A speed byte of 0 reads the delayed weight alone: paint 1 moves the vertex 0, paint 2 (0.1 s later,
    /// frametime 0.015) `2 · 0.75 · (1 − d)`. Delayed taken as current would move it 1.5 both times.
    /// </remarks>
    [Test]
    public void Paint_AModelPanelModelWithASlowFlex_RisesByItsDelayedWeights()
    {
        VguiModelPanel panel = Framed(startFramed: false, flexed: true, flexSpeed: 0);
        panel.FrameTime = 0.015f;
        HudViewport viewport = (HudViewport)panel.Parent!;
        float d = MathF.Exp(MathF.Log(0.8f) / 0.033f * 0.015f);
        float[] expected = [0f, 2f * 0.75f * (1f - d)];
        float[] at = [1f, 1.1f];

        for (int paint = 0; paint < expected.Length; paint++)
        {
            VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
            viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: at[paint]));
            panel.Paint(surface, viewport.Context!);

            float[] face = surface.Draws.ShouldHaveSingleItem().Models[0].Flex.ShouldNotBeNull();
            face[0].ShouldBe(expected[paint], 1e-5f, $"paint {paint + 1}");
        }
    }

    /// <remarks>
    /// **A new map sets the panel's model up again, from the new level's frames** (B779 review 2, the lead's loose end).
    /// `CModelPanel` listens for `game_newmap` (`basemodelpanel.cpp:56`) and marks itself dirty "to force the models to
    /// re-setup themselves" (:196-205), so `UpdateModel` (:520) builds its `CModelPanelModel` again at the next paint. The HUD
    /// outlives the level, and the next level can answer the path with other frames (a map's pakfile first): here the
    /// first frames' rule `jaw + 0.25` rests at 0.75 (the vertex 1.5), the next level's `jaw + 0.5` at 1.0 (the ramp's full
    /// 1, the vertex 2). Kept across the level, the panel would draw the old frames' 1.5.
    /// </remarks>
    [Test]
    public void Paint_AfterANewMapWithNewFramesForThePath_DrawsTheNewFrames()
    {
        PropModels.ModelFrames first = Frames(eyed: false, mouthed: false, flexed: true);
        PropModels.ModelFrames next = first with
        {
            Flex = first.Flex with
            {
                Rules = [new StudioFlexRule(0, [new StudioFlexOp(2, 0, 0f), new StudioFlexOp(1, 0, 0.5f), new StudioFlexOp(4, 0, 0f)])],
            },
        };
        PropModels.ModelFrames current = first;
        VguiModelPanel panel = Framed(startFramed: false, rootNow: () => current);
        HudViewport viewport = (HudViewport)panel.Parent!;

        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);
        PanelMeshFlexFixture.ShouldBeFlexedBy(surface.Draws[0].Models[0], 0.75f);

        current = next;
        viewport.NewMap();
        panel.Paint(surface, viewport.Context!);
        PanelMeshFlexFixture.ShouldBeFlexedBy(surface.Draws[1].Models[0], 1f);
    }

    /// <remarks>
    /// **`game_newmap` reaches a model panel at any depth** (B779 review 3 item 1c): every `CModelPanel` listens for it
    /// itself (`basemodelpanel.cpp:56`), wherever it sits; production's are grandchildren of the viewport (the match status
    /// holds the doors and the round sign). The control: set up, the panel is clean before the event.
    /// </remarks>
    [Test]
    public void NewMap_AModelPanelTwoLevelsBelowTheViewport_IsMarkedDirty()
    {
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = Context() };
        VguiPanel holder = new(viewport, "MatchStatus");
        VguiModelPanel panel = new(holder, "RoundSignModel", new FramesCache(_ => null, null));

        panel.UpdateModel();
        panel.PanelDirty.ShouldBeFalse("the control: nothing to set up, so it is clean");

        viewport.NewMap();

        panel.PanelDirty.ShouldBeTrue();
    }

    /// <remarks>
    /// **A panel whose model is not loaded yet is set up once it is, and then left alone** (B779 reviews 3, 5, 6): the
    /// engine's `SetupModel` loads the model in the call (`basemodelpanel.cpp:387`, `modelinfo` by name), so its `UpdateModel`
    /// (:520, the flag cleared at :534) always follows a set-up model. Ours precaches the panel's models on the frame, so a
    /// paint can come first: the panel clears its flag as the engine does, keeps the model it missed, and sets itself up
    /// again when that model can be found. First paint: nothing loaded, nothing drawn; second: drawn; third: the same model,
    /// the panel clean.
    /// </remarks>
    [Test]
    public void Paint_AModelNotLoadedAtTheFirstPaint_IsSetUpOnceItIs()
    {
        PropModels.ModelFrames frames = Frames(eyed: false, mouthed: false);
        PropModels.ModelFrames? current = null;
        VguiModelPanel panel = Framed(startFramed: false, rootNow: () => current);
        HudViewport viewport = (HudViewport)panel.Parent!;
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);
        surface.Draws.ShouldBeEmpty("the control: nothing loaded, nothing drawn");

        current = frames;
        panel.Paint(surface, viewport.Context!);

        surface.Draws.ShouldHaveSingleItem().Models.ShouldHaveSingleItem();
        object? setUp = panel.SetUpModel;

        panel.Paint(surface, viewport.Context!);

        panel.SetUpModel.ShouldBeSameAs(setUp, "set up once the model loaded, not again");
        panel.PanelDirty.ShouldBeFalse();
    }

    /// <remarks>
    /// **A level change releases the panel's model; the next paint sets it up again** (B779 reviews 3, 7, 8). `NewMap` is
    /// the level change: the engine's `CHLClient::LevelShutdown` releases every client entity (`cl_entitylist->Release()`,
    /// `cdll_client_int.cpp:1735`; `CClientEntityList::Release`, `cliententitylist.cpp:63-93`), the panel's client-only
    /// `CModelPanelModel` among them, so `m_hModel.Get()` is null until the next `SetupModel`; the new map's `game_newmap`
    /// marks the panel dirty (`basemodelpanel.cpp:196-205`). So before the next paint the panel has no model and
    /// `SetSequence` finds none; the paint sets a new one up. The CMDL panels drop the faces and weights they keep by
    /// frames at the same moment. The controls: both held them.
    /// </remarks>
    [Test]
    public void NewMap_ThenAPaint_ReplacesTheModelAndReleasesTheCmdlFaces()
    {
        VguiModelPanel panel = Framed(startFramed: false, flexed: true);
        HudViewport viewport = (HudViewport)panel.Parent!;
        PropModels.ModelFrames flexed = Frames(eyed: false, mouthed: false, flexed: true);
        VguiMdlPanel mdl = new(viewport, "mdl", new FramesCache(_ => flexed, null)) { Wide = 100, Tall = 100 };
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        mdl.SetMDL("models/player/demo.mdl");
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);
        mdl.Paint(surface, viewport.Context!);
        object? old = panel.SetUpModel.ShouldNotBeNull("the control");
        (mdl.KeptZeroControlWeights, mdl.RootHoldsMeshFace).ShouldBe((1, true), "the control");

        viewport.NewMap();

        panel.SetUpModel.ShouldBeNull("released with the level's entities, as `m_hModel` is");
        panel.SetSequence("ref").ShouldBeFalse("no model to act on until the next set-up");
        (mdl.KeptZeroControlWeights, mdl.RootHoldsMeshFace).ShouldBe((0, false));

        panel.Paint(surface, viewport.Context!);

        panel.SetUpModel.ShouldNotBeNull().ShouldNotBeSameAs(old);
    }

    /// <remarks>
    /// **The `game_newmap` event alone leaves the panel's model in place** (B779 reviews 7 and 8): `FireGameEvent` only
    /// marks the panel dirty (:196-205), and the model goes at the next `SetupModel` (`DeleteModelData`, :395), so
    /// `SetSequence` (:708) between the two still finds the model's `ref` sequence. Driven here as the event alone; a level
    /// change also releases the model (the row above). The control: before.
    /// </remarks>
    [Test]
    public void SetSequence_AfterTheGameNewMapEventAloneBeforeThePaint_ReachesTheModel()
    {
        VguiModelPanel panel = Framed(startFramed: false);
        HudViewport viewport = (HudViewport)panel.Parent!;

        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), viewport.Context!);
        panel.SetSequence("ref").ShouldBeTrue("the control");

        panel.OnGameEvent(VguiModelPanel.GameNewMap);

        panel.SetSequence("ref").ShouldBeTrue();
        panel.PanelDirty.ShouldBeTrue();
    }

    /// <remarks>
    /// **`game_newmap` releases a CMDL panel's merge models' mesh faces too** (B779 review 4 item 2): `ForgetLevel` walks the
    /// merge list as well as the root. The control: the drawn merge held one.
    /// </remarks>
    [Test]
    public void NewMap_AMergeModelDrawnWithAMeshFace_ReleasesIt()
    {
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = Context() };
        PropModels.ModelFrames flexed = Frames(eyed: false, mouthed: false, flexed: true);
        VguiMdlPanel mdl = new(viewport, "mdl", new FramesCache(_ => flexed, null)) { Wide = 100, Tall = 100 };

        mdl.SetMDL("models/player/demo.mdl");
        VguiMdl merge = mdl.SetMergeMDL("models/player/items/demo/hat.mdl").ShouldNotBeNull();
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        mdl.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), viewport.Context);
        merge.MeshFlex.HoldsFrames.ShouldBeTrue("the control");

        viewport.NewMap();

        merge.MeshFlex.HoldsFrames.ShouldBeFalse();
    }

    /// <remarks>
    /// **A CMDL panel poses the next level's model on a skeleton made from it** (B779 review 6 item 4): the panel keeps one
    /// posing entity per path, built from that model's `NewSkeleton()`; kept across a level, the path read again from the next
    /// map's files would be posed on the old model's skeleton, and keep the old level's model alive. `ForgetLevel` drops them
    /// with the faces. The control: within a level, the same path keeps its entity.
    /// </remarks>
    [Test]
    public void NewMap_ThenThePathReadAgain_PosesItOnANewEntity()
    {
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = Context() };
        PropModels.ModelFrames current = Frames(eyed: false, mouthed: false);
        VguiMdlPanel mdl = new(viewport, "mdl", new FramesCache(_ => current, null)) { Wide = 100, Tall = 100 };
        const string path = "models/player/demo.mdl";
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        mdl.SetMDL(path);
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        mdl.Paint(surface, viewport.Context);
        Animation.Animating.AnimatingEntity first = mdl.KeptEntity(path).ShouldNotBeNull();
        mdl.Paint(surface, viewport.Context);
        mdl.KeptEntity(path).ShouldBeSameAs(first, "the control: one entity within a level");

        viewport.NewMap();
        current = Frames(eyed: false, mouthed: false);
        mdl.Paint(surface, viewport.Context);

        mdl.KeptEntity(path).ShouldNotBeNull().ShouldNotBeSameAs(first);
    }

    /// <remarks>
    /// **A late attached model is set up once it loads, and then left alone** (B779 reviews 4-6): `SetupModel` skips an
    /// attached model it cannot find (`continue`, :473-478), and the engine's load is in the call (:387), so a late one is
    /// ours alone: the panel keeps the models a set-up missed and sets itself up again when one can be found. First paint:
    /// the root alone; second, once the attachment has loaded: both; third: the same model, the panel clean — nothing missed
    /// is left, so nothing is set up again.
    /// </remarks>
    [Test]
    public void Paint_AnAttachedModelNotLoadedAtTheFirstPaint_IsSetUpOnceItIs()
    {
        PropModels.ModelFrames attachedFrames = Frames(eyed: false, mouthed: false);
        bool attachedLoaded = false;
        PropModels.ModelFrames root = Frames(eyed: false, mouthed: false);
        VguiModelPanel panel = Framed(
            startFramed: false,
            attached: attachedFrames,
            rootNow: () => root,
            attachedNow: () => attachedLoaded ? attachedFrames : null);
        HudViewport viewport = (HudViewport)panel.Parent!;
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);
        surface.Draws.ShouldHaveSingleItem().Models.Count.ShouldBe(1, "the control: the root alone");

        attachedLoaded = true;
        panel.Paint(surface, viewport.Context!);

        surface.Draws[1].Models.Count.ShouldBe(2);
        object? setUp = panel.SetUpModel;

        panel.Paint(surface, viewport.Context!);

        panel.SetUpModel.ShouldBeSameAs(setUp, "set up once the attachment loaded, not again");
        panel.PanelDirty.ShouldBeFalse();
    }

    /// <remarks>
    /// **An attached model that never loads leaves the panel as the engine leaves it** (B779 review 5 item 1): `SetupModel`
    /// skips a failed attachment (`continue`, `basemodelpanel.cpp:473-478`) and `UpdateModel` clears `m_bPanelDirty` after it
    /// whatever happened (:534) — the panel clean, the root set up once and animating, the attachment absent. Held dirty, the
    /// panel would set its root up again at every paint (its cycle back to zero, its delayed flex weights back to rest). Three
    /// paints, the attachment never there: one model instance throughout, drawn alone, the panel clean. Its frames answer
    /// `Skinned` null, a load that failed rather than one not yet made; an empty `modelname` is a second attachment that
    /// names nothing.
    /// </remarks>
    [Test]
    public void Paint_AnAttachedModelThatNeverLoads_KeepsOneRootModelAndACleanPanel()
    {
        PropModels.ModelFrames root = Frames(eyed: false, mouthed: false);
        PropModels.ModelFrames failed = root with { Skinned = null };
        VguiModelPanel panel = Framed(startFramed: false, attached: failed, rootNow: () => root, emptyAttachment: true);
        HudViewport viewport = (HudViewport)panel.Parent!;
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        object?[] models = new object?[3];

        for (int paint = 0; paint < models.Length; paint++)
        {
            viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f + paint));
            panel.Paint(surface, viewport.Context!);
            models[paint] = panel.SetUpModel;
            surface.Draws[paint].Models.Count.ShouldBe(1, $"paint {paint + 1}: the root alone");
        }

        models[0].ShouldNotBeNull("the control: the root was set up");
        models[1].ShouldBeSameAs(models[0]);
        models[2].ShouldBeSameAs(models[0]);
        panel.PanelDirty.ShouldBeFalse();
    }

    /// <remarks>
    /// **A <c>CModelPanelModel</c> whose model has flex descriptors but no controllers draws at zero weights** (B779 review
    /// 1 item 7): `SetupGlobalWeights` zeroes the weights and returns false before any rule runs
    /// (`c_baseflex.cpp:1198-1207`, `:1152-1155`). The rule `jaw + 0.25` would give 0.25 even over a missing controller;
    /// at zero the fixture's ramp moves nothing, so the mesh is drawn unflexed.
    /// </remarks>
    [Test]
    public void Paint_AModelPanelModelWithoutControllers_DrawsAtZeroWeights()
    {
        PropModels.ModelFrames frames = Frames(eyed: false, mouthed: false, flexed: true);
        VguiModelPanel panel = Framed(startFramed: false, root: frames with { Flex = frames.Flex with { Controllers = [] } });
        HudViewport viewport = (HudViewport)panel.Parent!;
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);

        surface.Draws.ShouldHaveSingleItem().Models[0].Flex.ShouldBeNull();
    }

    /// <remarks>
    /// **A <c>CModelPanelModel</c> without controllers reads the render context's view target** (B725 5d): its
    /// `SetupGlobalWeights` returns false before `SetViewTarget` (`c_baseflex.cpp:1198-1207`), so it sets none and its eyes
    /// take the last one set. Left at (120, 300, 5) by an earlier draw: the eye at (120, 5, 5) looks along +y, right =
    /// forward × up = (1, 0, 0), `$irisu` = (−1, 0, 0) — where looking at the origin gives the case above.
    /// </remarks>
    [Test]
    public void Paint_AModelWithAnEyeAndNoControllers_LooksAtTheLastViewTargetSet()
    {
        StudioViewTarget shared = new() { Target = (120f, 300f, 5f) };
        VguiModelPanel panel = Framed(startFramed: false, eyed: true, viewTarget: shared);
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        HudViewport viewport = (HudViewport)panel.Parent!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);

        EyeShading eye = surface.Draws.ShouldHaveSingleItem().Models.ShouldHaveSingleItem().Face.ShouldNotBeNull().Eyes[0];
        eye.IrisU.X.ShouldBe(-1f, 1e-4f);
        eye.IrisU.Y.ShouldBe(0f, 1e-4f);
        shared.Target.ShouldBe((120f, 300f, 5f), "it sets none");
    }

    /// <remarks>
    /// **A CModelPanel's attached model is a plain C_BaseAnimating** (review 1 item 2): `new C_BaseAnimating`
    /// (`basemodelpanel.cpp:468`), whose `SetupWeights` is empty — so it sets no view target and reads the last one set, and
    /// its flex weights are the zeros `LockFlexWeights` hands a draw nobody fills (studiorender `0x1800584b0`, B513). The root
    /// here has no controllers, so it sets nothing either; the shared target left at (120, 300, 5) by an earlier draw stays,
    /// and the attached eye at (120, 5, 5) looks along +y: `$irisu` (−1, 0, 0). Its mouth reads descriptor 0 at zero: 0 —
    /// where run as a C_BaseFlex at rest it would set (0, 0, 0) and read 0.75.
    /// </remarks>
    [Test]
    public void Paint_AnAttachedModel_SetsNoTargetReadsTheLastAndHasZeroFlex()
    {
        StudioViewTarget shared = new() { Target = (120f, 300f, 5f) };
        VguiModelPanel panel = Framed(startFramed: false, viewTarget: shared, attached: Frames(eyed: true, mouthed: true));
        VguiModelPanelConformanceTests.RecordingModelSurface surface = new();

        HudViewport viewport = (HudViewport)panel.Parent!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(surface, viewport.Context!);

        IReadOnlyList<ModelInstance> models = surface.Draws.ShouldHaveSingleItem().Models;
        models.Count.ShouldBe(2);
        StudioFace face = models[1].Face.ShouldNotBeNull();
        face.Eyes[0].IrisU.X.ShouldBe(-1f, 1e-4f);
        face.Eyes[0].IrisU.Y.ShouldBe(0f, 1e-4f);
        face.Mouth.ShouldNotBeNull().IllumFactor.ShouldBe(0f);
        shared.Target.ShouldBe((120f, 300f, 5f), "neither the root nor the attached model sets one");
    }

    /// <remarks>
    /// **A CModelPanelModel with controllers sets the shared target** (`SetViewTarget`, `c_baseflex.cpp:643`): its
    /// `m_viewtarget` (0, 0, 0), no `eyes` attachment so unclamped. From (7, 7, 7) to (0, 0, 0) — into the cache's shared
    /// one, or, with a cache that shares none, the panel's own, which persists between paints.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void Paint_ARootWithControllers_SetsTheSharedTargetOrThePanelsOwn(bool cacheShares)
    {
        StudioViewTarget shared = new() { Target = (7f, 7f, 7f) };
        VguiModelPanel panel = Framed(startFramed: false, eyed: true, mouthed: true, viewTarget: shared, sharedTarget: cacheShares);

        StudioViewTarget written = cacheShares ? shared : panel.ViewTarget;

        if (!cacheShares)
        {
            written.ShouldNotBeSameAs(shared);
            written.Target = (7f, 7f, 7f);
        }

        HudViewport viewport = (HudViewport)panel.Parent!;
        viewport.Think(new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f));
        panel.Paint(new VguiModelPanelConformanceTests.RecordingModelSurface(), viewport.Context!);

        written.Target.ShouldBe((0f, 0f, 0f), "the paint wrote the target the panel reports");
        panel.ViewTarget.ShouldBeSameAs(written);
    }

    private static VguiModelPanel Framed(
        bool startFramed,
        bool eyed = false,
        bool mouthed = false,
        StudioViewTarget? viewTarget = null,
        PropModels.ModelFrames? attached = null,
        bool sharedTarget = true,
        bool flexed = false,
        byte flexSpeed = 255,
        PropModels.ModelFrames? root = null,
        Func<PropModels.ModelFrames?>? rootNow = null,
        Func<PropModels.ModelFrames?>? attachedNow = null,
        bool emptyAttachment = false)
    {
        PropModels.ModelFrames frames = root ?? Frames(eyed, mouthed, flexed, flexSpeed);
        Func<PropModels.ModelFrames?> rootFrames = rootNow ?? (() => frames);
        Func<PropModels.ModelFrames?> attachedFrames = attachedNow ?? (() => attached);
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        FramesCache cache = new(
            path => attached is not null && path == AttachedPath ? attachedFrames() : rootFrames(),
            sharedTarget ? viewTarget ?? new StudioViewTarget() : null);
        VguiModelPanel panel = new(viewport, "Framed", cache) { Wide = 640, Tall = 480 };
        string attachedLine = attached is null ? string.Empty : $"\"attached_model\" {{ \"modelname\" \"{AttachedPath}\" }}";

        if (emptyAttachment)
        {
            attachedLine += " \"attached_model\" { \"modelname\" \"\" }";
        }

        panel.ApplySettings(
            KeyValuesTree.Load(Encoding.UTF8.GetBytes($$"""
                "Framed"
                {
                    "fieldName" "Framed" "wide" "640" "tall" "480" "start_framed" "{{(startFramed ? 1 : 0)}}"
                    "model" { "modelname" "models/plain.mdl" "modelname_hwm" "models/plain_hwm.mdl" "animation" { "name" "ref" "sequence" "ref" "default" "1" } {{attachedLine}} }
                }
                """), "framed.res", _ => null),
            context);

        return panel;
    }

    private const string AttachedPath = "models/attached.mdl";

    /// <summary>
    /// A one-bone model, optionally with an eye, and with teeth, a mouth and the `jaw + 0.25` rule over 0.5..1; or, flexed,
    /// that rule and <see cref="PanelMeshFlexFixture"/>'s mesh flex without teeth.
    /// </summary>
    private static PropModels.ModelFrames Frames(bool eyed, bool mouthed, bool flexed = false, byte flexSpeed = 255)
    {
        PropModels.SkinnedModel labelled = SyntheticSkinnedModel.With("ref");
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithOneBone() with { Sequences = labelled.Sequences, Groups = labelled.Groups };

        PropModels.ModelFrames frames = new([], new Dictionary<int, (int, int, float)>(), [], [], HeaderBounds: new StudioBox(-10f, -20f, 0f, 10f, 20f, 40f), Skinned: model)
        {
            Eyeballs = eyed ? [new StudioEyeball(0, (10f, 0f, 0f), 0f, 0.5f, (0f, 0f, 1f), (1f, 0f, 0f), 1f)] : [],
            Mouths = mouthed ? [new StudioMouth(0, (1f, 0f, 0f), 0)] : [],
            HasTeeth = mouthed,
            Flex = mouthed || flexed
                ? new StudioFlexData(
                    ["jaw"],
                    [new StudioFlexController("default", "jaw", 0.5f, 1f)],
                    [new StudioFlexRule(0, [new StudioFlexOp(2, 0, 0f), new StudioFlexOp(1, 0, 0.25f), new StudioFlexOp(4, 0, 0f)])],
                    [])
                : StudioFlexData.None,
        };

        return flexed ? PanelMeshFlexFixture.Flexed(frames, speed: flexSpeed) : frames;
    }

    /// <summary>A cache answering by path, with the render context target it shares (or none).</summary>
    private sealed class FramesCache(Func<string, PropModels.ModelFrames?> find, StudioViewTarget? viewTarget) : IMdlCache
    {
        public StudioViewTarget? ViewTarget { get; } = viewTarget;

        public PropModels.ModelFrames? FindMdl(string path) => find(path);

        public IReadOnlyList<int>? PackedOrder(string path) => PanelMeshFlexFixture.Order;

        public int FindBodygroup(string modelPath, string group) => -1;

        public int SetBodygroup(string modelPath, int group, int value, int body) => body;
    }

    private static HudGameEvent Event(string name, SceneGameRules rules, params (string Key, object? Value)[] values)
    {
        Dictionary<string, object?> fields = [];

        foreach ((string key, object? value) in values)
        {
            fields[key] = value;
        }

        return new HudGameEvent(new SceneGameEvent(0, name, fields, new Dictionary<int, Core.Net.PlayerInfo>()), 10f, [], 1, rules, "cp_test", 0);
    }

    private static TfHudMatchStatus Built()
    {
        PropModels.SkinnedModel labelled = SyntheticSkinnedModel.With("ref", "open", "close", "intro", "outro");
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithOneBone() with { Sequences = labelled.Sequences, Groups = labelled.Groups };
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        TfHudMatchStatus status = new(viewport, new Cache(model));

        viewport.Think((new HudState(true, true, 0, 100, true, ObserverMode: ObserverModes.None, CurTime: 1f)).WithMatchHud(true));
        status.PerformApplySchemeSettings(context);
        viewport.Animations.SetScriptFile(viewport, "scripts/doors.txt", wipeAll: true, context).ShouldBeTrue();
        return status;
    }

    private static VguiContext Context()
    {
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/doors.txt"] = Encoding.UTF8.GetBytes("""
                event Close
                {
                    RunEventChild MatchDoors PlayDoorCloseAnim 0
                }
                event PlayDoorCloseAnim
                {
                    FireCommand 0 "animation close"
                }
                event HudMatchStatus_ShowMatchStartDoors
                {
                    Animate CountdownLabel Alpha 255 Linear 0.0 0.5
                }
                event HudMatchStatus_ShowMatchWinDoors
                {
                    Animate CountdownLabel Alpha 255 Linear 0.0 0.5
                }
                event HudMatchStatus_ShowMatchWinDoors_NoOpen
                {
                    Animate CountdownLabel Alpha 255 Linear 0.0 0.5
                    Animate CountdownLabel ypos 10 Linear 0.0 0.5
                }
                """),
            ["resource/UI/HudMatchStatus.res"] = Encoding.UTF8.GetBytes($$"""
                "Resource/UI/HudMatchStatus.res"
                {
                    "HudMatchStatus" { "fieldName" "HudMatchStatus" "wide" "640" "tall" "480" "visible" "1" }
                    "CountdownLabel" { "ControlName" "CExLabel" "fieldName" "CountdownLabel" "wide" "40" "tall" "40" "visible" "0" "labelText" "%countdown%" }
                    {{DoorsBlock}}
                    "FrontParticlePanel"
                    {
                        "ControlName" "CTFParticlePanel" "fieldName" "FrontParticlePanel" "wide" "640" "tall" "480" "visible" "1"
                        "ParticleEffects" { "0" { "particle_xpos" "c0" "particle_ypos" "c0" "particle_scale" "2" "particleName" "versus_door_slam" "start_activated" "0" "loop" "0" } }
                    }
                }
                """),
            ["resource/UI/HudObjectiveTimePanel.res"] = Encoding.UTF8.GetBytes("""
                "Resource/UI/HudObjectiveTimePanel.res"
                {
                    "ObjectiveStatusTimePanel" { "fieldName" "ObjectiveStatusTimePanel" "wide" "110" "tall" "150" "visible" "0" }
                }
                """),
        };

        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english")
        {
            Surface = new TextRecorder(),
            Read = files.GetValueOrDefault,
            Localize = _ => null,
        };

        return context;
    }

    /// <summary>Every path answers the one model; "logos" is bodygroup 3, and a value is written as <c>body + value * 10</c>.</summary>
    private sealed class Cache(PropModels.SkinnedModel model) : IMdlCache
    {
        public PropModels.ModelFrames? FindMdl(string path) =>
            new([], new Dictionary<int, (int, int, float)>(), [], [], Skinned: model);

        public int FindBodygroup(string modelPath, string group) => group == "logos" ? 3 : -1;

        public int SetBodygroup(string modelPath, int group, int value, int body) => body + (value * 10);
    }
}
