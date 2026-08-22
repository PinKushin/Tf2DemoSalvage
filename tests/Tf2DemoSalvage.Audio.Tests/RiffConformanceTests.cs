using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The RIFF chunks and format codes Valve declares, against the ones this reader handles.
/// </summary>
/// <remarks>
/// **`public/tier2/riff.h` is published, so none of this is guesswork.** It names the chunk
/// identifiers — including two of Valve's own, <c>VDAT</c> and <c>PADD</c> — and the four format
/// codes.
///
/// **Measured before writing the reader** (`SoundFormatProbe`), across both shipped sound archives,
/// 15,958 entries:
///
/// | | count | share |
/// |---|---|---|
/// | <c>.mp3</c> | 13,140 | 82% |
/// | <c>.wav</c> | 2,817 | 18% |
///
/// and of the WAVs, **2,815 plain PCM against 2 ADPCM**. So the WAV reader is a chunk walk and a
/// copy, and ADPCM is a case to REPORT rather than one to implement now — the whole hazard in this
/// area is a sound that silently does not play.
///
/// **Measuring one archive would have got that backwards**: `tf2_sound_misc` alone reads 2,757 WAV
/// to 472 MP3, which says MP3 is a corner. The voice archive is what inverts it.
/// </remarks>
public sealed class RiffConformanceTests
{
    private const string Riff = "src/public/tier2/riff.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void FormatCodes_TheOnesValveDeclares_AreTheOnesThisReaderNames()
    {
        Dictionary<string, int> declared = Declared();

        // The control: four codes, so a pattern that matched nothing cannot pass.
        declared.Count.ShouldBeGreaterThanOrEqualTo(4, "riff.h declares four WAVE_FORMAT_ codes");

        declared["WAVE_FORMAT_PCM"].ShouldBe((int)WaveFormat.Pcm);
        declared["WAVE_FORMAT_ADPCM"].ShouldBe((int)WaveFormat.Adpcm);
        declared["WAVE_FORMAT_XBOX_ADPCM"].ShouldBe((int)WaveFormat.XboxAdpcm);
        declared["WAVE_FORMAT_XMA"].ShouldBe((int)WaveFormat.Xma);
    }

    [Test]
    public void Read_APcmFile_YieldsItsFormatAndSamples()
    {
        // A minimal well-formed PCM file: 16-bit mono at 44,100, two samples.
        byte[] wave = Wave(
            format: (int)WaveFormat.Pcm, channels: 1, rate: 44100, bits: 16,
            data: [0x01, 0x00, 0xFF, 0x7F]);

        RiffWave read = RiffWave.Read(wave).ShouldNotBeNull();

        read.Format.ShouldBe(WaveFormat.Pcm);
        read.Channels.ShouldBe(1);
        read.SampleRate.ShouldBe(44100);
        read.BitsPerSample.ShouldBe(16);
        read.Data.Length.ShouldBe(4);
    }

    [Test]
    public void Read_AChunkBeforeTheFormat_IsWalkedRatherThanAssumed()
    {
        // **Valve ships files with their own chunks in them**, and riff.h names two: VDAT and PADD.
        // A reader that takes the format at offset 20 on faith reads whichever chunk happens to be
        // first and gets a plausible wrong answer — a sample rate of nonsense plays at the wrong
        // speed rather than failing.
        byte[] wave = Wave(
            format: (int)WaveFormat.Pcm, channels: 2, rate: 22050, bits: 16,
            data: [0, 0, 0, 0],
            leading: ("PADD"u8.ToArray(), [0xAA, 0xBB, 0xCC, 0xDD]));

        RiffWave read = RiffWave.Read(wave).ShouldNotBeNull();

        read.SampleRate.ShouldBe(22050, "the format chunk was found past the leading one");
        read.Channels.ShouldBe(2);
    }

    [Test]
    public void Read_AnOddSizedChunk_IsFollowedByItsPadByte()
    {
        // RIFF chunks are word-aligned: an odd size carries a pad byte that the size does NOT
        // count. A reader that skips `size` alone lands one byte early on every subsequent chunk
        // and reads garbage — and since it usually lands mid-chunk rather than off the end, it
        // fails as a wrong number rather than as an exception.
        byte[] wave = Wave(
            format: (int)WaveFormat.Pcm, channels: 1, rate: 11025, bits: 8,
            data: [0x40],
            leading: ("VDAT"u8.ToArray(), [0x01, 0x02, 0x03]));

        RiffWave read = RiffWave.Read(wave).ShouldNotBeNull();

        read.SampleRate.ShouldBe(11025);
        read.Data.Length.ShouldBe(1);
    }

    [Test]
    public void Read_AnAdpcmFile_IsReportedRatherThanDecodedOrSkipped()
    {
        // **Two files in the whole game, and they still must not be silent-by-omission.** The
        // format is surfaced so a caller can say "this one is ADPCM and is not supported yet"
        // rather than playing nothing and reporting nothing — the failure this whole area is prone
        // to (docs/memory/decode-must-be-total.md).
        byte[] wave = Wave(
            format: (int)WaveFormat.Adpcm, channels: 1, rate: 22050, bits: 4, data: [0x11, 0x22]);

        RiffWave read = RiffWave.Read(wave).ShouldNotBeNull();

        read.Format.ShouldBe(WaveFormat.Adpcm);
        read.IsPcm.ShouldBeFalse("a caller must be able to tell this apart without guessing");
    }

    [TestCase("RIFX")]
    [TestCase("RIFF")]
    public void Read_SomethingThatIsNotAWave_ReturnsNullRatherThanThrowing(string stamp)
    {
        // Hostile input: a VPK path may hold anything, and a malformed file must not take down the
        // sound pass. Null is "cannot be played", which the caller can report.
        byte[] bytes = new byte[64];
        System.Text.Encoding.ASCII.GetBytes(stamp).CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes("NOPE").CopyTo(bytes, 8);

        RiffWave.Read(bytes).ShouldBeNull();
    }

    [TestCase(0)]
    [TestCase(11)]
    [TestCase(43)]
    public void Read_ATruncatedFile_ReturnsNullRatherThanThrowing(int length)
    {
        RiffWave.Read(new byte[length]).ShouldBeNull();
    }

    [Test]
    public void Read_AChunkClaimingMoreThanTheFile_ReturnsNullRatherThanReadingPastIt()
    {
        // The classic: a length field a stranger controls. It must bound the read rather than the
        // buffer bounding it by throwing.
        byte[] wave = Wave(
            format: (int)WaveFormat.Pcm, channels: 1, rate: 44100, bits: 16, data: [0, 0]);

        // Rewrite the data chunk's size to something far past the end.
        int dataAt = wave.Length - 10;
        BitConverter.GetBytes(int.MaxValue).CopyTo(wave, dataAt + 4);

        RiffWave.Read(wave).ShouldBeNull();
    }

    [Test]
    public void Read_JunkAfterTheDataChunk_DoesNotDiscardTheAudio()
    {
        // **Found by running the reader over every shipped WAV, not by thinking about it.** One
        // file of 2,756 was refused: sound/player/taunt_eng_swoosh.wav, which carries a valid
        // `fmt ` and `data` and then LIST, bext and an `FLLR` filler chunk, after which the ids
        // read as `filr`, `ilrl` and four zero bytes — an authoring tool's padding.
        //
        // Refusing the file threw away audio already read correctly. The engine plays it.
        byte[] wave = Wave(
            format: (int)WaveFormat.Pcm, channels: 1, rate: 44100, bits: 16, data: [1, 2, 3, 4]);

        byte[] withJunk = new byte[wave.Length + 16];
        wave.CopyTo(withJunk, 0);

        // A chunk claiming far more than remains — the shape the real file ends with.
        "JUNK"u8.ToArray().CopyTo(withJunk, wave.Length);
        BitConverter.GetBytes(int.MaxValue).CopyTo(withJunk, wave.Length + 4);

        RiffWave read = RiffWave.Read(withJunk).ShouldNotBeNull(
            "the format and data were both read before the junk, so the file is playable");

        read.SampleRate.ShouldBe(44100);
        read.Data.Length.ShouldBe(4);
    }

    [Test]
    public void Read_EveryShippedWav_IsReadWithNoFailures()
    {
        // **The assertion the fixtures cannot make.** Every test above feeds this reader a file
        // this test wrote; agreeing with itself proves the two match, not that either matches what
        // TF2 ships. `docs/memory/output-level-assertion-or-it-is-not-done.md` is explicit that one
        // assertion against real data is the only one that can fail when the model is wrong.
        //
        // And the standard is totality, not a majority: the engine reads every one of these without
        // complaint, so a file this reader cannot open is our defect
        // (`docs/memory/decode-must-be-total.md`).
        if (GameFolder is not { } tf)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        string directory = Path.Combine(tf, "tf2_sound_misc_dir.vpk");

        if (!File.Exists(directory))
        {
            Assert.Ignore($"{directory} is not present.");
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        int read = 0;
        int pcm = 0;
        List<string> refused = [];
        Dictionary<int, int> rates = [];

        foreach (string path in archive.Paths)
        {
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (archive.ReadFile(path) is not { } bytes)
            {
                continue;
            }

            if (RiffWave.Read(bytes) is not { } wave)
            {
                if (refused.Count < 10)
                {
                    refused.Add(path);
                }

                continue;
            }

            read++;
            rates[wave.SampleRate] = rates.GetValueOrDefault(wave.SampleRate) + 1;

            if (wave.IsPcm)
            {
                pcm++;
            }

            // Every field has to be usable, not merely present. A channel count of zero or a rate
            // of zero would sail through a "did it parse" check and divide by zero downstream.
            wave.Channels.ShouldBeInRange(1, 8, path);
            wave.SampleRate.ShouldBeInRange(8000, 48000, path);
            wave.Data.Length.ShouldBeGreaterThan(0, path);
        }

        TestContext.Out.WriteLine($"wavs read: {read}, pcm: {pcm}, refused: {refused.Count}");

        foreach ((int rate, int count) in rates)
        {
            TestContext.Out.WriteLine($"  {rate} Hz  {count}");
        }

        read.ShouldBeGreaterThan(2000, "the archive should hold thousands of wavs to read");

        refused.ShouldBeEmpty(
            "the engine opens every one of these, so a file this reader refuses is our defect: "
            + string.Join(", ", refused));
    }

    /// <summary>Where the game is, when it is installed.</summary>
    private static string? GameFolder
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("TF2_FOLDER");

            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }

            foreach (string root in new[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
                @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
                @"D:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            })
            {
                if (File.Exists(Path.Combine(root, "tf2_textures_dir.vpk")))
                {
                    return root;
                }
            }

            return null;
        }
    }

    /// <summary>Builds a minimal RIFF/WAVE file, optionally with a chunk before the format.</summary>
    /// <remarks>
    /// Authored from <c>riff.h</c> and the RIFF specification rather than from this project's
    /// reader, which is the distinction <c>docs/memory/put-the-real-file-in-the-fixture.md</c>
    /// draws: a synthetic fixture is fine, sourcing it from our own code is not. It also reaches
    /// cases no shipped file contains — an odd-sized chunk, a length past the end.
    /// </remarks>
    private static byte[] Wave(
        int format, int channels, int rate, int bits, byte[] data,
        (byte[] Id, byte[] Body)? leading = null)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);

        writer.Write("RIFF"u8);
        writer.Write(0);                 // patched below
        writer.Write("WAVE"u8);

        if (leading is { } extra)
        {
            writer.Write(extra.Id);
            writer.Write(extra.Body.Length);
            writer.Write(extra.Body);

            if (extra.Body.Length % 2 != 0)
            {
                writer.Write((byte)0);   // the pad byte the size does not count
            }
        }

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)format);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);   // byte rate
        writer.Write((short)(channels * bits / 8)); // block align
        writer.Write((short)bits);

        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);

        writer.Flush();
        byte[] bytes = stream.ToArray();

        BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);

        return bytes;
    }

    /// <summary>The <c>WAVE_FORMAT_</c> codes riff.h declares.</summary>
    private static Dictionary<string, int> Declared()
    {
        Dictionary<string, int> found = [];

        foreach (Match match in Regex.Matches(
            Sdk(),
            @"#define\s+(?<name>WAVE_FORMAT_\w+)\s+(?<value>0x[0-9A-Fa-f]+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            found[match.Groups["name"].Value] =
                Convert.ToInt32(match.Groups["value"].Value[2..], 16);
        }

        return found;
    }

    /// <summary>Reads riff.h, or fails loudly.</summary>
    private static string Sdk() =>
        SourceSdk.Text(Riff) ?? throw new InvalidOperationException($"{Riff} is missing from the SDK");
}
