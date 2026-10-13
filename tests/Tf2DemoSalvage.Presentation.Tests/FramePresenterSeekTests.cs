using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What a seek rebuilds before its moment is shown, as it left the view (D214): `demo_gototick`'s frames — the engine
/// renders while it skips, a frame per 99 packets (`CDemoPlayer::ReadPacket`, engine.dll FUN_180072ee0; `_Host_RunFrame`,
/// FUN_1801a4570), and a skip back reads from the demo's start (FUN_180073b10) — and the decals' replay.
/// </summary>
/// <remarks>
/// The demo is one this file wrote (D38, D213): one player, and a temp entity late enough that the timeline spans a few
/// hundred ticks. No map is loaded, so nothing lands; asserted here is which frames a seek runs, what is held still
/// around them, where a HUD sound of a skip frame goes, and what the two log lines say.
///
/// **Not here:** what the frames MAKE with a map — the decals a replay places, the sounds a skip deals — which the
/// seek frames of `demostf-cp_process_f12` hold: sought to 52720, back to 40 and forward to 52410, byte for byte.
/// </remarks>
public sealed class FramePresenterSeekTests
{
    private const string HudScript = "Hud.Hint";

    private static readonly DemoTimeline Timeline = DemoTimeline.Build(SyntheticEffects.Demo(
        [new(1, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, 2))],
        [],
        [],
        [],
        new SyntheticEffects.Effect(900, "CTETFBlood", new Dictionary<string, PropertyValue>(StringComparer.Ordinal))));

    /// <summary>A frame presenter with its parts in reach, and everything a seek calls back recorded.</summary>
    private sealed class Seeking
    {
        public Seeking(bool scripts = true)
        {
            Moment = new MomentScene(Models, new ViewmodelScene(), NullLogger.Instance);
            Moments = new MomentPresenter(Moment, new FrameLedger(), NullLogger.Instance);
            Sound = new SoundPresenter(
                new SoundscapeSystem(new ActiveLoops(), static _ => null, NullLogger.Instance),
                new ActiveLoops(),
                static _ => null,
                NullLogger.Instance);

            if (scripts)
            {
                byte[] manifest = Encoding.UTF8.GetBytes("\"game_sounds_manifest\"\n{\n\t\"precache_file\" \"scripts/test.txt\"\n}\n");
                byte[] script = Encoding.UTF8.GetBytes($"\"{HudScript}\"\n{{\n\t\"channel\" \"CHAN_STATIC\"\n\t\"wave\" \"ui/hint.wav\"\n}}\n");

                Sound.Scripts = SoundScriptCatalog.Load(path => path switch
                {
                    "scripts/game_sounds_manifest.txt" => manifest,
                    "scripts/test.txt" => script,
                    _ => null,
                });
            }

            Frames = new FramePresenter(Models, Moment, Moments, Sound, Render, NullLogger.Instance, Audio);
        }

        public EntityModelSet Models { get; } = new();

        public MomentScene Moment { get; }

        public MomentPresenter Moments { get; }

        public SoundPresenter Sound { get; }

        public RecordingLogger Render { get; } = new();

        public RecordingLogger Audio { get; } = new();

        public FramePresenter Frames { get; }

        /// <summary>Every tick the seek asked the window to show, in order.</summary>
        public List<double> Shown { get; } = [];

        /// <summary>Every tick the seek ran a HUD frame at, in order.</summary>
        public List<int> HudFrames { get; } = [];

        /// <summary>What was held still each time the window was asked to show a tick.</summary>
        public List<(bool Corpses, bool EntitiesOnly, bool SimulatesOnly, bool PlayersOnly, bool WalksEvents, bool Replaying)> Held { get; } = [];

        /// <summary>Called inside each HUD frame, for a test that has the HUD ask for a sound.</summary>
        public Action<int>? InHudFrame { get; set; }

        public void Show(double tick, bool hud = true, IDecalUpload? device = null) =>
            Frames.ShowMomentOver(
                Frame(tick),
                tick,
                device,
                shown =>
                {
                    Shown.Add(shown);
                    Held.Add((Models.HoldsCorpses, Moment.EntitiesOnly, Models.SimulatesOnly, Moments.PlayersOnly, Models.WalksEvents, Frames.Replaying));

                    return Frame(shown);
                },
                hud
                    ? (_, through) =>
                    {
                        HudFrames.Add(through);
                        InHudFrame?.Invoke(through);
                    }
                    : null);

        private static ViewFrame Frame(double shown) =>
            new(Timeline, null, null, (int)shown, shown, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50);
    }

    [Test]
    public void ShowMomentOver_TheFirstSeek_RunsAHudFrameAndAShowEveryNinetyNinePacketsFromTheDemosStartThenShowsTheMoment()
    {
        Seeking seek = new();
        IReadOnlyList<int> frames = DemoSkip.Frames(Timeline.FirstTick, 500);

        frames.Count.ShouldBeGreaterThan(2, "the control: five hundred ticks is several frames of ninety-nine packets");

        seek.Show(500d);

        seek.HudFrames.ShouldBe(frames);
        seek.Shown.ShouldBe([.. frames.Select(static through => (double)through), 500d]);
        seek.Audio.Count($"skip: to 500 from the start in {frames.Count} frames made 0 HUD and 0 animation script sounds").ShouldBe(1);
    }

    [Test]
    public void ShowMomentOver_ASkipsFrames_AreShownWithCorpsesHeldAndOnlyEntitiesSimulatedAndTheMomentIsNot()
    {
        Seeking seek = new();

        seek.Show(500d);

        seek.Held.Take(seek.Held.Count - 1).Distinct()
            .ShouldBe([(true, true, true, false, true, false)], "every skip frame: HoldsCorpses, EntitiesOnly, SimulatesOnly");
        seek.Held[^1].ShouldBe((false, false, false, false, true, false), "and the moment itself is shown with nothing held");
    }

    [Test]
    public void ShowMomentOver_TheNextTick_IsNoSkip()
    {
        Seeking seek = new();

        seek.Show(500d);
        seek.HudFrames.Clear();
        seek.Shown.Clear();
        seek.Show(501d);

        seek.HudFrames.ShouldBeEmpty();
        seek.Shown.ShouldBe([501d]);
        seek.Audio.Count("skip: to 501").ShouldBe(0);
    }

    [Test]
    public void ShowMomentOver_ASeekForward_RunsItsFramesFromTheTickAfterTheLastShown()
    {
        Seeking seek = new();

        seek.Show(100d);
        seek.HudFrames.Clear();
        seek.Show(600d);

        seek.HudFrames.ShouldBe(DemoSkip.Frames(101, 600));
        seek.Audio.Count("skip: to 600 from 100 in ").ShouldBe(1);
    }

    [Test]
    public void ShowMomentOver_ASeekBack_RunsItsFramesFromTheDemosStartAgain()
    {
        Seeking seek = new();

        seek.Show(600d);
        seek.HudFrames.Clear();
        seek.Show(300d);

        seek.HudFrames.ShouldBe(DemoSkip.Frames(Timeline.FirstTick, 300), "a skip back reloads the demo and reads from its start");
        seek.Audio.Count("skip: to 300 from the start in ").ShouldBe(1);
    }

    [Test]
    public void ShowMomentOver_WithNoHudYet_RunsTheFramesWithoutItAndSaysSo()
    {
        Seeking seek = new();

        seek.Show(500d, hud: false);

        seek.HudFrames.ShouldBeEmpty();
        seek.Shown.Count.ShouldBe(DemoSkip.Frames(Timeline.FirstTick, 500).Count + 1);
        seek.Audio.Count("(no HUD yet)").ShouldBe(1);
    }

    [Test]
    public void ShowMomentOver_WithNoSoundScripts_ShowsTheMomentAndRunsNoFrames()
    {
        Seeking seek = new(scripts: false);

        seek.Show(500d);

        seek.HudFrames.ShouldBeEmpty();
        seek.Shown.ShouldBe([500d]);
    }

    [Test]
    public void HudSound_InsideASkipsFrame_IsDealtWithTheSkipAtTheFramesTickAndNotPlayed()
    {
        Seeking seek = new();

        seek.InHudFrame = _ => seek.Frames.HudSound(12345, HudScript, hudReplaying: false);
        seek.Show(500d);

        seek.Sound.Emitted.ShouldBeEmpty("a skip frame's sound is dealt with the skip, never played");
        seek.Audio.Count($"made {seek.HudFrames.Count} HUD and 0 animation script sounds").ShouldBe(1);
    }

    [Test]
    public void HudSound_OutsideASkip_IsEmittedAtTheTransportsTick()
    {
        Seeking seek = new();

        seek.Frames.HudSound(321, HudScript, hudReplaying: false);

        SceneSound sound = seek.Sound.Emitted.ShouldHaveSingleItem();

        sound.Name.ShouldBe("ui/hint.wav");
        sound.Tick.ShouldBe(321);
    }

    [Test]
    public void HudSound_WhileTheHudHandlesASeeksReplayedEvents_IsSilent()
    {
        Seeking seek = new();

        seek.Frames.HudSound(321, HudScript, hudReplaying: true);

        seek.Sound.Emitted.ShouldBeEmpty();
    }

    [Test]
    public void ShowMomentOver_WithADevice_HasTheDecalsReplayTheSeekAndLeavesNothingHeld()
    {
        Seeking seek = new();
        FakeFrameDevice device = new();

        seek.Show(500d, device: device);

        // No map, so the decals pose no tick; what they were asked and what is left afterwards is all there is to see.
        seek.Render.Count("model decals: a seek to 500 replayed ticks ").ShouldBe(1);
        seek.Render.Count("posing 0 of them").ShouldBe(1);
        seek.Frames.Replaying.ShouldBeFalse();
        seek.Held[^1].ShouldBe((false, false, false, false, true, false), "the moment itself is shown with nothing held");
        (seek.Models.HoldsCorpses, seek.Moments.PlayersOnly, seek.Models.WalksEvents).ShouldBe((false, false, true));
    }
}
