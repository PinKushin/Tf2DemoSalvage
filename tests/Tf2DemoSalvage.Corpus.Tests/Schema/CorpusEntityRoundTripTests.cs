using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Re-encodes decoded entity snapshots and compares the bits against the demo.
/// </summary>
/// <remarks>
/// **The evidence the entity decoder never had.** Every other check on it is blind to a property
/// read at the wrong width when the width happens to leave the walk in step: the entity count
/// comes out right, the class names resolve, the trace reads plausibly, and the value is simply
/// wrong. Re-encoding cannot be fooled that way — the bits either come back or they do not.
///
/// It is not circular, and the reason is worth stating because the encoder was written from the
/// decoder. Nothing here is replayed from raw bits: every bit written is derived from a value the
/// decoder produced, so a misread value cannot be written back correctly. The comparison is
/// against the demo's own bytes, not against a second pass of our own.
///
/// Reported per demo rather than gated, on the same principle as the codec coverage report: the
/// number is meant to be watched moving.
///
/// **This instrument and the assembly round trip disagree, and the disagreement is unresolved.**
/// Over whole demos this reports 96.87% while the assembly writer — which encodes through the same
/// <c>EntityDecoder.EncodeEntities</c> and falls back to raw on any difference — declines
/// nothing at all on the same files. One of the two is measuring something other than what it
/// says. The assembly gate is the stronger statement of the two, because it compares against the
/// demo end to end rather than body by body, so this number should be treated as unexplained
/// rather than as a defect count until they are reconciled. See RISKS B25.
/// </remarks>
public sealed class CorpusEntityRoundTripTests(ITestOutputHelper output)
{
    /// <summary>Commands read per demo, so a full run stays inside a normal test cycle.</summary>
    private const int CommandLimit = 900;

    [Fact]
    public void ReportHowManyEntitySnapshotsReEncodeExactly()
    {
        long snapshots = 0;
        long exact = 0;
        List<string> firstFailures = [];

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            (long total, long matched, string? failure) = Measure(path);
            snapshots += total;
            exact += matched;

            if (failure is not null && firstFailures.Count < 6)
            {
                firstFailures.Add($"{name}: {failure}");
            }

            if (total == 0)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {matched:N0} of {total:N0} snapshots re-encode exactly " +
                $"({100.0 * matched / total:F2}%)"));
        }

        foreach (string failure in firstFailures)
        {
            output.WriteLine($"    first mismatch - {failure}");
        }

        // Guards against the two ways this could report a clean run while measuring nothing: a
        // corpus that stopped being read, and a decoder that started failing every snapshot.
        snapshots.ShouldBeGreaterThan(1000);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"total: {exact:N0} of {snapshots:N0} ({100.0 * exact / snapshots:F2}%)"));
    }

    private static (long Total, long Exact, string? FirstFailure) Measure(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = Corpus.ProtocolOf(path);
        NetDecodeState state = new() { NetworkProtocol = protocol };
        EntityDecoder? decoder = null;

        long total = 0;
        long exact = 0;
        string? firstFailure = null;

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(CommandLimit))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                try
                {
                    DemoSchema schema = SendTableParser.Parse(command.Payload.Span, protocol);
                    decoder = new EntityDecoder(
                        schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
                }
                catch (InvalidDataException)
                {
                    // One corpus demo has no readable schema at all: a protocol-11 SourceTV
                    // recording whose writer truncated dem_datatables at 64 KiB (RISKS B24). It
                    // has no entities to re-encode, and that is the writer's fault rather than
                    // this decoder's, so it is skipped rather than counted as a failure.
                    return (0, 0, null);
                }

                continue;
            }

            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet) ||
                decoder is null)
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is not PacketEntitiesMessage snapshot)
                {
                    continue;
                }

                IReadOnlyList<DecodedEntity> entities;
                try
                {
                    entities = decoder.Decode(
                        snapshot.Body.Span, snapshot, snapshot.LengthBits);
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
                {
                    continue;
                }

                total++;
                byte[] rewritten = decoder.EncodeEntities(
                    entities, decoder.RemovedEntities, snapshot.IsDelta, snapshot.LengthBits,
                    out int encodedBits);

                int difference = FirstDifferingBit(
                    snapshot.Body.Span, rewritten, snapshot.LengthBits);

                if (difference < 0)
                {
                    exact++;
                    continue;
                }

                // Separated because they are different findings. A difference at or past the last
                // bit this encoder wrote is in the sender's trailing slack - the body is stated in
                // bits but built in bytes, and nothing says the leftover bits are zero. A
                // difference before that point is a field this project got wrong.
                firstFailure ??= difference >= encodedBits
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"tick {command.Tick}, bit {difference} of {snapshot.LengthBits}, past " +
                        $"the {encodedBits} bits of content - trailing slack, not a field")
                    : Describe(decoder, snapshot, entities, command.Tick, difference);
            }
        }

        return (total, exact, firstFailure);
    }

    /// <summary>
    /// Names the entity and property a mismatch starts in, rather than just the bit.
    /// </summary>
    /// <remarks>
    /// A bit offset says a snapshot is wrong and nothing about why. Encoding progressively longer
    /// prefixes of the entity list and finding the first one that stops matching identifies the
    /// entity; its property list then names the candidates. The prefixes are encoded as non-delta
    /// with no padding so they carry no removal section and no trailing zeros — both of which sit
    /// after the entities and would otherwise end every prefix in bits the original does not have
    /// at that offset.
    /// </remarks>
    private static string Describe(
        EntityDecoder decoder,
        PacketEntitiesMessage snapshot,
        IReadOnlyList<DecodedEntity> entities,
        int tick,
        int differingBit)
    {
        for (int count = 1; count <= entities.Count; count++)
        {
            byte[] prefix = decoder.EncodeEntities(
                [.. entities.Take(count)], [], isDelta: false, lengthBits: 0, out int prefixBits);

            if (FirstDifferingBit(snapshot.Body.Span, prefix, prefixBits) < 0)
            {
                continue;
            }

            int startBits = count == 1
                ? 0
                : BitsUpTo(decoder, entities, count - 1);

            DecodedEntity culprit = entities[count - 1];
            string original = Bits(snapshot.Body.Span, startBits, prefixBits - startBits);
            string rewritten = Bits(prefix, startBits, prefixBits - startBits);
            string properties = string.Join(
                ", ",
                culprit.Properties.Take(8).Select(
                    property => property.Definition.Property.Name));

            return string.Create(
                CultureInfo.InvariantCulture,
                $"tick {tick}, bit {differingBit} of {snapshot.LengthBits}, entity " +
                $"{culprit.EntityIndex} class {culprit.ClassId} {culprit.UpdateType} with " +
                $"{culprit.Properties.Count} properties: {properties}; " +
                $"wire {original} ours {rewritten} from bit {startBits}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"tick {tick}, bit {differingBit} of {snapshot.LengthBits}, in the removal list " +
            $"rather than in any entity");
    }

    /// <summary>Bits the first <paramref name="count"/> entities occupy.</summary>
    private static int BitsUpTo(
        EntityDecoder decoder, IReadOnlyList<DecodedEntity> entities, int count)
    {
        decoder.EncodeEntities(
            [.. entities.Take(count)], [], isDelta: false, lengthBits: 0, out int bits);
        return bits;
    }

    /// <summary>Renders a short run of bits, so a mismatch can be read rather than inferred.</summary>
    private static string Bits(ReadOnlySpan<byte> source, int startBit, int count)
    {
        System.Text.StringBuilder text = new();
        for (int i = 0; i < Math.Min(count, 48); i++)
        {
            int bit = startBit + i;
            int index = bit / 8;
            text.Append(index >= source.Length ? '.' : (char)('0' + ((source[index] >> (bit % 8)) & 1)));
        }

        return text.ToString();
    }

    /// <summary>Index of the first bit that differs, or -1 when they match.</summary>
    private static int FirstDifferingBit(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int index = bit / 8;
            if (index >= right.Length)
            {
                return bit;
            }

            int shift = bit % 8;
            if (((left[index] >> shift) & 1) != ((right[index] >> shift) & 1))
            {
                return bit;
            }
        }

        return -1;
    }
}
