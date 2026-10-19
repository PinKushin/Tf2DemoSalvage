using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Which <c>.pcf</c> files the game loads — <c>particles/particles_manifest.txt</c> (B415).</summary>
/// <remarks>
/// **`GetParticleManifest`, `particle_parse.cpp:65`**, which is the whole of it:
///
/// <code>
/// KeyValues *manifest = new KeyValues( PARTICLES_MANIFEST_FILE );
/// if ( manifest-&gt;LoadFromFile( filesystem, PARTICLES_MANIFEST_FILE, "GAME" ) )
/// {
///     for ( KeyValues *sub = manifest-&gt;GetFirstSubKey(); sub != NULL; sub = sub-&gt;GetNextKey() )
///     {
///         if ( !Q_stricmp( sub-&gt;GetName(), "file" ) )
///         {
///             list.AddToTail( sub-&gt;GetString() );
///             continue;
///         }
///         Warning( "… Manifest '%s' with bogus file type '%s', expecting 'file'\n", … );
///     }
/// }
/// </code>
///
/// and `ParseParticleEffects` (`:94`) then reads every one of them.
///
/// **The `!` prefix is a precache marker and not part of the path.** The engine strips it —
/// `if ( pFile[0] == '!' ) pFile++;` (`particle_parse.cpp:130`) — and ninety-odd of TF2's hundred entries carry
/// one, so a reader that kept it would find no file at all and report the manifest as naming nothing that ships.
///
/// **Every entry is kept, including a repeat.** `buildingdamage.pcf` is listed twice in the shipped file; Valve's
/// loop does not deduplicate and neither does this. A duplicate NAME is replaced by the later definition
/// (`AddParticleSystem`, see <see cref="Layer"/>).
///
/// **A map's own manifest is read too — B554, closed.** `ParseParticleEffectsMap` (`:175`) layers
/// `maps/&lt;name&gt;_particles.txt` on top, capped at `PARTICLES_MANIFEST_MAX_MAP_ENTRIES` = 64, a cap Valve's own
/// comment explains was added because people *"drop bogus manifests into download directory to DoS rival community
/// maps"*: <see cref="MapFiles"/> and <see cref="Layer"/>, finding 81.
/// </remarks>
public static class ParticleManifest
{
    /// <summary>The manifest's path, as the engine asks for it.</summary>
    public const string Path = "particles/particles_manifest.txt";

    /// <summary>The only key the engine accepts in it.</summary>
    private const string FileKey = "file";

    /// <summary>The precache marker, stripped before the path is used.</summary>
    private const char PrecacheMarker = '!';

    /// <summary>Every <c>.pcf</c> the manifest names, in its own order.</summary>
    /// <param name="readFile">Opens a path out of the game's content, or answers null.</param>
    /// <returns>The paths, with the precache marker removed; empty when the manifest is absent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readFile"/> is null.</exception>
    /// <remarks>
    /// **Order is kept because the engine's is.** `ReadParticleConfigFile` runs down the list, so which file
    /// declares a name first is a fact about the manifest rather than about a dictionary's iteration.
    /// </remarks>
    public static IReadOnlyList<string> Files(Func<string, byte[]?> readFile)
    {
        ArgumentNullException.ThrowIfNull(readFile);

        // Stryker disable once : a mutant that empties the guard body leaves 'manifest' unassigned, CS0165 — B410.
        if (readFile(Path) is not { Length: > 0 } manifest)
        {
            return [];
        }

        List<string> files = [];

        KeyValuesReader.Read(manifest, (key, value, _) =>
        {
            if (string.Equals(key, FileKey, StringComparison.OrdinalIgnoreCase) &&
                value is { Length: > 0 })
            {
                files.Add(value[0] == PrecacheMarker ? value[1..] : value);
            }

            return true;
        });

        return files;
    }

    /// <summary><c>PARTICLES_MANIFEST_MAX_MAP_ENTRIES</c> (<c>particle_parse.cpp:31</c>).</summary>
    public const int MaximumMapEntries = 64;

    /// <summary>The first candidate, a manifest at the pakfile's root (<c>particle_parse.cpp:195</c>).</summary>
    public const string PakRootPath = "particles.txt";

    /// <summary>The only folder a map's entry may name (<c>particle_parse.cpp:219</c>).</summary>
    private const string MapFolder = "particles";

    /// <summary><c>maps/&lt;name&gt;_particles.txt</c> (<c>particle_parse.cpp:186</c>).</summary>
    /// <param name="mapName">The level's name, without folder or extension.</param>
    /// <returns>The manifest's path.</returns>
    /// <remarks>
    /// The engine lowers the name first (<c>Q_strlower</c>, <c>:160</c>); not repeated here because every reader this
    /// is handed folds case (<c>PakFile.Normalise</c>, the archives' lookup), so the lowering is unobservable.
    /// </remarks>
    public static string MapPath(string mapName)
    {
        ArgumentNullException.ThrowIfNull(mapName);

        return $"maps/{mapName}_particles.txt";
    }

    /// <summary>The <c>.pcf</c> files a map's own manifest names — <c>ParseParticleEffectsMap</c> (B554, finding 81).</summary>
    /// <param name="mapName">The level's name, without folder or extension.</param>
    /// <param name="readPak">Opens a path in the map's pakfile alone (the <c>"BSP"</c> search path), or answers null.</param>
    /// <param name="readGame">Opens a path through <c>"GAME"</c> — the pakfile, then the install — or answers null.</param>
    /// <param name="warn">Where each rejected entry and the cap are reported, as the engine's <c>Warning</c>s are.</param>
    /// <returns>The accepted entries, precache marker kept (the read strips it); empty when there is no manifest.</returns>
    /// <remarks>
    /// **First candidate that loads, never a merge** (<c>:195-197</c>): pak <c>particles.txt</c>, pak
    /// <c>maps/&lt;name&gt;_particles.txt</c>, then GAME's. **Only a file directly in <c>particles/</c>** (<c>:214-227</c>,
    /// <c>V_StripFilename</c> then <c>Q_stricmp</c>). **The cap counts accepted entries and ends the walk with one warning**
    /// (<c>:205-210</c>). Whether a malformed first candidate falls through to the next is not modelled: the stream reader
    /// reports what it can and the candidate counts as loaded.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<string> MapFiles(
        string mapName, Func<string, byte[]?> readPak, Func<string, byte[]?> readGame, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(readPak);
        ArgumentNullException.ThrowIfNull(readGame);
        ArgumentNullException.ThrowIfNull(warn);

        string path = MapPath(mapName);
        byte[]? found;

        try
        {
            found = readPak(PakRootPath) ?? readPak(path) ?? readGame(path);
        }
        catch (System.IO.InvalidDataException failure)
        {
            warn($"map particles manifest '{path}' could not be read: {failure.Message}");
            return [];
        }

        // Stryker disable once : a mutant that empties the guard body leaves 'manifest' unassigned, CS0165 — B410.
        if (found is not { Length: > 0 } manifest)
        {
            return [];
        }

        List<string> files = [];

        KeyValuesReader.Read(manifest, (key, value, depth) =>
        {
            if (depth != 1)
            {
                return true;
            }

            if (files.Count >= MaximumMapEntries)
            {
                warn($"map particles manifest '{path}' specifies more than {MaximumMapEntries} entries");
                return false;
            }

            if (!string.Equals(key, FileKey, StringComparison.OrdinalIgnoreCase))
            {
                warn($"manifest '{path}' with bogus file type '{key}', expecting 'file'");
            }
            else if (InParticlesFolder(value ?? string.Empty))
            {
                files.Add(value!);
            }
            else
            {
                warn($"manifest '{path}' contains a particle file '{value}' that's not under the particles directory");
            }

            return true;
        });

        return files;
    }

    /// <summary><c>V_StripFilename</c> at the last <c>/</c> or <c>\</c>, one <c>!</c> skipped, then <c>particles</c> without case.</summary>
    private static bool InParticlesFolder(string entry)
    {
        int slash = entry.LastIndexOfAny(['/', '\\']);
        string folder = slash > 0 ? entry[..slash] : string.Empty;

        if (folder.StartsWith(PrecacheMarker))
        {
            folder = folder[1..];
        }

        return string.Equals(folder, MapFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A map's definitions layered over the install's, never written into them (B554).</summary>
    /// <param name="shared">The install's definitions (finding 80's per-install set).</param>
    /// <param name="files">What <see cref="MapFiles"/> accepted.</param>
    /// <param name="readGame">Opens a path through <c>"GAME"</c>, the pakfile first.</param>
    /// <param name="warn">Where a file that is missing, unreadable or declares nothing is reported.</param>
    /// <returns><paramref name="shared"/> itself when nothing was added; otherwise a copy with the map's on top.</returns>
    /// <remarks>
    /// **A repeated name is replaced, case folded** — <c>AddParticleSystem</c> destroys the old definition and takes the new
    /// (disassembled, <c>particles.obj</c> <c>000dcd90</c>; <c>CUtlStringMap( caseInsensitive = true )</c> at
    /// <c>000db680</c>), and the map's pass runs after the stock reload (<c>cdll_client_int.cpp:1607,1619</c>), so the map
    /// wins. **A file that fails is logged and the next is read** (<c>000e4f50</c>: <c>Particles: Missing '%s'</c>).
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyDictionary<string, ParticleSystem> Layer(
        IReadOnlyDictionary<string, ParticleSystem> shared,
        IReadOnlyList<string> files,
        Func<string, byte[]?> readGame,
        Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(readGame);
        ArgumentNullException.ThrowIfNull(warn);

        Dictionary<string, ParticleSystem>? layered = null;

        foreach (string entry in files)
        {
            string path = entry.StartsWith(PrecacheMarker) ? entry[1..] : entry;
            byte[]? file;

            try
            {
                file = readGame(path);
            }
            catch (System.IO.InvalidDataException failure)
            {
                warn($"particles: '{path}' could not be read: {failure.Message}");
                continue;
            }

            if (file is not { Length: > 0 })
            {
                warn($"particles: missing '{path}'");
                continue;
            }

            IReadOnlyDictionary<string, ParticleSystem> declared = ParticleSystems.Read(file);

            if (declared.Count == 0)
            {
                warn($"unable to read particle definitions from '{path}'");
                continue;
            }

            layered ??= new Dictionary<string, ParticleSystem>(shared, StringComparer.OrdinalIgnoreCase);

            foreach ((string name, ParticleSystem one) in declared)
            {
                layered[name] = one;
            }
        }

        return layered ?? shared;
    }
}
