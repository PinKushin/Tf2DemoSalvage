using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A level's sounds and sound scripts are read through its pakfile first (B485).</summary>
/// <remarks>
/// **Read from published source.** Every sound and every level sound script is opened through the <c>"GAME"</c> search
/// path (<c>SoundEmitterSystem.cpp:280-311</c>: <c>filesystem-&gt;FileExists( scriptfile, "GAME" )</c>), at whose head
/// the engine mounts the loaded map's pakfile — the precedent B465 established for soundscapes. So a wave the map ships
/// SHADOWS the install's copy of the same path, and a wave only the map ships EXTENDS it.
///
/// <c>CSoundEmitterSystem::LevelInitPreEntity</c> (<c>SoundEmitterSystem.cpp:258-315</c>) then adds the level's own
/// script with <c>AddSoundOverrides</c> — <c>maps/&lt;map&gt;_level_sounds.txt</c>, or on a map whose name holds
/// <c>mvm</c> four fixed MvM scripts — and <c>LevelShutdownPostEntity</c> calls <c>ClearSoundOverrides</c> (<c>:333-336</c>).
/// **That an override REPLACES a stock entry of the same name is read from the interface's comment, not its code**
/// (<c>isoundemittersystembase.h:257</c>, "override sound scripts for the mod with level specific overrides"); the
/// implementation is in the closed <c>soundemittersystem.dll</c>.
/// </remarks>
public sealed class SoundPakfileConformanceTests
{
    private const string Shared = "ambient/shared.wav";
    private const string PakOnly = "ambient/pak_only.wav";
    private const string InstallOnly = "ambient/install_only.wav";

    [Test]
    public void Sample_ThroughALevelsReader_ThePakfileShadowsTheInstallAndExtendsIt()
    {
        SoundCache cache = new(NullLogger.Instance);

        cache.Level(Pak().AheadOf(Install));

        cache.Sample(Shared).ShouldNotBeNull().SampleRate.ShouldBe(11025, "the pakfile's copy, not the install's");
        cache.Sample(PakOnly).ShouldNotBeNull().SampleRate.ShouldBe(11025, "a wave only the map ships opens");
        cache.Sample(InstallOnly).ShouldNotBeNull().SampleRate.ShouldBe(22050, "the install still answers behind it");
    }

    [Test]
    public void Level_TheNextLevel_ForgetsWhatThePreviousLevelsPakfileDecoded()
    {
        SoundCache cache = new(NullLogger.Instance);

        cache.Level(Pak().AheadOf(Install));
        cache.Sample(Shared).ShouldNotBeNull().SampleRate.ShouldBe(11025, "the control: the first level shadows it");

        cache.Level(Install);

        cache.Sample(Shared).ShouldNotBeNull().SampleRate.ShouldBe(22050, "a level without the pakfile hears the install");
        cache.Sample(PakOnly).ShouldBeNull("and the previous map's own wave is gone with it");
    }

    [Test]
    public void ForLevel_ALevelSoundsScript_ReplacesTheStockEntryAndAddsItsOwn()
    {
        SoundScriptCatalog stock = SoundScriptCatalog.Load(Text(new()
        {
            ["scripts/game_sounds_manifest.txt"] = "game_sounds_manifest\n{\n\t\"precache_file\"\t\"scripts/game_sounds.txt\"\n}\n",
            ["scripts/game_sounds.txt"] = Entry("Stock.Kept", "stock/kept.wav") + Entry("Stock.Replaced", "stock/old.wav"),
        }));

        SoundScriptCatalog level = stock.ForLevel(
            Text(new()
            {
                ["maps/cp_test_level_sounds.txt"] = Entry("Stock.Replaced", "map/new.wav") + Entry("Map.Own", "map/own.wav"),
            }),
            "cp_test");

        level.Resolve("Stock.Kept").Waves.ShouldBe(["sound/stock/kept.wav"]);
        level.Resolve("Stock.Replaced").Waves.ShouldBe(["sound/map/new.wav"], "the level's script overrides");
        level.Resolve("Map.Own").Waves.ShouldBe(["sound/map/own.wav"]);
        level.Scripts.ShouldBe(["scripts/game_sounds.txt", "maps/cp_test_level_sounds.txt"]);

        // ClearSoundOverrides: the stock catalog the next level starts from is untouched.
        stock.Resolve("Stock.Replaced").Waves.ShouldBe(["sound/stock/old.wav"]);
        stock.Entries.ShouldNotContainKey("Map.Own");
    }

    [Test]
    public void ForLevel_AnMvmMap_ReadsTheFourMvmScriptsInTheEnginesOrderAndNotItsOwnName()
    {
        List<string> asked = [];
        SoundScriptCatalog level = SoundScriptCatalog.Load(Text([])).ForLevel(
            path =>
            {
                asked.Add(path);
                return Encoding.UTF8.GetBytes(Entry(path, "x.wav"));
            },
            "MVM_Test");

        level.Scripts.ShouldBe(
        [
            "scripts/mvm_level_sounds.txt",
            "scripts/mvm_level_sound_tweaks.txt",
            "scripts/game_sounds_vo_mvm.txt",
            "scripts/game_sounds_vo_mvm_mighty.txt",
        ]);
        asked.ShouldNotContain("maps/mvm_test_level_sounds.txt");
    }

    /// <remarks>
    /// **<c>GetCleanMapName</c>** (<c>util_shared.cpp:1631-1674</c>, TF branch): a <c>maps/workshop/</c> name loses the
    /// <c>workshop/</c> and everything from <c>.ugc</c>, so a workshop map reads the same script name a stock copy would.
    /// Either separator — <c>Q_FixSlashes</c> runs first. Then <c>Q_StripExtension</c> drops a trailing extension.
    /// </remarks>
    [TestCase("workshop/cp_foo.ugc123456", "maps/cp_foo_level_sounds.txt")]
    [TestCase("workshop\\cp_foo.ugc1", "maps/cp_foo_level_sounds.txt")]
    [TestCase("cp_foo", "maps/cp_foo_level_sounds.txt")]
    [TestCase("cp_foo_rc1.bsp", "maps/cp_foo_rc1_level_sounds.txt")]
    [TestCase("cp_workshop/cp_foo.ugc1", "maps/cp_workshop/cp_foo_level_sounds.txt")]
    public void ForLevel_AMapName_ReadsTheLevelScriptOfItsCleanName(string mapName, string script)
    {
        List<string> asked = [];

        SoundScriptCatalog.Load(Text([])).ForLevel(
            path =>
            {
                asked.Add(path);
                return null;
            },
            mapName);

        asked.ShouldBe([script]);
    }

    /// <remarks>
    /// **Disassembly** (<c>soundemittersystem.dll</c> x64, <c>CSoundEmitterSystemBase::AddSoundsFromFile</c> at
    /// <c>180004870</c>): with the override flag set, a name already present is parsed into a NEW entry stored over the
    /// slot (<c>180004b58</c>) whether the old one was stock or itself an override (<c>180004a14</c>, the "duplicated
    /// replacements" count) — so a later override script's entry wins over an earlier one's.
    /// </remarks>
    [Test]
    public void ForLevel_ANameInTwoMvmScripts_IsTheLaterScripts()
    {
        SoundScriptCatalog level = SoundScriptCatalog.Load(Text([])).ForLevel(
            Text(new()
            {
                ["scripts/mvm_level_sounds.txt"] = Entry("MVM.Shared", "first.wav"),
                ["scripts/game_sounds_vo_mvm.txt"] = Entry("MVM.Shared", "later.wav"),
            }),
            "mvm_test");

        level.Resolve("MVM.Shared").Waves.ShouldBe(["sound/later.wav"]);
    }

    /// <summary>The map's pakfile: its own copy of the shared wave and one only it has, both at 11025 Hz.</summary>
    private static PakFile Pak() => PakFile.Read(PakZip.Of(new()
    {
        ["sound/" + Shared] = Wave(11025),
        ["sound/" + PakOnly] = Wave(11025),
    }));

    /// <summary>The install: the shared wave and one only it has, both at 22050 Hz.</summary>
    private static byte[]? Install(string path) =>
        path.Equals("sound/" + Shared, StringComparison.OrdinalIgnoreCase) ||
        path.Equals("sound/" + InstallOnly, StringComparison.OrdinalIgnoreCase)
            ? Wave(22050)
            : null;

    private static byte[] Wave(int rate) => SoundCacheTests.WaveBytes(16, 1, rate, [0x01, 0x00]);

    private static string Entry(string name, string wave) => $"\"{name}\"\n{{\n\t\"wave\"\t\"{wave}\"\n}}\n";

    private static Func<string, byte[]?> Text(Dictionary<string, string> files) =>
        path => files.TryGetValue(path, out string? text) ? Encoding.UTF8.GetBytes(text) : null;
}
