using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A map's own particle manifest and the definitions it layers over the install's (B554, finding 81).</summary>
/// <remarks>
/// **Synthetic throughout**: the pak and the install are lambdas over files written here, so every expected value is one
/// the test put there. The engine is `ParseParticleEffectsMap` (`particle_parse.cpp:175-255`) and, for what a repeated
/// name does, `CParticleSystemDictionary::AddParticleSystem` in the SDK's `particles.obj` (disassembled, `000dcd90`).
/// </remarks>
public sealed class MapParticleManifestTests
{
    private const string Map = "cp_custom";
    private const string MapManifest = "maps/cp_custom_particles.txt";

    // ---- which manifest ----

    /// <remarks>The control: no file anywhere, nothing read, and no warning (`:237` "Don't print a warning").</remarks>
    [Test]
    public void MapFiles_NoManifestAnywhere_IsEmptyAndSilent()
    {
        List<string> warnings = [];

        ParticleManifest.MapFiles(Map, _ => null, _ => null, warnings.Add).ShouldBeEmpty();
        warnings.ShouldBeEmpty();
    }

    /// <remarks>
    /// **Three candidates, first that loads wins, never merged** (`:195-197`): pak `particles.txt`, then pak
    /// `maps/&lt;name&gt;_particles.txt`, then the GAME path's. Kills a reorder of the `??` chain and a merge.
    /// </remarks>
    [Test]
    public void MapFiles_PakRootManifest_WinsOverTheMapNamedOnes()
    {
        Dictionary<string, object> pak = new()
        {
            ["particles.txt"] = Manifest("particles/root.pcf"),
            [MapManifest] = Manifest("particles/pak.pcf"),
        };

        ParticleManifest.MapFiles(Map, Reader(pak), Reader(new() { [MapManifest] = Manifest("particles/game.pcf") }), _ => { })
            .ShouldBe(["particles/root.pcf"]);
    }

    [Test]
    public void MapFiles_PakMapManifest_WinsOverTheGamePathsOne()
    {
        ParticleManifest.MapFiles(
                Map,
                Reader(new() { [MapManifest] = Manifest("particles/pak.pcf") }),
                Reader(new() { [MapManifest] = Manifest("particles/game.pcf") }),
                _ => { })
            .ShouldBe(["particles/pak.pcf"]);
    }

    [Test]
    public void MapFiles_OnlyTheGamePathHasOne_IsRead()
    {
        ParticleManifest.MapFiles(Map, _ => null, Reader(new() { [MapManifest] = Manifest("particles/game.pcf") }), _ => { })
            .ShouldBe(["particles/game.pcf"]);
    }

    // ---- which entries ----

    /// <remarks>
    /// **The directory must be exactly `particles`** (`:214-227`): strip the filename at the last `/` or `\`, skip one
    /// `!`, compare without case. A subfolder, another folder and a bare name are each warned about and skipped.
    /// </remarks>
    [Test]
    public void MapFiles_EntriesOutsideTheParticlesFolder_AreWarnedAndSkipped()
    {
        List<string> warnings = [];

        IReadOnlyList<string> files = ParticleManifest.MapFiles(
            Map,
            Reader(new()
            {
                [MapManifest] = Manifest(
                    "particles/a.pcf", "!PARTICLES\\b.pcf", "particles/sub/c.pcf", "custom/d.pcf", "e.pcf"),
            }),
            _ => null,
            warnings.Add);

        files.ShouldBe(["particles/a.pcf", "!PARTICLES\\b.pcf"]);
        warnings.Count.ShouldBe(3);
    }

    [Test]
    public void MapFiles_AKeyThatIsNotFile_IsWarnedAndSkipped()
    {
        List<string> warnings = [];

        ParticleManifest.MapFiles(
                Map,
                Reader(new() { [MapManifest] = "m { \"file\" \"particles/a.pcf\" \"bogus\" \"particles/b.pcf\" }" }),
                _ => null,
                warnings.Add)
            .ShouldBe(["particles/a.pcf"]);
        warnings.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **The cap counts ACCEPTED entries and stops the walk** (`:205-210`): checked before each subkey, one warning, then
    /// `break`. One rejected entry first and 65 valid after: the 64th valid is kept (kills counting subkeys), the 65th
    /// is not (kills `&gt;` for `&gt;=` and a `continue` for the `break`), and one warning (kills warning per entry).
    /// </remarks>
    [Test]
    public void MapFiles_SixtyFiveValidEntriesAfterARejectedOne_KeepsTheFirstSixtyFourAndWarnsOnce()
    {
        List<string> warnings = [];
        string[] valid = [.. Enumerable.Range(1, 65).Select(index => $"particles/p{index}.pcf")];

        IReadOnlyList<string> files = ParticleManifest.MapFiles(
            Map, Reader(new() { [MapManifest] = Manifest(["outside/x.pcf", .. valid]) }), _ => null, warnings.Add);

        files.Count.ShouldBe(ParticleManifest.MaximumMapEntries);
        files[^1].ShouldBe("particles/p64.pcf");
        files.ShouldNotContain("particles/p65.pcf");
        warnings.Count.ShouldBe(2, "one for the rejected entry, one for the cap");
    }

    /// <remarks>
    /// **Six past the cap, so stopping and going on differ** (`:205-210`, `break`): with 65 only one entry is dropped and
    /// both read the same. Kills `return false` → `return true` (the walk goes on, warning per dropped entry).
    /// </remarks>
    [Test]
    public void MapFiles_SeventyValidEntries_KeepsSixtyFourAndWarnsAboutTheCapOnce()
    {
        List<string> warnings = [];
        string[] valid = [.. Enumerable.Range(1, 70).Select(index => $"particles/p{index}.pcf")];

        IReadOnlyList<string> files = ParticleManifest.MapFiles(
            Map, Reader(new() { [MapManifest] = Manifest(valid) }), _ => null, warnings.Add);

        files.ShouldBe(valid[..ParticleManifest.MaximumMapEntries]);
        warnings.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A manifest cut off inside a quoted value keeps every entry, the cut one running to the end of the file, and
    /// nothing is warned.** The unclosed quote reads to end of file because `KeyValuesTokens` is `ReadToken` (B524). This
    /// predicts the stream reader's whole answer; whether `KeyValues::LoadFromFile` then refuses the unbalanced file and
    /// falls through to the next candidate is not modelled (finding 81). A reader change that drops or invents an entry
    /// is seen.
    /// </remarks>
    [Test]
    public void MapFiles_AManifestCutOffInsideAValue_KeepsTheCutEntryToTheEndOfTheFile()
    {
        List<string> warnings = [];

        ParticleManifest.MapFiles(
                Map,
                Reader(new() { [MapManifest] = "m\n{\n\t\"file\" \"particles/a.pcf\"\n\t\"file\" \"particles/b" }),
                _ => null,
                warnings.Add)
            .ShouldBe(["particles/a.pcf", "particles/b"]);
        warnings.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A `file` key that opens a block is rejected as outside `particles/`, and its contents are not entries.**
    /// `sub->GetString()` on a block is the default `""` (`KeyValues::GetString`, `KeyValues.cpp:1409-1460`, the
    /// `default:` case), `V_StripFilename("")` leaves it empty, and the folder test warns (`particle_parse.cpp:226`). The
    /// loop walks direct subkeys only, so the block's own `file` is never seen.
    /// </remarks>
    [Test]
    public void MapFiles_AFileKeyOpeningABlock_IsWarnedAndItsContentsAreNotEntries()
    {
        List<string> warnings = [];

        ParticleManifest.MapFiles(
                Map,
                Reader(new()
                {
                    [MapManifest] = "m\n{\n\t\"file\"\n\t{\n\t\t\"file\" \"particles/inner.pcf\"\n\t}\n\t\"file\" \"particles/a.pcf\"\n}\n",
                }),
                _ => null,
                warnings.Add)
            .ShouldBe(["particles/a.pcf"]);
        warnings.Count.ShouldBe(1);
    }

    // ---- the two passes a level load makes ----

    /// <remarks>
    /// **The two passes are handed two different names.** `ReloadParticleEffects` takes `Q_FileBase` of the level name
    /// (`particle_parse.cpp:159`): after the last separator, up to the LAST dot (`V_FileBase`, `strtools.cpp:1850`).
    /// `LevelInitPreEntity`'s name comes from the engine's own cut (`engine.dll` `FUN_18008dd50`, disassembled): after the
    /// last `\` or `/`, up to the FIRST dot (its `.` search is a forward scan, `18031a82c`). A workshop level splits them.
    /// </remarks>
    [TestCase("cp_custom", "cp_custom", "cp_custom")]
    [TestCase("workshop/cp_x.ugc123", "cp_x.ugc123", "cp_x")]
    [TestCase("maps/workshop/cp_x.ugc123.bsp", "cp_x.ugc123", "cp_x")]
    [TestCase("maps\\koth_y.bsp", "koth_y", "koth_y")]
    [TestCase("F:\\Team Fortress 2\\tf\\maps\\cp_y.bsp", "cp_y", "cp_y")]
    public void MapNames_ALevel_GivesEachPassItsName(string level, string reload, string levelInit)
    {
        ParticleManifest.MapNames(level).ShouldBe((reload, levelInit));
    }

    /// <remarks>
    /// **An ordinary map: the second pass runs after the stock reload, so its definition of a stock name wins**
    /// (`cdll_client_int.cpp:1607,1619`).
    /// </remarks>
    [Test]
    public void Level_AnOrdinaryMapsDefinitionOfAStockName_Wins()
    {
        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Level(
            Stock(), "cp_custom", _ => null,
            Reader(new()
            {
                [MapManifest] = Manifest("particles/custom.pcf"),
                ["particles/custom.pcf"] = Pcf(("stock_fx", "map")),
            }),
            _ => { });

        Material(layered["stock_fx"]).ShouldBe("map");
    }

    /// <remarks>
    /// **A workshop map whose manifest only the FIRST pass finds**: that pass runs BEFORE the stock reload
    /// (`particle_parse.cpp:161,166`), so the stock reload takes back every stock name and only the map's own names
    /// survive; the second pass looks for `maps/cp_x_particles.txt`, which is not there. Kills a single pass under either
    /// name.
    /// </remarks>
    [Test]
    public void Level_AWorkshopManifestOnlyTheFirstPassFinds_KeepsItsOwnNamesAndLosesStockOnes()
    {
        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Level(
            Stock(), "workshop/cp_x.ugc123", _ => null,
            Reader(new()
            {
                ["maps/cp_x.ugc123_particles.txt"] = Manifest("particles/custom.pcf"),
                ["particles/custom.pcf"] = Pcf(("stock_fx", "map"), ("unique_fx", "map")),
            }),
            _ => { });

        Material(layered["stock_fx"]).ShouldBe("stock");
        Material(layered["unique_fx"]).ShouldBe("map");
    }

    /// <remarks>The memory rule (finding 80), through both passes: no manifest is the shared instance itself.</remarks>
    [Test]
    public void Level_NoManifest_ReturnsTheSharedSetItself()
    {
        IReadOnlyDictionary<string, ParticleSystem> shared = Stock();

        ParticleManifest.Level(shared, "workshop/cp_x.ugc123", _ => null, _ => null, _ => { }).ShouldBeSameAs(shared);
    }

    // ---- layering ----

    /// <remarks>
    /// **No accepted entry is the shared set ITSELF** — the control for B554 and the memory rule of finding 80: a map
    /// without its own particles costs no copy. Kills building a copy unconditionally.
    /// </remarks>
    [Test]
    public void Layer_NoFiles_ReturnsTheSharedSetItself()
    {
        IReadOnlyDictionary<string, ParticleSystem> shared = Stock();

        ParticleManifest.Layer(shared, [], _ => null, _ => { }).ShouldBeSameAs(shared);
    }

    /// <remarks>The map's `.pcf`, read through GAME (pak first), adds its effect over every stock one.</remarks>
    [Test]
    public void Layer_AMapFile_AddsItsEffectAndKeepsTheStockOnes()
    {
        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            Stock(), ["!particles/custom.pcf"], Reader(new() { ["particles/custom.pcf"] = Pcf(("custom_fx", "map")) }), _ => { });

        Material(layered["custom_fx"]).ShouldBe("map");
        Material(layered["stock_fx"]).ShouldBe("stock");
    }

    /// <remarks>
    /// **A repeated name is REPLACED** — `AddParticleSystem` finds the symbol, destroys the old definition and assigns
    /// the new (`000dcd90`), and the map's pass runs after the stock reload (`cdll_client_int.cpp:1607,1619`), so the
    /// map's wins. The comparison folds case (`CUtlStringMap( caseInsensitive = true )`, `000db680`). Kills `TryAdd`.
    /// </remarks>
    [Test]
    public void Layer_AMapDefinitionOfAStockName_WinsWhateverItsCase()
    {
        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            Stock(), ["particles/custom.pcf"], Reader(new() { ["particles/custom.pcf"] = Pcf(("STOCK_FX", "map")) }), _ => { });

        Material(layered["stock_fx"]).ShouldBe("map");
        layered.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **Two maps on one install see only their own, and the shared set is not written into** — the hash of the
    /// shared set's names and definitions before and after. Kills layering into the shared dictionary.
    /// </remarks>
    [Test]
    public void Layer_TwoMapsOnOneShared_SeeOnlyTheirOwnAndLeaveTheSharedSetUnchanged()
    {
        IReadOnlyDictionary<string, ParticleSystem> shared = Stock();
        int before = Hash(shared);

        IReadOnlyDictionary<string, ParticleSystem> first = ParticleManifest.Layer(
            shared, ["particles/a.pcf"], Reader(new() { ["particles/a.pcf"] = Pcf(("a_fx", "a"), ("stock_fx", "a")) }), _ => { });
        IReadOnlyDictionary<string, ParticleSystem> second = ParticleManifest.Layer(
            shared, ["particles/b.pcf"], Reader(new() { ["particles/b.pcf"] = Pcf(("b_fx", "b")) }), _ => { });

        first.ContainsKey("b_fx").ShouldBeFalse();
        second.ContainsKey("a_fx").ShouldBeFalse();
        Material(second["stock_fx"]).ShouldBe("stock");
        Hash(shared).ShouldBe(before);
        shared.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A missing file and one that declares nothing are each logged and passed over** — `Particles: Missing '%s'` and
    /// `Unable to read particle definition` (`000e4f50`, `000e5150`), and the loop goes on to the next file.
    /// </remarks>
    [Test]
    public void Layer_AMissingFileAndAnUnreadableOne_AreLoggedAndTheNextIsStillRead()
    {
        List<string> warnings = [];

        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            Stock(),
            ["particles/missing.pcf", "particles/garbage.pcf", "particles/good.pcf"],
            Reader(new()
            {
                ["particles/garbage.pcf"] = "not a dmx file",
                ["particles/good.pcf"] = Pcf(("good_fx", "good")),
            }),
            warnings.Add);

        layered.ContainsKey("good_fx").ShouldBeTrue();
        warnings.Count.ShouldBe(2);
    }

    /// <remarks>
    /// **A missing file, then one whose read throws, then a good one: the good one is still read.** Kills `continue` →
    /// `break` in the `InvalidDataException` catch, which the throw-only test below cannot tell from going on.
    /// </remarks>
    [Test]
    public void Layer_AMissingThenAThrowingThenAGoodFile_StillReadsTheGoodOne()
    {
        List<string> warnings = [];

        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            Stock(),
            ["particles/missing.pcf", "particles/throws.pcf", "particles/good.pcf"],
            path => path switch
            {
                "particles/throws.pcf" => throw new System.IO.InvalidDataException("bad entry"),
                "particles/good.pcf" => Pcf(("good_fx", "good")),
                _ => null,
            },
            warnings.Add);

        layered.ContainsKey("good_fx").ShouldBeTrue();
        warnings.Count.ShouldBe(2);
    }

    /// <remarks>A reader that throws (a malformed pak entry, `PakFile.ReadFile`) is logged, not thrown out of the load.</remarks>
    [Test]
    public void Layer_AReaderThatThrows_IsLoggedAndTheLoadGoesOn()
    {
        List<string> warnings = [];

        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            Stock(), ["particles/bad.pcf"], _ => throw new System.IO.InvalidDataException("bad entry"), warnings.Add);

        layered.Count.ShouldBe(1);
        warnings.Count.ShouldBe(1);
    }

    /// <remarks>
    /// **B555: the install's own manifest, two files declaring one name — the LATER one wins.** `ParseParticleEffects`
    /// reads down the list (`particle_parse.cpp:104-107`) and each `AddParticleSystem` replaces (`000dcd90`). The stock
    /// set is built by this same call over an empty base. Kills `TryAdd`.
    /// </remarks>
    [Test]
    public void Layer_TwoFilesDeclaringOneName_TheLaterWins()
    {
        IReadOnlyDictionary<string, ParticleSystem> layered = ParticleManifest.Layer(
            new Dictionary<string, ParticleSystem>(),
            ["particles/first.pcf", "particles/second.pcf"],
            Reader(new()
            {
                ["particles/first.pcf"] = Pcf(("shared_fx", "first")),
                ["particles/second.pcf"] = Pcf(("shared_fx", "second")),
            }),
            _ => { });

        Material(layered["shared_fx"]).ShouldBe("second");
    }

    /// <remarks>
    /// **B555 inside one file too**: `ReadParticleDefinitions` calls `AddParticleSystem` for each element of
    /// `particleSystemDefinitions` in order (`000e5150`), so a name declared twice keeps the second. Kills the
    /// `ContainsKey` skip in `ParticleSystems.Read`.
    /// </remarks>
    [Test]
    public void Read_ANameDeclaredTwiceInOneFile_KeepsTheSecond()
    {
        Material(ParticleSystems.Read(Pcf(("twice_fx", "first"), ("TWICE_FX", "second")))["twice_fx"]).ShouldBe("second");
    }

    /// <remarks>
    /// **"Later" is the ROOT's `particleSystemDefinitions` ARRAY order, not the element table's.**
    /// `ReadParticleDefinitions` (`000e5150`, disassembled) unserializes the root, reads its
    /// `particleSystemDefinitions` attribute (type 0xf, an element array) and adds each in array order. Array `[B, A]`
    /// over elements stored `A, B`, both named alike: the engine keeps A's. Kills walking the element table.
    /// </remarks>
    [Test]
    public void Read_ArrayOrderDiffersFromElementOrder_TheArraysLaterWins()
    {
        Material(ParticleSystems.Read(SyntheticPcf.Rooted([1, 0], ("same_fx", "a"), ("same_fx", "b")))["same_fx"])
            .ShouldBe("a");
    }

    /// <remarks>
    /// **A definition the root's array does not name is not read.** Same reading as above; kills walking every element.
    /// </remarks>
    [Test]
    public void Read_ADefinitionTheArrayOmits_IsNotRead()
    {
        ParticleSystems.Read(SyntheticPcf.Rooted([0], ("named_fx", "a"), ("orphan_fx", "b"))).Keys.ShouldBe(["named_fx"]);
    }

    /// <remarks>
    /// **A root that IS a definition is added once and the read returns** (`000e5150`: the type compare against
    /// `DmeParticleSystemDefinition`, then `AddParticleSystem( root )` and `return 1`). Elements after it are not read.
    /// </remarks>
    [Test]
    public void Read_ARootThatIsADefinition_IsTheOnlyOneRead()
    {
        ParticleSystems.Read(SyntheticPcf.RootIsADefinition(("root_fx", "a"), ("after_fx", "b"))).Keys.ShouldBe(["root_fx"]);
    }

    // ---- fixtures ----

    private static IReadOnlyDictionary<string, ParticleSystem> Stock() => ParticleSystems.Read(Pcf(("stock_fx", "stock")));

    private static string Material(ParticleSystem system) => SyntheticPcf.Material(system);

    private static int Hash(IReadOnlyDictionary<string, ParticleSystem> systems)
    {
        HashCode hash = new();

        foreach ((string name, ParticleSystem one) in systems.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(name);
            hash.Add(Material(one));
        }

        return hash.ToHashCode();
    }

    private static string Manifest(params string[] files) =>
        "particles_manifest\n{\n" + string.Concat(files.Select(file => $"\t\"file\" \"{file}\"\n")) + "}\n";

    /// <summary>Files by path, each a text document or a binary one.</summary>
    private static Func<string, byte[]?> Reader(Dictionary<string, object> files) =>
        path => files.TryGetValue(path, out object? one) ? one as byte[] ?? Encoding.UTF8.GetBytes((string)one) : null;

    private static byte[] Pcf(params (string Name, string Material)[] definitions) => SyntheticPcf.Of(definitions);
}
