using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Are TF2's MP3s ordinary MPEG audio, or something Valve-specific?
/// </summary>
/// <remarks>
/// **The question that decides whether a third-party decoder costs control.** If Valve ships plain
/// MPEG-1 Layer III then an MP3 decoder is a commodity for a frozen standard and there is nothing
/// Valve-shaped to control. If they ship something unusual — odd sample rates, MPEG-2.5, free-format
/// frames, a custom container — then a library becomes a constraint rather than a convenience.
/// </remarks>
[Explicit("Scans TF2's sound VPKs; run deliberately.")]
public sealed class Mp3HeaderProbe
{
    private static readonly int[] Rates = [44100, 48000, 32000, 0];

    [Test]
    public void Mp3Files_TheirFrameHeaders_AreCounted()
    {
        string tf = @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

        if (!Directory.Exists(tf))
        {
            Assert.Ignore("not installed");
            return;
        }

        Dictionary<string, int> versions = [];
        Dictionary<int, int> rates = [];
        Dictionary<int, int> channelModes = [];
        int id3 = 0;
        int examined = 0;
        int noSync = 0;

        foreach (string name in new[] { "tf2_sound_misc_dir.vpk", "tf2_sound_vo_english_dir.vpk" })
        {
            string dir = Path.Combine(tf, name);

            if (!File.Exists(dir))
            {
                continue;
            }

            VpkArchive archive = VpkArchive.Open(dir);

            foreach (string path in archive.Paths)
            {
                if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || examined >= 3000)
                {
                    continue;
                }

                if (archive.ReadFile(path) is not { Length: > 16 } bytes)
                {
                    continue;
                }

                examined++;

                int at = 0;

                // ID3v2 header: "ID3", two version bytes, flags, then a syncsafe 28-bit size.
                if (bytes.Length > 10 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
                {
                    id3++;
                    at = 10 + ((bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9]);
                }

                // Find the first frame sync: eleven set bits.
                while (at + 4 <= bytes.Length &&
                       !(bytes[at] == 0xFF && (bytes[at + 1] & 0xE0) == 0xE0))
                {
                    at++;
                }

                if (at + 4 > bytes.Length)
                {
                    noSync++;
                    continue;
                }

                int versionBits = (bytes[at + 1] >> 3) & 0x3;
                int layerBits = (bytes[at + 1] >> 1) & 0x3;
                int rateIndex = (bytes[at + 2] >> 2) & 0x3;
                int mode = (bytes[at + 3] >> 6) & 0x3;

                string version = versionBits switch
                {
                    0 => "MPEG-2.5",
                    2 => "MPEG-2",
                    3 => "MPEG-1",
                    _ => "reserved",
                };

                string layer = layerBits switch
                {
                    1 => "III",
                    2 => "II",
                    3 => "I",
                    _ => "reserved",
                };

                versions[$"{version} Layer {layer}"] =
                    versions.GetValueOrDefault($"{version} Layer {layer}") + 1;

                int rate = Rates[rateIndex];

                if (versionBits == 2)
                {
                    rate /= 2;
                }
                else if (versionBits == 0)
                {
                    rate /= 4;
                }

                rates[rate] = rates.GetValueOrDefault(rate) + 1;
                channelModes[mode] = channelModes.GetValueOrDefault(mode) + 1;
            }
        }

        TestContext.Out.WriteLine($"mp3s examined: {examined}, with ID3v2: {id3}, no sync: {noSync}");
        TestContext.Out.WriteLine("version/layer:");

        foreach ((string what, int count) in versions.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine($"  {what}  {count}");
        }

        TestContext.Out.WriteLine("sample rates:");

        foreach ((int rate, int count) in rates.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine($"  {rate} Hz  {count}");
        }

        TestContext.Out.WriteLine("channel mode (0=stereo 1=joint 2=dual 3=mono):");

        foreach ((int mode, int count) in channelModes.OrderByDescending(e => e.Value))
        {
            TestContext.Out.WriteLine($"  mode {mode}  {count}");
        }

        examined.ShouldBeGreaterThan(0);
    }
}
