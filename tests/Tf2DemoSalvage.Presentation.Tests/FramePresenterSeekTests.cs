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

        /// <summary>Whether the models were told the frame covers no demo time, each time the window was asked to show a tick.</summary>
        public List<bool> Paused { get; } = [];

        private int _jumps;

        /// <summary>A render queue the seek is given, or null for none (B581).</summary>
        public Render.QueuedRenderThread? Queue { get; init; }

        /// <summary>Whether a frame was in flight on <see cref="Queue"/> each time a tick was shown.</summary>
        public List<bool> InFlight { get; } = [];

        /// <summary>Shows a moment; <paramref name="jump"/> is whether the transport jumped there, which is what a skip is (D221).</summary>
        public void Show(double tick, bool hud = true, IDecalUpload? device = null, double demoFrameSeconds = 0d, bool jump = true) =>
            Frames.ShowMomentOver(
                Frame(tick, demoFrameSeconds),
                tick,
                device,
                shown =>
                {
                    InFlight.Add(Queue?.InFlight ?? false);
                    Shown.Add(shown);
                    Held.Add((Models.HoldsCorpses, Moment.EntitiesOnly, Models.SimulatesOnly, Moments.PlayersOnly, Models.WalksEvents, Frames.Replaying));
                    Paused.Add(Models.Paused);

                    return Frame(shown, demoFrameSeconds);
                },
                hud
                    ? (_, through) =>
                    {
                        HudFrames.Add(through);
                        InHudFrame?.Invoke(through);
                    }
                    : null,
                jump ? ++_jumps : _jumps,
                Queue);

        private static ViewFrame Frame(double shown, double demoFrameSeconds) =>
            new(Timeline, null, null, (int)shown, shown, false, false, null, 1280, 720, 0.016f, demoFrameSeconds, true, true, true, 2048, 50);
    }

    /// <remarks>
    /// `C_BaseAnimating::Simulate` walks events only `if ( gpGlobals->frametime != 0.0f )` (`c_baseanimating.cpp:5162`).
    /// The client's frametime is zero while `IsPlaybackPaused()`, which answers FALSE for the length of a skip whatever
    /// the pause flag says and the flag once the skip has landed (engine.dll FUN_1800729f0, FUN_1800a2d40) — so a seek
    /// made while paused walks its skip frames and not the frame it lands on (B541).
    /// </remarks>
    [Test]
    public void ShowMomentOver_ASeekWhilePaused_WalksEventsOnItsSkipFramesAndNotOnTheMoment()
    {
        Seeking seek = new();

        seek.Show(500d);

        seek.Paused.Count.ShouldBeGreaterThan(2, "the control: the seek ran skip frames");
        seek.Paused.Take(seek.Paused.Count - 1).Distinct().ShouldBe([false], "IsPlaybackPaused is false while skipping");
        seek.Paused[^1].ShouldBeTrue("the frame the skip lands on is a paused frame: frametime zero");
    }

    /// <remarks>
    /// **A seek drains the render thread before its replay poses** (B581): the skip frames rewrite the bones, flex and
    /// instance lists the frame in flight draws. Deterministic: the in-flight draw is held on a gate only the drain opens,
    /// so a skip frame shown before the drain sees the frame in flight however fast the machine is.
    /// </remarks>
    [Test]
    public void ShowMomentOver_ASeekInModeTwo_DrainsTheFrameInFlightBeforeItsFirstSkipFrame()
    {
        using System.Threading.ManualResetEventSlim gate = new();
        using Render.QueuedRenderThread queue = new() { Mode = Render.MaterialThreadMode.QueuedThreaded };
        string? drainedBy = null;

        queue.Waiting = caller =>
        {
            drainedBy ??= caller;
            gate.Set();
        };

        try
        {
            Seeking seek = new() { Queue = queue };

            queue.Submit(gate.Wait);
            seek.Show(500d);

            seek.InFlight.Count.ShouldBeGreaterThan(2, "the control: the seek ran skip frames");
            seek.InFlight.ShouldAllBe(inFlight => !inFlight);
            drainedBy.ShouldBe("ReplaySkip");
        }
        finally
        {
            gate.Set();
        }
    }

    /// <remarks>
    /// **And the decals' replay drains on its own** (B581, B585): with no sound scripts the skip runs no frame and so
    /// no drain, and the replay's poses would rewrite the instance matrices pooled in place while the held draw reads them.
    /// </remarks>
    [Test]
    public void ShowMomentOver_ASeekWithNoSkipFramesInModeTwo_DrainsBeforeTheDecalsReplayPoses()
    {
        using System.Threading.ManualResetEventSlim gate = new();
        using Render.QueuedRenderThread queue = new() { Mode = Render.MaterialThreadMode.QueuedThreaded };
        string? drainedBy = null;

        queue.Waiting = caller =>
        {
            drainedBy ??= caller;
            gate.Set();
        };

        try
        {
            Seeking seek = new(scripts: false) { Queue = queue };

            queue.Submit(gate.Wait);
            seek.Show(500d, device: new FakeFrameDevice());

            seek.Render.Count("model decals: a seek to 500 replayed ticks ").ShouldBe(1, "the control: the decals replayed");
            seek.InFlight.ShouldAllBe(inFlight => !inFlight);
            drainedBy.ShouldBe("ReplayModelDecals");
        }
        finally
        {
            gate.Set();
        }
    }

    [Test]
    public void ShowMomentOver_WhilePlaying_WalksEventsOnTheMoment()
    {
        Seeking seek = new();

        seek.Show(500d, demoFrameSeconds: 0.015d);

        seek.Paused.Distinct().ShouldBe([false]);
    }

    /// <remarks>The pose comes after the show, so what the seek leaves set is what the landing frame's pose reads.</remarks>
    [Test]
    public void ShowMomentOver_ASeekWhilePaused_LeavesTheModelsPausedForTheMomentsPose()
    {
        Seeking seek = new();

        seek.Show(500d, demoFrameSeconds: 0.015d);
        seek.Show(900d);

        seek.Models.Paused.ShouldBeTrue();
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
        seek.Show(501d, jump: false);

        seek.HudFrames.ShouldBeEmpty();
        seek.Shown.ShouldBe([501d]);
        seek.Audio.Count("skip: to 501").ShouldBe(0);
    }

    /// <remarks>A jump that lands on the tick already shown is a skip all the same, and its readers see it (D221).</remarks>
    [Test]
    public void ShowMomentOver_ASeekToTheTickShown_IsASkip()
    {
        Seeking seek = new();

        seek.Show(500d);
        int skips = seek.Frames.Skip.ShouldNotBeNull().Skips;

        seek.Show(500d);

        seek.Frames.Skip.ShouldNotBeNull().Skips.ShouldBe(skips + 1);
    }

    /// <remarks>
    /// **A played frame is never a skip, however many ticks it covers** (D221): `Host_FilterTime` (engine.dll
    /// FUN_18019ffe0) clamps a demo frame's time to 0.1 s times the timescale, and only `demo_gototick` skips. At 8x a
    /// slow frame covers many ticks; it plays them.
    /// </remarks>
    [Test]
    public void ShowMomentOver_APlayedFrameCoveringThousandsOfTicks_IsNoSkip()
    {
        Seeking seek = new();

        seek.Show(100d);
        seek.HudFrames.Clear();
        seek.Show(2600d, jump: false);

        seek.HudFrames.ShouldBeEmpty();
        seek.Frames.Skip.ShouldNotBeNull().Skipped.ShouldBeFalse();
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
