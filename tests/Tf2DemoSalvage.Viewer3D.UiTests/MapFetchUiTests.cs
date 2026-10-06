using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FlaUI.Core.Tools;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>A demo whose map is not on the machine ends with that map fetched, loaded and drawn (B507).</summary>
/// <remarks>
/// **The owner's report, 2026-10-05:** opening <c>pass_sanctum_a2a</c> with the map absent left a mapless view; the
/// fetch began only after the 33 s decode, and its read ran on a pool thread beside the render loop. Reproduced
/// before the fix as a crash in <c>EntityModelSet.Collate</c>.
///
/// **Its own process, with nothing installed and an empty map cache** — Steam discovery pointed at an empty folder,
/// <c>TF2VIEW_MAP_CACHE</c> at a fresh temp folder, and <c>TF2VIEW_MAP_MIRROR</c> at a local HTTP listener serving one
/// file. So the only way the world can be built is through the fetch.
///
/// **The served BSP comes from this machine's own copy, or from the real mirror where there is none** (CI has no TF2,
/// and its UI suite already fetches maps from that mirror). That is the only network this test can touch, and it
/// happens in the test, before the viewer starts — the viewer's fetch is always the local one.
/// </remarks>
[TestFixture]
public sealed class MapFetchUiTests
{
    private const string DemoName = "tf2-2008-build3420-stv-cp_granary.dem";

    private const string MapName = "cp_granary";

    /// <summary>The fetch announcement, written by <c>MainForm.LoadDemoAsync</c>'s callback.</summary>
    private const string FetchLine = MapName + " is not installed; fetching it";

    /// <summary>The decode's own completion line — the fetch must come before it, from the header.</summary>
    private const string DecodedLine = "building the position timeline took";

    /// <summary>The world-build line's tail: <c>world: N vertices in M material batches for a WxH viewport</c>.</summary>
    private const string WorldBuilt = "material batches for a";

    private static readonly Regex WorldLine = new(
        @"\[render\] world: (?<vertices>\d+) vertices",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private string _root = string.Empty;

    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "tools", "corpus", "demos", DemoName));

    [SetUp]
    public void CreateRoot() =>
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-mapfetch-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void RemoveRoot()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException failure)
        {
            TestContext.Out.WriteLine("temp tree not removed: " + failure.Message);
        }
    }

    [Test]
    public async Task Open_ADemoWhoseMapIsAbsent_FetchesItFromTheHeaderAndDrawsTheWorld()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
            return;
        }

        byte[] map = await MapToServe().ConfigureAwait(false);
        string cache = Directory.CreateDirectory(Path.Combine(_root, "cache")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "emptysteam"));

        // **The download is held until the decode has finished** — the owner's case, where a 35 MB map arrives after
        // the timeline is built. A local file served at once would beat any decode and hide a load that never waits.
        TaskCompletionSource decoded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using LocalMirror mirror = new(MapName, map, decoded.Task);

        using ViewerApplication viewer = ViewerApplication.Launch(
            new Dictionary<string, string?>
            {
                [SteamInstall.SteamRootVariable] = Path.Combine(_root, "emptysteam"),
                [SteamInstall.OverrideVariable] = null,
                [MapDownloader.CacheVariable] = cache,
                [MapDownloader.MirrorVariable] = mirror.Url,
            },
            askForGameFolder: false,
            DemoPath);

        Retry.WhileFalse(() => viewer.Count(DecodedLine) > 0, TimeSpan.FromSeconds(180), throwOnTimeout: true);
        decoded.SetResult();

        // The condition, never the clock: the world line is the last thing the map load writes.
        Retry.WhileFalse(() => viewer.Count(WorldBuilt) > 0, TimeSpan.FromSeconds(180), throwOnTimeout: true);

        IReadOnlyList<string> log = viewer.Tail(int.MaxValue);
        string all = string.Join(Environment.NewLine, log);
        int fetchedAt = IndexOf(log, FetchLine);
        int decodedAt = IndexOf(log, DecodedLine);

        fetchedAt.ShouldBeGreaterThanOrEqualTo(0, "no fetch was announced:" + Environment.NewLine + all);
        decodedAt.ShouldBeGreaterThan(fetchedAt, "the fetch did not start before the decode finished");
        mirror.Served.ShouldBe(1, "the local mirror served the map a number of times other than once");
        (await File.ReadAllBytesAsync(Path.Combine(cache, MapName + ".bsp")).ConfigureAwait(false)).Length.ShouldBe(map.Length);

        string worldLine = viewer.LastLine(WorldBuilt).ShouldNotBeNull();
        Match world = WorldLine.Match(worldLine);
        world.Success.ShouldBeTrue(worldLine);
        int.Parse(world.Groups["vertices"].Value, CultureInfo.InvariantCulture).ShouldBeGreaterThan(0, "the world is empty");
    }

    private static int IndexOf(IReadOnlyList<string> log, string text)
    {
        for (int at = 0; at < log.Count; at++)
        {
            if (log[at].Contains(text, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>The map's bytes: this machine's copy, else the real mirror's.</summary>
    private async Task<byte[]> MapToServe()
    {
        using MapProvider here = MapProvider.Installed();

        if (here.Locate(MapName) is { } path)
        {
            return await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        }

        using MapDownloader real = MapDownloader.Create(Directory.CreateDirectory(Path.Combine(_root, "source")).FullName);

        string fetched = await real.TryDownloadAsync(MapName, CancellationToken.None).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{MapName} is neither on this machine nor on {MapDownloader.DefaultMirror}");

        return await File.ReadAllBytesAsync(fetched).ConfigureAwait(false);
    }

    /// <summary>One file at <c>maps/&lt;name&gt;.bsp</c> on localhost; 404 for anything else, the <c>.bsp.bz2</c> asked first included.</summary>
    private sealed class LocalMirror : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly string _path;
        private readonly byte[] _body;
        private readonly Task _release;
        private int _served;

        public LocalMirror(string mapName, byte[] body, Task release)
        {
            _path = "/maps/" + mapName + ".bsp";
            _body = body;
            _release = release;

            using (TcpListener probe = new(IPAddress.Loopback, 0))
            {
                probe.Start();
                Url = $"http://localhost:{((IPEndPoint)probe.LocalEndpoint).Port}/";
                probe.Stop();
            }

            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(Serve);
        }

        public string Url { get; }

        public int Served => Volatile.Read(ref _served);

        public void Dispose() => _listener.Close();

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception closed) when (closed is HttpListenerException or ObjectDisposedException)
                {
                    await TestContext.Progress.WriteLineAsync("local mirror stopped: " + closed.Message).ConfigureAwait(false);
                    return;
                }

                using HttpListenerResponse response = context.Response;

                if (context.Request.Url?.AbsolutePath == _path)
                {
                    await _release.ConfigureAwait(false);
                    Interlocked.Increment(ref _served);
                    response.ContentLength64 = _body.Length;
                    await response.OutputStream.WriteAsync(_body).ConfigureAwait(false);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
        }
    }
}
