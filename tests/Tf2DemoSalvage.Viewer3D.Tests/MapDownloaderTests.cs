using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
    public void TheDefaultMirrorIsHttps()
    {
        // A plain-HTTP mirror would let anyone on the path replace a map with a file of their
        // choosing, which is a 40 MB parser input this program will then read.
        MapDownloader.DefaultMirror.ShouldStartWith("https://");
    }

    private const string MirrorUrl = "https://example.invalid/maps/";

    private MapDownloader Downloader(byte[] response) =>
        new(new HttpClient(new RecordingHandler(response)), _folder, MirrorUrl);

    /// <summary>Bytes that pass the BSP check: the magic, a version, and a lump directory.</summary>
    private static byte[] FakeBsp(int length)
    {
        byte[] bytes = new byte[length];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(bytes, 0);
        BitConverter.GetBytes(20).CopyTo(bytes, 4);
        return bytes;
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
