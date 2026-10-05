using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>Reading soundscript entries from hand-written text (B217).</summary>
/// <remarks>
/// The conformance suite's parse tests read the shipped files and ignore themselves without TF2, so
/// the mutation box never saw an entry read end to end — 18 survivors in <see cref="SoundScript"/>.
/// </remarks>
public sealed class SoundScriptTests
{
    private static IReadOnlyDictionary<string, SoundScriptEntry> Read(string text) =>
        SoundScript.Read(Encoding.UTF8.GetBytes(text));

    [Test]
    public void Read_AnEntryStatingEveryKey_CarriesEachValue()
    {
        SoundScriptEntry entry = Read(
            "\"Test.Shot\"\n{\n\"channel\" \"CHAN_WEAPON\"\n\"volume\" \"0.5\"\n\"pitch\" \"110,90\"\n" +
            "\"soundlevel\" \"SNDLVL_80dB\"\n\"wave\" \"weapons/shot.wav\"\n}\n")["Test.Shot"];

        entry.Channel.ShouldBe(1);
        entry.Volume.ShouldBe(new SoundRange(0.5f, 0.5f));
        entry.Pitch.ShouldBe(new SoundRange(110f, 346f), "a reversed range is NOT ordered; its -20 wraps to the byte 236 (B487)");
        entry.SoundLevel.ShouldBe(new SoundRange(80f, 80f));
        entry.Waves.ShouldBe(["weapons/shot.wav"]);
    }

    [Test]
    public void Read_TwoEntries_KeepsBothAndResetsBetweenThem()
    {
        IReadOnlyDictionary<string, SoundScriptEntry> entries = Read(
            "\"A\"\n{\n\"channel\" \"CHAN_VOICE\"\n\"wave\" \"a.wav\"\n}\n" +
            "\"B\"\n{\n\"wave\" \"b.wav\"\n}\n");

        entries.Count.ShouldBe(2);
        entries["A"].Channel.ShouldBe(2);
        entries["B"].Channel.ShouldBe(0, "the second entry does not inherit the first's channel");
        entries["B"].Waves.ShouldBe(["b.wav"]);
    }

    [Test]
    public void Read_AnEntryWithNoWave_IsDropped()
    {
        Read("\"Silent\"\n{\n\"volume\" \"1\"\n}\n\"Loud\"\n{\n\"wave\" \"x.wav\"\n}\n")
            .Keys.ShouldBe(["Loud"]);
    }

    [Test]
    public void Read_ARangeWithAWordForAHalf_ReadsTheWordAsZero()
    {
        // atof of a word is 0 and nothing falls back (B487): "0.5,loud" is start 0.5, range -0.5, so the engine
        // draws RandomFloat( 0.5, 0 ); "fast,90" is start 0, range 90.
        SoundScriptEntry entry = Read(
            "\"E\"\n{\n\"volume\" \"0.5,loud\"\n\"pitch\" \"fast,90\"\n\"wave\" \"x.wav\"\n}\n")["E"];

        entry.Volume.ShouldBe(new SoundRange(0.5f, 0f));
        entry.Pitch.ShouldBe(new SoundRange(0f, 90f));
    }

    [Test]
    public void Rate_APitchOrZero_IsItsShareOfPitchNormOrUnshifted()
    {
        // The one conversion every sink caller shares (B462): a soundscape loop at pitch 50 plays at half rate.
        SoundScript.Rate(50).ShouldBe(0.5f);
        SoundScript.Rate(150).ShouldBe(1.5f);
        SoundScript.Rate(0).ShouldBe(1f, "no sink can play at rate zero, so it is unshifted");
    }

    [Test]
    public void Channel_EveryNameTheHeaderLists_HasItsNumber()
    {
        string[] names =
        [
            "CHAN_AUTO", "CHAN_WEAPON", "CHAN_VOICE", "CHAN_ITEM", "CHAN_BODY",
            "CHAN_STREAM", "CHAN_STATIC", "CHAN_VOICE2", "CHAN_VOICE_BASE",
        ];

        for (int i = 0; i < names.Length; i++)
        {
            SoundScript.Channel(names[i]).ShouldBe(i, names[i]);
        }
    }
}
