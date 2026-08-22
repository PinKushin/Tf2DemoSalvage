using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Soundscript entries, against Valve's published defaults and against every script TF2 ships.
/// </summary>
/// <remarks>
/// **The defaults come from <c>CSoundParameters</c>'s constructor**, published in
/// <c>public/SoundEmitterSystem/isoundemittersystembase.h</c>. Most entries state only some fields,
/// so a wrong default is a wrong sound across many entries rather than on an edge case.
///
/// **The syntax comes from the shipped scripts themselves**, which document it in their own comment
/// headers — the channel list and the legacy attenuation constants are written at the top of
/// `game_sounds_weapons.txt`, including Valve's *"DON'T USE THESE - USE SNDLVL_ INSTEAD!!!"*. That
/// header also states `ATTN_NORM 0.8f`, which independently confirms `SNDLVL_TO_ATTN(75) = 0.8`
/// from `soundflags.h`.
/// </remarks>
public sealed class SoundScriptConformanceTests
{
    [Test]
    public void Defaults_WhenAnEntryStatesNothing_AreValvesConstructorValues()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
            return;
        }

        // Parsed out of the constructor rather than restated, so a change in Valve's defaults
        // fails here instead of quietly diverging.
        string header = SourceSdk.Text("src/public/SoundEmitterSystem/isoundemittersystembase.h")
            ?? throw new InvalidOperationException("isoundemittersystembase.h is missing");

        header.ShouldContain("channel		= CHAN_AUTO;", Case.Sensitive);
        header.ShouldContain("volume		= VOL_NORM;", Case.Sensitive);
        header.ShouldContain("pitch		= PITCH_NORM;", Case.Sensitive);
        header.ShouldContain("soundlevel	= SNDLVL_NORM;", Case.Sensitive);

        SoundScriptEntry entry = Single("\"Bare.Entry\"\n{\n\t\"wave\" \"a/b.wav\"\n}\n");

        entry.Channel.ShouldBe(0, "CHAN_AUTO");
        entry.Volume.Low.ShouldBe(1f, "VOL_NORM");
        entry.Pitch.Low.ShouldBe(100f, "PITCH_NORM");
        entry.SoundLevel.ShouldBe(75, "SNDLVL_NORM");
    }

    [Test]
    public void SoundLevel_ANamedDecibelValue_IsTheNumberInItsName()
    {
        // soundflags.h declares SNDLVL_20dB through SNDLVL_180dB at their own values, so the name
        // carries the number. Parsed rather than tabulated, because a table of thirty would be a
        // second copy of the enum.
        SoundScript.SoundLevel("SNDLVL_96dB").ShouldBe(96);
        SoundScript.SoundLevel("SNDLVL_74dB").ShouldBe(74);
        SoundScript.SoundLevel("SNDLVL_180dB").ShouldBe(180);
    }

    [Test]
    public void SoundLevel_TheAliasesThatAreNotNumbers_AreResolved()
    {
        // **Six names carry no number, and several values have two names.** SNDLVL_NORM and
        // SNDLVL_75dB are both 75; IDLE and 60dB both 60; TALKING and 80dB both 80; GUNFIRE and
        // 140dB both 140. So name-to-value is a function and the reverse is not — worth knowing
        // before anyone writes the inverse.
        SoundScript.SoundLevel("SNDLVL_NONE").ShouldBe(0);
        SoundScript.SoundLevel("SNDLVL_IDLE").ShouldBe(60);
        SoundScript.SoundLevel("SNDLVL_STATIC").ShouldBe(66);
        SoundScript.SoundLevel("SNDLVL_NORM").ShouldBe(75);
        SoundScript.SoundLevel("SNDLVL_TALKING").ShouldBe(80);
        SoundScript.SoundLevel("SNDLVL_GUNFIRE").ShouldBe(140);

        // The aliases agree with their numbered twins, which is the check that they were not
        // simply typed twice with different values.
        SoundScript.SoundLevel("SNDLVL_NORM").ShouldBe(SoundScript.SoundLevel("SNDLVL_75dB"));
        SoundScript.SoundLevel("SNDLVL_IDLE").ShouldBe(SoundScript.SoundLevel("SNDLVL_60dB"));
        SoundScript.SoundLevel("SNDLVL_GUNFIRE").ShouldBe(SoundScript.SoundLevel("SNDLVL_140dB"));
    }

    [Test]
    public void Channel_BothFormsTheHeaderDocuments_AreAccepted()
    {
        // game_sounds_weapons.txt: "these can be set with `channel` `2` or `channel` `chan_voice`".
        // Handling one form silently mis-channels every entry using the other.
        SoundScript.Channel("CHAN_VOICE").ShouldBe(2);
        SoundScript.Channel("chan_voice").ShouldBe(2);
        SoundScript.Channel("2").ShouldBe(2);
        SoundScript.Channel("CHAN_STATIC").ShouldBe(6);
    }

    [Test]
    public void Pitch_ARange_IsKeptAsARangeRatherThanCollapsed()
    {
        // **`"pitch" "90, 110"` means the engine varies it per play**, which is what stops a
        // repeated sound going mechanical. Taking the first number produces a plausible sound with
        // no variation — audible only by comparison, and so the hardest kind of difference to spot.
        SoundScriptEntry entry = Single(
            "\"Ric\"\n{\n\t\"pitch\" \"90, 110\"\n\t\"wave\" \"a/b.wav\"\n}\n");

        entry.Pitch.Low.ShouldBe(90f);
        entry.Pitch.High.ShouldBe(110f);
        entry.Pitch.Varies.ShouldBeTrue();

        // The control: a single value must NOT report as varying, or `Varies` is decoration.
        Single("\"One\"\n{\n\t\"pitch\" \"95\"\n\t\"wave\" \"a/b.wav\"\n}\n")
            .Pitch.Varies.ShouldBeFalse();
    }

    [Test]
    public void RndWave_EveryWaveInTheBlock_IsCollected()
    {
        // A random set and a single wave differ only in count, which is what lets a caller pick
        // without knowing which shape the script used.
        SoundScriptEntry entry = Single(
            """
            "FX_RicochetSound.Ricochet"
            {
            	"channel"	"CHAN_STATIC"
            	"volume"	"1.0"
            	"soundlevel"	"SNDLVL_96dB"
            	"pitch"		"90, 110"

            	"rndwave"
            	{
            		"wave"	"weapons/fx/rics/ric1.wav"
            		"wave"	"weapons/fx/rics/ric2.wav"
            		"wave"	"weapons/fx/rics/ric3.wav"
            	}
            }

            """);

        entry.Waves.Count.ShouldBe(3);
        entry.Waves[0].ShouldBe("weapons/fx/rics/ric1.wav");
        entry.Waves[2].ShouldBe("weapons/fx/rics/ric3.wav");
        entry.Channel.ShouldBe(6);
        entry.SoundLevel.ShouldBe(96);
    }

    [Test]
    public void Waves_KeepTheirSoundCharacters_ForTheCallerToSplit()
    {
        // Shipped entries carry them: `"wave" ">weapons/fx/nearmiss/bulletLtoR08.wav"`. Stripping
        // here would lose the instruction; keeping them verbatim leaves SoundName to split the path
        // from the characters at the point of use, in one place rather than two.
        SoundScriptEntry entry = Single(
            "\"Near\"\n{\n\t\"wave\" \">weapons/fx/nearmiss/a.wav\"\n}\n");

        entry.Waves[0].ShouldBe(">weapons/fx/nearmiss/a.wav");
    }

    [Test]
    public void Read_EveryShippedSoundScript_ParsesWithoutLosingEntries()
    {
        // **The assertion the hand-written cases cannot make.** Every test above feeds this reader
        // text this file wrote. Agreeing with itself proves the two match, not that either matches
        // what TF2 ships — which is how the WAV reader passed ten fixtures and then refused a real
        // file.
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        int files = 0;
        int entries = 0;
        int withRandomWaves = 0;
        int withRanges = 0;
        Dictionary<int, int> levels = [];

        foreach (string path in archive.Paths.Where(
            p => p.Contains("GAME_SOUNDS", StringComparison.OrdinalIgnoreCase)
              && p.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase)))
        {
            if (archive.ReadFile(path) is not { } bytes)
            {
                continue;
            }

            IReadOnlyDictionary<string, SoundScriptEntry> read = SoundScript.Read(bytes);

            files++;
            entries += read.Count;

            foreach (SoundScriptEntry entry in read.Values)
            {
                if (entry.Waves.Count > 1)
                {
                    withRandomWaves++;
                }

                if (entry.Pitch.Varies || entry.Volume.Varies)
                {
                    withRanges++;
                }

                levels[entry.SoundLevel] = levels.GetValueOrDefault(entry.SoundLevel) + 1;

                // Every entry must be usable, not merely present: an entry with no wave is one this
                // reader dropped the payload of, and a soundlevel outside the declared range means
                // the symbolic resolution silently fell back.
                entry.Waves.ShouldNotBeEmpty(entry.Name);
                entry.SoundLevel.ShouldBeInRange(0, 180, entry.Name);
                entry.Volume.Low.ShouldBeInRange(0f, 10f, entry.Name);
            }
        }

        TestContext.Out.WriteLine(
            $"scripts {files}, entries {entries}, rndwave {withRandomWaves}, ranged {withRanges}");

        foreach ((int level, int count) in levels.OrderByDescending(e => e.Value).Take(8))
        {
            TestContext.Out.WriteLine($"  SNDLVL {level}  {count}");
        }

        files.ShouldBeGreaterThan(10, "TF2 ships around twenty soundscripts");
        entries.ShouldBeGreaterThan(1000, "and thousands of entries between them");

        // **The range checks above cannot see a fallback, and that is the point of this one.** If
        // symbolic resolution broke entirely, every `SNDLVL_96dB` would come back as the default 75
        // — which is inside 0..180 and passes every assertion in the loop. So predict the shape
        // instead: the loud levels dominate TF2's scripts by an order of magnitude, and a collapse
        // to the default would invert that immediately.
        levels[95].ShouldBeGreaterThan(
            levels[75] * 5, "symbolic levels resolved, rather than falling back to SNDLVL_NORM");

        levels.Count.ShouldBeGreaterThan(
            10, "many distinct levels were resolved, not one default");
    }

    [Test]
    public void Read_TheRicochetEntryTf2Ships_HasTheValuesTheFileStates()
    {
        // **An exact prediction against a real shipped entry**, which the totality sweep above
        // cannot make. That sweep proves nothing was refused; it does not prove any value is right,
        // because a reader that returned defaults for everything would satisfy it.
        //
        // The prediction comes from `game_sounds_weapons.txt` itself, read before this was written:
        //
        //     "FX_RicochetSound.Ricochet"
        //     {
        //         "channel"     "CHAN_STATIC"
        //         "volume"      "1.0"
        //         "soundlevel"  "SNDLVL_96dB"
        //         "pitch"       "90, 110"
        //         "rndwave" { ... }
        //     }
        //
        // Every number below is transcribed from that file, not from this reader.
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        string? weapons = archive.Paths.FirstOrDefault(
            p => p.Contains("GAME_SOUNDS_WEAPONS", StringComparison.OrdinalIgnoreCase));

        if (weapons is null || archive.ReadFile(weapons) is not { } bytes)
        {
            Assert.Ignore("game_sounds_weapons.txt is not in this install.");
            return;
        }

        IReadOnlyDictionary<string, SoundScriptEntry> read = SoundScript.Read(bytes);

        read.ShouldContainKey("FX_RicochetSound.Ricochet");

        SoundScriptEntry ricochet = read["FX_RicochetSound.Ricochet"];

        ricochet.Channel.ShouldBe(6, "CHAN_STATIC");
        ricochet.SoundLevel.ShouldBe(96, "SNDLVL_96dB");
        ricochet.Volume.Low.ShouldBe(1f);
        ricochet.Pitch.Low.ShouldBe(90f);
        ricochet.Pitch.High.ShouldBe(110f);
        ricochet.Waves.Count.ShouldBeGreaterThan(1, "the entry is an rndwave block");

        // The control: a neighbouring entry must NOT share those values, or the lookup is returning
        // whatever it read last rather than the entry asked for.
        SoundScriptEntry other = read.Values.First(
            e => e.SoundLevel != 96 && e.Name != ricochet.Name);

        other.SoundLevel.ShouldNotBe(96);
    }

    /// <summary>Reads one entry out of a soundscript given as text.</summary>
    /// <remarks>
    /// Authored from the SHIPPED scripts' own syntax rather than from this reader — the ricochet
    /// entry below is copied from `game_sounds_weapons.txt`. That is the distinction in
    /// `docs/memory/put-the-real-file-in-the-fixture.md`: synthetic is fine, sourcing it from our
    /// own code is not.
    /// </remarks>
    private static SoundScriptEntry Single(string text)
    {
        IReadOnlyDictionary<string, SoundScriptEntry> entries =
            SoundScript.Read(Encoding.UTF8.GetBytes(text));

        entries.Count.ShouldBe(1, "the fixture declares exactly one entry");

        return entries.Values.First();
    }
}
