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
/// loop does not deduplicate and neither does this. Deciding what a duplicate NAME means belongs to whoever merges
/// the systems, not here.
///
/// **What is NOT read, stated rather than hidden:** `ParseParticleEffectsMap` (`:175`) layers a map's own
/// `maps/&lt;name&gt;_particles.txt` on top, capped at `PARTICLES_MANIFEST_MAX_MAP_ENTRIES` = 64 — a cap Valve's own
/// comment explains was added because people *"drop bogus manifests into download directory to DoS rival community
/// maps"*. A map with custom particles therefore loses them here.
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
}
