using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

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
    public void Fetching_ForAMap_NamesTheMap()
    {
        MapProvider.Fetching("cp_badlands").ShouldContain("cp_badlands");
    }

    [Test]
    public void Construct_WithoutADownloader_Refuses()
    {
        Should.Throw<ArgumentNullException>(() => new MapProvider("a", "b", downloader: null!));
    }

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
