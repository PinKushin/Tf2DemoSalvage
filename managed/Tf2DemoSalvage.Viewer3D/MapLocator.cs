using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Finds a map's BSP: in the user's TF2 install first, then in this application's own folder.
/// </summary>
/// <remarks>
/// **The install path cannot be assumed.** Steam supports several library folders, and games
/// routinely live on a different drive from Steam itself — on this project's development machine
/// Steam is under Program Files while TF2 and its 233 maps are on <c>F:</c>. A locator that
/// guessed the default path would find nothing and be right about the guess.
///
/// So the libraries come from <c>steamapps/libraryfolders.vdf</c>, and the one holding TF2 is
/// identified by app id 440 appearing in its <c>apps</c> block rather than by looking for the
/// folder — a library can contain a leftover directory for a game that is no longer installed.
///
/// **The user's install is read-only to us**, per <c>DECISIONS.md</c> D32. Anything downloaded
/// lands in this application's own maps folder, so a parser bug cannot corrupt their game and a
/// malicious map cannot be planted where the game itself would load it.
/// </remarks>
internal sealed partial class MapLocator
{
    /// <summary>TF2's Steam application id.</summary>
    private const string Tf2AppId = "440";

    /// <summary>Where a library keeps an installed TF2's maps.</summary>
    private static readonly string[] MapsUnderLibrary =
        ["steamapps", "common", "Team Fortress 2", "tf", "maps"];

    private readonly string _libraryFile;
    private readonly string _ownMaps;
    private readonly string[] _userFolders;

    /// <summary>Creates a locator.</summary>
    /// <param name="libraryFile">Path to Steam's <c>libraryfolders.vdf</c>.</param>
    /// <param name="ownMapsFolder">This application's own maps folder.</param>
    /// <param name="userFolders">
    /// Folders the user configured explicitly, searched before anything auto-detected.
    /// </param>
    /// <remarks>
    /// **A configured folder beats a detected one, always.** Someone only sets this when detection
    /// got it wrong or when their maps live somewhere the scheme does not cover — a portable
    /// install, a network share, a folder of community maps kept outside Steam entirely. Ordering
    /// it after the automatic search would make the setting look broken in exactly the situation
    /// it exists for.
    /// </remarks>
    public MapLocator(string libraryFile, string ownMapsFolder, params string[] userFolders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownMapsFolder);
        ArgumentNullException.ThrowIfNull(userFolders);

        _libraryFile = libraryFile;
        _ownMaps = ownMapsFolder;
        _userFolders = userFolders;
    }

    /// <summary>Finds a map by name.</summary>
    /// <param name="mapName">Map name as a demo header carries it, without extension.</param>
    /// <returns>The full path, or <c>null</c> if no copy was found.</returns>
    /// <exception cref="ArgumentException">The name is empty or contains a path.</exception>
    /// <remarks>
    /// Null rather than an exception for a map nobody has: a demo can name a community map that
    /// was never installed, and the viewer still plays the demo without one.
    /// </remarks>
    public string? Find(string mapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        // **The name comes from a demo header, which is untrusted.** Without this, a header naming
        // `..\..\Windows\System32\config\SAM` would send the search wherever its author liked -
        // the classic path traversal, arriving through a file the user was told is just a replay.
        if (mapName.Contains('/', StringComparison.Ordinal) ||
            mapName.Contains('\\', StringComparison.Ordinal) ||
            mapName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(mapName) ||
            mapName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"'{mapName}' is not a map name; it contains a path.", nameof(mapName));
        }

        string file = mapName + ".bsp";

        // Explicitly configured folders first: the user set them because the automatic search was
        // wrong, so letting detection win would defeat the setting.
        foreach (string folder in _userFolders)
        {
            string configured = Path.Combine(folder, file);

            if (File.Exists(configured))
            {
                return configured;
            }
        }

        // The game's own copy first: it is the one the game would load, and the one nobody
        // downloaded from a stranger.
        foreach (string library in ReadLibrariesWithTf2())
        {
            string candidate = Path.Combine(library, Path.Combine(MapsUnderLibrary), file);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string ours = Path.Combine(_ownMaps, file);
        return File.Exists(ours) ? ours : null;
    }

    /// <summary>Reads the library folders that have TF2 installed.</summary>
    /// <remarks>
    /// A deliberately small VDF reader rather than a general one. The file is a nest of quoted
    /// key/value pairs, and all that is needed is each block's <c>path</c> and whether its
    /// <c>apps</c> list mentions 440 - so this tracks brace depth and the most recent path,
    /// which is enough and has no format surprises to get wrong.
    ///
    /// Any failure to read yields an empty list rather than throwing: Steam may not be installed,
    /// and this application is a demo viewer rather than a game launcher.
    /// </remarks>
    private List<string> ReadLibrariesWithTf2()
    {
        List<string> libraries = [];

        string content;

        try
        {
            content = File.ReadAllText(_libraryFile);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return libraries;
        }

        string? currentPath = null;

        foreach (GroupCollection groups in KeyValue().Matches(content).Select(match => match.Groups))
        {
            string key = groups["key"].Value;
            string value = groups["value"].Value;

            if (string.Equals(key, "path", StringComparison.OrdinalIgnoreCase))
            {
                // A VDF escapes backslashes. Reading them literally gives a path that exists
                // nowhere, and a locator that silently finds nothing.
                currentPath = value.Replace(@"\\", @"\", StringComparison.Ordinal);
                continue;
            }

            if (string.Equals(key, Tf2AppId, StringComparison.Ordinal) && currentPath is not null)
            {
                libraries.Add(currentPath);
                currentPath = null;
            }
        }

        return libraries;
    }

    /// <summary>Matches one quoted key and its quoted value.</summary>
    [GeneratedRegex("\"(?<key>[^\"]+)\"\\s+\"(?<value>[^\"]*)\"")]
    private static partial Regex KeyValue();
}
