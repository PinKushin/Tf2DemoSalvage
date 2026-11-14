using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What the sound presenter tells the sink for the channel steal (B730): each start's origin, the listener, whether its
/// source is the local player, and each followed channel's origin as its entity moves.
/// </summary>
/// <remarks>
/// `SND_PickDynamicChannel` (engine.dll FUN_18002a480) passes over the local player's channels for another source's start
/// (sound services +0x48, FUN_180018f30: `source == player slot + 1`), and weighs the squared distances of the new
/// sound's origin and a busy channel's `ch->origin` from the listener, the latter moved by `SND_Spatialize` each frame.
/// </remarks>
public sealed class SoundPresenterChannelPlaceTests
{
    private const int Player = 1;
    private const int Other = 7;

    [Test]
    public void Update_StartsFromThePlayerAndAnother_PlaceEachAtItsOriginAndOnlyThePlayersAsThePlayers()
    {
        Sink sink = new();
        SoundPresenter presenter = Presenter(
            origins: null,
            new SceneSound(50, "ui/a.wav", 1, Player, 0, 1f, 75, 100, 0f, 10f, 20f, 30f),
            new SceneSound(51, "ui/b.wav", 1, Other, 0, 1f, 75, 100, 0f, -5f, 0f, 2f));
        presenter.LocalPlayer = Player;

        presenter.Update(sink, 40, (1f, 2f, 3f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 60, (1f, 2f, 3f), (0f, -1f, 0f), now: 0.6);

        sink.Places.ShouldBe(
        [
            new SoundPlace((10f, 20f, 30f), (1f, 2f, 3f), FromLocalPlayer: true),
            new SoundPlace((-5f, 0f, 2f), (1f, 2f, 3f), FromLocalPlayer: false),
        ]);
    }

    [Test]
    public void Update_WithNoLocalPlayer_PlacesNoStartAsThePlayers()
    {
        Sink sink = new();
        SoundPresenter presenter = Presenter(origins: null, new SceneSound(50, "ui/a.wav", 1, Player, 0, 1f, 75, 100, 0f, 0f, 0f, 0f));

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.6);

        sink.Places.ShouldHaveSingleItem().FromLocalPlayer.ShouldBeFalse();
    }

    [Test]
    public void Update_AFollowedChannelsEntityMoving_MovesItsVoiceEachPass()
    {
        Sink sink = new();
        (float X, float Y, float Z) at = (5f, 0f, 0f);
        SoundPresenter presenter = Presenter(
            origins: entity => entity == Other ? (at.X, at.Y, at.Z, 0f) : null,
            new SceneSound(50, "ambient/hum_loop.wav", 1, Other, 0, 1f, 75, 100, 0f, 0f, 0f, 0f));

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.6);
        at = (9f, 1f, 2f);
        presenter.Update(sink, 61, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.61);

        sink.Moves[^1].ShouldBe((1, (9f, 1f, 2f)));
    }

    /// <remarks>
    /// `CL_DispatchSound` (engine.dll FUN_18008caf0, 0x18008cd57-0x18008cd9c): `params.delay` is the sound's own delay, but
    /// with `SND_DELAY` (`test cl, 0x10`) and a delay above -0.1f (`comiss`, 0x18037b9dc) it is
    /// `S_ComputeDelayForSoundtime( delay + last server tick time − (ticks this frame − 1) × interval, CLOCK_SYNC_SERVER )`,
    /// which the sink's mixer clock answers (B732). Ticks this frame is 1 on a demo: `_Host_RunFrame` (FUN_1801a5eb0) sets
    /// `DAT_180472a74 = 1` on its single-threaded path (0x1801a62c3), which `FUN_1801a4860` picks without a local server.
    /// </remarks>
    [Test]
    public void Update_DelayedStarts_AskTheSinkOnlyForSndDelayAboveMinusATenthAtTheTicksOwnTime()
    {
        Sink sink = new();
        SoundPresenter presenter = Presenter(
            origins: null,
            new SceneSound(50, "ui/a.wav", 1, Other, 0, 1f, 75, 100, -0.05f, 0f, 0f, 0f) { FromServer = true },
            new SceneSound(51, "ui/b.wav", 1, Other, 1, 1f, 75, 100, 0.03f, 0f, 0f, 0f) { ServerTimed = true },
            new SceneSound(52, "ui/c.wav", 1, Other, 3, 1f, 75, 100, -0.1f, 0f, 0f, 0f) { ServerTimed = true });
        presenter.SecondsPerTick = () => 0.015f;

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 52, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.6);

        sink.Places.Count.ShouldBe(3);
        sink.Places[0].Delay.ShouldBe(-0.05f, "no SND_DELAY: the delay as sent");
        sink.Places[0].FromServer.ShouldBeTrue();
        sink.Places[1].FromServer.ShouldBeFalse("the control: carried, not assumed");

        // Twelve ticks crossed this pass, and still no step back: one tick a frame on the engine's path.
        ServerSoundTime time = sink.Times.ShouldHaveSingleItem();
        time.Time.ShouldBe(0.03f + (51 * 0.015f), 1e-5f);
        time.IntervalPerTick.ShouldBe(0.015f);
        sink.Places[1].Delay.ShouldBe(Sink.FirstDelay, "the sink's answer is the delay");

        sink.Places[2].Delay.ShouldBe(-0.1f, "-0.1 is not above -0.1: the delay as sent, a skip ahead");
    }

    /// <remarks>
    /// The engine asks the clock once, in `CL_DispatchSound`, and a channel waiting on its wave keeps that delay; its mixer
    /// pays the wait only once the channel mixes (FUN_18003f2c0 leaves an unready mixer out of the pass). A sound retried
    /// here carries the delay its own pass got.
    /// </remarks>
    [Test]
    public void Update_ASndDelayStartWaitingOnItsWave_KeepsTheDelayItsOwnPassGot()
    {
        Sink sink = new();
        bool arrived = false;
        SoundSample tone = new(22050, 1, new float[] { 0.5f, -0.5f });
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => arrived ? tone with { Key = name } : null,
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule([new SceneSound(51, "ui/b.wav", 1, Other, 1, 1f, 75, 100, 0.03f, 0f, 0f, 0f) { ServerTimed = true }]),
            Loading = _ => !arrived,
        };

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 52, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.6);
        sink.Places.ShouldBeEmpty("the control: waiting on its wave");

        arrived = true;
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.7);

        sink.Times.Count.ShouldBe(1, "asked once, at dispatch");
        sink.Places.ShouldHaveSingleItem().Delay.ShouldBe(Sink.FirstDelay);
    }

    [Test]
    public void Update_AStartTheSinkRefuses_LogsTheSinksReason()
    {
        Sink sink = new() { Refuses = StartRefusal.LoopKept };
        RecordingLogger log = new();
        SoundSample tone = new(22050, 1, new float[] { 0.5f, -0.5f });
        SoundPresenter presenter = new(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => tone with { Key = name },
            log)
        {
            Schedule = new SoundSchedule([new SceneSound(50, "ui/a.wav", 1, Other, 1, 1f, 75, 100, 0f, 0f, 0f, 0f)]),
        };

        presenter.Update(sink, 40, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.4);
        presenter.Update(sink, 60, (0f, 0f, 0f), (0f, -1f, 0f), now: 0.6);

        sink.Places.ShouldHaveSingleItem("the control: the start reached the sink");
        log.Count("sound started no voice (LoopKept)").ShouldBe(1);
    }

    private static SoundPresenter Presenter(Func<int, (float X, float Y, float Z, float Radius)?>? origins, params SceneSound[] sounds)
    {
        SoundSample tone = new(22050, 1, new float[] { 0.5f, -0.5f });

        return new SoundPresenter(
            new SoundscapeSystem(new ActiveLoops(), _ => null, NullLogger.Instance),
            new ActiveLoops(),
            name => tone with { Key = name, Loops = name.EndsWith("_loop.wav", StringComparison.Ordinal) },
            NullLogger.Instance)
        {
            Schedule = new SoundSchedule(sounds),
            EntityOrigin = (entity, _) => origins?.Invoke(entity),
        };
    }

    private sealed class Sink : IAudioSink
    {
        /// <summary>The first delay <see cref="DelayFor"/> answers; the nth is n times it, so a second ask shows.</summary>
        public const float FirstDelay = 0.07f;

        private int _voices;

        public List<SoundPlace> Places { get; } = [];

        public List<ServerSoundTime> Times { get; } = [];

        /// <summary>When set, every start answers 0 and this is the reason.</summary>
        public StartRefusal Refuses { get; init; }

        public StartRefusal Refusal => Refuses;

        public float DelayFor(ServerSoundTime time)
        {
            Times.Add(time);

            return FirstDelay * Times.Count;
        }

        public List<(int Voice, (float X, float Y, float Z) Origin)> Moves { get; } = [];

        public float MasterGain { get; set; } = 1f;

        public bool HandsOutVoices => true;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            throw new InvalidOperationException("the presenter starts every sound with its place");

        public int PlayAt(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel, SoundPlace place)
        {
            Places.Add(place);

            return Refuses == StartRefusal.None ? ++_voices : 0;
        }

        public void MoveVoice(int voice, (float X, float Y, float Z) origin) => Moves.Add((voice, origin));

        public bool SetGain(int entity, int channel, float gain) => true;

        public bool SetVoiceGain(int voice, float gain) => true;

        public bool SetVoicePitch(int voice, float pitch) => true;

        public bool SetPitch(int entity, int channel, float pitch) => true;

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
}
