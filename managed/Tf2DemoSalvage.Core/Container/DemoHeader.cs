using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Core.Container;

/// <summary>
/// The fixed 1072-byte header at the start of every <c>.dem</c> file.
/// </summary>
/// <remarks>
/// Layout is CONFIRMED against three corpus demos spanning two point-of-view types and three
/// servers — see <c>docs/SPEC.md</c>. Little-endian throughout, no alignment padding.
///
/// Note what this type deliberately does *not* do: validate that the declared counts are
/// plausible. A salvage tool's job is to report what a file claims, including when the claim is
/// wrong. Callers compare these values against what the stream actually contains.
/// </remarks>
public sealed record DemoHeader
{
    /// <summary>Every TF2 demo begins with these eight bytes.</summary>
    private const string ExpectedStamp = "HL2DEMO";

    private const int StampWidth = 8;
    private const int TextFieldWidth = 260;

    /// <summary>Total size of the header, in bytes.</summary>
    public const int SizeBytes = 1072;

    /// <summary>Demo container format version. 3 for every corpus demo so far.</summary>
    public required int DemoProtocol { get; init; }

    /// <summary>
    /// Source network protocol version. 24 across TF2's modern history — this dates nothing,
    /// see <c>docs/SPEC.md</c>.
    /// </summary>
    public required int NetworkProtocol { get; init; }

    /// <summary>
    /// The server's <c>hostname</c> cvar. Operator-chosen free text, frequently an
    /// advertisement — not evidence of who ran the server.
    /// </summary>
    public required string ServerName { get; init; }

    /// <summary>
    /// Recording client's name, or <c>"SourceTV Demo"</c> for an STV recording. For a
    /// point-of-view demo this is the recording player's in-game name.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>Map the match was played on, e.g. <c>cp_process_final</c>.</summary>
    public required string MapName { get; init; }

    /// <summary>Game directory, <c>tf</c> for Team Fortress 2.</summary>
    public required string GameDirectory { get; init; }

    /// <summary>Declared playback length in seconds.</summary>
    public required float PlaybackTimeSeconds { get; init; }

    /// <summary>Declared tick count. The <c>dem_stop</c> command's tick should match this.</summary>
    public required int PlaybackTicks { get; init; }

    /// <summary>
    /// Declared frame count, which equals the number of <c>dem_packet</c> commands in a
    /// well-formed demo — the strongest available check that the container was walked correctly.
    /// </summary>
    public required int PlaybackFrames { get; init; }

    /// <summary>Length in bytes of the signon data — the embedded entity schema.</summary>
    public required int SignonLengthBytes { get; init; }

    /// <summary>Reads a header from the start of <paramref name="data"/>.</summary>
    /// <param name="data">Buffer positioned at the beginning of a demo file.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="EndOfStreamException">
    /// <paramref name="data"/> is shorter than <see cref="SizeBytes"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">The file stamp is not <c>HL2DEMO</c>.</exception>
    public static DemoHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < SizeBytes)
        {
            throw new EndOfStreamException(string.Create(
                CultureInfo.InvariantCulture,
                $"A demo header is {SizeBytes} bytes, but only {data.Length} are available."));
        }

        string stamp = ReadFixedText(data[..StampWidth]);
        if (!string.Equals(stamp, ExpectedStamp, StringComparison.Ordinal))
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected the file stamp '{ExpectedStamp}' but found '{stamp}'."));
        }

        float seconds = BitConverter.ToSingle(data[1056..]);
        int ticks = BitConverter.ToInt32(data[1060..]);
        int frames = BitConverter.ToInt32(data[1064..]);
        int signon = BitConverter.ToInt32(data[1068..]);

        // **Nothing below is validated, and that is a decision rather than an omission.** A
        // malformed value is the caller's to judge: this is a salvage tool, and a file whose header
        // claims -4 ticks may still hold a perfectly good match. Refusing to open it would lose the
        // match to protect nobody.
        //
        // The obligation that replaces validation is on the CONSUMERS, and it is not optional —
        // `DemoSurvey.Measure` treats any non-positive tick count as "the header states nothing"
        // and recovers the real extent by walking the command stream, which is salvage rather than
        // rejection. `DemoHeaderHostileInputTests` holds that line for each field: the value
        // survives parsing, and whatever reads it copes.
        //
        // Guards were briefly added here on 2026-08-21 and reverted the same day, because two
        // existing tests already recorded this decision and both went red. See
        // `Parse_NonPositiveSignonLength_IsAcceptedAndReported` and
        // `Measure_HeaderDeclaresNegativeTicks_IsTreatedAsUnstated`.
        return new DemoHeader
        {
            DemoProtocol = BitConverter.ToInt32(data[8..]),
            NetworkProtocol = BitConverter.ToInt32(data[12..]),
            ServerName = ReadFixedText(data.Slice(16, TextFieldWidth)),
            ClientName = ReadFixedText(data.Slice(276, TextFieldWidth)),
            MapName = ReadFixedText(data.Slice(536, TextFieldWidth)),
            GameDirectory = ReadFixedText(data.Slice(796, TextFieldWidth)),
            PlaybackTimeSeconds = seconds,
            PlaybackTicks = ticks,
            PlaybackFrames = frames,
            SignonLengthBytes = signon,
        };
    }

    /// <summary>
    /// Decodes a fixed-width NUL-padded field, stopping at the first NUL.
    /// </summary>
    /// <remarks>
    /// Bytes after the terminator are undefined and must not be trusted to be zero — real demos
    /// leave whatever was in the buffer there. Decoding the whole field would append garbage to
    /// the map name.
    ///
    /// **UTF-8, not ASCII.** These fields are raw bytes the engine copied from UTF-8 sources, so
    /// ASCII replaces every byte above 0x7F with a question mark. A demo recorded by a player
    /// named <c>miałker</c> reported the header client as <c>mia??ker</c> while the
    /// <c>userinfo</c> table — which already read UTF-8 — reported it correctly, in the same dump.
    /// Both fields held a plausible name and nothing failed; the wrong one was only visible
    /// beside the right one.
    /// </remarks>
    private static string ReadFixedText(ReadOnlySpan<byte> field)
    {
        int terminator = field.IndexOf((byte)0);
        ReadOnlySpan<byte> text = terminator < 0 ? field : field[..terminator];
        return Encoding.UTF8.GetString(text);
    }
}
