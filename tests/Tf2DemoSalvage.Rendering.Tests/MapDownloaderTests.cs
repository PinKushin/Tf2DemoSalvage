using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ICSharpCode.SharpZipLib.BZip2;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Fetching a map the user does not have.
/// </summary>
/// <remarks>
/// **This is the same thing a game server does when you join it**: the client pulls the map from
/// the server's <c>sv_downloadurl</c> and drops it in its own maps folder. There being no server
/// here, the source is a public fast-download mirror.
///
/// **Everything downloaded is hostile input under D32**, and this layer is where that starts:
/// the map name comes from a demo header written by a stranger, the response comes from a host
/// this project does not control, and the destination must never be the user's game install.
///
/// No test in this file touches the network. The handler is a stand-in, which is what makes these
/// runnable in CI and independent of a third party staying up; the real fetch has its own
/// integration test, gated on an environment variable.
/// </remarks>
public sealed class MapDownloaderTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Disposable temp folder; a lock must not fail an otherwise passing test.
        }
    }

    [Test]
    public async Task Download_AValidMap_IsWrittenToOurOwnFolder()
    {
        byte[] map = FakeBsp(4096);
        using MapDownloader downloader = Downloader(map);

        string? path = await downloader.TryDownloadAsync("cp_process_final", CancellationToken.None).ConfigureAwait(false);

        path.ShouldNotBeNull();
        Path.GetFullPath(path).ShouldStartWith(Path.GetFullPath(_folder));
        (await File.ReadAllBytesAsync(path).ConfigureAwait(false)).Length.ShouldBe(map.Length);
    }

    [Test]
    public async Task Download_RequestsTheMapByName()
    {
        RecordingHandler handler = new(FakeBsp(1024));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        await downloader.TryDownloadAsync("koth_product_final", CancellationToken.None).ConfigureAwait(false);

        handler.LastUrl.ShouldBe("https://example.invalid/maps/koth_product_final.bsp");
    }

    [Test]
    public async Task Download_AMissingMap_ReturnsNullRatherThanThrowing()
    {
        // Most maps in a real archive are community ones no mirror carries. A 404 is the normal
        // case, not an error - the viewer draws the players without a world behind them.
        using MapDownloader downloader = new(
            new HttpClient(new RecordingHandler([], HttpStatusCode.NotFound)), _folder, MirrorUrl);

        (await downloader.TryDownloadAsync("cp_madeup", CancellationToken.None).ConfigureAwait(false)).ShouldBeNull();
    }

    [Test]
    public async Task Download_SomethingThatIsNotABsp_IsRejectedAndNotKept()
    {
        // A mirror that answers 200 with an HTML error page, a login redirect, or anything else.
        // The parser already meets those - the demo archive run hit two - and a file kept here
        // would be loaded as a map on every later open.
        byte[] html = Encoding.UTF8.GetBytes("<html><body>404 not found</body></html>");
        using MapDownloader downloader = Downloader(html);

        (await downloader.TryDownloadAsync("cp_process_final", CancellationToken.None).ConfigureAwait(false)).ShouldBeNull();

        Directory.GetFiles(_folder).ShouldBeEmpty("a rejected download was left on disk");
    }

    [Test]
    public async Task Download_MoreBytesThanTheCap_IsRefusedAndNotKept()
    {
        // The response length is chosen by the host, not by us. Without a cap a hostile or broken
        // mirror can fill the disk - the same allocate-before-validate shape as the BSP lump sizes.
        using MapDownloader downloader = new(
            new HttpClient(new RecordingHandler(FakeBsp(4096))),
            _folder,
            MirrorUrl,
            maximumBytes: 1024);

        (await downloader.TryDownloadAsync("cp_process_final", CancellationToken.None).ConfigureAwait(false)).ShouldBeNull();

        Directory.GetFiles(_folder).ShouldBeEmpty("an oversized download was left on disk");
    }

    [TestCase("../../../windows/system32/config/sam")]
    [TestCase("..\\..\\evil")]
    [TestCase("maps/cp_process")]
    [TestCase("C:\\absolute")]
    public void Download_AMapNameThatIsAPath_IsRefused(string name)
    {
        // The name comes out of a demo header written by a stranger. This is the same check the
        // locator makes, and it has to exist on BOTH paths: one that only searched safely would
        // still be handing a traversal to the writer.
        using MapDownloader downloader = Downloader(FakeBsp(64));

        Should.Throw<ArgumentException>(
            () => downloader.TryDownloadAsync(name, CancellationToken.None));
    }

    [Test]
    public async Task Download_AMapAlreadyPresent_IsNotFetchedAgain()
    {
        // Bandwidth belongs to the user. A 40 MB map re-fetched on every open of every demo on
        // that map is the kind of thing that gets a client blocked by a mirror.
        RecordingHandler handler = new(FakeBsp(1024));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        await downloader.TryDownloadAsync("cp_badlands", CancellationToken.None).ConfigureAwait(false);
        int after = handler.Requests;

        await downloader.TryDownloadAsync("cp_badlands", CancellationToken.None).ConfigureAwait(false);

        handler.Requests.ShouldBe(after, "the map was downloaded twice");
    }

    [Test]
    public async Task Download_AFailedRequest_ReturnsNullRatherThanThrowing()
    {
        // No network, DNS failure, a mirror that is down. None of that is a reason for the viewer
        // to stop working.
        using MapDownloader downloader = new(
            new HttpClient(new ThrowingHandler()), _folder, MirrorUrl);

        (await downloader.TryDownloadAsync("cp_process_final", CancellationToken.None).ConfigureAwait(false)).ShouldBeNull();
    }

    [Test]
    public void MapDownloader_TheDefaultMirror_IsHttps()
    {
        // A plain-HTTP mirror would let anyone on the path replace a map with a file of their
        // choosing, which is a 40 MB parser input this program will then read.
        MapDownloader.DefaultMirror.ShouldStartWith("https://");
    }

    /// <remarks>
    /// **The compressed file first, as the engine queues it** (D162). `engine.dll`'s download queue
    /// formats <c>"%s.bz2"</c> and queues it before the plain file whenever the server's URL is HTTP,
    /// so a fast-download server that carries only <c>.bsp.bz2</c> — most of them — still works.
    /// </remarks>
    [Test]
    public async Task Download_ACompressedMapOnTheServer_IsAskedForFirstAndDecompressed()
    {
        byte[] map = FakeBsp(4096, seed: 7);
        UrlHandler handler = new((MirrorUrl + "maps/cp_granary.bsp.bz2", Bzip2(map)));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        string? path = await downloader
            .TryDownloadAsync(new MapWanted("cp_granary"), CancellationToken.None)
            .ConfigureAwait(false);

        path.ShouldNotBeNull();
        (await File.ReadAllBytesAsync(path).ConfigureAwait(false)).ShouldBe(map);
        handler.Asked[0].ShouldBe(MirrorUrl + "maps/cp_granary.bsp.bz2");
    }

    /// <remarks>
    /// **Then the plain file**, which the engine queues second.
    /// </remarks>
    [Test]
    public async Task Download_NoCompressedFile_FallsBackToThePlainOne()
    {
        byte[] map = FakeBsp(4096, seed: 3);
        UrlHandler handler = new((MirrorUrl + "maps/cp_granary.bsp", map));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        string? path = await downloader
            .TryDownloadAsync(new MapWanted("cp_granary"), CancellationToken.None)
            .ConfigureAwait(false);

        path.ShouldNotBeNull();
        handler.Asked.ShouldBe([MirrorUrl + "maps/cp_granary.bsp.bz2", MirrorUrl + "maps/cp_granary.bsp"]);
    }

    /// <remarks>
    /// **The demo's own server first** (D162). A match demo records its server's <c>sv_downloadurl</c>,
    /// which is where that server's own clients fetched the map from; the fixed mirror is the fallback.
    /// </remarks>
    [Test]
    public async Task Download_TheDemosOwnDownloadUrl_IsAskedBeforeTheMirror()
    {
        const string Demo = "https://server.invalid/tf/";
        byte[] map = FakeBsp(4096, seed: 5);
        UrlHandler handler = new((Demo + "maps/koth_x.bsp", map));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        string? path = await downloader
            .TryDownloadAsync(new MapWanted("koth_x", DemoDownloadUrl: new Uri(Demo)), CancellationToken.None)
            .ConfigureAwait(false);

        path.ShouldNotBeNull();
        handler.Asked[0].ShouldBe(Demo + "maps/koth_x.bsp.bz2");
        handler.Asked.ShouldNotContain(url => url.StartsWith(MirrorUrl, StringComparison.Ordinal));
    }

    /// <remarks>
    /// **Only an HTTP address is a download URL**: the engine tests the value for <c>"http://"</c> and
    /// <c>"https://"</c> before using it. Anything else — a typo, a bare host — is not asked.
    /// </remarks>
    [Test]
    public async Task Download_ADemoDownloadUrlThatIsNotHttp_IsNotAsked()
    {
        UrlHandler handler = new((MirrorUrl + "maps/koth_x.bsp", FakeBsp(4096, seed: 5)));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        await downloader
            .TryDownloadAsync(new MapWanted("koth_x", DemoDownloadUrl: new Uri("ftp://server.invalid/tf/")), CancellationToken.None)
            .ConfigureAwait(false);

        handler.Asked.ShouldAllBe(url => url.StartsWith(MirrorUrl, StringComparison.Ordinal));
    }

    /// <remarks>
    /// **The wrong version is refused and the next source tried** (D162). The demo's own server holds a
    /// map whose checksum is not the recorded one; the mirror holds the right one, which is kept.
    /// </remarks>
    [Test]
    public async Task Download_AMapWhoseChecksumDiffers_IsRefusedAndTheNextSourceTried()
    {
        const string Demo = "https://server.invalid/tf/";
        byte[] wrong = FakeBsp(4096, seed: 1);
        byte[] right = FakeBsp(4096, seed: 2);
        UrlHandler handler = new(
            (Demo + "maps/cp_badlands.bsp", wrong),
            (MirrorUrl + "maps/cp_badlands.bsp", right));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        string? path = await downloader
            .TryDownloadAsync(new MapWanted("cp_badlands", Md5(right), new Uri(Demo)), CancellationToken.None)
            .ConfigureAwait(false);

        path.ShouldNotBeNull();
        (await File.ReadAllBytesAsync(path).ConfigureAwait(false)).ShouldBe(right);
    }

    /// <remarks>
    /// **Nothing that matches, nothing kept.** Every source holds only a different version, so the answer
    /// is no map, and no file is left behind for a later open to find.
    /// </remarks>
    [Test]
    public async Task Download_OnlyTheWrongVersionAnywhere_IsNullAndKeepsNothing()
    {
        UrlHandler handler = new((MirrorUrl + "maps/cp_badlands.bsp", FakeBsp(4096, seed: 1)));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);

        string? path = await downloader
            .TryDownloadAsync(new MapWanted("cp_badlands", Md5(FakeBsp(4096, seed: 2))), CancellationToken.None)
            .ConfigureAwait(false);

        path.ShouldBeNull();
        Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    /// <remarks>
    /// **A version is kept under its checksum**, so two versions of one Valve map — which keeps its name
    /// across updates — sit side by side, and the right one is found again without a download.
    /// </remarks>
    [Test]
    public async Task Download_AMapWithAKnownChecksum_IsKeptUnderItAndNotFetchedAgain()
    {
        byte[] map = FakeBsp(4096, seed: 9);
        UrlHandler handler = new((MirrorUrl + "maps/cp_badlands.bsp", map));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl);
        MapWanted wanted = new("cp_badlands", Md5(map));

        string? first = await downloader.TryDownloadAsync(wanted, CancellationToken.None).ConfigureAwait(false);
        int asked = handler.Asked.Count;
        string? second = await downloader.TryDownloadAsync(wanted, CancellationToken.None).ConfigureAwait(false);

        first.ShouldNotBeNull();
        Path.GetFileName(first).ShouldBe("cp_badlands." + Convert.ToHexString(Md5(map)) + ".bsp");
        second.ShouldBe(first);
        handler.Asked.Count.ShouldBe(asked, "the right version was downloaded twice");
    }

    /// <remarks>
    /// **A compressed file that expands past the cap is refused**, because a few kilobytes of bzip2 can
    /// decompress to gigabytes and the server chooses what it sends.
    /// </remarks>
    [Test]
    public async Task Download_ACompressedFileThatExpandsPastTheCap_IsRefusedAndNotKept()
    {
        UrlHandler handler = new((MirrorUrl + "maps/cp_granary.bsp.bz2", Bzip2(FakeBsp(65536, seed: 4))));
        using MapDownloader downloader = new(new HttpClient(handler), _folder, MirrorUrl, maximumBytes: 8192);

        (await downloader.TryDownloadAsync(new MapWanted("cp_granary"), CancellationToken.None).ConfigureAwait(false))
            .ShouldBeNull();

        Directory.GetFiles(_folder, "*", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private const string MirrorUrl = "https://example.invalid/";

    private MapDownloader Downloader(byte[] response) =>
        new(new HttpClient(new RecordingHandler(response)), _folder, MirrorUrl);

    /// <summary>Bytes that pass the BSP check: the magic, a version, and a lump directory.</summary>
    /// <param name="length">The file's length; at least 3072 when a seed is given.</param>
    /// <param name="seed">Fills lump 1, so two seeds give two maps with different checksums.</param>
    private static byte[] FakeBsp(int length, byte seed = 0)
    {
        byte[] bytes = new byte[length];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(bytes, 0);
        BitConverter.GetBytes(20).CopyTo(bytes, 4);

        if (seed != 0)
        {
            // Lump 1's directory entry — fileofs, filelen — pointing at 1024 bytes of the seed. Lump 0,
            // the entities, is the one the engine's checksum leaves out, so the content goes in 1.
            // **Past the 1,036-byte header**: magic, version, 64 lumps of 16 bytes, the map revision. A
            // first version put the lump at 1024, inside the header, and the checksum refused it.
            const int Offset = 2048;
            const int Length = 1024;
            BitConverter.GetBytes(Offset).CopyTo(bytes, 8 + 16);
            BitConverter.GetBytes(Length).CopyTo(bytes, 8 + 16 + 4);
            bytes.AsSpan(Offset, Length).Fill(seed);
        }

        return bytes;
    }

    /// <summary>The bytes, bzip2-compressed, as a fast-download server stores a map.</summary>
    private static byte[] Bzip2(byte[] bytes)
    {
        using MemoryStream compressed = new();

        using (BZip2OutputStream writer = new(compressed) { IsStreamOwner = false })
        {
            writer.Write(bytes);
        }

        return compressed.ToArray();
    }

    /// <summary>A map's sixteen-byte checksum, the form a 2013-onward demo records.</summary>
    private static byte[] Md5(byte[] map) => BspMapChecksum.OfMap(map).Md5;

    /// <summary>Answers the URLs it was given and 404 for the rest, recording every request in order.</summary>
    private sealed class UrlHandler(params (string Url, byte[] Body)[] files) : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = files.ToDictionary(
            file => file.Url, file => file.Body, StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;
            Asked.Add(url);

            return Task.FromResult(_files.TryGetValue(url, out byte[]? body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) });
        }
    }

    private sealed class RecordingHandler(byte[] response, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            Requests++;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(response),
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("the mirror is unreachable");
    }
}
