using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Why a map is or is not available.</summary>
/// <remarks>
/// **Three answers where there were two**, because "not here" had two causes and one of them is the
/// person's to fix. See <see cref="MapProvider.Find"/>.
/// </remarks>
public enum MapOutcome
{
    /// <summary>The map is on disk and can be read.</summary>
    Found,

    /// <summary>TF2 is installed and this map is not among its maps. Worth downloading.</summary>
    NotInstalled,

    /// <summary>
    /// No TF2 installation was located, so nothing can be said about the map. Downloading one would
    /// put it nowhere useful, and the person needs to be told this rather than told about the map.
    /// </summary>
    NoGame,
}

/// <summary>The result of looking for a map.</summary>
/// <param name="Outcome">Which of the three cases this is.</param>
/// <param name="Path">The map's full path when <see cref="MapOutcome.Found"/>, otherwise null.</param>
public readonly record struct MapSearch(MapOutcome Outcome, string? Path);

/// <summary>What came of trying to fetch a map.</summary>
/// <param name="Path">Where it landed, or null if it did not arrive.</param>
/// <param name="Status">A line to show the user, whichever way it went.</param>
public readonly record struct MapFetch(string? Path, string Status);

/// <summary>Where maps come from: the disk first, then the network.</summary>
/// <remarks>
/// **This was `FindMap`, `DownloadMapAsync` and the `_downloader` field on <c>MainForm</c>**
/// (B188, D90). None of it is view work — a window should not know Steam's directory layout, own an
/// `HttpClient`, or decide that a missing map is worth fetching.
///
/// **The two search roots are policy, and they are why this is not just a call to `MapLocator`.**
/// Steam's `libraryfolders.vdf` is the installed game; `%LOCALAPPDATA%/Tf2DemoSalvage/maps` is where
/// our own downloads land. Which of those to consult, and in what order, is a decision about this
/// application rather than about locating a file.
///
/// **The failure text comes from `MapDownloader.DescribeFailure`, deliberately.** It names the map
/// and where it was sought; writing a second sentence here is how two messages drift until one is
/// wrong.
/// </remarks>
public sealed class MapProvider : IDisposable
{
    private readonly string _steamLibraryFile;
    private readonly string _ownMapsFolder;
    private readonly Func<MapDownloader> _downloader;

    private MapDownloader? _open;

    /// <summary>A provider over explicit search roots.</summary>
    /// <param name="steamLibraryFile">Steam's `libraryfolders.vdf`.</param>
    /// <param name="ownMapsFolder">Where our own downloads land.</param>
    /// <param name="downloader">Builds the downloader, once, on first use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="downloader"/> is null.</exception>
    public MapProvider(
        string steamLibraryFile, string ownMapsFolder, Func<MapDownloader> downloader)
    {
        ArgumentNullException.ThrowIfNull(downloader);

        _steamLibraryFile = steamLibraryFile;
        _ownMapsFolder = ownMapsFolder;
        _downloader = downloader;
    }

    /// <summary>Steam's library index, where an installed TF2's maps are listed.</summary>
    public static string SteamLibraryFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Steam",
        "steamapps",
        "libraryfolders.vdf");

    /// <summary>Where maps this viewer downloaded are kept.</summary>
    /// <remarks>
    /// **Deliberately the downloader's own folder, not a second path that happens to match.**
    /// `MainForm` spelled this out twice — `FindMap` searched
    /// `%LOCALAPPDATA%/Tf2DemoSalvage/maps` while `DownloadMapAsync` wrote to
    /// `MapDownloader.DefaultFolder` — and the two agreed only because the same three components
    /// were typed in both places. **The folder we fetch into and the folder we search are the same
    /// fact**, and if they ever drifted the symptom would be a map re-downloading on every open
    /// with no error anywhere.
    /// </remarks>
    public static string OwnMapsFolder => MapDownloader.DefaultFolder;

    /// <summary>A provider over this machine's usual places.</summary>
    /// <returns>The provider.</returns>
    public static MapProvider Installed() =>
        new(SteamLibraryFile, OwnMapsFolder, () => MapDownloader.Create(OwnMapsFolder));

    /// <summary>What to say while a map is being fetched.</summary>
    /// <param name="mapName">The map.</param>
    /// <returns>The line.</returns>
    public static string Fetching(string mapName) => "Downloading map " + mapName + "...";

    /// <summary>What to say when a map was found but could not be read.</summary>
    /// <param name="mapName">The map.</param>
    /// <param name="failure">Why it could not be read.</param>
    /// <returns>The line.</returns>
    /// <remarks>
    /// **A different case from every other map message**, and worth keeping distinct: the file is
    /// here and the install is here, so neither downloading it nor pointing at TF2 will help. The
    /// reason carries that — a truncated BSP and a locked file need different answers.
    ///
    /// Written out in `MainForm` until 2026-08-26, beside `Fetching`'s call site, which had been
    /// doing it properly all along (B188, D90).
    /// </remarks>
    public static string CouldNotRead(string mapName, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return "Map " + mapName + " could not be read: " + failure.Message;
    }

    /// <summary>Where a map already is, if it is anywhere.</summary>
    /// <param name="mapName">The map, without extension.</param>
    /// <returns>Its path, or null if no search root holds it.</returns>
    /// <remarks>
    /// **Null rather than an exception when a search root is unusable.** `MapLocator` validates its
    /// paths, and a viewer whose Steam install is missing or oddly placed must still open a demo —
    /// it simply cannot find the map that way.
    /// </remarks>
    public string? Locate(string mapName)
    {
        try
        {
            return new MapLocator(_steamLibraryFile, _ownMapsFolder).Find(mapName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Finds a map, and says which of two failures happened when it cannot.</summary>
    /// <param name="mapName">The map the demo names.</param>
    /// <returns>Where the map is, or why it is not here.</returns>
    /// <remarks>
    /// **<see cref="Locate"/> answers null for two different facts**, and the viewer could not tell
    /// them apart: the map is absent from an install we found, and we never found an install. So a
    /// machine with no TF2 was told *"cp_badlands is not installed; fetching it"* and a download
    /// started — the wrong cause, and pointless work, for a problem the person could have fixed in
    /// one step if anything had said what it was.
    ///
    /// **The owner's requirement, 2026-08-26:** *"the user has to point us to their tf2 folder
    /// before we can do anything, and the program cant crash because its missing it must just error
    /// and mention it"*. Nothing here throws; the answer is a value that names which case it is.
    ///
    /// This is <c>docs/memory/sentinels-conflate-unknown-with-answer.md</c> in a place it had not
    /// been looked for: one null standing in for "no" and for "I do not know".
    /// </remarks>
    public MapSearch Find(string mapName)
    {
        if (Locate(mapName) is { } path)
        {
            return new MapSearch(MapOutcome.Found, path);
        }

        // **Asked second, deliberately.** A found map proves an install without a second search, and
        // this walk reads library folders off disk — so the ordinary case pays nothing for the
        // diagnosis of the unusual one.
        return GameFolder() is null
            ? new MapSearch(MapOutcome.NoGame, null)
            : new MapSearch(MapOutcome.NotInstalled, null);
    }

    /// <summary>Where TF2 itself is installed, if it is.</summary>
    /// <returns>The <c>tf</c> folder, or null when the game is not installed.</returns>
    /// <remarks>
    /// **The same Steam search as <see cref="Locate"/>, stopping one level higher**: the locator
    /// wants <c>tf/maps</c> and this wants <c>tf</c> itself, where the archives and the custom
    /// folder live. Null costs the stock textures and nothing else.
    ///
    /// **It is here because it was the THIRD copy of the Steam path in `MainForm`** — `FindMap`,
    /// `FindGameFolder` and the downloader's default folder each spelled out
    /// `ProgramFilesX86/Steam/steamapps/libraryfolders.vdf`. Three hand-typed copies of one path is
    /// three chances to fix a bug in one of them.
    ///
    /// **Catches `IOException` as well as `ArgumentException`, unlike `Locate`**, and that is not an
    /// oversight in either: this one enumerates library folders and reads what it finds, so the disk
    /// can fail underneath it. `Find` resolves a path and does not.
    /// </remarks>
    public string? GameFolder()
    {
        try
        {
            return new MapLocator(_steamLibraryFile, _ownMapsFolder).FindGameFolder();
        }
        catch (Exception failure) when (failure is IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Fetch a map that is not installed.</summary>
    /// <param name="mapName">The map, without extension.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Where it landed and what to tell the user.</returns>
    public async Task<MapFetch> FetchAsync(string mapName, CancellationToken cancellationToken)
    {
        try
        {
            // **Built inside the try, because it was inside the old one.** `MainForm` had
            // `_downloader ??= MapDownloader.Create(...)` within the `catch (ArgumentException)`,
            // and the downloader's constructor validates its folder. Hoisting it out would be a
            // faithful-looking move that quietly narrows what is handled.
            MapDownloader downloader = _open ??= _downloader();

            string? landed = await downloader
                .TryDownloadAsync(mapName, cancellationToken)
                .ConfigureAwait(false);

            return landed is null
                ? new MapFetch(Path: null, downloader.DescribeFailure(mapName))
                : new MapFetch(landed, Status: string.Empty);
        }
        catch (ArgumentException failure)
        {
            return new MapFetch(
                Path: null, "Map " + mapName + " could not be fetched: " + failure.Message);
        }
    }

    /// <summary>Closes the downloader, if one was ever built.</summary>
    public void Dispose()
    {
        _open?.Dispose();
        _open = null;
    }
}
