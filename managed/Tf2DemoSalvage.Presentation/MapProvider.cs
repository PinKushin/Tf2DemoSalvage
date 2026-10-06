using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Container;
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
/// <param name="VersionMismatch">
/// True when a file was found but a checksum was asked for and it is not that version (D162). The
/// file is still the one to fall back to if nothing else has the recorded version — this is not a
/// fourth outcome, because the caller's next step depends on what asked, not on what was found.
/// </param>
public readonly record struct MapSearch(MapOutcome Outcome, string? Path, bool VersionMismatch = false);

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
    private readonly Func<string?> _gameFolder;

    private MapDownloader? _open;

    /// <summary>A provider over explicit search roots.</summary>
    /// <param name="steamLibraryFile">Steam's `libraryfolders.vdf`.</param>
    /// <param name="ownMapsFolder">Where our own downloads land.</param>
    /// <param name="downloader">Builds the downloader, once, on first use.</param>
    /// <param name="gameFolder">
    /// Where the <c>tf</c> folder is — <see cref="SteamInstall.GameFolder"/> in the viewer, so
    /// <c>TF2_FOLDER</c> reaches maps as well as archives and configs (D203). Null searches the
    /// library file alone.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="downloader"/> is null.</exception>
    public MapProvider(
        string steamLibraryFile,
        string ownMapsFolder,
        Func<MapDownloader> downloader,
        Func<string?>? gameFolder = null)
    {
        ArgumentNullException.ThrowIfNull(downloader);

        _steamLibraryFile = steamLibraryFile;
        _ownMapsFolder = ownMapsFolder;
        _downloader = downloader;
        _gameFolder = gameFolder ?? (() => new MapLocator(_steamLibraryFile, _ownMapsFolder).FindGameFolder());
    }

    /// <summary>Steam's library index, where an installed TF2's maps are listed.</summary>
    /// <remarks>
    /// From <see cref="SteamInstall"/>, which reads where Steam recorded itself. Empty when no Steam
    /// folder is named at all, which <see cref="Locate"/> and <see cref="GameFolder"/> answer as null.
    /// </remarks>
    public static string SteamLibraryFile => SteamInstall.Machine.LibraryFile ?? string.Empty;

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
    /// <remarks>
    /// **A lambda, not the method group <c>SteamInstall.Machine.GameFolder</c>** (D210): the method
    /// group binds ONE <c>Machine</c>, built with whatever the cfg said when this provider was made, so
    /// a folder chosen in the startup picker would never reach the map load that follows it.
    /// </remarks>
    public static MapProvider Installed() =>
        new(SteamLibraryFile, OwnMapsFolder, () => MapDownloader.Create(OwnMapsFolder), () => SteamInstall.Machine.GameFolder());

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
            // The game folder's own maps come first, as a configured folder: under TF2_FOLDER the
            // library list may not name it at all (D203).
            string[] game = GameFolder() is { } tf ? [Path.Combine(tf, "maps")] : [];

            return new MapLocator(_steamLibraryFile, _ownMapsFolder, game).Find(mapName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Finds a map, and says which of two failures happened when it cannot.</summary>
    /// <param name="mapName">The map the demo names.</param>
    /// <param name="checksum">
    /// The demo's <c>DemoTimeline.MapHash</c> (never <c>MapCrc</c>, an unidentified field — finding
    /// 43), or null to accept whatever copy is on disk. A Valve map keeps its name across an update,
    /// so a name match alone says nothing about the version.
    /// </param>
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
    public MapSearch Find(string mapName, IReadOnlyList<byte>? checksum = null)
    {
        if (Locate(mapName) is not { } path)
        {
            // **Asked second, deliberately.** A found map proves an install without a second search,
            // and this walk reads library folders off disk — so the ordinary case pays nothing for
            // the diagnosis of the unusual one.
            return GameFolder() is null
                ? new MapSearch(MapOutcome.NoGame, null)
                : new MapSearch(MapOutcome.NotInstalled, null);
        }

        if (checksum is not { Count: > 0 })
        {
            return new MapSearch(MapOutcome.Found, path);
        }

        bool mismatch;

        try
        {
            mismatch = BspMapChecksum.Matches(File.ReadAllBytes(path), checksum) == false;
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            // Can't be confirmed as the recorded map, so it is treated the same as a definite
            // mismatch: worth trying to fetch the right version, worth falling back to if that fails.
            mismatch = true;
        }

        return new MapSearch(MapOutcome.Found, path, VersionMismatch: mismatch);
    }

    /// <summary>Where TF2 itself is installed, if it is.</summary>
    /// <returns>The <c>tf</c> folder, or null when the game is not installed.</returns>
    /// <remarks>
    /// **The one game-folder answer**: <see cref="Locate"/> searches its <c>maps</c>, and the archives,
    /// the custom folder and the configs are read from it, so <c>TF2_FOLDER</c> reaches all of them
    /// through <see cref="SteamInstall.GameFolder"/> (D203). Null costs the stock textures and
    /// nothing else.
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
            return _gameFolder();
        }
        catch (Exception failure) when (failure is IOException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Fetch a map that is not installed, or not the recorded version.</summary>
    /// <param name="mapName">The map, without extension.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Where it landed and what to tell the user.</returns>
    /// <remarks>
    /// Delegates to <see cref="FetchAsync(MapWanted, CancellationToken)"/> with no checksum and no
    /// download URL, for a caller that has neither — the command line and the tests before D162.
    /// </remarks>
    public Task<MapFetch> FetchAsync(string mapName, CancellationToken cancellationToken) =>
        FetchAsync(new MapWanted(mapName), cancellationToken);

    /// <summary>Fetch the version of a map a demo was recorded on, from the first source that has it.</summary>
    /// <param name="wanted">The map, its recorded checksum, and the demo's own download URL (D162).</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Where it landed and what to tell the user.</returns>
    public async Task<MapFetch> FetchAsync(MapWanted wanted, CancellationToken cancellationToken)
    {
        try
        {
            // **Built inside the try, because it was inside the old one.** `MainForm` had
            // `_downloader ??= MapDownloader.Create(...)` within the `catch (ArgumentException)`,
            // and the downloader's constructor validates its folder. Hoisting it out would be a
            // faithful-looking move that quietly narrows what is handled.
            MapDownloader downloader = _open ??= _downloader();

            string? landed = await downloader
                .TryDownloadAsync(wanted, cancellationToken)
                .ConfigureAwait(false);

            return landed is null
                ? new MapFetch(Path: null, downloader.DescribeFailure(wanted.Name))
                : new MapFetch(landed, Status: string.Empty);
        }
        catch (ArgumentException failure)
        {
            return new MapFetch(
                Path: null, "Map " + wanted.Name + " could not be fetched: " + failure.Message);
        }
    }

    /// <summary>Makes a demo's map available — found on disk or fetched — from the header alone.</summary>
    /// <param name="demoPath">The demo being opened.</param>
    /// <param name="fetching">Told the map's name when, and only when, a download starts.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>Where the map is and what to tell the user; a null path with a status when it is not to be had.</returns>
    /// <remarks>
    /// **Started the moment a demo is opened, alongside the decode, never after it** (owner, 2026-10-05: *"as soon as
    /// the map name is decoded"*). The name is in the 1,072-byte header; the decode it used to wait on took 33 s on
    /// <c>pass_sanctum_a2a</c>, and only then did a fetch begin, fire-and-forget, whose map read ran on a pool thread
    /// beside the render loop — mapless at best, a crash in <c>EntityModelSet.Collate</c> at worst (B507). The map read
    /// now awaits THIS task inside the load, behind the same barrier as any installed map.
    ///
    /// **Never throws for a bad file or a failed download** — the decode reports an unreadable demo with its own
    /// message, and a map that cannot be had is a status line, not a failed load. Only cancellation escapes.
    /// </remarks>
    public Task<MapFetch> PrepareAsync(string demoPath, Action<string> fetching, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetching);

        return Task.Run(
            async () =>
            {
                try
                {
                    byte[] header = new byte[DemoHeader.SizeBytes];
                    int read;

                    using (FileStream file = File.OpenRead(demoPath))
                    {
                        read = await file.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    string mapName = DemoHeader.Parse(header.AsSpan(0, read)).MapName;

                    if (Locate(mapName) is { } path)
                    {
                        return new MapFetch(path, Status: string.Empty);
                    }

                    fetching(mapName);

                    return await FetchAsync(mapName, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is IOException or InvalidDataException or UnauthorizedAccessException
                    or System.Net.Http.HttpRequestException or ObjectDisposedException)
                {
                    // ObjectDisposedException: `Dispose` mid-download (the window closing), which the old fire-and-forget
                    // caught for the same reason — nothing may escape a task nobody may ever await.
                    return new MapFetch(Path: null, "No map for " + Path.GetFileName(demoPath) + ": " + failure.Message);
                }
            },
            cancellationToken);
    }

    /// <summary>Closes the downloader, if one was ever built.</summary>
    public void Dispose()
    {
        _open?.Dispose();
        _open = null;
    }
}
