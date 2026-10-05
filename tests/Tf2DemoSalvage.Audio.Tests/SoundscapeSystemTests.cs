using System.Collections.Generic;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

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
        SoundscapeVoice voice = new(1, "ambient/indoors.wav", 0.5f, 100, Position: null, SoundLevel: 75);

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
            100,
            Position: (1000f, 0f, 0f),
            SoundLevel: 70);

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

    /// <remarks>
    /// **The trigger's touch reaches the mixer** (B483): a triggerable 9,000 units off with a 1-unit radius cannot win the
    /// contest, so only its trigger can make it current. Touching starts its loop; the control, the same map with no
    /// touch test supplied, starts nothing.
    /// </remarks>
    [Test]
    public void Update_InsideATriggerOfAFarTriggerable_StartsItsLoop()
    {
        Sink touched = new();
        Sink control = new();

        SoundscapeSystem system = Triggered();
        system.Inside = (_, _) => true;
        system.Update(touched, Origin, Right, now: 1d);

        Triggered().Update(control, Origin, Right, now: 1d);

        touched.Played.ShouldHaveSingleItem();
        control.Played.ShouldBeEmpty();
    }

    /// <remarks>
    /// **Through the recorder's eyes the client hears its networked params, not a simulation.**
    /// <c>C_SoundscapeSystem::UpdateAudioParams</c> (<c>c_soundscape.cpp:555-576</c>) copies <c>m_audio</c> when the
    /// index or the entity changed and starts the soundscape only for <c>entIndex &gt; 0</c> and a valid index. Here the
    /// listener stands inside the radius of `test.room`, which the contest would choose — and the recorded params name
    /// no entity, so nothing starts. Then they name entity 3, index 0, slot 0 at (5, 6, 7), heard from 9,000 units
    /// up where the contest reaches nothing: it starts, as that placement.
    /// </remarks>
    [Test]
    public void Update_WithRecordedParams_PlaysThemInsteadOfTheContest()
    {
        Sink sink = new();
        SoundscapeSystem system = Playing(opens: true);
        (float X, float Y, float Z)?[] slots = [(5f, 6f, 7f), null, null, null, null, null, null, null];

        system.Update(sink, Origin, Right, now: 1d, recorded: new SceneSoundscape(0, 0, slots, 0));

        sink.Played.ShouldBeEmpty("entIndex 0 starts nothing, whatever the contest would choose");
        system.Current.ShouldBeNull();

        system.Update(sink, (0f, 0f, 9000f), Right, now: 1.05d, recorded: new SceneSoundscape(0, 1, slots, 3));

        sink.Played.ShouldHaveSingleItem();
        SoundscapePlacement current = system.Current.ShouldNotBeNull();
        current.Id.ShouldBe(2, "entity 3 is the third soundscape the server listed, id 2 here");
        current.Index.ShouldBe(0);
        current.Positions[0].ShouldBe((5f, 6f, 7f));
    }

    /// <remarks>A slot whose <c>localBits</c> bit is clear is unused (<c>c_soundscape.cpp:797-804</c>), whatever it holds.</remarks>
    [Test]
    public void Recorded_ASlotWithItsBitClear_IsNoPosition()
    {
        (float X, float Y, float Z)?[] slots = [(1f, 2f, 3f), (4f, 5f, 6f), null, null, null, null, null, null];

        SoundscapePlacement placed = SoundscapeSystem.Recorded(new SceneSoundscape(4, 0b10, slots, 7), catalog: null)
            .ShouldNotBeNull();

        placed.Positions[0].ShouldBeNull();
        placed.Positions[1].ShouldBe((4f, 5f, 6f));
        placed.Id.ShouldBe(6);
        placed.Index.ShouldBe(4);
    }

    private static SoundscapeSystem Triggered()
    {
        SoundscapeSystem system = Playing(opens: true);

        system.Placements = SoundscapePlacements.From(
            Tf2DemoSalvage.Content.Bsp.BspEntities.Parse(global::System.Text.Encoding.UTF8.GetBytes(
                "{\n\"classname\" \"env_soundscape_triggerable\"\n\"targetname\" \"far\"\n\"soundscape\" \"test.room\"\n" +
                "\"origin\" \"9000 0 0\"\n\"radius\" \"1\"\n}\n" +
                "{\n\"classname\" \"trigger_soundscape\"\n\"model\" \"*1\"\n\"soundscape\" \"far\"\n}\n")),
            system.Catalog!,
            models:
            [
                new(default, default, default, 0, 0, 0),
                new(default, default, default, 7, 0, 0),
            ]);

        return system;
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

        public bool SetPitch(int entity, int channel, float pitch) => true;

        public void Silence(int entity, int channel) => Silenced.Add((entity, channel));

        public void Silence(int entity, int channel, SoundSample sample) => Silenced.Add((entity, channel));

        public void SilenceAll() => Silenced.Add((AllEntities, AllChannels));

        public int Reclaim() => 0;

        /// <summary>Marks a SilenceAll in <see cref="Silenced"/>, so a seek is distinguishable.</summary>
        public const int AllEntities = int.MinValue;

        /// <summary>The channel half of that marker.</summary>
        public const int AllChannels = int.MinValue;
    }
}
