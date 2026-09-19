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
        entry.Pitch.ShouldBe(new SoundRange(90f, 110f), "a reversed range is ordered");
        entry.SoundLevel.ShouldBe(80);
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
    public void Read_ARangeWithAnUnreadableHalf_FallsBackToNormal()
    {
        SoundScriptEntry entry = Read(
            "\"E\"\n{\n\"volume\" \"0.5,loud\"\n\"pitch\" \"fast,90\"\n\"wave\" \"x.wav\"\n}\n")["E"];

        entry.Volume.ShouldBe(new SoundRange(1f, 1f));
        entry.Pitch.ShouldBe(new SoundRange(100f, 100f));
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
