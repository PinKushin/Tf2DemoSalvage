using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The manifest decides which soundscripts exist, and what a precached name resolves to.
/// </summary>
/// <remarks>
/// **`scripts/game_sounds_manifest.txt` is the authority, and globbing is not equivalent to it.**
/// The SDK states the rule from the other side, in both `baseentity.h` and `c_baseentity.h`:
/// *"These files need to be listed in scripts/game_sounds_manifest.txt"*. A script that is not
/// listed is not loaded.
///
/// **That distinction is not academic in TF2's shipped data.** The install carries 21
/// `game_sounds*.txt` files; the manifest lists 16, comments three out with <c>//</c>, and never
/// mentions `game_sounds_footsteps.txt` or `game_sounds_vo_phonemes.txt` at all. A catalog built by
/// globbing would load entries the engine does not have — including two MvM voice scripts Valve
/// deliberately disabled — and every one of those would resolve to a plausible sound.
/// </remarks>
public sealed class SoundScriptCatalogConformanceTests
{
    [Test]
    public void Load_TheManifest_DecidesWhichScriptsAreRead()
    {
        // The manifest's two key names, both of which name a script to load. `preload_file` differs
        // from `precache_file` in WHEN the engine pulls the samples into memory, not in whether the
        // entries exist — so a reader that handled only `precache_file` would lose
        // game_sounds_player.txt, which is the one script every footstep and pain sound is in.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake(new()
        {
            ["scripts/game_sounds_manifest.txt"] = """
                game_sounds_manifest
                {
                	"precache_file"		"scripts/game_sounds_weapons.txt"
                	"preload_file"  	"scripts/game_sounds_player.txt"
                }
                """,
            ["scripts/game_sounds_weapons.txt"] = Entry("Weapon.Fire", "weapons/fire.wav"),
            ["scripts/game_sounds_player.txt"] = Entry("Player.Hurt", "player/hurt.wav"),
        }));

        catalog.Scripts.Count.ShouldBe(2);
        catalog.Entries.ShouldContainKey("Weapon.Fire");
        catalog.Entries.ShouldContainKey("Player.Hurt", "preload_file names a script too");
    }

    [Test]
    public void Load_ACommentedOutEntry_IsNotRead()
    {
        // **Valve ships three of these**, and they are not decoration: two MvM voice scripts and
        // mvm_level_sounds.txt are commented out in the shipped manifest. Reading them would add
        // entries the engine does not have, and nothing downstream could tell the difference —
        // a resolved sound plays whether or not the engine would have had it.
        //
        // **The extra token on the commented line is deliberate, and the test is worthless without
        // it.** Written in Valve's exact shape — `//` then the key then the path — this test PASSED
        // with comment handling sabotaged, because an unhandled `//` becomes a token itself and
        // shifts the pairing: `("//", "precache_file")` then `"scripts/disabled.txt"` orphaned in
        // key position, so the script is not loaded either way and the assertion cannot tell the
        // two worlds apart. That is a wrong CONDITION, not a weak assertion, so the input is what
        // changes. With one more token ahead of the key, an unhandled comment pairs
        // `("//", "x")` and then `("precache_file", "scripts/disabled.txt")` — which loads it.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake(new()
        {
            ["scripts/game_sounds_manifest.txt"] = """
                game_sounds_manifest
                {
                	"precache_file"		"scripts/live.txt"
                //	x	"precache_file"		"scripts/disabled.txt"
                }
                """,
            ["scripts/live.txt"] = Entry("Live.Sound", "a/live.wav"),
            ["scripts/disabled.txt"] = Entry("Disabled.Sound", "a/disabled.wav"),
        }));

        catalog.Entries.ShouldContainKey("Live.Sound");

        // The control, and the whole point of the test: the file EXISTS and is readable. If the
        // reader ignored comments, it would load fine and nothing would look wrong.
        catalog.Entries.ShouldNotContainKey("Disabled.Sound");
        catalog.Scripts.Count.ShouldBe(1);
    }

    [Test]
    public void Load_AListedScriptThatIsAbsent_IsSkippedRatherThanFatal()
    {
        // The shipped manifest lists `scripts/game_sounds_mvm.txt` and others that a partial or
        // content-stripped install may not have. One missing script must not cost the other fifteen.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake(new()
        {
            ["scripts/game_sounds_manifest.txt"] = """
                game_sounds_manifest
                {
                	"precache_file"		"scripts/present.txt"
                	"precache_file"		"scripts/absent.txt"
                }
                """,
            ["scripts/present.txt"] = Entry("Present.Sound", "a/present.wav"),
        }));

        catalog.Entries.ShouldContainKey("Present.Sound");
        catalog.Scripts.Count.ShouldBe(1, "the absent one is skipped, not fatal");
    }

    [Test]
    public void Load_NoManifestAtAll_IsEmptyRatherThanThrowing()
    {
        // A machine without TF2 installed, which is the same case `GameArchives` handles by
        // returning nothing: a viewer with no game content still opens demos.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake([]));

        catalog.Entries.ShouldBeEmpty();
        catalog.Scripts.ShouldBeEmpty();
        catalog.Resolve("Anything.At.All").Waves.ShouldBeEmpty();
    }

    [Test]
    public void Resolve_AScriptName_YieldsTheEntrysWavesAndParameters()
    {
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake(new()
        {
            ["scripts/game_sounds_manifest.txt"] =
                "game_sounds_manifest\n{\n\t\"precache_file\"\t\"scripts/a.txt\"\n}\n",
            ["scripts/a.txt"] = """
                "Weapon_Shotgun.Single"
                {
                	"channel"		"CHAN_WEAPON"
                	"soundlevel"	"SNDLVL_95dB"
                	"pitch"			"95, 105"
                	"wave"			"weapons/shotgun_shoot.wav"
                }
                """,
        }));

        ResolvedSound sound = catalog.Resolve("Weapon_Shotgun.Single");

        sound.FromScript.ShouldBeTrue();
        sound.Channel.ShouldBe(1, "CHAN_WEAPON");
        sound.SoundLevel.ShouldBe(95);
        sound.Pitch.High.ShouldBe(105f);
        sound.Waves.Count.ShouldBe(1);
        sound.Waves[0].ShouldBe("sound/weapons/shotgun_shoot.wav");
    }

    [Test]
    public void Resolve_APlainPath_YieldsValvesDefaultsRatherThanNothing()
    {
        // **A precached name is a path OR a script key, and both must resolve.** `svc_Sounds`
        // carries plenty of raw paths, and a resolver that only knew script keys would silently
        // drop them — which sounds exactly like a sound that was never played.
        //
        // The parameters for a raw path are `CSoundParameters`' defaults, since no script states
        // otherwise.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake([]));

        ResolvedSound sound = catalog.Resolve("ambient/water/water_splash1.wav");

        sound.FromScript.ShouldBeFalse();
        sound.Channel.ShouldBe(0, "CHAN_AUTO");
        sound.SoundLevel.ShouldBe(75, "SNDLVL_NORM");
        sound.Volume.Low.ShouldBe(1f, "VOL_NORM");
        sound.Pitch.Low.ShouldBe(100f, "PITCH_NORM");
        sound.Waves.Count.ShouldBe(1);
        sound.Waves[0].ShouldBe("sound/ambient/water/water_splash1.wav");
    }

    [Test]
    public void Resolve_ANameLedBySoundCharacters_StripsThemFromThePath()
    {
        // soundchars.h's prefixes are instructions, not part of the filename. A path kept verbatim
        // would fail to open; the characters are carried separately so the mixer can still act on
        // them.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake([]));

        ResolvedSound sound = catalog.Resolve(")#weapons/fx/nearmiss/bullet.wav");

        sound.Waves[0].ShouldBe("sound/weapons/fx/nearmiss/bullet.wav");
        sound.Characters.ShouldContain(')');
        sound.Characters.ShouldContain('#');
    }

    [Test]
    public void Resolve_AScriptWaveLedBySoundCharacters_StripsThemToo()
    {
        // Shipped entries carry them inside the script as well:
        // `"wave" ">weapons/fx/nearmiss/bulletLtoR08.wav"`. Handling the precached name but not the
        // script's own wave would leave exactly those sounds unopenable — and they are the ones
        // whose prefix says they matter spatially.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake(new()
        {
            ["scripts/game_sounds_manifest.txt"] =
                "game_sounds_manifest\n{\n\t\"precache_file\"\t\"scripts/a.txt\"\n}\n",
            ["scripts/a.txt"] = Entry("Near.Miss", ">weapons/fx/nearmiss/a.wav"),
        }));

        ResolvedSound sound = catalog.Resolve("Near.Miss");

        sound.Waves[0].ShouldBe("sound/weapons/fx/nearmiss/a.wav");
        sound.Characters.ShouldContain('>');
    }

    [Test]
    public void Resolve_AnUnknownName_IsEmptyRatherThanAGuess()
    {
        // A name that is neither a script key nor plausibly a path resolves to nothing, so a caller
        // can report it. Guessing a path would produce a file-not-found at play time instead, which
        // is the same failure one layer later and harder to attribute.
        SoundScriptCatalog catalog = SoundScriptCatalog.Load(Fake([]));

        catalog.Resolve("Some.Script.Key.That.Does.Not.Exist").Waves.ShouldBeEmpty();
        catalog.Resolve("").Waves.ShouldBeEmpty();
    }

    [Test]
    public void Load_TheManifestTf2Ships_ListsFewerScriptsThanTheInstallCarries()
    {
        // **The output-level assertion, and it measures the gap globbing would hide.** The install
        // carries 21 game_sounds*.txt files. The manifest lists fewer, comments three out, and never
        // names footsteps or phonemes at all.
        //
        // Asserting `<` rather than an exact pair of numbers: both counts are Valve's to change in
        // any update, and the claim being made is that they DIFFER — which is what makes reading the
        // manifest necessary rather than merely tidy.
        if (GameInstall.Vpk("tf2_misc") is not { } directory)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        VpkArchive archive = VpkArchive.Open(directory);

        int shipped = archive.Paths.Count(
            p => p.Contains("GAME_SOUNDS", StringComparison.OrdinalIgnoreCase)
              && p.EndsWith(".TXT", StringComparison.OrdinalIgnoreCase)
              && !p.Contains("MANIFEST", StringComparison.OrdinalIgnoreCase));

        SoundScriptCatalog catalog = SoundScriptCatalog.Load(
            path => archive.ReadFile(path.ToUpperInvariant()));

        TestContext.Out.WriteLine(
            $"manifest lists {catalog.Scripts.Count} of {shipped} shipped scripts, " +
            $"{catalog.Entries.Count} entries");

        foreach (string script in catalog.Scripts)
        {
            TestContext.Out.WriteLine($"  {script}");
        }

        catalog.Scripts.Count.ShouldBeGreaterThan(5, "the manifest was read at all");
        catalog.Entries.Count.ShouldBeGreaterThan(1000, "and its scripts loaded");

        catalog.Scripts.Count.ShouldBeLessThan(
            shipped, "the install carries scripts the manifest does not list");

        // The disabled MvM voice scripts are the specific ones Valve commented out, so name them
        // rather than trusting the count alone — a count can come out right for the wrong reason.
        catalog.Scripts.ShouldNotContain(
            script => script.Contains("vo_mvm_mighty", StringComparison.OrdinalIgnoreCase),
            "game_sounds_vo_mvm_mighty.txt is commented out in the shipped manifest");
    }

    /// <summary>One soundscript entry as text, in the shipped syntax.</summary>
    private static string Entry(string name, string wave) =>
        $"\"{name}\"\n{{\n\t\"wave\"\t\"{wave}\"\n}}\n";

    /// <summary>A reader over an in-memory set of files, standing in for the game's archives.</summary>
    /// <remarks>
    /// **The catalog takes a delegate rather than a <c>GameArchives</c>**, which is what lets these
    /// cases exist at all: the commented-out entry, the absent script and the empty install are each
    /// a specific arrangement of files, and none of them can be produced from a real install.
    /// `docs/memory/author-the-specimen-the-corpus-lacks.md` — a case the shipped data does not
    /// contain can be written rather than hunted for.
    ///
    /// **This helper carried a real bug, and the absent-script test is what caught it.** Written to
    /// return `ReadOnlyMemory&lt;byte&gt;?`, the ternary took `byte[]` as its natural type, so the
    /// `null` branch converted to an EMPTY memory rather than to no memory at all — and every absent
    /// file arrived as a present, empty one. `Scripts.Count` came back 2 against a single file that
    /// existed. The signature is `byte[]?` now, which cannot express the mistake.
    /// </remarks>
    private static Func<string, byte[]?> Fake(Dictionary<string, string> files) =>
        path => files.TryGetValue(path, out string? text)
            ? Encoding.UTF8.GetBytes(text)
            : null;
}
