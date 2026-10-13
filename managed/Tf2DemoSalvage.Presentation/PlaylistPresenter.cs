using System.Collections.Generic;
using System.Globalization;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>One row of the playlist, as text: the demo's name, its folder, and the path a load is asked for.</summary>
/// <param name="Name">The demo's file name.</param>
/// <param name="Folder">The folder it came from, as the library shows it.</param>
/// <param name="Path">The demo's full path.</param>
public readonly record struct PlaylistRow(string Name, string Folder, string Path);

/// <summary>
/// Owns the playlist: the library of demos, its display order and the rows a search leaves (D214) [no engine
/// counterpart: the game's demo browser lists one folder and filters nothing].
/// </summary>
/// <remarks>
/// **Moved out of the window, which held the library and two lists of its entries.** The window gets strings and
/// counts: how many rows there are now, a row's text, the path at an index, and the words for the status line.
/// </remarks>
public sealed class PlaylistPresenter
{
    private readonly DemoLibrary _library = new();

    /// <summary>The library sorted for display: folder first, then name.</summary>
    private IReadOnlyList<DemoEntry> _ordered = [];

    /// <summary>The rows the playlist is currently showing.</summary>
    private IReadOnlyList<DemoEntry> _shown = [];

    /// <summary>How many rows the playlist shows now.</summary>
    public int Count => _shown.Count;

    /// <summary>Adds files or folders to the library, and filters again by the search box's text.</summary>
    /// <param name="search">The search box's text.</param>
    /// <param name="paths">Files or folders; a folder's subfolders are included.</param>
    /// <returns>How many demos the library holds, in words for the status line.</returns>
    public string Add(string search, params string[] paths)
    {
        System.ArgumentNullException.ThrowIfNull(paths);

        foreach (string path in paths)
        {
            _library.Open(path);
        }

        // Sorted once per library change rather than per keystroke: folder first so the list
        // reads as folders, then name within each.
        // **Beside the filter whose contract depends on it** (B188, D90). This was three lines here
        // while `PlaylistFilter.Apply` documented its dependence on them — a precondition stated in
        // one assembly and satisfied in another is a precondition nothing enforces.
        _ordered = PlaylistFilter.Order(_library.Entries);

        Filter(search);

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        return _library.Entries.Count switch
        {
            0 => "No demos found.",
            1 => "1 demo.",
            int count => string.Create(CultureInfo.InvariantCulture, $"{count} demos."),
        };
    }

    /// <summary>Narrows the rows to those a search matches.</summary>
    /// <param name="search">The search box's text; empty shows everything.</param>
    public void Filter(string search) => _shown = PlaylistFilter.Apply(_ordered, search);

    /// <summary>One row's text, or null for an index the list no longer has.</summary>
    /// <param name="index">The row.</param>
    /// <remarks>
    /// Null is reachable during a resize that races a filter change. A blank row is survivable; an exception out of
    /// a paint handler is not.
    /// </remarks>
    public PlaylistRow? Row(int index)
    {
        if (index < 0 || index >= _shown.Count)
        {
            return null;
        }

        DemoEntry entry = _shown[index];

        return new PlaylistRow(entry.Name, entry.Folder, entry.Path);
    }
}
