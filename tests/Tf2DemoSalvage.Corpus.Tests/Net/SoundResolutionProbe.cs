using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Harvests the sounds real demos precache, so the fast tests can be built from real names.
/// </summary>
/// <remarks>
/// **A probe, not the test.** The suite that checks resolution is synthetic and lives in
/// `Audio.Tests`, because that one runs in CI and on the measurement boxes in milliseconds and can
/// cover edge cases no demo happens to contain. This needs a TF2 install and the corpus, so as a
/// test it would `Assert.Ignore` on exactly the machines where the checking matters.
///
/// What it is *for* is supplying the fixture. `docs/memory/put-the-real-file-in-the-fixture.md`:
/// synthetic is right, and sourcing it from OUR OWN code is what makes a fixture worthless. So the
/// names come from here — measured off real demos — and the fast suite asserts against them.
///
/// Run deliberately when the corpus grows or the resolver changes shape:
///
/// <code>
/// dotnet test tests/Tf2DemoSalvage.Corpus.Tests --filter FullyQualifiedName~SoundResolutionProbe
/// </code>
/// </remarks>
[Explicit("Harvests precached names from the corpus; run deliberately to refresh the fixture.")]
public sealed class SoundResolutionProbe
{
    [Test]
    public void PrecachedSounds_AcrossTheCorpus_AreHarvestedAndResolved()
    {
        if (GameInstallFolder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);

        if (archives.IsEmpty)
        {
            Assert.Ignore("No game content was found to resolve against.");
            return;
        }

        SoundScriptCatalog catalog = SoundScriptCatalog.Load(archives.Read);

        catalog.Entries.Count.ShouldBeGreaterThan(
            1000, "the catalog loaded, so a resolution failure below is not just a missing catalog");

        int demos = 0;
        int precached = 0;
        int fromScript = 0;
        int fromPath = 0;
        List<string> unresolved = [];
        List<string> missing = [];

        foreach (string path in Corpus.Files())
        {
            (SoundNames names, HashSet<int> played) = Walk(path);

            if (names.Count == 0)
            {
                continue;
            }

            demos++;
            int demoMissing = 0;

            // **The population that matters is what the demo PLAYS, not what it precaches.** A
            // precache table lists everything the map and game modes might use; measuring against
            // it answers a question about the install's completeness rather than about whether
            // this demo can be voiced. `docs/memory/measure-the-output-not-the-capability.md`.
            int playedMissing = 0;
            int playedResolved = 0;

            foreach (int index in played)
            {
                if (names.Resolve(index) is not { } playedName)
                {
                    continue;
                }

                ResolvedSound sound = catalog.Resolve(playedName);

                // Through SoundFile, so the container fallback is exercised: 60 of the 63 sounds
                // this used to report missing are the same stem shipping as MP3.
                if (sound.Waves.Count == 0 || SoundFile.Open(sound.Waves[0], archives.Read) is null)
                {
                    playedMissing++;
                }
                else
                {
                    playedResolved++;
                }
            }

            TestContext.Out.WriteLine(
                $"PLAYED {Path.GetFileName(path)}: {played.Count} distinct sounds played, " +
                $"{playedResolved} open, {playedMissing} missing");

            foreach (string name in names.Names)
            {
                precached++;

                ResolvedSound sound = catalog.Resolve(name);

                if (sound.Waves.Count == 0)
                {
                    if (unresolved.Count < 20)
                    {
                        unresolved.Add($"{Path.GetFileName(path)}: {name}");
                    }

                    continue;
                }

                if (sound.FromScript)
                {
                    fromScript++;
                }
                else
                {
                    fromPath++;
                }

                // **Opening it is the point.** A resolver that produced a plausible path for every
                // name would satisfy every assertion above and play nothing.
                foreach (string wave in sound.Waves)
                {
                    if (archives.Read(wave) is null)
                    {
                        demoMissing++;

                        if (missing.Count < 20)
                        {
                            missing.Add($"{Path.GetFileName(path)}: {name} -> {wave}");
                        }
                    }
                }
            }

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)} (protocol {Corpus.ProtocolOf(path)}): " +
                $"{names.Count} precached, {demoMissing} unopenable");
        }

        TestContext.Out.WriteLine(
            $"TOTAL {demos} demos, {precached} precached names: " +
            $"{fromScript} from soundscripts, {fromPath} raw paths, " +
            $"{unresolved.Count} unresolved, {missing.Count} unopenable");

        foreach (string line in unresolved.Take(10))
        {
            TestContext.Out.WriteLine($"  UNRESOLVED {line}");
        }

        foreach (string line in missing.Take(10))
        {
            TestContext.Out.WriteLine($"  MISSING    {line}");
        }

        demos.ShouldBeGreaterThan(0, "no demo carried a soundprecache table");
        precached.ShouldBeGreaterThan(100, "and the tables were not empty");

        // Both branches have to be exercised or half the resolver is untested: a corpus resolving
        // only raw paths would pass with the soundscript lookup never taken.
        fromScript.ShouldBeGreaterThan(0, "no precached name resolved through a soundscript");
        fromPath.ShouldBeGreaterThan(0, "no precached name resolved as a raw path");
    }

    [Test]
    public void MissingSounds_SearchedByFilenameAcrossTheInstall_AreMovedOrTrulyGone()
    {
        // **Tests the owner's hypothesis rather than assuming it**: "i really dont think any sounds
        // have been removed, only moved and renamed". If that holds, a mapping table is enough and
        // nothing has to be shipped. If it does not, whatever is genuinely absent is the list that
        // would have to travel with the app.
        //
        // The search is by FILENAME across every sound the install carries, so a file that moved
        // folders is found wherever it went. A file that was renamed is not, which is why the
        // report below prints the near misses by stem as well.
        if (GameInstallFolder is not { } game)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(archives.Read);

        Dictionary<string, List<string>> byFilename = new(StringComparer.OrdinalIgnoreCase);

        foreach (string archive in new[] { "tf2_sound_misc", "tf2_sound_vo_english", "tf2_misc" })
        {
            string? directory = Path.Combine(game, archive + "_dir.vpk");

            if (!File.Exists(directory))
            {
                continue;
            }

            foreach (string entry in VpkArchive.Open(directory).Paths)
            {
                // **Keyed by STEM, not by filename.** Searching for `scout_BattleCry01.wav`
                // finds nothing if Valve now ships `scout_BattleCry01.mp3`, and this project has
                // already measured that TF2's voice lines are largely MP3 today. An index that
                // keeps the extension answers "was this exact file moved" when the question is
                // "does this sound still exist".
                string stem = Path.GetFileNameWithoutExtension(entry);

                if (!byFilename.TryGetValue(stem, out List<string>? where))
                {
                    byFilename[stem] = where = [];
                }

                where.Add(entry);
            }
        }

        byFilename.Count.ShouldBeGreaterThan(
            1000, "the install's sound files were indexed, so an absence below means something");

        HashSet<string> missing = [];

        foreach (string path in Corpus.Files())
        {
            (SoundNames names, HashSet<int> played) = Walk(path);

            foreach (int index in played)
            {
                if (names.Resolve(index) is not { } name)
                {
                    continue;
                }

                ResolvedSound sound = catalog.Resolve(name);

                if (sound.Waves.Count > 0 && archives.Read(sound.Waves[0]) is null)
                {
                    missing.Add(sound.Waves[0]);
                }
            }
        }

        int moved = 0;
        List<string> gone = [];

        foreach (string wave in missing.OrderBy(w => w, StringComparer.Ordinal))
        {
            string file = Path.GetFileNameWithoutExtension(wave);

            if (byFilename.TryGetValue(file, out List<string>? where))
            {
                moved++;
                TestContext.Out.WriteLine($"MOVED   {wave}  ->  {where[0]}");
            }
            else
            {
                gone.Add(wave);
            }
        }

        TestContext.Out.WriteLine(
            $"MISSING {missing.Count} distinct played sounds: {moved} moved, {gone.Count} not found");

        foreach (string wave in gone.Take(40))
        {
            TestContext.Out.WriteLine($"GONE    {wave}");
        }

        missing.Count.ShouldBeGreaterThan(0, "nothing was missing, so this measures nothing");
    }

    /// <summary>
    /// One demo's soundprecache table, and the set of sound indices it actually played.
    /// </summary>
    /// <remarks>
    /// Both in one walk, because the table and the plays arrive interleaved in the same message
    /// stream and reading the file twice would double the probe's cost for nothing.
    /// </remarks>
    private static (SoundNames Names, HashSet<int> Played) Walk(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = Corpus.ProtocolOf(path);
        NetDecodeState state = new() { NetworkProtocol = protocol };
        SoundNames names = new();
        HashSet<int> played = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is CreateStringTableMessage created)
                {
                    names.Add(created);
                }
                else if (message is SoundsMessage sounds && !sounds.Body.IsEmpty)
                {
                    foreach (DecodedSound sound in SoundDecoder.Decode(
                        sounds.Body.Span, sounds.Count, sounds.BodyBits, protocol))
                    {
                        played.Add(sound.SoundNumber);
                    }
                }
            }
        }

        return (names, played);
    }

    /// <summary>Where the game is, when it is installed.</summary>
    private static string? GameInstallFolder =>
        new[]
        {
            Environment.GetEnvironmentVariable("TF2_FOLDER"),
            @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf",
            @"C:\Program Files (x86)\Steam\steamapps\common\Team Fortress 2\tf",
        }
        .FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            File.Exists(Path.Combine(candidate, "tf2_textures_dir.vpk")));
}
