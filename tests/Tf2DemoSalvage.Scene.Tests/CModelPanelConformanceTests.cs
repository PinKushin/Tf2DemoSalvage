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

        effect.System.ShouldNotBeNull().Particles.Count.ShouldBe(33, "66 a second for half a second, on engine->Time()");
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

    private static VguiModelPanel Framed(bool startFramed)
    {
        PropModels.SkinnedModel labelled = SyntheticSkinnedModel.With("ref");
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithOneBone() with { Sequences = labelled.Sequences, Groups = labelled.Groups };
        PropModels.ModelFrames frames = new([], new Dictionary<int, (int, int, float)>(), [], [], HeaderBounds: new StudioBox(-10f, -20f, 0f, 10f, 20f, 40f), Skinned: model);
        VguiContext context = Context();
        HudViewport viewport = new() { Wide = 640, Tall = 480, Context = context };
        VguiModelPanel panel = new(viewport, "Framed", new FramesCache(frames)) { Wide = 640, Tall = 480 };

        panel.ApplySettings(
            KeyValuesTree.Load(Encoding.UTF8.GetBytes($$"""
                "Framed"
                {
                    "fieldName" "Framed" "wide" "640" "tall" "480" "start_framed" "{{(startFramed ? 1 : 0)}}"
                    "model" { "modelname" "models/plain.mdl" "modelname_hwm" "models/plain_hwm.mdl" "animation" { "name" "ref" "sequence" "ref" "default" "1" } }
                }
                """), "framed.res", _ => null),
            context);

        return panel;
    }

    /// <summary>Every path answers the given frames.</summary>
    private sealed class FramesCache(PropModels.ModelFrames frames) : IMdlCache
    {
        public PropModels.ModelFrames? FindMdl(string path) => frames;

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
