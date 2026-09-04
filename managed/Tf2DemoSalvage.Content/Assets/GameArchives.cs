using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Everywhere the game's content can live, searched in the order the engine searches it.
/// </summary>
/// <remarks>
/// **VPK is not the only answer, and for old content it is the wrong one.** Source shipped its
/// content in Steam's GCF caches until Valve moved TF2 to VPK around 2013, and loose files have
/// worked the whole time. So the search covers all the shapes that still exist on a real machine:
///
/// | Where | Why |
/// |---|---|
/// | <c>tf/custom/*/materials</c> | where custom content goes today, and it OVERRIDES the game |
/// | <c>tf/materials</c> | loose files, including anything extracted from a pre-VPK install |
/// | <c>tf/*_dir.vpk</c> | the modern archives |
/// | <c>hl2/</c> and <c>hl2/*_dir.vpk</c> | what TF2's own gameinfo.txt mounts after its own |
///
/// That order is the engine's: custom beats loose, loose beats packed. A viewer that searched the
/// VPKs first would show the stock texture where the game shows the user's replacement.
///
/// **GCF is not read.** Extracting one needs a Steam cache format that has not shipped in over a
/// decade, and a machine with GCF-era content also has the files loose or has since been updated.
/// If a case ever turns up, the loose-file path is where it would be handled.
///
/// Opening the archives is not free — a 1.5 MB directory tree and a 2.4 MB one — and nothing about
/// them changes between maps, so they are read once and kept.
///
/// **This lives in <c>Content</c> rather than in the viewer, and it used to sit inside
/// <c>MapAssets.cs</c>.** Every other reader of the game's files is here — <see cref="VpkArchive"/>,
/// <see cref="GameSearchPath"/>, <see cref="VmtMaterial"/>, <see cref="VtfTexture"/> — and this is
/// the type that finds the file for all of them. It was in the viewer only because the viewer
/// happened to need it first. Sound needs it now too, and the viewer is due to be rebuilt around
/// MVP (D54), which is a poor moment for a content reader to be embedded in a form's dependency
/// graph.
/// </remarks>
public sealed class GameArchives
{
    /// <summary>Every place to look, in the order the game declares them.</summary>
    /// <remarks>
    /// **One ordered list rather than folders-then-archives**, because gameinfo.txt interleaves
    /// them and the order IS the priority. TF2 lists tf/custom/* first, then its VPKs, then the
    /// loose mod folder — so a VPK beats a loose file in tf/, which is the opposite of the
    /// folklore and is what the file says. Searching all folders before all archives, as this did,
    /// silently inverts that for anyone with content extracted into tf/.
    ///
    /// **And the folklore is half right, which is why it survives.** A custom HUD does override the
    /// game's copy — because it lives in <c>tf/custom/</c>, which the file lists FIRST, above the
    /// archives. Loose files dropped into <c>tf/</c> itself are listed LAST and do not. One file,
    /// both behaviours, and no contradiction once it is read rather than recalled.
    /// </remarks>
    private readonly List<(string Path, VpkArchive? Archive)> _sources = [];

    private GameArchives(IEnumerable<(string Path, VpkArchive? Archive)> sources) =>
        _sources.AddRange(sources);

    /// <summary>Whether nothing at all was found.</summary>
    public bool IsEmpty => _sources.Count == 0;

    /// <summary>How many loose content folders are being searched.</summary>
    public int FolderCount => _sources.Count(source => source.Archive is null);

    /// <summary>Opens the game's content, wherever it lives.</summary>
    /// <param name="gameFolder">The <c>tf</c> folder of a TF2 install, or null.</param>
    /// <param name="log">
    /// Where to report what was found, as (area, message). Null discards it.
    /// </param>
    /// <returns>The content sources, empty when the game is not installed.</returns>
    /// <remarks>
    /// **A missing install is not an error.** Someone reviewing demos on a machine without TF2 gets
    /// the map's own content and untextured stock surfaces, which is worse than the alternative and
    /// far better than a viewer that refuses to open anything.
    ///
    /// **The log is injected rather than called directly**, because this used to write to the
    /// viewer's own <c>ViewerLog</c> and that is the one thing here that was genuinely viewer-shaped.
    /// The viewer passes its writer and keeps the same output; a test or the audio layer passes
    /// nothing. Inverting it was the whole cost of moving this out of the viewer.
    /// </remarks>
    public static GameArchives Open(string? gameFolder, Action<string, string>? log = null)
    {
        List<(string Path, VpkArchive? Archive)> sources = [];

        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            return new GameArchives(sources);
        }

        IReadOnlyList<SearchPathEntry> declared = GameSearchPath.Read(gameFolder);

        if (declared.Count == 0)
        {
            // **No gameinfo.txt falls back to the behaviour that predates all of this**: the mod
            // folder's loose files, which is what Source read before VPKs and before tf/custom
            // existed. Nothing else can be assumed - custom is a convention the FILE declares, and
            // inventing it for a game that never declared it is the same hardcoding this type was
            // written to remove.
            sources.Add((gameFolder, null));

            return new GameArchives(sources);
        }

        // **Exactly the order the file declares**, including that TF2 lists its VPKs above the
        // loose mod folder. That ordering is easy to get backwards from memory - the folklore says
        // a loose file overrides its archived copy - and the file settles it without anyone having
        // to remember.
        foreach (SearchPathEntry entry in declared)
        {
            try
            {
                if (entry.IsArchive)
                {
                    if (File.Exists(entry.Path))
                    {
                        sources.Add((string.Empty, VpkArchive.Open(entry.Path)));
                    }
                }
                else if (Directory.Exists(entry.Path))
                {
                    sources.Add((entry.Path, null));
                }
            }
            catch (Exception failure) when (
                failure is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A damaged archive or unreadable folder costs its content, not the viewer.
            }
        }

        log?.Invoke(
            "assets",
            $"search path: {sources.Count} sources from gameinfo.txt " +
            $"({sources.Count(source => source.Archive is not null)} archives)");

        // **Named, not counted.** "archives plus 8 folders" cannot answer which archive a
        // missing material should have come from, and TF2 splits its content: the VTFs live in
        // tf2_textures and the VMTs in tf2_misc, so losing one of them loses every material while
        // the other still resolves textures.
        log?.Invoke(
            "assets",
            "content: " + string.Join(
                ", ",
                sources.Select(source => source.Archive is null
                    ? "folder " + source.Path
                    : "archive")));

        return new GameArchives(sources);
    }

    /// <summary>Every path the ARCHIVES declare, for a measurement over what the game ships.</summary>
    /// <returns>Each packed path, in no particular order; duplicates across archives are possible.</returns>
    /// <remarks>
    /// **For probes and censuses, not for drawing anything.** Nothing in the render path enumerates
    /// the game — a map names its materials and a model names its own — and this exists so a
    /// question about the SHIPPED DATA can have a denominator (B326). CLAUDE.md's fifth source is
    /// the game's own files, and a claim about them needs to be counted rather than sampled.
    ///
    /// **Loose folders are deliberately not walked.** A VPK carries a directory this can read
    /// without touching the disk; a folder source would mean a recursive scan of a `custom/` tree
    /// of unknown size, and a census of what VALVE ships must not silently include what the user
    /// added. <see cref="Read"/> still searches both, as it must.
    /// </remarks>
    public IEnumerable<string> Paths()
    {
        foreach ((_, VpkArchive? archive) in _sources)
        {
            if (archive is null)
            {
                continue;
            }

            foreach (string path in archive.Paths)
            {
                yield return path;
            }
        }
    }

    /// <summary>Finds a file, searching every source in the order the game declares.</summary>
    /// <param name="path">Path such as <c>materials/concrete/x.vmt</c>.</param>
    /// <returns>The bytes, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    public byte[]? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach ((string folder, VpkArchive? archive) in _sources)
        {
            try
            {
                if (archive is not null)
                {
                    if (archive.ReadFile(path) is { } packed)
                    {
                        return packed;
                    }

                    continue;
                }

                // **The path is joined and then checked to be inside the folder.** It comes from a
                // material name in a map written by a stranger, and ".." in one would otherwise
                // read any file on the machine (D32).
                string candidate = Path.GetFullPath(Path.Combine(folder, path));

                if (!candidate.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // One unreadable source must not stop the search.
            }
        }

        return null;
    }
}
