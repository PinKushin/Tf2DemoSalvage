using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
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
/// **Not here:** the HUD's scripts and attribute hooks, which need `items_game.txt`, and the class model panel's
/// precache, which needs a HUD with a model panel in it. The first-person frames hold the drawn HUD, byte for byte.
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
