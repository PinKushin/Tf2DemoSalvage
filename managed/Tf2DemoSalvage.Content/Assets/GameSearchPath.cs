using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One place the game looks for content.</summary>
/// <param name="Path">The folder, or the archive's directory file.</param>
/// <param name="IsArchive">Whether it is a VPK rather than a folder.</param>
public readonly record struct SearchPathEntry(string Path, bool IsArchive);

/// <summary>
/// Where a Source game looks for content, read from its own <c>gameinfo.txt</c>.
/// </summary>
/// <remarks>
/// **The game declares this and the engine obeys it, so guessing is not necessary.** An earlier
/// version of this project hardcoded <c>tf</c>, its two archives, then <c>hl2</c> and its two — a
/// list that is correct for a stock Team Fortress 2 install and for nothing else. A mod, another
/// Source game, or an install with extra mounts has a search path no inference reaches, and the
/// failure is silent: a material resolves to nothing and its surface draws white.
///
/// The file states it plainly:
///
/// <code>
///   SearchPaths
///   {
///       game+mod+custom_mod  tf/custom/*
///       game+mod             tf/tf2_textures.vpk
///       game                 |all_source_engine_paths|hl2/hl2_textures.vpk
///       mod+...              |gameinfo_path|.
///   }
/// </code>
///
/// Four details that only the real file shows, each of which a reasonable guess gets wrong:
///
/// **Archives are named without <c>_dir</c>.** The entry says <c>tf2_textures.vpk</c> and the file
/// on disk is <c>tf2_textures_dir.vpk</c>; the engine appends it. Opening the name as written finds
/// nothing.
///
/// **The file lists VPKs before loose files**, with Valve's own comment saying why: searching an
/// archive is cheaper than thousands of failed file system calls. **The observed behaviour is the
/// opposite** — a loose file overrides the archived copy, which is how content has been replaced
/// since long before <c>custom/</c> existed, and is the owner's stated experience of the game.
///
/// This type reports the order as declared and does not reconcile the two. The caller decides,
/// because that is where the disagreement belongs: a reader of a file should say what the file
/// says. See <c>GameArchives</c>, which searches loose folders first for exactly this reason.
///
/// **The order within the block is the search order**, and duplicate keys are normal — the keys are
/// path-type tags (<c>game</c>, <c>mod</c>, <c>platform</c>), not identifiers. Any reader that
/// treats this as a dictionary loses most of the entries.
///
/// **One key is a condition rather than a tag.** <c>game_lv</c> names the low-violence archive,
/// mounted only in that mode, and TF2 does not ship <c>tf2_lv_dir.vpk</c> on an ordinary install.
///
/// **Two tokens appear in the values.** <c>|gameinfo_path|</c> is the folder holding this file, and
/// <c>|all_source_engine_paths|</c> is the install root above it.
/// </remarks>
public static class GameSearchPath
{
    /// <summary>The token standing for the folder holding <c>gameinfo.txt</c>.</summary>
    private const string GameInfoToken = "|gameinfo_path|";

    /// <summary>The token standing for the install root.</summary>
    private const string EnginePathsToken = "|all_source_engine_paths|";

    /// <summary>What a VPK entry is called on disk, once the engine has finished with it.</summary>
    private const string ArchiveSuffix = "_dir.vpk";

    private const string ArchiveExtension = ".vpk";

    /// <summary>Reads a game's declared search path.</summary>
    /// <param name="gameFolder">The folder holding <c>gameinfo.txt</c>, such as <c>.../tf</c>.</param>
    /// <returns>Every place to look, in the order the game lists them; empty if it says nothing.</returns>
    /// <exception cref="ArgumentException"><paramref name="gameFolder"/> is null or blank.</exception>
    /// <remarks>
    /// **Returns empty rather than throwing when there is no gameinfo.** A folder that is not a
    /// Source game is a normal thing to be handed, and the caller has its own fallback.
    /// </remarks>
    public static IReadOnlyList<SearchPathEntry> Read(string gameFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);

        string file = Path.Combine(gameFolder, "gameinfo.txt");

        string text;

        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            text = File.ReadAllText(file, Encoding.UTF8);
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }

        return Parse(text, gameFolder);
    }

    /// <summary>Reads a search path out of the text of a <c>gameinfo.txt</c>.</summary>
    /// <param name="text">The file's contents.</param>
    /// <param name="gameFolder">The folder it came from, for resolving tokens.</param>
    /// <returns>Every place to look, in order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SearchPathEntry> Parse(string text, string gameFolder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);

        string root = Path.GetDirectoryName(Path.GetFullPath(gameFolder)) ?? gameFolder;

        List<SearchPathEntry> entries = [];
        bool inside = false;
        int depth = 0;

        foreach (string raw in text.Split('\n'))
        {
            string line = Strip(raw);

            if (line.Length == 0)
            {
                continue;
            }

            if (!inside)
            {
                if (line.Contains("SearchPaths", StringComparison.OrdinalIgnoreCase))
                {
                    inside = true;
                    depth = 0;
                }

                continue;
            }

            if (line.StartsWith('{'))
            {
                depth++;
                continue;
            }

            if (line.StartsWith('}'))
            {
                // The block's closing brace ends the search path; anything after it belongs to
                // another section.
                break;
            }

            // **The key is a set of path-type tags and the value is the rest of the line.** Splitting
            // on whitespace and taking everything after the first field keeps values containing
            // spaces intact, which a naive "last field" would break on a path like
            // "Team Fortress 2/tf".
            int split = line.IndexOfAny([' ', '\t']);

            if (split < 0 || depth == 0)
            {
                continue;
            }

            string key = line[..split].Trim().Trim('"');
            string value = line[split..].Trim().Trim('"');

            if (value.Length == 0 || IsConditional(key))
            {
                continue;
            }

            Add(entries, Resolve(value, gameFolder, root));
        }

        return entries;
    }

    /// <summary>Whether a path type is one the engine mounts only in a mode this is not in.</summary>
    /// <remarks>
    /// **The keys are mostly tags and one of them is a condition.** <c>game_lv</c> names the
    /// low-violence archive, which the engine mounts only when low violence is on — and TF2 does
    /// not ship <c>tf2_lv_dir.vpk</c> at all on an ordinary install, so mounting it unconditionally
    /// asks for a file that is not there.
    ///
    /// Caught by a test asserting that every archive the search path names actually exists, which
    /// is worth keeping precisely because a missing archive is otherwise silent: it costs its
    /// content and reports nothing.
    /// </remarks>
    private static bool IsConditional(string key) =>
        key.Contains("_lv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Expands the tokens and turns a declared entry into a real path.</summary>
    private static string Resolve(string value, string gameFolder, string root)
    {
        string path = value
            .Replace(GameInfoToken, gameFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Replace(EnginePathsToken, root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar);

        // A trailing "." is how the file names the mod folder itself.
        if (path.EndsWith(Path.DirectorySeparatorChar + ".", StringComparison.Ordinal))
        {
            path = path[..^2];
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(root, path);
    }

    /// <summary>Adds one resolved entry, expanding a wildcard and naming archives properly.</summary>
    private static void Add(List<SearchPathEntry> entries, string path)
    {
        if (path.EndsWith('*'))
        {
            // **tf/custom/* is a real wildcard**, and the engine mounts what it finds in
            // alphabetical order - both the folders and any VPKs dropped in, which is how a mod is
            // distributed.
            string folder = Path.GetDirectoryName(path) ?? path;

            if (!Directory.Exists(folder))
            {
                return;
            }

            try
            {
                // **Alphabetical, because Valve's own comment says so**: the engine scans for VPKs
                // and subfolders and mounts them in alphabetical order. So inside custom a packed
                // mod does not outrank a loose one - the name decides. The file system's own
                // enumeration order is not guaranteed to be sorted.
                foreach (string found in Directory
                    .GetFileSystemEntries(folder)
                    .OrderBy(entry => entry, StringComparer.Ordinal))
                {
                    if (Directory.Exists(found))
                    {
                        entries.Add(new SearchPathEntry(found, IsArchive: false));
                    }
                    else if (found.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new SearchPathEntry(found, IsArchive: true));
                    }
                }
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException)
            {
                // An unreadable custom folder costs its overrides, not the search path.
            }

            return;
        }

        if (!path.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            entries.Add(new SearchPathEntry(path, IsArchive: false));
            return;
        }

        // The engine appends _dir to a VPK name: the file says tf2_textures.vpk and the archive on
        // disk is tf2_textures_dir.vpk. Both spellings are accepted, because a custom VPK dropped
        // into tf/custom is already named in full.
        string directory = path.EndsWith(ArchiveSuffix, StringComparison.OrdinalIgnoreCase)
            ? path
            : path[..^ArchiveExtension.Length] + ArchiveSuffix;

        entries.Add(new SearchPathEntry(directory, IsArchive: true));
    }

    /// <summary>Removes a comment and surrounding space from one line.</summary>
    private static string Strip(string line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);

        return (comment < 0 ? line : line[..comment]).Trim();
    }
}
