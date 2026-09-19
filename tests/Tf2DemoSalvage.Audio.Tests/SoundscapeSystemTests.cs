using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Choosing, fading and stopping the map's ambience.
/// </summary>
/// <remarks>
/// **None of this could be asserted before the move**, and that is its point rather than the line
/// count (B188, B184). It lived in <c>MainForm</c>, so reaching it meant an STA, a device and the
/// desktop lock — and it talked to a concrete <see cref="AudioOutput"/>, so even extracted it would
/// have needed a sound card. <see cref="IAudioSink"/> is what closes that.
///
/// Valve's arrangement is the same shape: <c>C_SoundscapeSystem</c> decides what should be playing
/// and calls through the <c>IEngineSound</c> interface rather than the mixer itself.
///
/// Choosing is tested in <c>SoundscapePlacementsTests</c> on hand-written entity lumps (B217); the
/// <c>Update</c> tests below build a one-soundscape map the same way.
/// </remarks>
public sealed class SoundscapeSystemTests
{
    [Test]
    public void Update_WithNoPlacementsRead_PlaysNothing()
    {
        // A map with no `env_soundscape` entities, or none read yet — which is every frame before
        // the first map load, and every frame on a machine with no TF2. Silence rather than a
        // throw: a viewer with no map still pumps frames.
        Sink sink = new();

        System().Update(sink, Origin, Right, now: 1d);

        sink.Played.ShouldBeEmpty();
        sink.Silenced.ShouldBeEmpty();
    }

    [Test]
    public void Clear_WithNothingLoaded_IsSafe()
    {
        // **This is the seek path, and leaving it out was a silent bug.** `StopAll` deletes every
        // source, but the voice keys survived it — so the system saw them as already playing and
        // only ever called SetGain on sources that no longer existed. The map's room tone died at
        // the first seek and never came back, with nothing reported anywhere.
        //
        // A seek can happen before any map is read, so this must not depend on one.
        Should.NotThrow(() => System().Clear());
    }

    [Test]
    public void GainOf_AnUnpositionedVoice_IsItsVolumeWhereverTheListenerStands()
    {
        // **Room tone rather than a thing in the room.** A soundscape sound with no position plays
        // AT the listener, so the distance is zero by construction and a falloff would be
        // meaningless. Two very different listener positions, because one cannot tell "ignores
        // distance" from "happened to be zero away".
        SoundscapeVoice voice = new(1, "ambient/indoors.wav", 0.5f, Position: null, Attenuation: null);

        SoundscapeSystem.GainOf(voice, (0f, 0f, 0f)).ShouldBe(0.5f);
        SoundscapeSystem.GainOf(voice, (4000f, 3000f, 500f)).ShouldBe(0.5f);
    }

    [Test]
    public void GainOf_APositionedVoice_IsQuieterFurtherAway()
    {
        // **The control for the pair.** A voice placed at a target IS a source in the world, so the
        // same volume must reach the listener quieter from further off — and two distances are
        // needed, because a single one cannot separate "attenuates" from "returns some constant".
        SoundscapeVoice voice = new(
            1,
            "ambient/generator.wav",
            1f,
            Position: (1000f, 0f, 0f),
            Attenuation: 1f);

        float near = SoundscapeSystem.GainOf(voice, (900f, 0f, 0f));
        float far = SoundscapeSystem.GainOf(voice, (0f, 0f, 0f));

        near.ShouldBeGreaterThan(far);
        far.ShouldBeGreaterThan(0f, "attenuation is a falloff, not a cutoff");
    }

    /// <remarks>
    /// **The whole path on a synthetic map** (B217): a soundscape placed in range, whose catalog entry loops one wave, starts
    /// that wave once on the soundscape's own entity and only re-gains it afterwards. These were all uncovered on the mutation
    /// box, whose only system tests never loaded a placement.
    /// </remarks>
    [Test]
    public void Update_ASoundscapeInRange_StartsItsLoopOnceAndThenOnlyRegainsIt()
    {
        Sink sink = new();
        SoundscapeSystem system = Playing(opens: true);

        system.Update(sink, Origin, Right, now: 1d);
        system.Update(sink, Origin, Right, now: 1.5d);
        system.Update(sink, Origin, Right, now: 2d);

        sink.Played.ShouldHaveSingleItem().Entity.ShouldBe(SoundscapeSystem.SoundscapeEntity);
        sink.Regained.ShouldBeGreaterThan(0, "a playing loop is re-gained, not restarted");
    }

    [Test]
    public void Update_AWaveThatWillNotOpen_IsNotAskedForAgain()
    {
        Sink sink = new();
        int asked = 0;
        SoundscapeSystem system = Playing(opens: false, onAsk: () => asked++);

        system.Update(sink, Origin, Right, now: 1d);
        system.Update(sink, Origin, Right, now: 2d);

        sink.Played.ShouldBeEmpty();
        asked.ShouldBe(1);
    }

    [Test]
    public void Update_AfterLevelShutdown_PlaysNothingNew()
    {
        Sink sink = new();
        SoundscapeSystem system = Playing(opens: true);

        system.LevelShutdownPreEntity();
        system.Update(sink, Origin, Right, now: 1d);

        sink.Played.ShouldBeEmpty();
        system.Placements.ShouldBeNull();
    }

    [Test]
    public void Update_AListenerOutOfEveryRadius_StartsNothing()
    {
        Sink sink = new();
        SoundscapeSystem system = Playing(opens: true);

        system.Update(sink, (0f, 0f, 9000f), Right, now: 1d);
        system.Update(sink, (0f, 0f, 9000f), Right, now: 2d);

        sink.Played.ShouldBeEmpty();
    }

    private static SoundscapeSystem Playing(bool opens, global::System.Action? onAsk = null)
    {
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(path => path switch
        {
            "scripts/soundscapes_manifest.txt" => global::System.Text.Encoding.UTF8.GetBytes(
                "\"soundscapes_manifest\"\n{\n    \"file\"    \"scripts/soundscapes_test.txt\"\n}\n"),
            "scripts/soundscapes_test.txt" => global::System.Text.Encoding.UTF8.GetBytes(
                "\"test.room\"\n{\n    \"playlooping\"\n    {\n        \"volume\"    \"0.5\"\n" +
                "        \"wave\"    \"ambient/room.wav\"\n    }\n}\n"),
            _ => null,
        });

        SoundscapePlacements placements = SoundscapePlacements.From(
            Tf2DemoSalvage.Content.Bsp.BspEntities.Parse(global::System.Text.Encoding.UTF8.GetBytes(
                "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.room\"\n\"origin\" \"0 0 0\"\n\"radius\" \"500\"\n}\n")),
            catalog);

        return new SoundscapeSystem(
            new ActiveLoops(),
            _ =>
            {
                onAsk?.Invoke();
                return opens ? new SoundSample(44100, 1, new float[441]) : null;
            },
            NullLogger.Instance)
        {
            Catalog = catalog,
            Placements = placements,
        };
    }

    private static readonly (float X, float Y, float Z) Origin = (0f, 0f, 0f);
    private static readonly (float X, float Y, float Z) Right = (1f, 0f, 0f);

    private static SoundscapeSystem System() =>
        new(new ActiveLoops(), _ => null, NullLogger.Instance);

    /// <summary>A sink that records rather than making a sound.</summary>
    /// <remarks>
    /// The whole reason <see cref="IAudioSink"/> exists: three methods is all this system uses, so
    /// a stand-in is nine lines rather than an OpenAL device.
    /// </remarks>
    private sealed class Sink : IAudioSink
    {
        public List<(int Entity, int Channel)> Played { get; } = [];

        public List<(int Entity, int Channel)> Silenced { get; } = [];

        public void Play(
            SoundSample sample,
            float leftPan,
            float rightPan,
            float gain,
            float pitch,
            int entity,
            int channel) => Played.Add((entity, channel));

        public int Regained { get; private set; }

        public bool SetGain(int entity, int channel, float gain)
        {
            Regained++;
            return true;
        }

        public void Silence(int entity, int channel) => Silenced.Add((entity, channel));

        public void SilenceAll() => Silenced.Add((AllEntities, AllChannels));

        public int Reclaim() => 0;

        /// <summary>Marks a SilenceAll in <see cref="Silenced"/>, so a seek is distinguishable.</summary>
        public const int AllEntities = int.MinValue;

        /// <summary>The channel half of that marker.</summary>
        public const int AllChannels = int.MinValue;
    }
}
