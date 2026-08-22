using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>What a precached sound name turns out to be: files to play, and how to play them.</summary>
/// <param name="Name">The precached name this came from.</param>
/// <param name="Waves">
/// Full content paths, ready to open. Empty when the name resolved to nothing.
/// </param>
/// <param name="Characters">
/// The <c>soundchars.h</c> prefixes that led the name or its wave, kept for the mixer.
/// </param>
/// <param name="Channel">The channel, <c>CHAN_AUTO</c> when nothing said otherwise.</param>
/// <param name="Volume">Volume, <c>VOL_NORM</c> when nothing said otherwise.</param>
/// <param name="Pitch">Pitch percentage, <c>PITCH_NORM</c> when nothing said otherwise.</param>
/// <param name="SoundLevel">The <c>soundlevel_t</c> the attenuation is computed from.</param>
/// <param name="FromScript">
/// Whether a soundscript entry supplied these, or they are Valve's defaults for a raw path.
/// </param>
/// <remarks>
/// **<see cref="FromScript"/> exists so a caller can tell "the defaults applied" from "a script
/// chose these values".** They are not the same claim, and every field here can hold a default that
/// looks exactly like a decision — which is the shape
/// `docs/memory/sentinels-conflate-unknown-with-answer.md` warns about.
/// </remarks>
public readonly record struct ResolvedSound(
    string Name,
    IReadOnlyList<string> Waves,
    IReadOnlyList<char> Characters,
    int Channel,
    SoundRange Volume,
    SoundRange Pitch,
    int SoundLevel,
    bool FromScript);

/// <summary>
/// Every soundscript the game loads, and what a precached name resolves to.
/// </summary>
/// <remarks>
/// **The manifest decides which scripts exist. Globbing `game_sounds*.txt` is not equivalent.** The
/// SDK states the rule from the other side, in `baseentity.h` and `c_baseentity.h`: *"These files
/// need to be listed in scripts/game_sounds_manifest.txt"*.
///
/// Measured on the shipped install, that difference is real rather than theoretical: TF2 carries 21
/// `game_sounds*.txt` files, and the manifest lists 16 of them, comments three out with <c>//</c>,
/// and never names `game_sounds_footsteps.txt` or `game_sounds_vo_phonemes.txt` at all. A catalog
/// built by globbing would hold entries the engine does not — including two MvM voice scripts Valve
/// deliberately disabled — and each would resolve to a perfectly plausible sound.
///
/// **Two manifest keys name a script, not one.** `precache_file` and `preload_file` differ in when
/// the engine pulls samples into memory, not in whether the entries exist. Reading only
/// `precache_file` loses `game_sounds_player.txt`, which holds the pain and footstep sounds.
///
/// **Content is reached through a delegate rather than an archive type**, which is dependency
/// inversion doing real work here: the catalog needs nothing from VPKs, and the cases worth testing
/// — a commented-out entry, a listed script that is absent, an install with no manifest — are
/// arrangements of files that cannot be produced from a real install.
/// </remarks>
public sealed class SoundScriptCatalog
{
    /// <summary>Where the manifest lives, relative to the game's content root.</summary>
    private const string ManifestPath = "scripts/game_sounds_manifest.txt";

    /// <summary>The folder every sound path is relative to.</summary>
    /// <remarks>
    /// **Neither a precached name nor a soundscript wave carries it.** Both are written relative to
    /// <c>sound/</c> — `weapons/shotgun_shoot.wav` is `sound/weapons/shotgun_shoot.wav` on disk —
    /// so a resolver that returned the name unchanged would hand every caller a path that does not
    /// open. Added once here rather than by each caller, so there is one place it can be wrong.
    /// </remarks>
    private const string SoundRoot = "sound/";

    private readonly Dictionary<string, SoundScriptEntry> _entries;
    private readonly List<string> _scripts;

    private SoundScriptCatalog(
        Dictionary<string, SoundScriptEntry> entries, List<string> scripts)
    {
        _entries = entries;
        _scripts = scripts;
    }

    /// <summary>Every entry across every script the manifest listed, keyed by name.</summary>
    public IReadOnlyDictionary<string, SoundScriptEntry> Entries => _entries;

    /// <summary>The scripts that were listed AND readable, in manifest order.</summary>
    public IReadOnlyList<string> Scripts => _scripts;

    /// <summary>Reads the manifest and every script it lists.</summary>
    /// <param name="read">
    /// Opens a content path such as <c>scripts/game_sounds.txt</c>, returning null when absent.
    /// </param>
    /// <remarks>
    /// **The delegate returns <c>byte[]?</c> and not <c>ReadOnlyMemory&lt;byte&gt;?</c>, which is
    /// not a stylistic choice.** `byte[]` converts implicitly to `ReadOnlyMemory&lt;byte&gt;`, so a
    /// lambda written `... ? bytes : null` takes `byte[]` as its natural type and a NULL array
    /// converts to an EMPTY memory — a nullable that has a value. Every absent file then arrives as
    /// a present, empty one, and `is not { }` never fires.
    ///
    /// That is not hypothetical: it was written that way here first, and the "a listed script that
    /// is absent is skipped" test caught it. `byte[]?` has no such conversion and cannot express the
    /// bug. Related: `docs/memory/nullable-pattern-on-a-struct-is-dead-code.md`.
    /// </remarks>
    /// <returns>The catalog; empty when there is no manifest to read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is null.</exception>
    /// <remarks>
    /// **A missing manifest is empty, not an error**, matching how `GameArchives` treats a missing
    /// install: someone reviewing demos without TF2 gets no sound rather than no viewer. A listed
    /// script that is absent is skipped for the same reason — a partial install must not cost the
    /// other fifteen scripts.
    /// </remarks>
    public static SoundScriptCatalog Load(Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        Dictionary<string, SoundScriptEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        List<string> scripts = [];

        if (read(ManifestPath) is not { } manifest)
        {
            return new SoundScriptCatalog(entries, scripts);
        }

        foreach (string path in Listed(manifest))
        {
            if (read(path) is not { } script)
            {
                continue;
            }

            scripts.Add(path);

            foreach ((string name, SoundScriptEntry entry) in SoundScript.Read(script))
            {
                // **First listed wins, matching the manifest's order.** A later script redefining a
                // name is the engine's override mechanism and the manifest order is the priority;
                // reversing this silently prefers whichever file happens to be read last.
                entries.TryAdd(name, entry);
            }
        }

        return new SoundScriptCatalog(entries, scripts);
    }

    /// <summary>The script paths a manifest names, in order.</summary>
    /// <remarks>
    /// Comments are the <see cref="KeyValuesReader"/>'s job, and it drops them — which is what keeps
    /// the three entries Valve commented out of the shipped manifest out of this list.
    /// </remarks>
    private static List<string> Listed(ReadOnlyMemory<byte> manifest)
    {
        List<string> paths = [];

        KeyValuesReader.Read(manifest.Span, (key, value, _) =>
        {
            if (value is { Length: > 0 } &&
                (key.Equals("precache_file", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("preload_file", StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(value);
            }

            return true;
        });

        return paths;
    }

    /// <summary>Resolves a precached name to files and parameters.</summary>
    /// <param name="precached">The name as <c>soundprecache</c> carries it.</param>
    /// <returns>
    /// What to play. <see cref="ResolvedSound.Waves"/> is empty when the name resolved to nothing.
    /// </returns>
    /// <remarks>
    /// **A precached name is a script key OR a path, and both occur**, so a resolver that knew only
    /// script keys would silently drop the raw paths — which is indistinguishable from a sound that
    /// was never played.
    ///
    /// **An unknown name resolves to nothing rather than to a guessed path.** Guessing turns a
    /// resolution failure into a file-not-found one layer later, where it is harder to attribute.
    /// </remarks>
    public ResolvedSound Resolve(string precached)
    {
        ArgumentNullException.ThrowIfNull(precached);

        SoundName parsed = SoundName.Parse(precached);

        if (_entries.TryGetValue(parsed.Path, out SoundScriptEntry entry))
        {
            List<string> waves = [];
            List<char> characters = [.. parsed.Characters];

            foreach (string wave in entry.Waves)
            {
                // **The script's own waves carry sound characters too**, so they are parsed the
                // same way rather than trusted as paths. Shipped entries include
                // `"wave" ">weapons/fx/nearmiss/bulletLtoR08.wav"`.
                SoundName inner = SoundName.Parse(wave);

                waves.Add(SoundRoot + inner.Path);

                foreach (char character in inner.Characters)
                {
                    if (!characters.Contains(character))
                    {
                        characters.Add(character);
                    }
                }
            }

            return new ResolvedSound(
                precached,
                waves,
                characters,
                entry.Channel,
                entry.Volume,
                entry.Pitch,
                entry.SoundLevel,
                FromScript: true);
        }

        // Not a script key. A name carrying an audio extension is a path; anything else is a script
        // key this catalog does not have, and saying so is more useful than inventing a file.
        string[] path = LooksLikeAPath(parsed.Path) ? [SoundRoot + parsed.Path] : [];

        return new ResolvedSound(
            precached,
            path,
            parsed.Characters,
            SoundScript.AutoChannel,
            new SoundRange(SoundScript.NormalVolume, SoundScript.NormalVolume),
            new SoundRange(SoundScript.NormalPitch, SoundScript.NormalPitch),
            SoundScript.NormalSoundLevel,
            FromScript: false);
    }

    /// <summary>Whether a name is a file rather than a script key.</summary>
    /// <remarks>
    /// **By extension, because that is the only thing that distinguishes them.** Script keys are
    /// dotted (`Weapon_Shotgun.Single`) and so are paths, so "contains a dot" cannot decide it. The
    /// three extensions are what the engine's sound system opens.
    /// </remarks>
    private static bool LooksLikeAPath(string name) =>
        name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);
}
