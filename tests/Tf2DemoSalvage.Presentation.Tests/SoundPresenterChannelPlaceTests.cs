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
        private int _voices;

        public List<SoundPlace> Places { get; } = [];

        public List<(int Voice, (float X, float Y, float Z) Origin)> Moves { get; } = [];

        public float MasterGain { get; set; } = 1f;

        public bool HandsOutVoices => true;

        public int Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            throw new InvalidOperationException("the presenter starts every sound with its place");

        public int PlayAt(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel, SoundPlace place)
        {
            Places.Add(place);

            return ++_voices;
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
