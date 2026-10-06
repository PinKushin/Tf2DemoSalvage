using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Finding a map on disk, and fetching one that is not there.</summary>
/// <remarks>
/// **This was four methods inside <c>MainForm</c>** (B188, D90) — `FindMap`, `ReadMapNamed`,
/// `DownloadMapAsync` and the downloader's lifetime. A window has no business knowing Steam's
/// directory layout, and still less that a missing map can be fetched over HTTP.
/// </remarks>
public sealed class MapProviderTests
{
    [Test]
    public void Locate_WhenNoFolderHoldsIt_ReturnsNull()
    {
        using MapProvider maps = Provider(TempFolder());

        maps.Locate("cp_badlands").ShouldBeNull();
    }

    [Test]
    public void Locate_WhenOurFolderHoldsIt_FindsIt()
    {
        // **A control against the test above passing for the wrong reason.** A `Locate` that always
        // returned null would satisfy the missing-map case perfectly; only a present map can tell
        // "searched and found nothing" from "never searched".
        string folder = TempFolder();
        using MapProvider maps = Provider(folder);

        string expected = Path.Combine(folder, "cp_badlands.bsp");
        File.WriteAllBytes(expected, [0x56, 0x42, 0x53, 0x50]);

        maps.Locate("cp_badlands").ShouldBe(expected);
    }

    [Test]
    public void Locate_WithAnUnusableSearchPath_ReturnsNullRatherThanThrowing()
    {
        // `MapLocator` validates its paths and throws `ArgumentException`. A viewer whose Steam
        // install is missing or oddly placed must still open a demo — it just cannot find the map
        // that way — so this is handled rather than propagated.
        using MapProvider maps = new(
            steamLibraryFile: "   ", ownMapsFolder: "   ", () => Downloader(TempFolder()));

        maps.Locate("cp_badlands").ShouldBeNull();
    }

    [Test]
    public async Task FetchAsync_WhenTheDownloadFails_ReportsTheDownloadersOwnDescription()
    {
        // **The message comes from `MapDownloader.DescribeFailure`, not from here.** It names the
        // map and where it was looked for, and duplicating that wording in the presenter is how two
        // messages drift apart until one of them is wrong.
        using MapProvider maps = Provider(TempFolder(), HttpStatusCode.NotFound);

        MapFetch fetch = await maps.FetchAsync("cp_badlands", CancellationToken.None)
            .ConfigureAwait(false);

        fetch.Path.ShouldBeNull();
        fetch.Status.ShouldContain("cp_badlands");
    }

    [Test]
    public async Task FetchAsync_WhenTheDownloadSucceeds_ReturnsWhereItLanded()
    {
        using MapProvider maps = Provider(TempFolder(), HttpStatusCode.OK);

        MapFetch fetch = await maps.FetchAsync("cp_badlands", CancellationToken.None)
            .ConfigureAwait(false);

        fetch.Path.ShouldNotBeNull();
        File.Exists(fetch.Path).ShouldBeTrue("a fetched map has to actually be on disk");
    }

    [Test]
    public async Task FetchAsync_CalledTwice_BuildsOneDownloader()
    {
        // The view held `_downloader ??= MapDownloader.Create(...)` and disposed it once. That
        // lifetime moved here, and a provider building a fresh `HttpClient` per fetch would leak
        // sockets invisibly — every fetch would still succeed.
        string folder = TempFolder();
        int built = 0;

        using MapProvider maps = new(
            Path.Combine(folder, "libraryfolders.vdf"),
            folder,
            () =>
            {
                built++;
                return Downloader(folder);
            });

        await maps.FetchAsync("cp_badlands", CancellationToken.None).ConfigureAwait(false);
        await maps.FetchAsync("cp_granary", CancellationToken.None).ConfigureAwait(false);

        built.ShouldBe(1);
    }

    [Test]
    public void GameFolder_WithNoSteamLibraryFile_ReturnsNullRatherThanThrowing()
    {
        // Same contract as `Locate`, and it catches one exception more: this walks library folders
        // and reads what it finds, so the disk can fail underneath it. A viewer with no TF2 install
        // still opens demos — it just loses the stock textures.
        using MapProvider maps = Provider(TempFolder());

        maps.GameFolder().ShouldBeNull();
    }

    [Test]
    public void Find_WithNoGameInstalled_SaysSoRatherThanBlamingTheMap()
    {
        // **The owner's requirement, 2026-08-26:** *"the user has to point us to their tf2 folder
        // before we can do anything, and the program cant crash because its missing it must just
        // error and mention it"*.
        //
        // `Locate` answers null for two different facts — the map is absent from a known install,
        // and the install is not known at all — so the viewer reported "cp_badlands is not
        // installed; fetching it" and started a DOWNLOAD when the real problem was that nobody had
        // said where TF2 is. Telling someone the wrong cause is worse than telling them nothing,
        // and downloading a map into a game you cannot find helps no one.
        using MapProvider maps = Provider(TempFolder());

        MapSearch found = maps.Find("cp_badlands");

        found.Outcome.ShouldBe(MapOutcome.NoGame);
        found.Path.ShouldBeNull();
    }

    [Test]
    public void Find_WithTheGameInstalledButNoSuchMap_BlamesTheMap()
    {
        // **The control, and the reason this is not just a renamed `Locate`.** With an install
        // present a missing map really IS a missing map, and downloading it is the right answer —
        // so the two outcomes have to be distinguishable rather than merged into one safe message.
        string folder = TempFolder();

        InstallTf2(folder);

        using MapProvider maps = Provider(folder);

        maps.Find("cp_badlands").Outcome.ShouldBe(MapOutcome.NotInstalled);
    }

    [Test]
    public void Find_WithTheMapPresent_HandsBackItsPath()
    {
        string folder = TempFolder();

        File.WriteAllBytes(
            Path.Combine(InstallTf2(folder), "cp_badlands.bsp"), BspHeader);

        using MapProvider maps = Provider(folder);

        MapSearch found = maps.Find("cp_badlands");

        found.Outcome.ShouldBe(MapOutcome.Found);
        found.Path.ShouldNotBeNull();
    }

    /// <summary>Lays out a TF2 install Steam's locator will actually find.</summary>
    /// <param name="folder">The library root.</param>
    /// <returns>The <c>tf/maps</c> folder.</returns>
    /// <remarks>
    /// **Both halves are required and the first draft had neither right.** `MapLocator` walks
    /// <c>steamapps/common/Team Fortress 2/tf/maps</c> under each library, and a library only counts
    /// if `libraryfolders.vdf` names app id **440** inside it — a `"path"` alone registers nothing.
    /// A fixture that cannot satisfy the code under test measures nothing, which is the same mistake
    /// <see cref="BspHeader"/> was written to avoid.
    /// </remarks>
    private static string InstallTf2(string folder)
    {
        string maps = Path.Combine(
            folder, "steamapps", "common", "Team Fortress 2", "tf", "maps");

        Directory.CreateDirectory(maps);

        File.WriteAllText(
            Path.Combine(folder, "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\""
            + folder.Replace(@"\", @"\\", StringComparison.Ordinal)
            + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"440\"\t\t\"1\"\n\t\t}\n\t}\n}\n");

        return maps;
    }

    /// <remarks>
    /// **TF2_FOLDER is a full override (owner, 2026-10-02, D203):** the resolver's game folder is
    /// the one answer for maps as well as archives and configs, so a folder that Steam's library
    /// list does not name is still searched. The library file here is empty on purpose.
    /// </remarks>
    [Test]
    public void GameFolder_WithAResolvedFolder_IsThatFolder()
    {
        string tf = TempFolder();
        using MapProvider maps = new(
            Path.Combine(TempFolder(), "libraryfolders.vdf"), TempFolder(), () => Downloader(tf), () => tf);

        maps.GameFolder().ShouldBe(tf);
    }

    [Test]
    public void Locate_WithAResolvedFolderHoldingTheMap_FindsItUnderItsMaps()
    {
        string tf = TempFolder();
        string expected = Path.Combine(Directory.CreateDirectory(Path.Combine(tf, "maps")).FullName, "cp_badlands.bsp");
        File.WriteAllBytes(expected, [0x56, 0x42, 0x53, 0x50]);
        using MapProvider maps = new(
            Path.Combine(TempFolder(), "libraryfolders.vdf"), TempFolder(), () => Downloader(tf), () => tf);

        maps.Locate("cp_badlands").ShouldBe(expected);
    }

    [Test]
    public void Find_WithAResolvedFolderLackingTheMap_IsNotInstalledRatherThanNoGame()
    {
        string tf = TempFolder();
        using MapProvider maps = new(
            Path.Combine(TempFolder(), "libraryfolders.vdf"), TempFolder(), () => Downloader(tf), () => tf);

        maps.Find("cp_badlands").Outcome.ShouldBe(MapOutcome.NotInstalled);
    }

    [Test]
    public void Construct_WithoutADownloader_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => new MapProvider("a", "b", downloader: null!));
    }

    /// <remarks>
    /// **The control against every checksum test below**, and why <c>Find</c> without one must
    /// still trust whatever copy is on disk: a demo the timeline never carried a checksum for (or
    /// one where the checksum was never asked) has to keep playing against the install it finds.
    /// </remarks>
    [Test]
    public void Find_WithNoChecksumAsked_TrustsWhateverIsThere()
    {
        string folder = TempFolder();

        File.WriteAllBytes(Path.Combine(InstallTf2(folder), "cp_badlands.bsp"), BspHeader);

        using MapProvider maps = Provider(folder);

        MapSearch found = maps.Find("cp_badlands");

        found.Outcome.ShouldBe(MapOutcome.Found);
        found.VersionMismatch.ShouldBeFalse();
    }

    [Test]
    public void Find_WithTheRecordedChecksum_IsNotAMismatch()
    {
        string folder = TempFolder();
        byte[] map = FakeMap(seed: 7);

        File.WriteAllBytes(Path.Combine(InstallTf2(folder), "cp_badlands.bsp"), map);

        using MapProvider maps = Provider(folder);

        MapSearch found = maps.Find("cp_badlands", Md5(map));

        found.Outcome.ShouldBe(MapOutcome.Found);
        found.VersionMismatch.ShouldBeFalse();
    }

    [Test]
    public void Find_WithAnotherVersionsChecksum_IsAMismatchButStillFound()
    {
        // **Still `Found`, with its path, because a mismatched copy is the fallback if nothing else
        // has the recorded version (D162)** — "unless we cant find the map or changed data".
        string folder = TempFolder();
        byte[] installed = FakeMap(seed: 7);

        File.WriteAllBytes(Path.Combine(InstallTf2(folder), "cp_badlands.bsp"), installed);

        using MapProvider maps = Provider(folder);

        MapSearch found = maps.Find("cp_badlands", Md5(FakeMap(seed: 9)));

        found.Outcome.ShouldBe(MapOutcome.Found);
        found.Path.ShouldNotBeNull();
        found.VersionMismatch.ShouldBeTrue();
    }

    [Test]
    public async Task FetchAsync_WithAMapWantedCarryingAChecksum_RefusesAMirrorThatServesAnotherVersion()
    {
        byte[] wrong = FakeMap(seed: 1);
        string folder = TempFolder();

        using MapProvider maps = new(
            Path.Combine(folder, "libraryfolders.vdf"),
            folder,
            () => new MapDownloader(
                new HttpClient(new UrlHandler((MirrorUrl + "maps/cp_badlands.bsp", wrong))),
                folder,
                MirrorUrl));

        // A checksum that is not `wrong`'s — no map on this mirror can satisfy it.
        MapWanted wanted = new("cp_badlands", Md5(FakeMap(seed: 2)));

        MapFetch first = await maps.FetchAsync(wanted, CancellationToken.None).ConfigureAwait(false);

        first.Path.ShouldBeNull("the only file the mirror served was another version");
        Directory.GetFiles(folder, "*.bsp*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The owner's requirement (2026-10-05): the fetch starts as soon as the map name is decoded from the header**, in
    /// parallel with the rest of the load. The file here IS only a header — no command stream at all — so a prepare that
    /// waited on the decode, or read anything past byte 1072, could not produce this map.
    /// </remarks>
    [Test]
    public async Task PrepareAsync_OnAFileThatIsOnlyAHeader_FetchesTheMapTheHeaderNames()
    {
        string folder = TempFolder();
        UrlHandler mirror = new((MirrorUrl + "maps/cp_fake.bsp", FakeMap(seed: 3)));
        using MapProvider maps = MirrorProvider(folder, mirror);
        List<string> announced = [];

        MapFetch fetch = await maps.PrepareAsync(HeaderOnlyDemo("cp_fake"), announced.Add, CancellationToken.None)
            .ConfigureAwait(false);

        announced.ShouldBe(["cp_fake"]);
        fetch.Path.ShouldBe(Path.Combine(folder, "cp_fake.bsp"));
        (await File.ReadAllBytesAsync(fetch.Path!).ConfigureAwait(false)).ShouldBe(FakeMap(seed: 3));
    }

    /// <remarks>The control for the test above: a map already on disk costs no request and announces no fetch.</remarks>
    [Test]
    public async Task PrepareAsync_WithTheMapAlreadyInTheCache_FetchesNothing()
    {
        string folder = TempFolder();
        string cached = Path.Combine(folder, "cp_fake.bsp");
        await File.WriteAllBytesAsync(cached, FakeMap(seed: 4)).ConfigureAwait(false);
        UrlHandler mirror = new((MirrorUrl + "maps/cp_fake.bsp", FakeMap(seed: 3)));
        using MapProvider maps = MirrorProvider(folder, mirror);
        List<string> announced = [];

        MapFetch fetch = await maps.PrepareAsync(HeaderOnlyDemo("cp_fake"), announced.Add, CancellationToken.None)
            .ConfigureAwait(false);

        fetch.Path.ShouldBe(cached);
        announced.ShouldBeEmpty();
        mirror.Requests.ShouldBe(0);
    }

    /// <remarks>
    /// **The folder fetched into is the folder searched — one place.** The map read after a fetch goes through
    /// <c>Find</c>, not the fetch's own path, so a cache the locator does not search would fetch, succeed and draw nothing.
    /// </remarks>
    [Test]
    public async Task PrepareAsync_AfterAFetch_FindLocatesTheFetchedFile()
    {
        string folder = TempFolder();
        using MapProvider maps = MirrorProvider(folder, new UrlHandler((MirrorUrl + "maps/cp_fake.bsp", FakeMap(seed: 5))));

        MapFetch fetch = await maps.PrepareAsync(HeaderOnlyDemo("cp_fake"), _ => { }, CancellationToken.None)
            .ConfigureAwait(false);

        maps.Find("cp_fake").Path.ShouldBe(fetch.Path.ShouldNotBeNull());
    }

    [Test]
    public async Task PrepareAsync_OnAFileThatIsNotADemo_ReportsInsteadOfThrowing()
    {
        string folder = TempFolder();
        string notADemo = Path.Combine(folder, "pointer.dem");
        await File.WriteAllTextAsync(notADemo, "version https://git-lfs.github.com/spec/v1\n").ConfigureAwait(false);
        UrlHandler mirror = new();
        using MapProvider maps = MirrorProvider(folder, mirror);

        MapFetch fetch = await maps.PrepareAsync(notADemo, _ => { }, CancellationToken.None).ConfigureAwait(false);

        fetch.Path.ShouldBeNull();
        fetch.Status.ShouldContain("pointer.dem");
        mirror.Requests.ShouldBe(0);
    }

    /// <summary>A demo file of exactly one header naming <paramref name="map"/>, and nothing after it.</summary>
    private static string HeaderOnlyDemo(string map)
    {
        byte[] header = new byte[Tf2DemoSalvage.Core.Container.DemoHeader.SizeBytes];
        "HL2DEMO\0"u8.CopyTo(header);
        BitConverter.GetBytes(3).CopyTo(header, 8);
        BitConverter.GetBytes(24).CopyTo(header, 12);
        System.Text.Encoding.ASCII.GetBytes(map).CopyTo(header, 536);

        string path = Path.Combine(TempFolder(), "header-only.dem");
        File.WriteAllBytes(path, header);

        return path;
    }

    private static MapProvider MirrorProvider(string folder, UrlHandler mirror) =>
        new(
            Path.Combine(folder, "libraryfolders.vdf"),
            folder,
            () => new MapDownloader(new HttpClient(mirror), folder, MirrorUrl));

    /// <summary>A mirror URL nothing real answers; the handler stands in for it.</summary>
    private const string MirrorUrl = "https://example.invalid/";

    /// <summary>Answers a fixed URL and 404 for anything else — the <c>.bsp.bz2</c> attempt included.</summary>
    private sealed class UrlHandler(params (string Url, byte[] Body)[] files) : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = files.ToDictionary(
            file => file.Url, file => file.Body, StringComparer.Ordinal);

        /// <summary>How many requests reached this mirror.</summary>
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            string url = request.RequestUri?.ToString() ?? string.Empty;

            return Task.FromResult(_files.TryGetValue(url, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        }
    }

    /// <summary>A map with real lump content, so two seeds give two different checksums.</summary>
    private static byte[] FakeMap(byte seed)
    {
        byte[] bytes = new byte[4096];
        BspHeader.CopyTo(bytes, 0);

        const int Offset = 2048;
        const int Length = 1024;
        BitConverter.GetBytes(Offset).CopyTo(bytes, 8 + 16);
        BitConverter.GetBytes(Length).CopyTo(bytes, 8 + 16 + 4);
        bytes.AsSpan(Offset, Length).Fill(seed);

        return bytes;
    }

    /// <summary>A map's checksum, the form a 2013-onward demo records — and the only one <c>MapHash</c>
    /// carries that this project's own research has confirmed (finding 43).</summary>
    private static byte[] Md5(byte[] map) => BspMapChecksum.OfMap(map).Md5;

    /// <summary>A folder nothing else is using.</summary>
    private static string TempFolder()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "tf2ds-maps-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(folder);

        return folder;
    }

    private static MapProvider Provider(
        string folder, HttpStatusCode status = HttpStatusCode.NotFound) =>
        new(Path.Combine(folder, "libraryfolders.vdf"), folder, () => Downloader(folder, status));

    private static MapDownloader Downloader(
        string folder, HttpStatusCode status = HttpStatusCode.NotFound) =>
        new(new HttpClient(new StubHandler(status)), folder);

    /// <summary>A BSP header the downloader will accept: `VBSP` and version 20.</summary>
    /// <remarks>
    /// **Not four arbitrary bytes.** `MapDownloader.LooksLikeBsp` requires eight bytes, the `VBSP`
    /// magic, and a version in 17..21 — so a shorter or wrongly-versioned body is rejected and the
    /// success path never runs. A stub that cannot satisfy the code under test measures nothing,
    /// and the first draft of this file made exactly that mistake.
    /// </remarks>
    private static readonly byte[] BspHeader =
        [0x56, 0x42, 0x53, 0x50, 0x14, 0x00, 0x00, 0x00];

    /// <summary>Answers every request the same way, with no network behind it.</summary>
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(BspHeader),
            });
    }
}
