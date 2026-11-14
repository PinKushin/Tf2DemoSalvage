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

    private static void Stamp(Seeking seeking) => seeking.Models.LightAverage.Draw(1, "model", null, default, null, [], 0f, 0f, 0f, 1d, 0, null);

    /// <summary>A skip back reloads the demo in TF2, so every instance is new (B676); a skip forward keeps them, and so does a frame behind within its tick.</summary>
    [Test]
    public void Show_ASkipBack_ForgetsTheModelsLightHistoryAndNothingForwardDoes()
    {
        Seeking seeking = new();

        seeking.Show(100d);
        Stamp(seeking);

        seeking.Show(200.75d);
        seeking.Models.LightAverage.Instances.ShouldBe(1, "a skip forward keeps them");

        seeking.Show(200.25d, jump: false);
        seeking.Models.LightAverage.Instances.ShouldBe(1, "a played frame behind within its tick keeps them");

        seeking.Show(50d);
        seeking.Models.LightAverage.Instances.ShouldBe(0);
    }

    /// <summary>Reverse play changes no jump count, and going back behaves as a reload, or each model holds its light until forward play passes the old time.</summary>
    [Test]
    public void Show_ReversePlayWithNoJump_ForgetsTheModelsLightHistory()
    {
        Seeking seeking = new();

        seeking.Show(100d);
        seeking.Show(120d, jump: false);
        Stamp(seeking);

        seeking.Show(119d, jump: false);

        seeking.Models.LightAverage.Instances.ShouldBe(0);
    }

    /// <summary>A walk's own shows return before the decision, so what clears a jump back is the outer call, before the walk's first frame.</summary>
    [Test]
    public void Show_AJumpBack_HasClearedBeforeItsWalksFirstFrame()
    {
        Seeking seeking = new();
        List<int> during = [];

        seeking.Show(500d);
        Stamp(seeking);
        seeking.InHudFrame = _ => during.Add(seeking.Models.LightAverage.Instances);

        seeking.Show(300d);

        during.ShouldNotBeEmpty("the instrument: the walk ran");
        during.ShouldAllBe(static count => count == 0);
    }

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
                Audio);

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

        /// <summary>Called each time the window is asked to show a tick, for a test that reads the presenter there.</summary>
        public Action? InShow { get; set; }

        /// <summary>A tick whose show throws, once, for a test of what a failure mid-walk leaves; null for none.</summary>
        public int? ThrowAt { get; set; }

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
                    if (ThrowAt is { } failing && (int)shown == failing)
                    {
                        ThrowAt = null;
                        throw new InvalidOperationException("the window failed to show " + shown);
                    }

                    InFlight.Add(Queue?.InFlight ?? false);
                    Shown.Add(shown);
                    Held.Add((Models.HoldsCorpses, Moment.EntitiesOnly, Models.SimulatesOnly, Moments.PlayersOnly, Models.WalksEvents, Frames.Replaying));
                    Paused.Add(Models.Paused);
                    InShow?.Invoke();

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

        /// <summary>The demo the window has open.</summary>
        public DemoTimeline Demo { get; set; } = Timeline;

        private ViewFrame Frame(double shown, double demoFrameSeconds) =>
            new(Demo, null, null, (int)shown, shown, false, false, null, 1280, 720, 0.016f, demoFrameSeconds, true, true, true, 2048, 50);

        /// <summary>One frame's step of the walk: the host's frame, one batch long, over the open demo.</summary>
        public bool Step() =>
            Frames.StepSkip(new ViewFrame(Demo, null, null, 0, 0d, false, false, null, 1280, 720, 0f, 0d, true, true, true, 2048, 50));

        /// <summary>Hands the deck what the walk made, as the update on the frame it lands on does.</summary>
        public void Landed(int tick) =>
            Sound.Update(new NoSink(), tick, (0f, 0f, 0f), (0f, -1f, 0f), 0d, move: Frames.Skip);
    }

    /// <summary>Plays nothing.</summary>
    private sealed class NoSink : IAudioSink
    {
        public float MasterGain { get; set; } = 1f;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) => 0;

        public bool SetVoiceGain(int voice, float gain) => false;

        public bool SetVoicePitch(int voice, float pitch) => false;

        public bool SetGain(int entity, int channel, float gain) => false;

        public bool SetPitch(int entity, int channel, float pitch) => false;

        public void SilenceAll()
        {
            // Nothing is held.
        }

        public int Reclaim() => 0;

        public void Silence(int entity, int channel)
        {
            // Nothing is held.
        }

        public int Silence(int entity, int channel, SoundSample sample) => 0;
    }

    /// <remarks>
    /// A second demo opened during a walk is not a new target for it: its ticks are another recording's, and what the walk made is
    /// not its history (`Skip`'s deck outlives demos, B504, so an old frame handed over would deal into the new one).
    /// </remarks>
    [Test]
    public void ShowMomentOver_AnotherDemoDuringASkip_DropsTheWalkAndSkipsInTheNewOne()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Step();

        seek.Demo = DemoTimeline.Build(SyntheticEffects.Demo(
            [new(1, SyntheticEffects.PlayerClass, SyntheticEffects.Standing(0f, 0f, 0f, 2))],
            [],
            [],
            [],
            new SyntheticEffects.Effect(900, "CTETFBlood", new Dictionary<string, PropertyValue>(StringComparer.Ordinal))));
        seek.HudFrames.Clear();
        seek.Show(300d);

        seek.Frames.Skipping.ShouldBeTrue("the new demo's own seek");

        while (seek.Frames.Skipping)
        {
            seek.Step();
        }

        seek.Shown[^1].ShouldBe(300d, "the moment asked of the new demo is shown");
        seek.HudFrames[0].ShouldBe(seek.Demo.FirstTick + DemoSkip.PacketsPerFrame - 1, "from the new demo's start");
        seek.Audio.Count("skip: to 500").ShouldBe(0, "the old walk hands nothing to the deck");
        seek.Render.Count(FramePresenter.SkipLandedLine + " 500").ShouldBe(0);
        seek.Render.Count(FramePresenter.SkipLandedLine + " 300").ShouldBe(1);
    }

    /// <summary>The close and load paths' call: `RenderFrame` steps nothing during a map read, so nothing else would end the walk.</summary>
    [Test]
    public void DropSkip_DuringASkip_LandsNothingDealsNothingAndHoldsNothing()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Step();
        seek.Models.HoldsCorpses.ShouldBeTrue("the control: the walk holds them");

        seek.Frames.DropSkip();

        seek.Frames.Skipping.ShouldBeFalse();
        seek.Render.Count(FramePresenter.SkipLandedLine).ShouldBe(0);
        seek.Audio.Count("skip: to 500").ShouldBe(0);
        (seek.Models.HoldsCorpses, seek.Models.WalksEvents).ShouldBe((false, true));
        seek.Step().ShouldBeFalse("nothing is left to step");
    }

    /// <remarks>
    /// `SkipToTick` (engine.dll FUN_180073b10) with a target ahead only writes it (+0x5bc), so the read goes on and everything it read
    /// stands, whichever target it was read towards (B504's deck, B708).
    /// </remarks>
    [Test]
    public void ShowMomentOver_ATargetAheadDuringASkip_DealsEveryFrameOfBothLegs()
    {
        Seeking seek = new();

        seek.Sound.Schedule = new SoundSchedule([]);
        seek.Frames.SkipsAcrossFrames = true;
        seek.InHudFrame = _ => seek.Frames.HudSound(0, HudScript, hudReplaying: false);
        seek.Show(500d);
        seek.Step();
        seek.Step();
        seek.Show(700d);
        seek.Frames.FinishSkip();
        seek.Landed(700);

        int frames = DemoSkip.Frames(Timeline.FirstTick, 700).Count;

        seek.HudFrames.Count.ShouldBe(frames, "the control: the read went on through both");
        seek.Audio.Count($"skip: to 700 from the start in {frames} frames made {frames} HUD").ShouldBe(1);
        seek.Audio.Count($"wave deck: a skip to tick 700 from the start over {frames + 1} frames dealt {frames} script sounds").ShouldBe(1, "the landing's frame closes the list");
    }

    /// <remarks>
    /// A target behind what it has read reloads and reads from the start (FUN_180073b10), and what the first read dealt stays dealt: the
    /// flags live in soundemittersystem.dll, which a reload does not touch (B504). So the deck takes the first read, ended at the last
    /// batch it reached and not at the target it was going to, and then the second.
    /// </remarks>
    [Test]
    public void ShowMomentOver_ATargetBehindDuringASkip_DealsWhatItReadUpToTheLastBatchAndThenTheNewRead()
    {
        Seeking seek = new();

        seek.Sound.Schedule = new SoundSchedule([]);
        seek.Frames.SkipsAcrossFrames = true;
        seek.InHudFrame = _ => seek.Frames.HudSound(0, HudScript, hudReplaying: false);
        seek.Show(800d);
        seek.Step();
        seek.Step();
        seek.Step();

        int reached = seek.HudFrames[^1];

        seek.Show(reached - 150d);
        seek.Frames.FinishSkip();
        seek.Landed(reached - 150);

        int second = DemoSkip.Frames(Timeline.FirstTick, reached - 150).Count;

        seek.Audio.Count($"wave deck: a skip to tick {reached} from the start over 3 frames dealt 3 script sounds").ShouldBe(1);
        seek.Audio.Count($"wave deck: a skip to tick {reached - 150} from the start over {second + 1} frames dealt {second} script sounds").ShouldBe(1);
    }

    /// <summary>A jump a walk takes is that walk's: the next played tick is no skip of its own, and there is one landing (D221).</summary>
    [Test]
    public void ShowMomentOver_APlayedTickAfterARetargetedWalkLands_IsNoSecondSkip()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Step();
        seek.Show(600d);
        seek.Frames.FinishSkip();

        seek.Show(601d, jump: false);

        seek.Frames.Skipping.ShouldBeFalse("a played tick starts no walk");
        seek.Frames.Skip.ShouldNotBeNull().Skipped.ShouldBeFalse();
        seek.Render.Count(FramePresenter.SkipLandedLine).ShouldBe(1);
        seek.Shown[^1].ShouldBe(601d);
    }

    /// <summary>`SkipToTick` reloads only for a target behind what has been read, so a drag back does not restart the read every frame.</summary>
    [Test]
    public void ShowMomentOver_RepeatedTargetsBehindTheOldOneButAheadOfTheRead_ReloadOnce()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(800d);
        seek.Step();
        seek.Step();
        seek.Show(120d);
        seek.Step();

        int reached = seek.HudFrames[^1];

        reached.ShouldBe(Timeline.FirstTick + DemoSkip.PacketsPerFrame - 1, "the control: 120 was behind what it had read, so it reloaded");

        // Ahead of what the reload has read, though behind the first target: only the target moves.
        seek.Show(650d);
        seek.Show(640d);
        seek.Show(630d);
        seek.Step();

        seek.HudFrames[^1].ShouldBe(reached + DemoSkip.PacketsPerFrame, "the read went on");
    }

    /// <remarks>
    /// The frame drawn between batches shows the last batch's tick, so the HUD's feed and the camera only move forward, and it poses with
    /// corpses held and walks no event (B504; a `C_TFRagdoll` made while skipping makes no ragdoll, `c_tf_player.cpp:583`, `:1190-1193`).
    /// </remarks>
    [Test]
    public void StepSkip_TheFrameItDraws_ShowsTheLastBatchWithCorpsesHeldAndEventsNotWalked()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Step();

        int through = seek.HudFrames[^1];

        seek.Shown[^1].ShouldBe(through);
        seek.Held[^1].ShouldBe((true, false, false, false, false, false), "the drawn frame: every entity, corpses held, no event walked");
        (seek.Models.HoldsCorpses, seek.Models.WalksEvents).ShouldBe((true, false), "and so the pose that follows it in the camera stage");

        seek.Frames.FinishSkip();

        (seek.Models.HoldsCorpses, seek.Models.WalksEvents).ShouldBe((false, true), "released at the landing");
        seek.Held[^1].ShouldBe((false, false, false, false, true, false));
    }

    /// <summary>
    /// `RenderPlayerSprites` runs on every frame a skip draws (viewrender.cpp:2140, B549): a skip's frame, which hands the
    /// device no particles of its own, still tells it the player sprites.
    /// </summary>
    [Test]
    public void DrawOver_ASkipsFrame_StillHandsTheDeviceThePlayerSprites()
    {
        Seeking seek = new();
        FakeFrameDevice device = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(150d, device: new FakeFrameDevice());
        seek.Step();
        seek.Frames.Skipping.ShouldBeTrue("the control: a skip under way");

        seek.Frames.DrawOver(
            new ViewFrame(seek.Demo, null, null, 150, 150d, false, false, null, 1280, 720, 0.016f, 0d, true, true, true, 2048, 50),
            new FreeCamera(),
            device);

        (device.PlayerSprites.Count, device.Particles.ShouldHaveSingleItem().Count).ShouldBe((1, 0));
    }

    /// <summary>A target asked for during the decals' part: the frames go on, the replay restarts for it, and one landing comes.</summary>
    [Test]
    public void ShowMomentOver_AnotherTargetAfterItsFramesHaveEnded_GoesOnAndLandsOnce()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(150d, device: new FakeFrameDevice());
        seek.Step();

        // One batch fits under 150 from the demo's start, so this step ends the frames; the landing is next.
        seek.Step();
        seek.Frames.Skipping.ShouldBeTrue("the control: it has not landed");

        int read = seek.HudFrames.Count;

        seek.Show(600d, device: new FakeFrameDevice());

        while (seek.Frames.Skipping)
        {
            seek.Step();
        }

        seek.HudFrames.Count.ShouldBeGreaterThan(read, "the read went on to the new target");
        seek.Render.Count(FramePresenter.SkipLandedLine).ShouldBe(1);
        seek.Render.Count(FramePresenter.SkipLandedLine + " 600").ShouldBe(1);
        seek.Render.Count("model decals: a seek to 150").ShouldBe(0, "the replay it was running was for a tick it never landed on");
        seek.Render.Count("model decals: a seek to 600").ShouldBe(1);
        seek.Shown[^1].ShouldBe(600d);
    }

    /// <remarks>`IsPlaybackPaused` answers false for a skip's length (FUN_1800729f0): a paused seek's batches walk, the landing's frame does not.</remarks>
    [Test]
    public void StepSkip_ASeekWhilePausedAcrossFrames_WalksItsBatchesAndLandsPaused()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);

        while (seek.Frames.Skipping)
        {
            seek.Step();
        }

        seek.Shown[^1].ShouldBe(500d);
        seek.Paused[^1].ShouldBeTrue("the frame it lands on has the flag's frametime");
        seek.Paused.ShouldContain(false, "the batches do not");
        seek.Models.Paused.ShouldBeTrue();
    }

    /// <summary>A sound the HUD asks for while painting a frame drawn during a skip is that batch's, dealt with the skip, not played at the landing.</summary>
    [Test]
    public void HudSound_OnAFrameDrawnDuringASkip_IsDealtWithTheSkipAndNotPlayed()
    {
        Seeking seek = new();

        seek.Sound.Schedule = new SoundSchedule([]);
        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Step();

        seek.Frames.HudSound(500, HudScript, hudReplaying: false);

        seek.Sound.Emitted.ShouldBeEmpty();

        seek.Frames.FinishSkip();
        seek.Landed(500);

        seek.Audio.Count("made 1 HUD and 0 animation script sounds").ShouldBe(1);
        seek.Audio.Count(" dealt 1 script sounds").ShouldBe(1);
    }

    /// <summary>The one count the walk's line carries of its per-frame cap: the batches it actually read, at their largest.</summary>
    [Test]
    public void StepSkip_AFrameOfManyTicks_SaysTheMostBatchesItReadInAFrame()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Frames.StepSkip(HostFrame(3.5f * Timeline.IntervalPerTick));
        seek.Frames.StepSkip(HostFrame(0f));
        seek.Frames.FinishSkip();

        seek.Render.Count("at most 3 in a frame").ShouldBe(1);
    }

    /// <remarks>
    /// `CDemoPlayer::ReadPacket` (engine.dll FUN_180072ee0) returns null at the 100th packet while skipping, ending the read loop, and
    /// `_Host_RunFrame` (FUN_1801a4570) renders after it; the main loop at `0x18020d460` drains the window's queue before each
    /// `CEngine::Frame`. So one frame reads one batch and asks nothing of the caller until the frame's own step.
    /// </remarks>
    [Test]
    public void StepSkip_AcrossFrames_ReadsAtMostOneBatchAFrameAndLandsOnTheTick()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);

        seek.Frames.Skipping.ShouldBeTrue("the call that asked reads nothing: the frames do");
        seek.HudFrames.ShouldBeEmpty();

        List<int> perFrame = [];
        int steps = 0;

        while (seek.Frames.Skipping && steps++ < 100)
        {
            int before = seek.HudFrames.Count;

            seek.Frames.StepSkip(HostFrame(0f));
            perFrame.Add(seek.HudFrames.Count - before);
        }

        perFrame.ShouldAllBe(batches => batches <= 1, "99 packets a frame");
        seek.HudFrames.Count.ShouldBeGreaterThan(2, "the control: it read batches");

        int last = Timeline.FirstTick - 1;

        foreach (int through in seek.HudFrames)
        {
            (through - last).ShouldBe(DemoSkip.PacketsPerFrame);
            last = through;
        }

        seek.Frames.Skipping.ShouldBeFalse();
        seek.Shown[^1].ShouldBe(500d, "it lands on the tick asked for");
    }

    [Test]
    public void StepSkip_AFrameOfManyTicks_ReadsOneBatchPerTick()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Frames.StepSkip(HostFrame(3.5f * Timeline.IntervalPerTick));

        seek.HudFrames.Count.ShouldBe(3, "`_Host_RunFrame` runs a read loop per whole tick it accumulated");
    }

    [Test]
    public void ShowMomentOver_AnotherTickDuringASkip_GoesOnForwardAndRestartsBackward()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Frames.StepSkip(HostFrame(0f));
        seek.Frames.StepSkip(HostFrame(0f));

        int reached = seek.HudFrames[^1];

        seek.Show(600d);
        seek.Frames.StepSkip(HostFrame(0f));
        seek.HudFrames[^1].ShouldBe(reached + DemoSkip.PacketsPerFrame, "a target ahead moves the target and the reading goes on");

        seek.Show(250d);
        seek.Frames.StepSkip(HostFrame(0f));
        seek.HudFrames[^1].ShouldBe(Timeline.FirstTick + DemoSkip.PacketsPerFrame - 1, "a target behind reads from the start, as `SkipToTick` reloads");

        seek.Frames.FinishSkip();
        seek.Shown[^1].ShouldBe(250d);
    }

    /// <summary>
    /// Rewind is the owner's addition above the engine (D246). B504 made it read from tick 0 with nobody told; this test holds what came before:
    /// a step back never reads the demo from its start. D217's bound is one 2 s checkpoint span.
    /// </summary>
    [Test]
    public void Rewind_AShortStepBack_ReadsNoMoreThanOneCheckpointSpan()
    {
        const int CheckpointSpan = 133;

        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.HudFrames.ShouldNotBeEmpty("the control: a seek forward does read frames");
        seek.HudFrames.Clear();

        seek.Show(490d);
        seek.Frames.FinishSkip();

        seek.HudFrames.ShouldAllBe(tick => tick >= 490 - CheckpointSpan, "a step back must not read from the demo's start");
        seek.Shown[^1].ShouldBe(490d);
    }

    /// <remarks>
    /// **A rewind rebuilds what playing accumulated in its entities** (B527, `docs/memory/a-seek-rebuilds-what-playing-accumulated.md`):
    /// a transition fading out, the animation-event walk, the minigun's barrel speed and the duck jump are each built frame by frame, and
    /// each forgets its past within a second. So a rewind forgets every entity's state and poses the two seconds before its target from
    /// the timeline — entities only, events walked and not dealt, no HUD frame and no demo read (D246) — then shows the target. The
    /// forward show of 500 is the control: it walks HUD frames, which this must not.
    /// </remarks>
    [Test]
    public void Rewind_AStepBack_PosesTheTwoSecondsBeforeItsTargetSilently()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.HudFrames.ShouldNotBeEmpty("the control: a seek forward reads frames");
        seek.HudFrames.Clear();
        seek.Shown.Clear();
        seek.Held.Clear();

        seek.Show(400d);
        seek.Frames.FinishSkip();

        seek.HudFrames.ShouldBeEmpty("a rewind reads no HUD frame");
        seek.Shown[^1].ShouldBe(400d);

        List<double> preRoll = seek.Shown[..^1];

        preRoll.ShouldBe([.. Enumerable.Range(400 - FramePresenter.RewindPreRollTicks, FramePresenter.RewindPreRollTicks).Select(static tick => (double)tick)]);
        seek.Held[..^1].ShouldAllBe(held => held.EntitiesOnly && held.SimulatesOnly && held.WalksEvents && held.Corpses);

        // Everything put back as it was before the rewind: the target is shown whole, walking its events, its corpses stepped.
        seek.Held[^1].ShouldBe((false, false, false, false, true, false), "the target is shown with nothing held");
    }

    /// <remarks>
    /// **The window draws the tick being posed while a rewind rebuilds** (B727): `Skipping` is what `ViewerSession.TickOfThisFrame`
    /// reads to take the shown tick rather than the transport's, so a first-person view, the followed entity and the fog of each
    /// pre-roll tick are that tick's, not the target's against earlier players. False again for the target's own show.
    /// </remarks>
    [Test]
    public void Rewind_EachPreRollShow_IsSkippingAndTheTargetsIsNot()
    {
        Seeking seek = new();
        List<bool> skipping = [];

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Shown.Clear();
        seek.InShow = () => skipping.Add(seek.Frames.Skipping);

        seek.Show(400d);
        seek.Frames.FinishSkip();

        seek.Shown.Count.ShouldBe(FramePresenter.RewindPreRollTicks + 1, "the control: the pre-roll ran");
        skipping[..^1].ShouldAllBe(static skipped => skipped);
        skipping[^1].ShouldBeFalse();
        seek.Frames.Skipping.ShouldBeFalse();
    }

    /// <remarks>
    /// **A backward scrub drag jumps on every event** (`OnScrubbed`), and a whole pre-roll cost 230 ms a rewind on f12 mid-match
    /// (`docs/measurements/2026-11-13-b727-rewind-rebuild-cost.md`), so the rebuild spreads over drawn frames as a skip does (B708): no
    /// event poses more than a frame's ticks, a new jump starts the rebuild again for its own target, and only the tick the drag stops on
    /// is posed through its whole window (B527) before it is shown.
    /// </remarks>
    [Test]
    public void Rewind_ADragBackOverFrames_PosesAFramesTicksAnEventAndTheLastTargetsWholeWindow()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Shown.Clear();

        List<int> perEvent = [];

        foreach (double target in (double[])[450d, 430d, 410d])
        {
            int before = seek.Shown.Count;

            seek.Show(target);
            seek.Step();
            perEvent.Add(seek.Shown.Count - before);
        }

        perEvent.ShouldAllBe(static shown => shown <= FramePresenter.RewindTicksPerFrame + 1, "an event and its frame pose at most a frame's ticks, and draw one");
        seek.Frames.Skipping.ShouldBeTrue("the control: the last target has not landed in one frame");

        seek.Frames.FinishSkip();

        int posed = seek.Shown.LastIndexOf(410d - FramePresenter.RewindPreRollTicks);

        // A drawn frame between two batches shows the tick its batch posed last, so the window's ticks repeat but never go back.
        seek.Shown[posed..].ShouldBeInOrder();
        seek.Shown[posed..].Distinct().ShouldBe([.. Enumerable.Range(410 - FramePresenter.RewindPreRollTicks, FramePresenter.RewindPreRollTicks + 1).Select(static tick => (double)tick)]);
        seek.Held[^1].ShouldBe((false, false, false, false, true, false), "the target is shown with nothing held");
        seek.Frames.Skipping.ShouldBeFalse();
    }

    /// <summary>
    /// A drag that turns forward while a rebuild is part way lands the rebuild first, then walks forward from its target as any
    /// forward skip does: the walk starts from entities rebuilt for that target (B527), not from part of a window, and it reads its
    /// HUD frames and lands with its own line, which the UI suite's rewind waits on (`Transport_JumpBackToTheStart_ShowsItWithNoWalk`).
    /// </summary>
    [Test]
    public void Rewind_AForwardJumpDuringARebuild_LandsItThenWalksForward()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Shown.Clear();
        seek.Show(400d);
        seek.Step();
        seek.Frames.Skipping.ShouldBeTrue("the control: the rebuild is part way");
        seek.HudFrames.Clear();

        seek.Show(650d);
        seek.Frames.FinishSkip();

        int landed = seek.Shown.IndexOf(400d);

        landed.ShouldBeGreaterThan(0, "the rebuild landed on its target");
        seek.Shown[..landed].Distinct().ShouldBe([.. Enumerable.Range(400 - FramePresenter.RewindPreRollTicks, FramePresenter.RewindPreRollTicks).Select(static tick => (double)tick)], "its whole window first");
        seek.HudFrames.ShouldNotBeEmpty("the forward jump walks, a batch a 99 packets");
        seek.HudFrames.ShouldAllBe(static tick => tick > 400);
        seek.Shown[^1].ShouldBe(650d);
        seek.Render.Count($"{FramePresenter.SkipLandedLine} 650").ShouldBe(1);
    }

    /// <summary>
    /// A batch whose show throws leaves no rewind behind: the corpses, the event walk and the entities-only show are all put back, so
    /// the next show is a whole one rather than a viewer wedged holding them (review of B727).
    /// </summary>
    [Test]
    public void Rewind_AShowThatThrowsMidRebuild_DropsItWithEveryFlagPutBack()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Show(400d);
        seek.Frames.Skipping.ShouldBeTrue("the control: the rebuild is under way");
        seek.ThrowAt = 400 - FramePresenter.RewindPreRollTicks + 2;

        Should.Throw<InvalidOperationException>(() => seek.Step());

        seek.Frames.Skipping.ShouldBeFalse();
        (seek.Models.HoldsCorpses, seek.Moment.EntitiesOnly, seek.Models.SimulatesOnly, seek.Models.WalksEvents).ShouldBe((false, false, false, true));
        seek.Shown.Clear();

        seek.Show(300d, jump: false);

        seek.Shown.ShouldBe([300d], "a show after it is the tick alone, with no rebuild left to land");
    }

    /// <summary>
    /// A repaint of the target while the rebuild runs waits for the landing, and a pause taken there holds: the landing shows the
    /// target with the pause its last frame gave, not the one the jump had (review of B727; `Paused` is per frame, B541).
    /// </summary>
    [Test]
    public void Rewind_APausedRepaintOfItsTarget_WaitsAndLandsPaused()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d, demoFrameSeconds: 0.015);
        seek.Frames.FinishSkip();
        seek.Show(400d, demoFrameSeconds: 0.015);
        seek.Step();

        int before = seek.Shown.Count;

        seek.Show(400d, demoFrameSeconds: 0d, jump: false);

        seek.Shown.Count.ShouldBe(before, "a repaint of the target shows nothing until the landing");
        seek.Frames.Skipping.ShouldBeTrue();

        seek.Frames.FinishSkip();

        seek.Shown[^1].ShouldBe(400d);
        seek.Paused[^1].ShouldBeTrue("the landing shows the pause the last frame gave");
    }

    /// <summary>A step back that is no jump — reverse play, one tick a frame — rebuilds nothing: only a jump is a seek (D221).</summary>
    [Test]
    public void Rewind_ReversePlayWithNoJump_PosesOnlyItsOwnTick()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Shown.Clear();

        seek.Show(499d, jump: false);

        seek.Shown.ShouldBe([499d]);
    }

    /// <summary>A step back during a forward walk is a rewind too, and the walk it interrupts is dropped.</summary>
    [Test]
    public void Rewind_DuringAForwardWalk_ShowsItsTickWithNoFurtherFrames()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(500d);
        seek.Frames.FinishSkip();
        seek.Show(600d);
        seek.Frames.StepSkip(HostFrame(0f));
        seek.HudFrames.Clear();

        seek.Show(400d);
        seek.Frames.FinishSkip();

        seek.Frames.Skipping.ShouldBeFalse();
        seek.HudFrames.ShouldBeEmpty();
        seek.Shown[^1].ShouldBe(400d);
    }

    /// <summary>Behind where the walk is going but ahead of what it has drawn: still a step back, and `Retarget` would read that from the start.</summary>
    [Test]
    public void Rewind_DuringAWalkToATickBetweenShownAndTarget_ReadsNoFurtherFrames()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Frames.RewindsAtOnce = true;
        seek.Show(100d);
        seek.Frames.FinishSkip();
        seek.Show(600d);
        seek.Frames.StepSkip(HostFrame(0f));

        int shown = seek.HudFrames[^1];
        double between = shown + 10d;

        between.ShouldBeLessThan(600d, "the case needs a tick between what is drawn and the target");
        seek.HudFrames.Clear();

        seek.Show(between);
        seek.Frames.FinishSkip();

        seek.Frames.Skipping.ShouldBeFalse();
        seek.HudFrames.ShouldBeEmpty();
        seek.Shown[^1].ShouldBe(between);
    }

    [Test]
    public void ShowMomentOver_TheSameTickAgainDuringASkip_DoesNoWorkOfItsOwn()
    {
        Seeking seek = new();

        seek.Frames.SkipsAcrossFrames = true;
        seek.Show(500d);
        seek.Frames.StepSkip(HostFrame(0f));

        int read = seek.HudFrames.Count;

        seek.Show(500d, jump: false);
        seek.Show(500d, jump: false);

        seek.HudFrames.Count.ShouldBe(read, "a repaint asks again; only the frame's step reads");
    }

    [Test]
    public void BatchesThisFrame_ByFrameLength_IsOnePerWholeTickCappedAtTheHostsLongestFrame()
    {
        const float Interval = 0.015f;

        DemoSkip.PacketsPerFrame.ShouldBe(99, "`ReadPacket` returns null at the 100th packet of a skip (`if ( 99 < count )`)");
        DemoSkip.BatchesThisFrame(0d, Interval).ShouldBe(1, "one with none, while skipping");
        DemoSkip.BatchesThisFrame(0.0301, Interval).ShouldBe(2);
        DemoSkip.BatchesThisFrame(5d, Interval).ShouldBe(6, "`Host_FilterTime` caps a demo frame at 0.1 s");
        DemoSkip.BatchesThisFrame(1d, 0f).ShouldBe(1, "a demo that names no interval");
    }

    private static ViewFrame HostFrame(float frameSeconds) =>
        new(Timeline, null, null, 0, 0d, false, false, null, 1280, 720, frameSeconds, 0d, true, true, true, 2048, 50);

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
            drainedBy.ShouldBe("RunSkipFrame");
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
            drainedBy.ShouldBe("BeginDecals");
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
