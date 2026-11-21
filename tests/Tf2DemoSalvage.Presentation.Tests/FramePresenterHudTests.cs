using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// The frame's VGUI as it left the view (D214) [no engine counterpart: which HUD frames this viewer runs, and what it
/// asks its window for, is the viewer's own]: built once an install is open, asked of the window at the overlay's
/// tick, and run unpainted for each frame of a seek.
/// </summary>
/// <remarks>
/// The install is `GameContent.Open( null )` — the machine with no TF2, which is every CI run — so the HUD runs on an
/// empty scheme and draws defaults. **What the HUD draws is `VguiHud`'s and has its own suites**; asserted here is when
/// it exists, what it asks the window and at which ticks.
///
/// **Not here:** the HUD's scripts and attribute hooks, which need `items_game.txt`, and which models the class panel's
/// precache names, which needs a class model; that the frame asks for it is here (B779 review 3). The first-person frames
/// hold the drawn HUD, byte for byte.
/// </remarks>
public sealed class FramePresenterHudTests
{
    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticEffects.Demo(
        [new(1, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, 2))],
        [],
        [],
        [],
        new SyntheticEffects.Effect(900, "CTETFBlood", new Dictionary<string, PropertyValue>(StringComparer.Ordinal))));

    /// <summary>A frame presenter with a window attached that records everything the HUD asks it.</summary>
    private sealed class Window
    {
        public Window()
        {
            EntityModelSet models = new();
            MomentScene moment = new(models, new ViewmodelScene(), NullLogger.Instance);
            SoundPresenter sound = new(
                new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
                new ActiveLoops(),
                static _ => null,
                NullLogger.Instance);

            byte[] manifest = Encoding.UTF8.GetBytes("\"game_sounds_manifest\"\n{\n\t\"precache_file\" \"scripts/test.txt\"\n}\n");
            byte[] script = Encoding.UTF8.GetBytes("\"Hud.Hint\"\n{\n\t\"wave\" \"ui/hint.wav\"\n}\n");

            sound.Scripts = SoundScriptCatalog.Load(path => path switch
            {
                "scripts/game_sounds_manifest.txt" => manifest,
                "scripts/test.txt" => script,
                _ => null,
            });

            Game = GameContent.Open(null, NullLoggerFactory.Instance);
            Models = models;
            Frames = new FramePresenter(
                models, moment, new MomentPresenter(moment, new FrameLedger(), NullLogger.Instance), sound, NullLogger.Instance, NullLogger.Instance, Audio);
            Frames.AttachHud(new HudWindow(
                new FpsPanelTests.SolidGdi(),
                () => Game,
                static () => "cp_test",
                tick =>
                {
                    Asked.Add(tick);

                    return (new HudView(Timeline, tick, null, null, null, null, 1f, 90f, CameraMode.Free, null), [], Frame(tick));
                },
                () =>
                {
                    Positions++;

                    return default;
                },
                () =>
                {
                    Clocks++;

                    return 2.5d;
                },
                static () => null));
        }

        public GameContent? Game { get; }

        /// <summary>The model set the presenter and its HUD share.</summary>
        public EntityModelSet Models { get; }

        public FramePresenter Frames { get; }

        public RecordingLogger Audio { get; } = new();

        /// <summary>Every tick the HUD asked the window about, in order.</summary>
        public List<int> Asked { get; } = [];

        public int Positions { get; private set; }

        public int Clocks { get; private set; }

        public VguiDrawList? Overlay(int tick) =>
            Frames.BuildOverlay(new OverlayFrame(Timeline, null, "cp_test", tick, 1280, 720, new CrosshairSettings(), 0, false, 0.016d), null);

        /// <summary>A seek: the transport jumps, so the move is a skip (D221).</summary>
        public void Seek(double tick) => Frames.ShowMoment(Frame(tick), tick, null, static shown => Frame(shown), ++_jumps);

        private int _jumps;

        private static ViewFrame Frame(double shown) =>
            new(Timeline, null, null, (int)shown, shown, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50);
    }

    [Test]
    public void BuildOverlay_WithAnInstallOpen_BuildsTheHudOnceAndAsksTheWindowAtTheOverlaysTick()
    {
        Window window = new();

        window.Frames.HasHud.ShouldBeFalse("the control: nothing is built before the first overlay");

        VguiDrawList first = window.Overlay(100).ShouldNotBeNull();
        VguiDrawList second = window.Overlay(101).ShouldNotBeNull();

        window.Frames.HasHud.ShouldBeTrue();
        second.ShouldBeSameAs(first, "one surface for the life of the install");
        window.Asked.ShouldBe([100, 101]);
        window.Positions.ShouldBe(2, "cl_showpos is read when the tools panel is framed, once an overlay");
        window.Clocks.ShouldBe(2);
    }

    /// <remarks>
    /// **Every overlay hands the HUD's panel models to the model set's precache** (B779 review 3 item 1b): what the class
    /// panel, the match doors, the round sign and the item panels will draw must be packed before they paint, and a model
    /// first needed mid-demo reaches the device only this way. Counted by the asks the set takes; the set decides whether
    /// the list changed.
    /// </remarks>
    [Test]
    public void BuildOverlay_EachFrame_AsksTheModelSetToPrecacheTheHudsPanelModels()
    {
        Window window = new();

        window.Overlay(100);
        window.Overlay(101);

        window.Models.PrecacheAsks.ShouldBe(2);
    }

    /// <remarks>
    /// **A level change after a level releases the HUD's model panels' models and marks them dirty** (B779 reviews 3, 4, 8, 9,
    /// 10). Both halves run from `FramePresenter.ClearMap`, which `ViewerPresenter.ClearMap` runs before every load: the old
    /// level's shutdown releases each `CModelPanel`'s model with every client entity (`cl_entitylist->Release()`,
    /// `cdll_client_int.cpp:1735`), and the new map's `game_newmap` marks the panel dirty (`basemodelpanel.cpp:56`,
    /// :196-205) so it sets its model up again from the new level's frames. **The shutdown half is armed by a frame of a
    /// level** (B779 review 11): the HUD's own precache of its panel models, in `BuildOverlay`, after a load — whatever the
    /// load came to (a map, none, superseded, failed), the frames that ran after it are the level the panels hold — as
    /// `g_bLevelInitialized` arms the engine's (:1714-1715). Here: a first load (nothing to release), a frame, a second load.
    /// The control: the round sign, set up, is clean before the change.
    /// </remarks>
    [Test]
    public void ClearMap_AfterAFrameOfALevel_ReleasesTheModelPanelsModelsAndMarksThemDirty()
    {
        Window window = new();
        VguiModelPanel sign = SignWithAModel(window);

        window.Frames.ClearMap();
        sign.UpdateModel();
        sign.SetUpModel.ShouldNotBeNull("the control: the first load released nothing, and the sign is set up");
        window.Overlay(101);
        window.Frames.ClearMap();

        sign.SetUpModel.ShouldBeNull("the level's shutdown released it (cdll_client_int.cpp:1735)");
        sign.PanelDirty.ShouldBeTrue();
    }

    /// <remarks>
    /// **A shutdown disarms itself** (B779 review 10 item 1): the engine clears `g_bLevelInitialized` in `LevelShutdown`
    /// (`cdll_client_int.cpp:1717`), so a second shutdown with no level started between them returns at once (:1714-1715).
    /// A load, a frame of its level, a second load (the model released), the sign set up again, and a third load with no
    /// frame between: the model kept.
    /// </remarks>
    [Test]
    public void ClearMap_TwiceWithNoLevelBetween_ReleasesOnlyOnTheFirst()
    {
        Window window = new();
        VguiModelPanel sign = SignWithAModel(window);

        window.Frames.ClearMap();
        window.Overlay(101);
        window.Frames.ClearMap();
        sign.SetUpModel.ShouldBeNull("the control: the first load after a level released it");

        sign.UpdateModel();
        object model = sign.SetUpModel.ShouldNotBeNull("the control: set up again");
        window.Frames.ClearMap();

        sign.SetUpModel.ShouldBeSameAs(model);
    }

    /// <remarks>
    /// **The first level's load releases nothing** (B779 review 9 item 1): `CHLClient::LevelShutdown` returns at once while
    /// no level was initialised (`cdll_client_int.cpp:1714-1715`; `g_bLevelInitialized` is set at `LevelInitPreEntity`,
    /// :1610-1612). So a load before any level was initialised leaves the panel's model, and the new map's event still marks
    /// the panel dirty. The control: the sign held a model.
    /// </remarks>
    [Test]
    public void ClearMap_BeforeAnyLevel_KeepsTheModelPanelsModelsAndMarksThemDirty()
    {
        Window window = new();
        VguiModelPanel sign = SignWithAModel(window);
        object model = sign.SetUpModel.ShouldNotBeNull();

        window.Frames.ClearMap();

        sign.SetUpModel.ShouldBeSameAs(model);
        sign.PanelDirty.ShouldBeTrue();
    }

    /// <summary>An empty scheme's context, for a panel's own settings: the install of nothing has no `ClientScheme.res`.</summary>
    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Colors { } Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 720), scheme.Find("Fonts")!, 1280, 720, "english");
    }

    /// <summary>One bone at the origin, one sequence: enough for a panel to set the model up.</summary>
    private static PropModels.SkinnedModel OneBone()
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups =
            [(0, [new StudioSequence(Animation: 0, Flags: 0, Label: "idle", Blend: null, Activity: "idle", ActivityWeight: 1)])];

        return new PropModels.SkinnedModel(
            Bones:
            [
                new StudioBone(
                    Name: "root",
                    Parent: -1,
                    Position: (0f, 0f, 0f),
                    Rotation: (0f, 0f, 0f, 1f),
                    PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                    Flags: ~0),
            ],
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [],
            MasterPose: []);
    }

    /// <summary>The HUD built, its round sign given a model the presenter's set has packed, and set up.</summary>
    private static VguiModelPanel SignWithAModel(Window window)
    {
        const string path = "models/vgui/round_sign.mdl";
        PropModels.ModelFrames frames = new(
            [
                new PropVertex[]
                {
                    new(0f, 0f, 0f, 0f, 0f, MaterialIndex: 1) { Weights = (1f, 0f, 0f), Bones = (0, 0, 0) },
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 1) { Weights = (1f, 0f, 0f), Bones = (0, 0, 0) },
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: OneBone());

        window.Models.Geometry = requested => requested == path ? frames : null;
        window.Models.Precache([path]);
        window.Overlay(100);

        VguiHud hud = window.Frames.Hud.ShouldNotBeNull();
        VguiModelPanel sign = hud.MatchStatus.RoundSignModel;

        sign.ApplySettings(
            KeyValuesTree.Load(
                Encoding.UTF8.GetBytes($$"""
                    "Panel" { "fieldName" "Panel" "wide" "100" "tall" "100" "model" { "modelname" "{{path}}" } }
                    """),
                "panel.res",
                _ => null),
            Context());
        sign.UpdateModel();
        sign.SetUpModel.ShouldNotBeNull("the control: the sign has a model");
        sign.PanelDirty.ShouldBeFalse("the control");

        return sign;
    }

    [Test]
    public void ShowMoment_OnceTheHudExists_RunsAHudFrameForEachFrameOfTheSkipAtThatFramesTick()
    {
        Window window = new();

        window.Overlay(0);
        window.Asked.Clear();
        window.Seek(500d);

        window.Asked.ShouldBe(DemoSkip.Frames(Timeline.FirstTick, 500), "each skip frame asks the window about its own last tick");
        window.Audio.Count("(no HUD yet)").ShouldBe(0);
    }

    [Test]
    public void ShowMoment_BeforeAnyOverlay_SkipsWithNoHudAndSaysSo()
    {
        Window window = new();

        window.Seek(500d);

        window.Asked.ShouldBeEmpty();
        window.Audio.Count("(no HUD yet)").ShouldBe(1);
    }
}
