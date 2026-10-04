using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A <c>playlooping</c> rule as the CLIENT plays it, read from <c>game/client/c_soundscape.cpp</c> (B462, B463).</summary>
/// <remarks>
/// **Every case is one branch of three functions**, cited by line and written before the code it checks:
/// <c>ProcessPlayLooping</c> (<c>:722-807</c>), which reads the rule; <c>AddLoopingSound</c> (<c>:1103-1197</c>), which
/// decides whether a loop already sounding can carry it; and <c>UpdateAudioParams</c> (<c>:555-576</c>), which decides
/// whether anything starts at all.
///
/// Census of the shipped scripts, 2026-10-04 (all 40 files the manifest lists): 543 `playlooping` rules; 2 with an
/// interval volume (`Halloween.Outside`, `Halloween_sf14.Outside`); 8 at a pitch other than 100 (both Halloween sets,
/// `Halloween.Underworld`, `underground_soho`); 130 positioned, every one with an `attenuation` or `soundlevel`; 80
/// `soundlevel` keys and 51 `attenuation` keys.
/// </remarks>
public sealed class SoundscapeLoopingConformanceTests
{
    private static (float X, float Y, float Z)? Nowhere => null;

    // ---- ProcessPlayLooping: reading the rule ----------------------------------------------------------------------

    [Test]
    public void Load_APlayloopingWithNoVolume_HasAZeroVolume()
    {
        // `float volume = 0;` (:724) — and only a `volume` key changes it.
        Rule("\"wave\" \"ambient/hum.wav\"").Volume.ShouldBe(new Interval(0f, 0f));
    }

    [Test]
    public void Load_IntervalValues_AreKeptAsIntervals()
    {
        // `RandomInterval( ReadInterval( pKey->GetString() ) )` for volume (:735) and pitch (:739). These failed to
        // parse as numbers and fell back to 1.0 and 100.
        SoundscapeSound sound = Rule("\"wave\" \"ambient/wind.wav\"\n\"volume\" \".2, .3\"\n\"pitch\" \"90, 110\"");

        sound.Volume.ShouldBe(Interval.Read(".2, .3"));
        sound.Pitch.ShouldBe(new Interval(90f, 20f));
    }

    [Test]
    public void Load_NoPitch_IsPitchNorm()
    {
        // `int pitch = PITCH_NORM;` (:727).
        Rule("\"wave\" \"ambient/hum.wav\"").Pitch.ShouldBe(new Interval(100f, 0f));
    }

    [Test]
    public void Load_NoAttenuationOrSoundlevel_DrawsAttnNormAsSeventyFive()
    {
        // `soundlevel_t soundlevel = ATTN_TO_SNDLVL(ATTN_NORM);` (:725): (int)(50 + 20 / 0.8f) = 75 by arithmetic.
        Rule("\"wave\" \"ambient/hum.wav\"").Level.Draw(new UniformRandomStream()).ShouldBe(75);
    }

    [Test]
    public void Load_ASoundlevelName_IsThatLevel()
    {
        // `if ( !Q_strncasecmp( pKey->GetString(), "SNDLVL_", ... ) ) soundlevel = TextToSoundLevel(...)` (:755-758).
        // The census's commonest: 34 of the 80 `soundlevel` keys are SNDLVL_75dB.
        Rule("\"wave\" \"ambient/hum.wav\"\n\"soundlevel\" \"sndlvl_85dB\"").Level.Draw(new UniformRandomStream()).ShouldBe(85);
    }

    [Test]
    public void Load_ANumericSoundlevel_IsTheIntervalTruncated()
    {
        // `soundlevel = (soundlevel_t)((int)RandomInterval( ReadInterval( ... ) ))` (:761).
        Rule("\"wave\" \"ambient/hum.wav\"\n\"soundlevel\" \"70.9\"").Level.Draw(new UniformRandomStream()).ShouldBe(70);
    }

    [Test]
    public void Load_AnAttenuation_IsAttnToSndlvlOfIt()
    {
        // `soundlevel = ATTN_TO_SNDLVL( RandomInterval( ReadInterval( ... ) ) )` (:751): (int)(50 + 20 / 0.7f) = 78.
        Rule("\"wave\" \"ambient/hum.wav\"\n\"attenuation\" \"0.7\"").Level.Draw(new UniformRandomStream()).ShouldBe(78);
    }

    [Test]
    public void Load_AttenuationAfterSoundlevel_WinsBecauseTheKeysRunInOrder()
    {
        // Both keys write the one `soundlevel` variable in the key loop, so the later one is what plays.
        Rule("\"wave\" \"a.wav\"\n\"soundlevel\" \"SNDLVL_90dB\"\n\"attenuation\" \"1\"")
            .Level.Draw(new UniformRandomStream()).ShouldBe(70);
        Rule("\"wave\" \"a.wav\"\n\"attenuation\" \"1\"\n\"soundlevel\" \"SNDLVL_90dB\"")
            .Level.Draw(new UniformRandomStream()).ShouldBe(90);
    }

    // ---- ProcessPlayLooping: what it hands on --------------------------------------------------------------------

    [Test]
    public void MoveTo_ALoopWithZeroVolume_IsNotStarted()
    {
        // `if ( volume != 0 && pSoundName != NULL )` (:790). It played at full volume, because the reader defaulted to 1.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(
            Placement(0, 1),
            Scape(1, Sound("ambient/silent.wav", 0f), Sound("ambient/hum.wav", 0.5f)));

        mixer.Advance(0f).ShouldHaveSingleItem().Wave.ShouldBe("ambient/hum.wav");
    }

    [Test]
    public void MoveTo_ALoopsPitch_IsItsVoicesPitch()
    {
        // `AddLoopingAmbient( pSoundName, volume, pitch )` (:793) and `ep.m_nPitch = pitch` (:1171). Every loop played at
        // rate 1. `Halloween.Outside` loops at pitch 50.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 1), Scape(1, Sound("ambient/wind.wav", 1f, pitch: 50)));

        mixer.Advance(0f).ShouldHaveSingleItem().Pitch.ShouldBe(50);
    }

    [Test]
    public void MoveTo_AnIntervalVolume_IsOneDrawOverItsRange()
    {
        // `volume = params.masterVolume * RandomInterval( ... )` (:735), master 1.0 at the top level (:610). *Interpolated:*
        // the draw — the engine's comes from its global stream; this one is Valve's generator seeded by the placement.
        SoundscapeMixer mixer = new();
        SoundscapeSound wind = new("ambient/wind.wav", Interval.Read(".2, .3"), new Interval(100f, 0f), null, SoundscapeLevel.Normal);

        mixer.MoveTo(Placement(5, 1), Scape(1, wind));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        UniformRandomStream control = new();
        control.SetSeed(5);

        float target = mixer.Advance(0f).ShouldHaveSingleItem().Volume;

        // The range is `atof( " .3" ) - tmp.start` in double, narrowed once (`interval.cpp:38`, B479).
        target.ShouldBe(0.2f + control.RandomFloat(0f, (float)(0.3d - 0.2f)));
        target.ShouldBeInRange(0.2f, 0.3f);
        target.ShouldNotBe(0.2f, "a draw that happened to be zero could not tell drawing from not");
    }

    [Test]
    public void MoveTo_APositionedLoop_CarriesItsSoundlevelAndAnAmbientOneSoundlevelNorm()
    {
        // `AddLoopingSound( ..., false, volume, soundlevel, pitch, localSound[N] )` (:803) for a positioned loop;
        // `AddLoopingAmbient` passes `SNDLVL_NORM` (:1097) whatever the script said.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(
            Placement(0, 1, (100f, 0f, 0f)),
            Scape(
                1,
                Sound("ambient/placed.wav", 1f, position: 0, level: "SNDLVL_95dB"),
                Sound("ambient/room.wav", 1f, level: "SNDLVL_95dB")));

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        voices.Single(voice => voice.Position is not null).SoundLevel.ShouldBe(95);
        voices.Single(voice => voice.Position is null).SoundLevel.ShouldBe(75);
    }

    [Test]
    public void MoveTo_ANegativePosition_PlaysAsAnAmbientLoop()
    {
        // `positionIndex = startingPosition + GetInt()` (:747); `if ( positionIndex < 0 ) positionIndex =
        // ambientPositionOverride` (:769-772), -1 at the top level; `if ( positionIndex < 0 ) AddLoopingAmbient` (:792).
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 1), Scape(1, Sound("ambient/hum.wav", 1f, position: -1)));

        mixer.Advance(0f).ShouldHaveSingleItem().Position.ShouldBeNull();
    }

    [Test]
    public void MoveTo_ASlotTheMapDidNotSet_SuppressesTheLoop()
    {
        // `if ( positionIndex > 31 || !(m_params.localBits & (1<<positionIndex)) ) return;` (:797-801). A slot below a
        // set one is not set: the list keeps its index (B464), so the gap is a null rather than a shift.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(
            Placement(0, 1, Nowhere, (200f, 0f, 0f)),
            Scape(1, Sound("ambient/zero.wav", 1f, position: 0), Sound("ambient/one.wav", 1f, position: 1)));

        SoundscapeVoice only = mixer.Advance(0f).ShouldHaveSingleItem();

        only.Wave.ShouldBe("ambient/one.wav");
        only.Position.ShouldBe((200f, 0f, 0f));
    }

    // ---- AddLoopingSound: reusing what is already sounding ------------------------------------------------------

    [Test]
    public void MoveTo_AWaveTwoSoundscapesShare_KeepsItsVoiceAndItsVolume()
    {
        // `if ( sound.id != m_loopingSoundId && sound.pitch == pitch && !Q_strcasecmp( pSoundName, sound.pWaveName ) )`
        // then `if ( isAmbient == true && sound.isAmbient == true ) { // reuse this sound` (:1112-1121) — with no
        // condition on which soundscape owned it. It only reused within ONE soundscape index.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 41), Scape(41, Sound("ambient/wind.wav", 1f)));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        int key = mixer.Advance(0f).Single().Key;

        mixer.MoveTo(Placement(1, 42), Scape(42, Sound("AMBIENT/WIND.WAV", 0.5f), Sound("ambient/birds.wav", 1f)));

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        voices.Count.ShouldBe(2, "the shared wave is one voice, not one fading out under another fading in");
        SoundscapeVoice wind = voices.Single(voice => voice.Key == key);
        wind.Volume.ShouldBe(1f, "it fades from where it stands toward 0.5, not from silence");

        mixer.Advance(SoundscapeMixer.FadeSeconds);
        mixer.Advance(0f).Single(voice => voice.Key == key).Volume.ShouldBe(0.5f);
    }

    [Test]
    public void MoveTo_TheSameWaveAtAnotherPitch_IsANewVoiceFromSilence()
    {
        // The control for the case above: `sound.pitch == pitch` (:1112) is half the match.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 41), Scape(41, Sound("ambient/wind.wav", 1f)));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(Placement(1, 42), Scape(42, Sound("ambient/wind.wav", 1f, pitch: 90)));

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        voices.Count.ShouldBe(2);
        voices.Single(voice => voice.Pitch == 90).Volume.ShouldBe(0f);
    }

    [Test]
    public void MoveTo_APositionedLoopThatMoved_RestartsAtOnceAtTheVolumeItHad()
    {
        // `else { StopLoopingSound(sound); pSoundSlot = &sound; bForceSoundUpdate = true; }` (:1130-1144): the old
        // sound stops immediately and its slot — its current volume included — restarts at the new position. Not a
        // crossfade, despite the comment above it; Valve's note says why: alternating fade commands naming one sound
        // *"will occasionally cause the sound to vanish entirely"*.
        SoundscapeMixer mixer = new();
        Soundscape hum = Scape(1, Sound("ambient/machine_hum.wav", 1f, position: 0));

        mixer.MoveTo(Placement(0, 1, (100f, 0f, 0f)), hum);
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        int key = mixer.Advance(0f).Single().Key;

        mixer.MoveTo(Placement(1, 1, (900f, 0f, 0f)), hum);

        SoundscapeVoice moved = mixer.Advance(0f).ShouldHaveSingleItem();

        moved.Key.ShouldNotBe(key, "a new key is how the sink is told to stop the old sound and start this one");
        moved.Position.ShouldBe((900f, 0f, 0f));
        moved.Volume.ShouldBe(1f);
    }

    [Test]
    public void MoveTo_APositionedLoopThatDidNotMove_KeepsItsVoice()
    {
        // `if ( VectorsAreEqual( position, sound.position, 0.1f ) ) { // reuse this sound` (:1124-1129).
        SoundscapeMixer mixer = new();
        Soundscape hum = Scape(1, Sound("ambient/machine_hum.wav", 1f, position: 0));

        mixer.MoveTo(Placement(0, 1, (100f, 0f, 0f)), hum);

        int key = mixer.Advance(0f).Single().Key;

        mixer.MoveTo(Placement(1, 1, (100.1f, -0.1f, 0f)), hum);

        mixer.Advance(0f).ShouldHaveSingleItem().Key.ShouldBe(key, "within 0.1 on every axis is the same place");

        mixer.MoveTo(Placement(2, 1, (100.25f, 0f, 0f)), hum);

        mixer.Advance(0f).ShouldHaveSingleItem().Key.ShouldNotBe(key, "the control: 0.25 off is another place");
    }

    [Test]
    public void MoveTo_ANewPositionedLoop_StartsAtFivePercent()
    {
        // `// non-ambients at 0 volume are culled, so start at 0.05` ... `volumeCurrent = 0.05` (:1167-1178); an ambient
        // starts at 0 (:1160-1164).
        SoundscapeMixer mixer = new();

        mixer.MoveTo(
            Placement(0, 1, (100f, 0f, 0f)),
            Scape(1, Sound("ambient/placed.wav", 1f, position: 0), Sound("ambient/room.wav", 1f)));

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        voices.Single(voice => voice.Position is not null).Volume.ShouldBe(0.05f);
        voices.Single(voice => voice.Position is null).Volume.ShouldBe(0f);
    }

    [Test]
    public void MoveTo_PositionedLoopsOfOneWave_ArePairedFromTheEndOfTheList()
    {
        // `int soundSlot = m_loopingSounds.Count() - 1; while ( soundSlot >= 0 ) { ... soundSlot--; }` (:1106-1152)
        // scans BACKWARDS, and the new loops are added in script order — so the first new loop meets the LAST old one.
        // Two loops of one wave at targets that did not move are therefore crossed over and both restart: the engine's
        // "Will always restart/crossfade positional sounds" (:1111) is this order, not a rule.
        SoundscapeMixer mixer = new();
        Soundscape pair = Scape(
            1,
            Sound("ambient/machine_hum.wav", 1f, position: 0),
            Sound("ambient/machine_hum.wav", 1f, position: 1));

        mixer.MoveTo(Placement(0, 1, (100f, 0f, 0f), (200f, 0f, 0f)), pair);

        int[] before = [.. mixer.Advance(0f).Select(voice => voice.Key)];

        mixer.MoveTo(Placement(1, 1, (100f, 0f, 0f), (200f, 0f, 0f)), pair);

        IReadOnlyList<SoundscapeVoice> after = mixer.Advance(0f);

        after.Count.ShouldBe(2);
        after.Select(voice => voice.Key).Intersect(before).ShouldBeEmpty("both were restarted, crossed over");
    }

    [Test]
    public void Advance_ALoopCancelledBeforeItRose_StaysToBeReclaimed()
    {
        // `if ( sound.volumeCurrent != sound.volumeTarget )` (:512) guards the removal as well as the fade, so a slot
        // started at 0 and zeroed before any time passed is never removed — and the next soundscape playing its wave
        // reclaims it rather than adding another.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 1), Scape(1, Sound("ambient/wind.wav", 1f)));

        int key = mixer.Advance(0f).Single().Key;

        mixer.MoveTo(Placement(1, 2), Scape(2, Sound("ambient/birds.wav", 1f)));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.Advance(0f).Single(voice => voice.Key == key).Volume.ShouldBe(0f, "silent, and still in the list");

        mixer.MoveTo(Placement(2, 3), Scape(3, Sound("ambient/wind.wav", 1f)));

        mixer.Advance(0f).Count(voice => voice.Wave == "ambient/wind.wav").ShouldBe(1);
        mixer.Advance(0f).Single(voice => voice.Wave == "ambient/wind.wav").Key.ShouldBe(key);
    }

    [Test]
    public void Advance_AFinishedLoop_IsReplacedByTheLastOneInTheList()
    {
        // `m_loopingSounds.FastRemove( fadeCount )` (:519) moves the LAST element into the hole, and the backward scan
        // in `AddLoopingSound` then meets the two copies of a wave in the swapped order. Three loops, the first quieter
        // so it reaches zero first: [quiet, hum A, hum B] becomes [hum B, hum A], and the next hum reclaims A.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(
            Placement(0, 1),
            Scape(1, Sound("ambient/quiet.wav", 0.1f), Sound("ambient/hum.wav", 1f), Sound("ambient/hum.wav", 1f)));

        IReadOnlyList<SoundscapeVoice> risen = mixer.Advance(SoundscapeMixer.FadeSeconds);
        int first = risen[1].Key;
        int second = risen[2].Key;

        mixer.MoveTo(Placement(1, 2), Scape(2));
        mixer.Advance(0.1f * SoundscapeMixer.FadeSeconds).Count.ShouldBe(2, "the quiet loop has finished");

        mixer.MoveTo(Placement(2, 3), Scape(3, Sound("ambient/hum.wav", 1f)));

        // Both hums stand at 0.9; a tenth of the fade later the reclaimed one is back at 1 and the other at 0.8.
        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0.1f * SoundscapeMixer.FadeSeconds);

        voices.Single(voice => voice.Key == first).Volume.ShouldBe(1f, 0.0001d, "the first hum was reclaimed");
        voices.Single(voice => voice.Key == second).Volume.ShouldBe(0.8f, 0.0001d, "the second is fading out");
    }

    // ---- UpdateAudioParams: whether anything starts --------------------------------------------------------------

    [Test]
    public void MoveTo_AnIndexTheCatalogLacks_LeavesTheLoopsPlaying()
    {
        // `if ( audio.entIndex > 0 && audio.soundscapeIndex >= 0 && audio.soundscapeIndex < m_soundscapes.Count() )
        // StartNewSoundscape(...)` (:562-566) — otherwise only `m_params` changes, and nothing fades.
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0, 1), Scape(1, Sound("ambient/room.wav", 1f)));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(Placement(1, -1), null);
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.Advance(0f).ShouldHaveSingleItem().Volume.ShouldBe(1f);
        mixer.Current.ShouldNotBeNull().Id.ShouldBe(1, "the params did change, so the next placement compares against this one");
    }

    // ---- What reaches the sink ---------------------------------------------------------------------------------

    [Test]
    public void Update_ALoopAtPitchFifty_PlaysAtHalfRate()
    {
        PitchSink sink = new();

        SoundscapeCatalog catalog = SoundscapeCatalog.Load(path => path switch
        {
            "scripts/soundscapes_manifest.txt" => Encoding.UTF8.GetBytes(
                "\"soundscapes_manifest\"\n{\n    \"file\"    \"scripts/soundscapes_test.txt\"\n}\n"),
            "scripts/soundscapes_test.txt" => Encoding.UTF8.GetBytes(
                "\"test.room\"\n{\n    \"playlooping\"\n    {\n        \"volume\"    \"1\"\n        \"pitch\"    \"50\"\n" +
                "        \"wave\"    \"ambient/room.wav\"\n    }\n}\n"),
            _ => null,
        });

        SoundscapeSystem system = new(new ActiveLoops(), _ => new SoundSample(44100, 1, new float[441]), NullLogger.Instance)
        {
            Catalog = catalog,
            Placements = SoundscapePlacements.From(
                Tf2DemoSalvage.Content.Bsp.BspEntities.Parse(Encoding.UTF8.GetBytes(
                    "{\n\"classname\" \"env_soundscape\"\n\"soundscape\" \"test.room\"\n\"origin\" \"0 0 0\"\n}\n")),
                catalog),
        };

        system.Update(sink, (0f, 0f, 0f), (1f, 0f, 0f), now: 1d);

        sink.Pitches.ShouldHaveSingleItem().ShouldBe(0.5f);
    }

    [Test]
    public void GainOf_APositionedVoice_FallsOffAtItsOwnSoundlevel()
    {
        // The soundlevel is what positions a loop's falloff (`ep.m_SoundLevel = soundlevel`, :1170). A positioned loop
        // with no level key used to have NO falloff, because the missing attenuation was read as level 0.
        SoundscapeVoice near = new(1, "ambient/hum.wav", 1f, 100, (500f, 0f, 0f), 75);
        SoundscapeVoice loud = near with { SoundLevel = 95 };

        SoundscapeSystem.GainOf(near, (0f, 0f, 0f)).ShouldBe(SoundGain.AtDistance(75, 500f));
        SoundscapeSystem.GainOf(loud, (0f, 0f, 0f)).ShouldBe(SoundGain.AtDistance(95, 500f));
        SoundGain.AtDistance(75, 500f).ShouldBeLessThan(SoundGain.AtDistance(95, 500f), "the two levels must differ here");
    }

    // ---- fixtures ------------------------------------------------------------------------------------------------

    /// <summary>The one loop a single-rule soundscape script declares, read by the production reader.</summary>
    private static SoundscapeSound Rule(string keys) =>
        SoundscapeCatalog.Load(path => path switch
        {
            "scripts/soundscapes_manifest.txt" => Encoding.UTF8.GetBytes(
                "\"soundscapes_manifest\"\n{\n    \"file\"    \"scripts/soundscapes_test.txt\"\n}\n"),
            "scripts/soundscapes_test.txt" => Encoding.UTF8.GetBytes(
                $"\"test.room\"\n{{\n    \"playlooping\"\n    {{\n{keys}\n    }}\n}}\n"),
            _ => null,
        }).At(0).ShouldNotBeNull().Looping.ShouldHaveSingleItem();

    private static SoundscapeSound Sound(string wave, float volume, int pitch = 100, int? position = null, string? level = null) =>
        new(
            wave,
            new Interval(volume, 0f),
            new Interval(pitch, 0f),
            position,
            level is null ? SoundscapeLevel.Normal : new SoundscapeLevel(new Interval(SoundScript.SoundLevel(level), 0f), false));

    private static Soundscape Scape(int index, params SoundscapeSound[] sounds) =>
        new($"test.{index}", 0, sounds, []);

    private static SoundscapePlacement Placement(int id, int index, params (float X, float Y, float Z)?[] positions) =>
        new(id, $"test.{index}", index, 0f, 0f, 0f, -1f, positions);

    /// <summary>A sink that records the rate each sound starts at.</summary>
    private sealed class PitchSink : IAudioSink
    {
        public List<float> Pitches { get; } = [];

        public void Play(SoundSample sample, float leftPan, float rightPan, float gain, float pitch, int entity, int channel) =>
            Pitches.Add(pitch);

        public bool SetGain(int entity, int channel, float gain) => true;

        public bool SetPitch(int entity, int channel, float pitch) => true;

        public void Silence(int entity, int channel)
        {
            // Nothing to record: this sink measures starts only.
        }

        public void Silence(int entity, int channel, SoundSample sample)
        {
            // Nothing to record: this sink measures starts only.
        }

        public void SilenceAll()
        {
            // Nothing to record: this sink measures starts only.
        }

        public int Reclaim() => 0;
    }
}
