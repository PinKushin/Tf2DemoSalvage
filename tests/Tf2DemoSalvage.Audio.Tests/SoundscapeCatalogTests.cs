using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// Loading soundscapes from inputs the shipped game never contains.
/// </summary>
/// <remarks>
/// **Written against surviving mutants, not against a feature** (B217). Every existing test of this
/// type reads the INSTALLED game — `SoundscapeCatalogConformanceTests` opens the real VPKs and
/// compares against what the client reported — which is the right way to prove the parser agrees
/// with Valve, and the wrong way to reach a branch no shipped file takes.
///
/// So twenty-one mutants survived here while the type looked well covered. Real soundscape scripts
/// never carry an empty `file` value, a `playlooping` with no `wave`, or a map name that is the empty
/// string, so no assertion ever depended on what happens then. That is
/// `docs/memory/fixtures-are-the-weak-point.md#real-data-hides-bugs-small-inputs-expose` measured rather than recalled.
///
/// **The fixtures are hand-built KeyValues**, which is what makes those branches reachable at all:
/// `Load` takes a `Func&lt;string, byte[]?&gt;`, so a test can be the whole file system.
/// </remarks>
public sealed class SoundscapeCatalogTests
{
    private const string Manifest = "scripts/soundscapes_manifest.txt";

    /// <summary>A reader over a fixed set of files, standing in for the game's archives.</summary>
    private static System.Func<string, byte[]?> Files(Dictionary<string, string> files) =>
        path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;

    /// <summary>A manifest naming the given script paths.</summary>
    private static string Listing(params string[] paths) =>
        "\"soundscapes_manifest\"\n{\n" +
        string.Concat(paths.Select(p => $"    \"file\"    \"{p}\"\n")) +
        "}\n";

    [Test]
    public void Load_APlayloopingRuleWithNoWave_IsNotCountedAsALoop()
    {
        // **The wave is what a loop IS**, so a `playlooping` block that never names one has nothing
        // to play. Shipped files always name one, which is why nothing asserted this: the mutant
        // dropping `wave.Length > 0` survived because no input ever reached the false branch.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(new()
        {
            [Manifest] = Listing("scripts/soundscapes_test.txt"),
            ["scripts/soundscapes_test.txt"] =
                "\"test.silent\"\n{\n" +
                "    \"playlooping\"\n    {\n        \"volume\"    \"0.5\"\n    }\n" +
                "}\n",
        }));

        Soundscape only = catalog.At(0).ShouldNotBeNull();

        only.Name.ShouldBe("test.silent");
        only.Looping.ShouldBeEmpty("a playlooping block with no wave has nothing to play");
    }

    [Test]
    public void Load_APlayloopingRuleWithAWave_IsCountedAsALoop()
    {
        // The control. Without it the assertion above is satisfied by a parser that records no
        // loops at all, which is a far larger defect than the one it is meant to catch.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(new()
        {
            [Manifest] = Listing("scripts/soundscapes_test.txt"),
            ["scripts/soundscapes_test.txt"] =
                "\"test.room\"\n{\n" +
                "    \"playlooping\"\n    {\n" +
                "        \"volume\"    \"0.5\"\n        \"wave\"    \"ambient/hum.wav\"\n    }\n" +
                "}\n",
        }));

        Soundscape only = catalog.At(0).ShouldNotBeNull();

        only.Looping.Count.ShouldBe(1);
        only.Looping[0].Wave.ShouldBe("ambient/hum.wav");
        only.Looping[0].Volume.ShouldBe(new Tf2DemoSalvage.Core.Primitives.Interval(0.5f, 0f));
    }

    [Test]
    public void Load_ARuleThatIsNotPlaylooping_IsRecordedRatherThanPlayed()
    {
        // `playrandom` and `playsoundscape` are real rules this project does not implement, and the
        // type records their names so the gap is countable. Two mutants survived on this branch —
        // one flipping the equality and one removing the negation — because every fixture until now
        // came from shipped scripts, where the two lists happen to be filled together.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(new()
        {
            [Manifest] = Listing("scripts/soundscapes_test.txt"),
            ["scripts/soundscapes_test.txt"] =
                "\"test.mixed\"\n{\n" +
                "    \"playrandom\"\n    {\n        \"wave\"    \"ambient/bird.wav\"\n    }\n" +
                "    \"playlooping\"\n    {\n        \"wave\"    \"ambient/hum.wav\"\n    }\n" +
                "}\n",
        }));

        Soundscape only = catalog.At(0).ShouldNotBeNull();

        only.OtherRules.ShouldBe(["playrandom"], "an unimplemented rule is named, not silently dropped");
        only.Looping.Count.ShouldBe(1, "and the looping rule beside it is still played");
    }

    [Test]
    public void Load_AManifestEntryWithNoValue_IsNotTreatedAsAFile()
    {
        // `Listed` requires both that the key is `file` and that the value is non-empty. Real
        // manifests satisfy both on every line, so the guard was never exercised — and an empty
        // path reaching `read` would ask the archives for "".
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(new()
        {
            [Manifest] =
                "\"soundscapes_manifest\"\n{\n" +
                "    \"file\"    \"\"\n" +
                "    \"file\"    \"scripts/soundscapes_test.txt\"\n" +
                "}\n",
            ["scripts/soundscapes_test.txt"] =
                "\"test.room\"\n{\n    \"dsp\"    \"1\"\n}\n",
        }));

        catalog.At(0).ShouldNotBeNull().Name.ShouldBe("test.room");
        catalog.At(1).ShouldBeNull("the empty entry must not have produced a second soundscape");
    }

    [Test]
    public void Load_AManifestKeyThatIsNotFile_IsIgnored()
    {
        // The other half of the same guard, and a separate mutant: a manifest may carry keys that
        // are not paths, and treating one as a path asks the archives for a name that is not one.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(new()
        {
            [Manifest] =
                "\"soundscapes_manifest\"\n{\n" +
                "    \"comment\"    \"scripts/soundscapes_test.txt\"\n" +
                "}\n",
            ["scripts/soundscapes_test.txt"] =
                "\"test.room\"\n{\n    \"dsp\"    \"1\"\n}\n",
        }));

        catalog.At(0).ShouldBeNull("only a `file` key names a script to load");
    }

    [Test]
    public void Load_WithAnEmptyMapName_LooksForNoMapFile()
    {
        // **An empty map name is not a map**, so it must not become `soundscapes_.txt`. Four mutants
        // survived on this one pattern — the length test, and both arms of the conditional — because
        // every caller until now passed either a real name or nothing at all.
        Dictionary<string, string> files = new()
        {
            [Manifest] = Listing("scripts/soundscapes_test.txt"),
            ["scripts/soundscapes_test.txt"] = "\"test.room\"\n{\n    \"dsp\"    \"1\"\n}\n",
            ["scripts/soundscapes_.txt"] = "\"test.phantom\"\n{\n    \"dsp\"    \"2\"\n}\n",
        };

        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files(files), mapName: string.Empty);

        catalog.At(0).ShouldNotBeNull().Name.ShouldBe("test.room");
        catalog.At(1).ShouldBeNull("scripts/soundscapes_.txt is not a map file and must not load");
    }

    [Test]
    public void Load_WithAMapName_AppendsThatMapsFileAfterTheManifest()
    {
        // The control for the case above: with a real name the map's own file DOES load, and it
        // goes last, because the engine lets a map override what the manifest already named.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(
            Files(new()
            {
                [Manifest] = Listing("scripts/soundscapes_test.txt"),
                ["scripts/soundscapes_test.txt"] = "\"test.room\"\n{\n    \"dsp\"    \"1\"\n}\n",
                ["scripts/soundscapes_cp_process.txt"] =
                    "\"process.yard\"\n{\n    \"dsp\"    \"2\"\n}\n",
            }),
            mapName: "cp_process");

        catalog.At(0).ShouldNotBeNull().Name.ShouldBe("test.room");
        catalog.At(1).ShouldNotBeNull().Name.ShouldBe("process.yard");
    }

    [Test]
    public void Load_WhenTheManifestAlreadyNamesTheMapFile_LoadsItOnceRatherThanTwice()
    {
        // **The reason `mapFileListed` exists**, and three mutants sat on its condition. A map whose
        // file the manifest already lists would otherwise be appended a second time, and every index
        // after it would name a different soundscape than the client's — which is the exact failure
        // the conformance suite checks against the shipped list, and cannot check for a map that is
        // not shipped that way.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(
            Files(new()
            {
                [Manifest] = Listing("scripts/soundscapes_cp_process.txt"),
                ["scripts/soundscapes_cp_process.txt"] =
                    "\"process.yard\"\n{\n    \"dsp\"    \"2\"\n}\n",
            }),
            mapName: "cp_process");

        catalog.At(0).ShouldNotBeNull().Name.ShouldBe("process.yard");
        catalog.At(1).ShouldBeNull("the manifest already named it, so it must not load again");
    }

    /// <remarks>
    /// **The map's pakfile is searched ahead of the install, for every file the catalog reads** — the manifest's files as
    /// much as the map's own (B465). The engine mounts the loaded map's pakfile at the head of the <c>"GAME"</c> path, and
    /// <c>C_SoundscapeSystem::Init</c> reads through that path (<c>c_soundscape.cpp:274,314,333</c>). A synthetic zip, so
    /// the machine without TF2 runs it: the same script name in both places answers the pakfile's, and the map's own
    /// script, which only the pakfile has, is appended last.
    /// </remarks>
    [Test]
    public void ForLevel_AScriptInBothThePakfileAndTheInstall_IsThePakfilesAndTheMapsOwnGoesLast()
    {
        Tf2DemoSalvage.Content.Assets.PakFile pak = Tf2DemoSalvage.Content.Assets.PakFile.Read(Zip(new()
        {
            ["scripts/soundscapes_test.txt"] = "\"test.pak\"\n{\n    \"dsp\"    \"1\"\n}\n",
            ["scripts/soundscapes_cp_test.txt"] = "\"test.map\"\n{\n    \"dsp\"    \"2\"\n}\n",
        }));
        System.Func<string, byte[]?> install = Files(new()
        {
            [Manifest] = Listing("scripts/soundscapes_test.txt"),
            ["scripts/soundscapes_test.txt"] = "\"test.install\"\n{\n    \"dsp\"    \"1\"\n}\n",
        });

        SoundscapeCatalog level = SoundscapeCatalog.ForLevel(pak, install, "cp_test");

        level.Soundscapes.Select(soundscape => soundscape.Name).ShouldBe(["test.pak", "test.map"]);

        // The control: the install alone has neither the pakfile's copy nor the map's script.
        SoundscapeCatalog.Load(install, "cp_test").Soundscapes.Select(soundscape => soundscape.Name)
            .ShouldBe(["test.install"]);
    }

    /// <summary>A zip of the given text files, as a map's pakfile holds them.</summary>
    private static byte[] Zip(Dictionary<string, string> files) =>
        PakZip.Of(files.ToDictionary(file => file.Key, file => Encoding.UTF8.GetBytes(file.Value)));

    [Test]
    public void Load_WithNoManifest_IsAnEmptyCatalogRatherThanAnError()
    {
        // A machine with no game installed reaches this, and it is the path CI takes.
        SoundscapeCatalog catalog = SoundscapeCatalog.Load(Files([]));

        catalog.At(0).ShouldBeNull();
        catalog.WaveNames().ShouldBeEmpty();
    }
}
