using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Fetches a map the user does not have, from a public fast-download mirror.
/// </summary>
/// <remarks>
/// **This is the mechanism a game server already uses.** Joining a server whose map you lack pulls
/// the file from its <c>sv_downloadurl</c> and drops it into your own maps folder. There is no
/// server here, so the source is a public mirror serving the same layout — <c>/maps/NAME.bsp</c>.
///
/// The mirror serves both <c>.bsp</c> and <c>.bsp.bz2</c>. The uncompressed form is taken
/// deliberately: bzip2 would be a decompressor to add, to maintain, and to harden against hostile
/// input, in exchange for bandwidth that is not this project's to optimise.
///
/// **Everything here is D32 territory.** The map name comes out of a demo header written by a
/// stranger, the response comes from a host nobody here controls, and the result is fed straight
/// into a binary parser. So:
///
/// - The name is refused if it is a path, exactly as <see cref="MapLocator"/> refuses one. The
///   check has to exist on both sides: a program that only *searches* safely still hands a
///   traversal to whatever writes the file.
/// - The download is capped, because the length is the host's choice and not ours.
/// - The bytes are checked for a BSP header before anything is kept. A mirror answering 200 with
///   an HTML error page is a real case — the demo archive run met two of those — and a file kept
///   here would be re-read as a map on every later open.
/// - It writes ONLY into this application's own folder. The user's game install is read-only to
///   this program, which is why <see cref="MapLocator"/> searches it and this never touches it.
/// </remarks>
public sealed class MapDownloader : IDisposable
{
    /// <summary>The mirror used when none is configured.</summary>
    /// <remarks>
    /// HTTPS, and that is not decoration: over plain HTTP anyone on the path could substitute a
    /// file of their choosing, and the result is a forty-megabyte input to a binary parser.
    /// </remarks>
    public const string DefaultMirror = "https://fastdl.serveme.tf/maps/";

    /// <summary>Largest map this will accept, in bytes.</summary>
    /// <remarks>
    /// The biggest map in a stock TF2 install is about 92 MB, so 256 MB leaves room for a large
    /// community map while still bounding what a hostile or broken mirror can write to the disk.
    /// </remarks>
    public const int DefaultMaximumBytes = 256 * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly string _folder;
    private readonly string _mirror;
    private readonly int _maximumBytes;

    /// <summary>Builds a downloader that owns its own HTTP client.</summary>
    /// <param name="folder">This application's own maps folder.</param>
    /// <returns>A downloader; disposing it disposes the client it made.</returns>
    /// <remarks>
    /// **Exists so ownership of the <see cref="HttpClient"/> never has to cross a call site.** The
    /// constructor below takes one and disposes it, which is correct and which CA2000 cannot see —
    /// it watches the `new HttpClient()` at the caller and reports it as undisposed, because the
    /// transfer of ownership through a constructor is invisible to it. Callers that have no reason
    /// to care which client is used should call this instead of arguing with the analyzer.
    ///
    /// The constructor stays public for the tests, which supply a client with a stubbed handler.
    /// </remarks>
    public static MapDownloader Create(string folder) => new(new HttpClient(), folder);

    /// <summary>Builds a downloader.</summary>
    /// <param name="client">Client to fetch with; disposed with this object.</param>
    /// <param name="folder">This application's own maps folder.</param>
    /// <param name="mirror">Base URL ending in a slash.</param>
    /// <param name="maximumBytes">Largest file to accept.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public MapDownloader(
        HttpClient client,
        string folder,
        string mirror = DefaultMirror,
        int maximumBytes = DefaultMaximumBytes)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(mirror);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        _client = client;
        _folder = folder;
        _mirror = mirror.EndsWith('/') ? mirror : mirror + "/";
        _maximumBytes = maximumBytes;
    }

    /// <summary>Downloads a map, unless it is already present.</summary>
    /// <param name="mapName">Map name from a demo header, without extension.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The path to the map, or null if it could not be fetched.</returns>
    /// <exception cref="ArgumentException"><paramref name="mapName"/> is a path.</exception>
    /// <remarks>
    /// **Null is the ordinary answer, not a failure.** Most maps in a real demo archive are
    /// community maps no mirror carries, and a demo of one still plays — the viewer shows the
    /// players without a world behind them. Anything that would turn "no map" into "no viewer" is
    /// the wrong behaviour for a program whose entire purpose is salvage.
    /// </remarks>
    public async Task<string?> TryDownloadAsync(string mapName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        EnsureIsAName(mapName);

        string destination = Path.Combine(_folder, mapName + ".bsp");

        if (File.Exists(destination))
        {
            // Bandwidth belongs to the user. Re-fetching a 40 MB map for every demo recorded on it
            // is also how a client gets itself blocked by a mirror.
            return destination;
        }

        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(new Uri(_mirror + mapName + ".bsp"), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Checked before reading where the header offers it, and again by counting as the body
            // arrives. A Content-Length is a claim by the same host that sends the body.
            if (response.Content.Headers.ContentLength > _maximumBytes)
            {
                return null;
            }

            byte[] bytes = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);

            if (bytes.Length == 0 || !LooksLikeBsp(bytes))
            {
                return null;
            }

            Directory.CreateDirectory(_folder);

            // Written to a temporary name and moved into place, so an interrupted download cannot
            // leave a half-file that later runs will find and treat as the cached map.
            string partial = destination + ".part";
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(partial, destination, overwrite: true);

            return destination;
        }
        catch (Exception failure) when (
            failure is HttpRequestException or IOException or TaskCanceledException
                or UnauthorizedAccessException)
        {
            // No network, a mirror that is down, a full disk. None of it is a reason for the
            // viewer to stop working, and the caller reports it in the status line.
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>Refuses a name that is really a path.</summary>
    /// <remarks>
    /// The same rule as <see cref="MapLocator"/>. A header naming
    /// <c>..\..\Windows\System32\config\SAM</c> must not choose where a downloaded file lands, and
    /// it must not choose what gets requested from the mirror either.
    /// </remarks>
    private static void EnsureIsAName(string mapName)
    {
        if (mapName.Contains('/', StringComparison.Ordinal) ||
            mapName.Contains('\\', StringComparison.Ordinal) ||
            mapName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(mapName) ||
            mapName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                $"'{mapName}' is not a map name; it contains a path.", nameof(mapName));
        }
    }

    /// <summary>Reads the body, stopping if it exceeds the cap.</summary>
    /// <remarks>
    /// Counted while reading rather than trusted from a header: a host that lies about
    /// Content-Length, or omits it under chunked encoding, is exactly the host this guards against.
    /// </remarks>
    private async Task<byte[]> ReadCappedAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using Stream body = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];

        while (true)
        {
            int read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > _maximumBytes)
            {
                return [];
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    /// <summary>Whether the bytes begin like a Source map.</summary>
    /// <remarks>
    /// The magic and a plausible version, not a full parse. This is a cheap gate against a mirror
    /// answering 200 with an HTML error page or a login redirect; the real validation is
    /// <see cref="BspHeader"/>, which runs when the map is read.
    /// </remarks>
    private static bool LooksLikeBsp(byte[] bytes)
    {
        if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual("VBSP"u8))
        {
            return false;
        }

        int version = BitConverter.ToInt32(bytes, 4);

        // Source BSP versions run from 17 to 21 in practice; TF2 ships 20 and 21.
        return version is >= 17 and <= 21;
    }

    /// <summary>Where downloaded maps are kept.</summary>
    /// <remarks>
    /// Under LocalApplicationData, and never the game's own maps folder. Writing a downloaded file
    /// into a Steam install would put a stranger's bytes where the game loads them from, which is
    /// a different and much worse program than this one.
    /// </remarks>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "maps");

    /// <summary>Describes a failed attempt for the status line.</summary>
    /// <param name="mapName">The map that was wanted.</param>
    /// <returns>A sentence for the user.</returns>
    public string DescribeFailure(string mapName) => string.Create(
        CultureInfo.InvariantCulture,
        $"Map {mapName} was not found here or on {new Uri(_mirror).Host}.");
}
