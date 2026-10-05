using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// A soundscript's volume, pitch and soundlevel, read the way <c>CSoundParametersInternal</c> reads them (B487).
/// </summary>
/// <remarks>
/// **Published source, <c>public/SoundParametersInternal.cpp:498-550</c>:** the named constant, else
/// <c>FromInterval( ReadInterval( sz ) )</c> — <c>atof</c> of the first two comma tokens, start and range, never
/// ordered and never refused. **Settled in disassembly** of the x64 <c>soundemittersystem.dll</c> (Ghidra project
/// <c>tf2soundemitter</c>), because the SDK leaves the storage narrowing to the compiler:
///
/// - <c>PitchFromString</c> (<c>FUN_180007150</c>): <c>PITCH_NORM</c> 0x64, <c>PITCH_LOW</c> 0x5f,
///   <c>PITCH_HIGH</c> 0x78; otherwise <c>CVTTSS2SI</c> of start and range, each stored as its LOW BYTE
///   (<c>pitch_interval_t</c> is <c>uint8</c>), so a descending pair's negative range wraps.
/// - <c>SoundLevelFromString</c> (<c>FUN_1800072b0</c>): the same truncation stored as <c>uint16</c>.
/// - <c>VolumeFromString</c> (<c>FUN_180007460</c>): start and range each through <c>ConvertFloatTo16bits</c>
///   (<c>FUN_180006ee0</c>), which TRUNCATES the mantissa (<c>compressed_vector.h:368-459</c>).
/// - <c>GetParametersForSound</c> (<c>FUN_180003370</c>): volume <c>RandomFloat( start, start + range )</c> over the
///   halves widened back; pitch and soundlevel <c>(int)RandomFloat( start, start + range )</c> over the unsigned
///   stored values summed as ints.
///
/// So a <see cref="SoundRange"/> holds exactly the two bounds that <c>RandomFloat</c> is called with.
/// </remarks>
public sealed class SoundScriptIntervalConformanceTests
{
    [Test]
    public void Pitch_ADescendingPair_WrapsItsRangeThroughAByte()
    {
        // "110,90": start 110, range -20, stored as the byte 236 — the engine draws 110..346, not 90..110.
        SoundRange pitch = Entry("pitch", "110,90").Pitch;

        pitch.Low.ShouldBe(110f);
        pitch.High.ShouldBe(346f);
    }

    [Test]
    public void Pitch_ATrailingWord_IsReadByAtofNotRefused()
    {
        // B487's own example: atof("110x") is 110, so this is the range 90..110 and never the default 100.
        SoundRange pitch = Entry("pitch", "90,110x").Pitch;

        pitch.Low.ShouldBe(90f);
        pitch.High.ShouldBe(110f);
    }

    [Test]
    public void Pitch_PitchLowAndPitchHigh_Are95And120()
    {
        // The constants `PitchFromString` stores, read off its `MOV word ptr [RBX + 0x1c]` immediates.
        Entry("pitch", "PITCH_LOW").Pitch.ShouldBe(new SoundRange(95f, 95f));
        Entry("pitch", "pitch_high").Pitch.ShouldBe(new SoundRange(120f, 120f));
    }

    [Test]
    public void Pitch_PastAByte_KeepsItsLowEightBits()
    {
        // (int)300 is 0x12C; the byte store keeps 0x2C.
        Entry("pitch", "300").Pitch.ShouldBe(new SoundRange(44f, 44f));
    }

    [Test]
    public void Pitch_AWord_IsZero()
    {
        // atof of a word is 0; no fallback to PITCH_NORM.
        Entry("pitch", "loud").Pitch.ShouldBe(new SoundRange(0f, 0f));
    }

    [Test]
    public void SoundLevel_ANumericRange_IsDrawnNotDefaulted()
    {
        // B487's other example: "80, 90" is the range 80..90, where an integer parse fell back to 75.
        Entry("soundlevel", "80, 90").SoundLevel.ShouldBe(new SoundRange(80f, 90f));
    }

    [Test]
    public void SoundLevel_ADescendingPair_WrapsItsRangeThroughAUInt16()
    {
        // range -10 stored as 65526, so the engine calls RandomFloat( 90, 65616 ).
        Entry("soundlevel", "90,80").SoundLevel.ShouldBe(new SoundRange(90f, 65616f));
    }

    [Test]
    public void SoundLevel_ANamedLevel_HasNoRange()
    {
        Entry("soundlevel", "SNDLVL_96dB").SoundLevel.ShouldBe(new SoundRange(96f, 96f));
    }

    [Test]
    public void Volume_ASingleValue_IsTruncatedToAHalf()
    {
        // 0.7 is 1.4 * 2^-1; the half keeps 10 mantissa bits, floor(0.4 * 1024) = 409, so (1 + 409/1024) / 2.
        Entry("volume", "0.7").Volume.ShouldBe(new SoundRange(0.69970703125f, 0.69970703125f));
    }

    [Test]
    public void Volume_VolNorm_IsExactlyOne()
    {
        Entry("volume", "VOL_NORM").Volume.ShouldBe(new SoundRange(1f, 1f));
    }

    [Test]
    public void Read_TheCowManglerExplosionTf2Ships_HasItsVolumeAsHalves()
    {
        // **Output level, on a shipped entry.** `game_sounds_weapons*.txt` writes
        //     "Weapon_CowMangler.Explode" { "volume" "0.665000, 0.700000" ... }
        // start (float)0.665 halves to 1361/2048; range 0.7 - 0.66500002 = 0.03499998 halves to 1146/32768; the
        // engine's upper bound is their float sum, 22922/32768. The old reader gave 0.665 and 0.7.
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        SoundScriptEntry? found = null;

        foreach (string path in archive.Paths.Where(
            p => p.Contains("GAME_SOUNDS", StringComparison.OrdinalIgnoreCase)
              && p.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase)))
        {
            if (archive.ReadFile(path) is { } bytes
                && SoundScript.Read(bytes).TryGetValue("Weapon_CowMangler.Explode", out SoundScriptEntry entry))
            {
                found = entry;
            }
        }

        found.ShouldNotBeNull("the entry ships in a game_sounds script");
        found.Value.Volume.Low.ShouldBe(1361f / 2048f);
        found.Value.Volume.High.ShouldBe(22922f / 32768f);
    }

    private static SoundScriptEntry Entry(string key, string value)
    {
        IReadOnlyDictionary<string, SoundScriptEntry> read = SoundScript.Read(Encoding.UTF8.GetBytes(
            $"\"E\"\n{{\n\t\"{key}\" \"{value}\"\n\t\"wave\" \"a/b.wav\"\n}}\n"));

        return read["E"];
    }
}
