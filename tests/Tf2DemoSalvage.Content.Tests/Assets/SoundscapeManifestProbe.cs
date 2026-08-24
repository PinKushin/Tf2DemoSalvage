using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// What TF2 actually ships for soundscapes, before anything is built against it.
/// </summary>
/// <remarks>
/// **Read the shipped data before writing the reader (B173).** The soundscape index a demo carries
/// is a POSITION in the client's load order, not a name, so getting the order wrong plays the wrong
/// ambience rather than none — a plausible sound instead of an error, which is this project's worst
/// failure mode. The order is defined by `scripts/soundscapes_manifest.txt` and the top-level
/// sections of each file it lists, so both need looking at before a line of parsing exists.
///
/// A probe rather than a test: it reports what is there. The assertions come once the shape is
/// known, which is the order `docs/memory/read-the-spec-before-measuring-our-data.md` argues for.
/// </remarks>
[Explicit("Reports TF2's shipped soundscape data; run deliberately.")]
public sealed class SoundscapeManifestProbe
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    public void Soundscapes_TheShippedManifestAndFiles_AreReported()
    {
        if (!Directory.Exists(Game))
        {
            Assert.Ignore("Team Fortress 2 is not installed");
            return;
        }

        GameArchives archives = GameArchives.Open(Game);

        byte[]? manifest = archives.Read("scripts/soundscapes_manifest.txt");

        manifest.ShouldNotBeNull("the manifest is what defines the load order");

        string text = Encoding.UTF8.GetString(manifest);

        TestContext.Out.WriteLine($"=== manifest, {manifest.Length} bytes ===");
        TestContext.Out.WriteLine(text.Length > 1600 ? text[..1600] : text);

        // Every file the manifest names, in the order it names them — which IS the index order.
        string[] files =
        [
            .. text.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("\"file\"", StringComparison.OrdinalIgnoreCase) ||
                               line.StartsWith("file", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Split('"').FirstOrDefault(part =>
                    part.Contains("soundscapes", StringComparison.OrdinalIgnoreCase)) ?? string.Empty)
                .Where(name => name.Length > 0),
        ];

        TestContext.Out.WriteLine($"\n=== {files.Length} files named by the manifest ===");

        int running = 0;

        foreach (string file in files)
        {
            byte[]? body = archives.Read(file);

            if (body is null)
            {
                TestContext.Out.WriteLine($"  {file}: NOT FOUND");
                continue;
            }

            // **The NAMES in order, because that is what the client's own dump prints.**
            // `cl_soundscape_printdebuginfo` runs `PrintDebugInfo`, which writes `- %d: %s` for
            // every entry (`c_soundscape.cpp:146`) — so this list and that one can be compared
            // directly, and the ordering rule stops being something inferred from the SDK.
            //
            // A top-level section is a depth-zero key that opens a block, which is exactly what the
            // engine counts: `if ( pKeys->GetFirstSubKey() ) { m_soundscapes.AddString( ... ) }`.
            List<string> sections = [];

            KeyValuesReader.Read(body, (key, value, depth) =>
            {
                if (depth == 0 && value is null)
                {
                    sections.Add(key);
                }

                return true;
            });

            TestContext.Out.WriteLine($"  {file}: {sections.Count} sections");

            foreach (string section in sections)
            {
                TestContext.Out.WriteLine(
                    $"    [{running.ToString(CultureInfo.InvariantCulture)}] {section}");
                running++;
            }
        }

        TestContext.Out.WriteLine($"\n~{running} soundscapes before any map-specific file is appended");

        // **The definition behind index 0, verified against the live client.** Running
        // `soundscape_dumpclient` in cp_process's spawn reported "soundscape index: 0", and this
        // reconstruction names index 0 `tf2.respawn_room` — so the ordering rule is confirmed
        // against the engine rather than inferred from it. That is the entry whose loops the owner
        // reported missing, so its shape is what has to be played.
        if (archives.Read("scripts/soundscapes.txt") is { } baseFile)
        {
            TestContext.Out.WriteLine("\n=== scripts/soundscapes.txt, verbatim ===");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(baseFile));
        }

        // The map's own file is appended LAST when the manifest did not already list it, so a
        // per-map soundscape's index depends on everything above it.
        foreach (string map in new[] { "cp_process_f12", "cp_process", "koth_viaduct", "cp_badlands" })
        {
            string path = $"scripts/soundscapes_{map}.txt";

            TestContext.Out.WriteLine(
                $"  {path}: {(archives.Read(path) is null ? "absent" : "PRESENT")}" +
                (files.Contains(path, StringComparer.OrdinalIgnoreCase) ? " (already in manifest)" : string.Empty));
        }
    }
}
