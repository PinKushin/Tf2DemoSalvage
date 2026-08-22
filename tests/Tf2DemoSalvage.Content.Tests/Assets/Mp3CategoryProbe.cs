using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What TF2's MP3s actually are, by folder — so the decoder question is about a real population.
/// </summary>
/// <remarks>
/// **13,140 of 15,958 sound files are MP3, and "we need an MP3 decoder" is too coarse a conclusion
/// to act on.** Main-menu music is never played from a demo; announcer lines are, in pubs at least.
/// This splits them so the dependency is argued about the files that can actually be heard.
/// </remarks>
[Explicit("Scans TF2's sound VPKs; run deliberately.")]
public sealed class Mp3CategoryProbe
{
    [Test]
    public void Mp3Files_ByFolder_AreCounted()
    {
        string tf = @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

        if (!Directory.Exists(tf))
        {
            Assert.Ignore("not installed");
            return;
        }

        Dictionary<string, int> byFolder = [];
        Dictionary<string, long> bytesByFolder = [];
        int total = 0;

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
                if (!path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                total++;

                // **The DIRECTORY, not the first three segments.** Keying on three segments makes
                // the third the FILENAME for anything at `sound/vo/x.mp3`, so the largest category
                // — ordinary class and announcer lines, which sit directly under sound/vo — split
                // into thousands of singletons and vanished from the top of the list entirely.
                // The first run of this probe reported MvM as the biggest population because of it.
                int slash = path.LastIndexOf('/');
                string key = slash > 0 ? path[..slash] : "(root)";

                byFolder[key] = byFolder.GetValueOrDefault(key) + 1;

                if (archive.TryFind(path, out VpkEntry entry))
                {
                    bytesByFolder[key] = bytesByFolder.GetValueOrDefault(key) + entry.Size;
                }
            }
        }

        TestContext.Out.WriteLine($"mp3 files: {total}");

        foreach ((string folder, int count) in byFolder.OrderByDescending(e => e.Value).Take(25))
        {
            double mb = bytesByFolder.GetValueOrDefault(folder) / 1024.0 / 1024.0;
            TestContext.Out.WriteLine($"  {count,6}  {mb,8:0.0} MB  {folder}");
        }

        total.ShouldBeGreaterThan(0);
    }
}
