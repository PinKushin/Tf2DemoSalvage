using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Which audio formats TF2 actually ships, counted across the sound VPKs.
/// </summary>
/// <remarks>
/// **Measured before writing a decoder, for the same reason the sound-name prefixes were.**
/// <c>public/tier2/riff.h</c> declares four format codes — PCM, ADPCM, Xbox ADPCM and XMA — and
/// which of them matter here is a question about the shipped data rather than about the header. The
/// two console formats are out of scope by D46 regardless; what is worth knowing is the split
/// between PCM and ADPCM, because ADPCM needs a decoder and PCM does not.
///
/// It also answers a second question cheaply: how much of TF2's audio is MP3 rather than RIFF at
/// all. The corpus's precached names include <c>vo/announcer_am_lastmanalive01.mp3</c>, so at least
/// some of what a demo asks for is not a WAV.
///
/// `[Explicit]` because it opens every sound VPK; its numbers belong in a finding.
/// </remarks>
[Explicit("Scans TF2's sound VPKs; run deliberately.")]
public sealed class SoundFormatProbe
{
    /// <summary>Where the game is, when it is installed.</summary>
    private static string? GameFolder => GameInstall.Root;

    [Test]
    public void SoundFiles_AcrossTheShippedVpks_AreCountedByFormat()
    {
        if (GameFolder is not { } tf)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        // **Both sound archives, because they hold different things.** `tf2_sound_misc` is effects
        // and music; `tf2_sound_vo_english` is the spoken lines, and a demo asks for plenty of
        // those — the corpus's precache includes `vo/announcer_am_lastmanalive01.mp3`. Measuring
        // one and generalising would have got the format split badly wrong.
        List<VpkArchive> archives = [];

        foreach (string name in new[] { "tf2_sound_misc_dir.vpk", "tf2_sound_vo_english_dir.vpk" })
        {
            string directory = Path.Combine(tf, name);

            if (File.Exists(directory))
            {
                archives.Add(VpkArchive.Open(directory));
            }
        }

        if (archives.Count == 0)
        {
            Assert.Ignore("no sound VPK is present.");
            return;
        }

        Dictionary<string, int> byExtension = [];
        Dictionary<int, int> byFormat = [];
        int examined = 0;
        int unreadable = 0;
        int entries = 0;

        foreach (VpkArchive archive in archives)
        {
            entries += archive.Count;

            foreach (string path in archive.Paths)
            {
                // Upper rather than lower: CA1308 flags ToLowerInvariant, because lowercasing is
                // not round-trip safe in every culture.
                string extension = Path.GetExtension(path).ToUpperInvariant();
                byExtension[extension] = byExtension.GetValueOrDefault(extension) + 1;

                if (extension != ".WAV")
                {
                    continue;
                }

                if (!archive.TryFind(path, out VpkEntry entry) || entry.Size < 44)
                {
                    unreadable++;
                    continue;
                }

                byte[]? bytes = archive.ReadFile(path);

                if (bytes is null || bytes.Length < 44)
                {
                    unreadable++;
                    continue;
                }

                examined++;

                int format = FormatOf(bytes);
                byFormat[format] = byFormat.GetValueOrDefault(format) + 1;
            }
        }

        TestContext.Out.WriteLine($"entries across {archives.Count} sound archives: {entries}");
        TestContext.Out.WriteLine("by extension:");

        foreach ((string extension, int count) in byExtension.OrderByDescending(e => e.Value).Take(8))
        {
            TestContext.Out.WriteLine($"  {(extension.Length == 0 ? "(none)" : extension)}  {count}");
        }

        TestContext.Out.WriteLine($"wav files examined: {examined}, unreadable: {unreadable}");
        TestContext.Out.WriteLine("by WAVE format code:");

        foreach ((int format, int count) in byFormat.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine($"  {Name(format)} (0x{format:x4})  {count}");
        }

        entries.ShouldBeGreaterThan(0, "the archives read as empty, so the counts mean nothing");

        static string Name(int format) => format switch
        {
            0x0001 => "PCM",
            0x0002 => "ADPCM",
            0x0069 => "XBOX_ADPCM",
            0x0165 => "XMA",
            _ => "unknown",
        };
    }

    /// <summary>The <c>wFormatTag</c> out of a RIFF/WAVE header, or -1.</summary>
    private static int FormatOf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 ||
            !bytes[..4].SequenceEqual("RIFF"u8) ||
            !bytes.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            return -1;
        }

        // Walk the chunks rather than assuming `fmt ` is first: Valve's own VDAT and PADD chunks
        // appear in shipped files, and a reader that takes offset 20 on faith reads whichever chunk
        // happens to be there.
        int at = 12;

        while (at + 8 <= bytes.Length)
        {
            ReadOnlySpan<byte> id = bytes.Slice(at, 4);
            int size = BitConverter.ToInt32(bytes.Slice(at + 4, 4));

            if (id.SequenceEqual("fmt "u8))
            {
                return at + 10 <= bytes.Length ? BitConverter.ToUInt16(bytes.Slice(at + 8, 2)) : -1;
            }

            if (size < 0)
            {
                return -1;
            }

            // Chunks are word-aligned; an odd size carries a pad byte that is not counted.
            at += 8 + size + (size % 2);
        }

        return -1;
    }
}
