using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.BZip2;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>A map an old demo needs: its name, the version it recorded, and where its server served maps.</summary>
/// <param name="Name">The map, without extension, as the demo header names it.</param>
/// <param name="Checksum">
/// What <c>svc_ServerInfo</c> recorded — the four-byte CRC through 2011, the sixteen-byte hash from 2013 —
/// or null when the demo says nothing comparable; see <see cref="BspMapChecksum.Matches"/>.
/// </param>
/// <param name="DemoDownloadUrl">
/// The recording server's <c>sv_downloadurl</c>, or null when it sent none or sent something that is not
/// an absolute address. Only an http or https one is ever asked.
/// </param>
public readonly record struct MapWanted(
    string Name,
    IReadOnlyList<byte>? Checksum = null,
    Uri? DemoDownloadUrl = null);

/// <summary>
/// Fetches a map the user does not have, from a public fast-download mirror.
/// </summary>
/// <remarks>
/// **This is the mechanism a game server already uses.** Joining a server whose map you lack pulls
/// the file from its <c>sv_downloadurl</c> and drops it into your own maps folder. A demo recorded on
/// such a server carries that URL, so it is asked first; a public mirror serving the same layout —
/// <c>ROOT/maps/NAME.bsp</c> — is the fallback (D162).
///
/// **In the engine's order, read from `engine.dll`'s download queue.** It uses the server's URL only
/// when the value begins <c>"http://"</c> or <c>"https://"</c>, and queues <c>"%s.bz2"</c> — the
/// compressed file — before the plain one, unless the compressed one is already on disk. This took the
/// uncompressed form alone until D162, on the reasoning that a decompressor was not worth adding. A
/// server that carries only <c>.bsp.bz2</c>, which is common, then had nothing to give.
///
/// **And the version the demo recorded.** A Valve map keeps its name across updates, so the name
/// cannot choose the version; the demo's checksum can, through <see cref="BspMapChecksum"/>. A file
/// whose checksum differs is refused and the next source tried.
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
    /// <remarks>
    /// A ROOT, as a server's <c>sv_downloadurl</c> is: files are asked for as <c>ROOT/maps/NAME.bsp</c>.
    /// It named the <c>maps/</c> folder itself until the demo's own URL could be asked too (D162).
    /// </remarks>
    public const string DefaultMirror = "https://fastdl.serveme.tf/";

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
    public async Task<string?> TryDownloadAsync(string mapName, CancellationToken cancellationToken) =>
        await TryDownloadAsync(new MapWanted(mapName), cancellationToken).ConfigureAwait(false);

    /// <summary>Downloads the version of a map a demo was recorded against, unless it is already here.</summary>
    /// <param name="wanted">The map, the checksum the demo recorded, and the demo's own download URL.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The path to the map, or null if no source had that version.</returns>
    /// <exception cref="ArgumentException">The map name is a path.</exception>
    /// <remarks>
    /// **Each source, then each form, in the engine's order** (D162): the demo's own
    /// <c>sv_downloadurl</c> when it is an HTTP address, then the mirror; at each, <c>.bsp.bz2</c> and
    /// then <c>.bsp</c>. The first file that is a map and, where the demo recorded a checksum, IS that
    /// map, is kept.
    ///
    /// **Kept under its checksum when one is known**, so two versions of a Valve map — one name, many
    /// files over the years — sit side by side and each is found again without a download. With no
    /// checksum the file keeps the plain name it always had.
    /// </remarks>
    public async Task<string?> TryDownloadAsync(MapWanted wanted, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wanted.Name);
        EnsureIsAName(wanted.Name);

        string destination = Path.Combine(_folder, CacheName(wanted));

        if (File.Exists(destination))
        {
            // Bandwidth belongs to the user. Re-fetching a 40 MB map for every demo recorded on it
            // is also how a client gets itself blocked by a mirror.
            return destination;
        }

        foreach (string root in Roots(wanted))
        {
            foreach ((string suffix, bool compressed) in Forms)
            {
                byte[]? bytes = await TryFetchAsync(
                        new Uri(root + "maps/" + wanted.Name + suffix), compressed, cancellationToken)
                    .ConfigureAwait(false);

                if (bytes is null || !LooksLikeBsp(bytes) || IsAnotherVersion(bytes, wanted.Checksum))
                {
                    continue;
                }

                return await KeepAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>The two forms a map is asked for in, compressed first, as the engine queues them.</summary>
    private static readonly (string Suffix, bool Compressed)[] Forms = [(".bsp.bz2", true), (".bsp", false)];

    /// <summary>Where a map is kept: under its checksum when the demo recorded one.</summary>
    private static string CacheName(MapWanted wanted) =>
        wanted.Checksum is { Count: > 0 } checksum
            ? wanted.Name + "." + Convert.ToHexString([.. checksum]) + ".bsp"
            : wanted.Name + ".bsp";

    /// <summary>The download roots to try, the demo's own first.</summary>
    /// <remarks>
    /// **Only an HTTP address**: the engine compares the value with <c>"http://"</c> and
    /// <c>"https://"</c> before using it, which here is the address's scheme. A scheme is
    /// case-insensitive, so an upper-case <c>HTTP://</c> is admitted; whether the engine's own comparison
    /// ignores case is not settled by the decompile. **A trailing slash is added** where the value lacks
    /// one, so the path joins; whether the engine does the same is likewise not read, and a server whose
    /// value lacks the slash would otherwise produce a URL nobody serves.
    /// </remarks>
    private IEnumerable<string> Roots(MapWanted wanted)
    {
        if (wanted.DemoDownloadUrl is { IsAbsoluteUri: true } own &&
            (own.Scheme == Uri.UriSchemeHttp || own.Scheme == Uri.UriSchemeHttps))
        {
            string address = own.AbsoluteUri;
            string root = address.EndsWith('/') ? address : address + "/";

            if (!string.Equals(root, _mirror, StringComparison.OrdinalIgnoreCase))
            {
                yield return root;
            }
        }

        yield return _mirror;
    }

    /// <summary>Whether a map is a different version from the one the demo recorded.</summary>
    /// <remarks>
    /// **Only a definite no refuses it.** No recorded checksum means nothing to compare, and the file
    /// is taken. A file whose header will not parse cannot be the recorded map, so it is refused too.
    /// </remarks>
    private static bool IsAnotherVersion(byte[] bytes, IReadOnlyList<byte>? checksum)
    {
        try
        {
            return BspMapChecksum.Matches(bytes, checksum) == false;
        }
        catch (InvalidDataException)
        {
            // A map whose lump directory will not parse is not the recorded map; the next source may
            // have one that does, so this is a refusal rather than a failure.
            return true;
        }
    }

    /// <summary>One request, decompressed when it is the <c>.bz2</c> form, or null when it gave no file.</summary>
    private async Task<byte[]?> TryFetchAsync(Uri url, bool compressed, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(url, cancellationToken)
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

            if (bytes.Length == 0)
            {
                return null;
            }

            return compressed ? Decompress(bytes) : bytes;
        }
        catch (Exception failure) when (
            failure is HttpRequestException or IOException or TaskCanceledException)
        {
            // No network, a source that is down, a timeout. The next form or source is tried, and
            // the caller reports it in the status line if none gives a map.
            return null;
        }
    }

    /// <summary>A bzip2 body, expanded, or null when it is not bzip2 or expands past the cap.</summary>
    /// <remarks>
    /// **Capped while expanding**, because the server chooses what it sends and a few kilobytes of
    /// bzip2 can expand to gigabytes. The cap is the same one the download is held to.
    /// </remarks>
    private byte[]? Decompress(byte[] compressed)
    {
        try
        {
            using MemoryStream input = new(compressed);
            using BZip2InputStream expanding = new(input);
            using MemoryStream output = new();
            byte[] chunk = new byte[81920];

            while (expanding.Read(chunk, 0, chunk.Length) is > 0 and int read)
            {
                if (output.Length + read > _maximumBytes)
                {
                    return null;
                }

                output.Write(chunk, 0, read);
            }

            return output.ToArray();
        }
        catch (Exception failure) when (failure is SharpZipBaseException or IOException)
        {
            // Not bzip2 — a server that answers the `.bz2` name with the plain file, or with an error
            // page. The plain form is asked next, so this is a miss rather than a failure.
            return null;
        }
    }

    /// <summary>Writes a map into this program's folder and answers where.</summary>
    private async Task<string?> KeepAsync(string destination, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_folder);

            // Written to a temporary name and moved into place, so an interrupted download cannot
            // leave a half-file that later runs will find and treat as the cached map.
            string partial = destination + ".part";
            await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(partial, destination, overwrite: true);

            return destination;
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            // A full disk or a locked folder. Not a reason for the viewer to stop working; the
            // caller reports the missing map in the status line.
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
