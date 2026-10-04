using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Crossfading between soundscapes, which is the difference between ambience and clicking.
/// </summary>
/// <remarks>
/// **Every case here is one that would otherwise be found by listening**, and two of them sound like
/// a working viewer until you notice what is wrong: a fade that starts at full volume, and a
/// soundscape that never restarts when the listener moves to a different entity naming the same
/// one. cp_process has 21 entities all naming `Gorge.Inside`.
///
/// Device-free, so it runs where the gate runs.
/// </remarks>
public sealed class SoundscapeMixerTests
{
    private static SoundscapePlacement Placement(int id, int index = 42) =>
        new(id, "Gorge.Inside", index, 0f, 0f, 0f, -1f, [(100f, 0f, 0f), (200f, 0f, 0f)]);

    private static Soundscape Room(params float[] volumes) =>
        new(
            "Gorge.Inside",
            1,
            [.. volumes.Select((volume, at) => Loop($"ambient/{at}.wav", volume))],
            []);

    /// <summary>A soundscape whose wave no <see cref="Room"/> shares, so moving to it cannot reclaim a loop (B463).</summary>
    private static Soundscape OtherRoom(float volume) =>
        new("Gorge.Outside", 1, [Loop("ambient/outside.wav", volume)], []);

    private static SoundscapeSound Loop(string wave, float volume, int? position = null) =>
        new(wave, new Interval(volume, 0f), new Interval(100f, 0f), position, SoundscapeLevel.Normal);

    [Test]
    public void MoveTo_ANewSoundscape_FadesItInFromSilence()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));

        // **Zero at the moment it starts.** Starting at the target would make the new room arrive
        // instantly while the old one faded out — half a crossfade, and audible as a jump.
        IReadOnlyList<SoundscapeVoice> first = mixer.Advance(0f);

        first.Count.ShouldBe(1);
        first[0].Volume.ShouldBe(0f);

        // Half the fade time gets half the volume: Approach moves by seconds / FadeSeconds.
        SoundscapeVoice half = mixer.Advance(SoundscapeMixer.FadeSeconds / 2f).Single();

        half.Volume.ShouldBe(0.5f, 0.01d);

        // And it stops at the script's volume rather than overshooting.
        mixer.Advance(SoundscapeMixer.FadeSeconds).Single().Volume.ShouldBe(1f, 0.01d);
        mixer.Advance(SoundscapeMixer.FadeSeconds).Single().Volume.ShouldBe(1f, 0.01d);
    }

    [Test]
    public void MoveTo_AnotherSoundscape_OverlapsTheTwoWhileFading()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(Placement(1, index: 41), OtherRoom(1f));

        // **Both at once**, which is what a crossfade is. Replacing the set instead would be a
        // clean switch and audibly wrong on every threshold — and cp_process has 42 of them.
        IReadOnlyList<SoundscapeVoice> mid = mixer.Advance(SoundscapeMixer.FadeSeconds / 2f);

        mid.Count.ShouldBe(2, "the old loop should still be fading out as the new one rises");
        mid.Count(voice => voice.Volume > 0.4f && voice.Volume < 0.6f)
            .ShouldBe(2, "one falling through the middle, one rising through it");
    }

    [Test]
    public void Advance_AfterAFullFade_DropsTheOldVoices()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(Placement(1, index: 41), OtherRoom(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        // The old one has reached zero and is gone; only the new one remains. Keeping it would leak
        // a voice per threshold crossed, which on a full match is hundreds.
        mixer.Advance(0f).Count.ShouldBe(1);
        mixer.Count.ShouldBe(1);
    }

    [Test]
    public void MoveTo_TheSameSoundscapeAndEntity_ChangesNothing()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(Placement(0), Room(1f));

        // **The common case, asked every update.** Restarting here would retrigger the fade
        // continuously and hold the ambience near silence for ever — which is exactly what "the
        // hum is inaudible" sounds like.
        mixer.Advance(0f).Single().Volume.ShouldBe(1f, 0.01d);
        mixer.Count.ShouldBe(1);
    }

    [Test]
    public void MoveTo_TheSameSoundscapeFromAnEntityWithDifferentPositions_RestartsTheLoopThere()
    {
        SoundscapeMixer mixer = new();

        Soundscape positioned = new("Gorge.Inside", 1, [Loop("ambient/machine_hum.wav", 1f, position: 0)], []);

        mixer.MoveTo(Placement(0), positioned);
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        // Same soundscape index, different entity, and its `position0` is somewhere else — which is
        // the reason `UpdateAudioParams` keys on `entIndex` at all. The loop has to move, and a
        // loop moving means stopping it and starting it where it now belongs.
        //
        // **One voice, not two.** This expected a crossfade of the old and new copies, which is what the engine's
        // comment says and not what its code does: `StopLoopingSound` then a forced update of the same slot
        // (`c_soundscape.cpp:1130-1144`, B463).
        SoundscapePlacement elsewhere = new(
            1, "Gorge.Inside", 42, 0f, 0f, 0f, -1f, [(900f, 0f, 0f)]);

        mixer.MoveTo(elsewhere, positioned);

        mixer.Advance(0f).ShouldHaveSingleItem().Position.ShouldBe((900f, 0f, 0f), "restarted where it now belongs");
    }

    [Test]
    public void MoveTo_TheSameSoundscapeFromAnEntityWithNoPositions_KeepsPlaying()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        // **The map's ordinary case, and the whole of why the outdoor ambience was inaudible.**
        // `Gorge.Outside` is two UNpositioned loops, and cp_process supplies no position targets on
        // any of its 44 entities — measured twice over: the entity lump carries no `position0..7`
        // keys, and `localBits` is 0 in every soundscape sample of all three demos.
        //
        // So an entity change here carries no information: the loops are identical, at identical
        // volumes, at the listener. Restarting throws them away and fades in from zero — and the
        // selection crosses between co-named entities every few hundred milliseconds against a
        // three-second fade, so the ambience never once reached full volume. Measured on the live
        // viewer: placements 0, 7 and 10 alternating at the 250 ms selection interval.
        mixer.MoveTo(Placement(1), Room(1f));

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        voices.Count.ShouldBe(1, "an entity change with nothing to re-place is not a change");
        voices[0].Volume.ShouldBe(1f, 0.01d, "and it must not drop back into a fade");
    }

    [Test]
    public void MoveTo_APositionedLoop_PlaysAtTheEntitysTarget()
    {
        SoundscapeMixer mixer = new();

        Soundscape positioned = new(
            "Gorge.Inside",
            1,
            [
                Loop("ambient/at_listener.wav", 1f),
                Loop("ambient/at_one.wav", 1f, position: 1),
            ],
            []);

        mixer.MoveTo(Placement(0), positioned);

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        // A loop with no position plays at the listener; one with `position 1` plays at the
        // entity's second target. That is how a single soundscape covers a whole map.
        voices.Single(voice => voice.Wave.EndsWith("at_listener.wav", StringComparison.Ordinal))
            .Position.ShouldBeNull();

        voices.Single(voice => voice.Wave.EndsWith("at_one.wav", StringComparison.Ordinal))
            .Position.ShouldBe((200f, 0f, 0f));
    }

    [Test]
    public void MoveTo_ALoopWhosePositionTheMapDoesNotSupply_IsSuppressed()
    {
        SoundscapeMixer mixer = new();

        Soundscape positioned = new(
            "Gorge.Inside",
            1,
            [
                Loop("ambient/room_tone.wav", 1f),
                Loop("ambient/machine_hum.wav", 0.75f, position: 5),
            ],
            []);

        // A placement with only two targets, so position 5 does not exist — which is the ordinary
        // case: cp_process's env_soundscape entities carry no position keys at all.
        mixer.MoveTo(Placement(0), positioned);

        IReadOnlyList<SoundscapeVoice> voices = mixer.Advance(0f);

        // **Suppressed, not moved to the listener.** The engine returns early on an unavailable
        // position (`c_soundscape.cpp:797`). Playing it at the listener instead stacks it
        // unattenuated in the ear — seven copies of machine_hum on Gorge.Inside, which the owner
        // heard as the CPU sound drowning everything else.
        voices.Count.ShouldBe(1, "a loop with no available position must not play at all");
        voices[0].Wave.ShouldBe("ambient/room_tone.wav");
    }

    [Test]
    public void MoveTo_Null_FadesEverythingOut()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.MoveTo(null, null);
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        mixer.Advance(0f).ShouldBeEmpty("leaving every soundscape should end in silence");
        mixer.Current.ShouldBeNull();
    }

    [Test]
    public void Ended_VoicesThatStopped_AreReportedSoTheyCanBeSilenced()
    {
        SoundscapeMixer mixer = new();

        mixer.MoveTo(Placement(0), Room(1f));

        // Risen first: a loop cancelled while still at zero is never removed, as the engine never removes it
        // (`c_soundscape.cpp:512`, `SoundscapeLoopingConformanceTests`).
        IReadOnlyList<SoundscapeVoice> before = mixer.Advance(SoundscapeMixer.FadeSeconds);
        int[] keys = [.. before.Select(voice => voice.Key)];

        mixer.MoveTo(null, null);
        mixer.Advance(SoundscapeMixer.FadeSeconds);

        // The sink holds a voice until told otherwise, so a dropped fade has to be reported or the
        // sound plays for ever at the volume it last had.
        SoundscapeMixer.Ended(mixer.Advance(0f), keys).ShouldBe(keys);
    }
}
