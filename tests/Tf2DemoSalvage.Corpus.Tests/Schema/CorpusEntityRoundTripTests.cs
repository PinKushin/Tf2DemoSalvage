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
/// **This compares CONTENT, and the distinction is what reconciled it with the assembly gate.**
/// It used to compare the whole stated body length, which meant comparing the caller's zero-fill
/// against whatever the sender happened to leave after its last field - and reporting that as a
/// decoder defect. Over whole demos that read as 96.87%; measured over the content it is 99.59%,
/// and the difference was never about the decoder at all.
///
/// The leftover is reported separately because it is a fact about the format: 32,407 snapshots end
/// before their stated length, 3.47 M bits in total. <c>EntityDecoder.EncodeEntities</c>
/// cannot reproduce those - it is given entities, not the sender's buffer - which is exactly why
/// the assembly writer carries them on a <c>slack</c> line instead.
/// </remarks>
public sealed class CorpusEntityRoundTripTests
{
    /// <summary>Commands read per demo, so a full run stays inside a normal test cycle.</summary>
    private const int CommandLimit = 900;

    [Test]
    public void EntityRoundTrip_TheCorpus_IsReported()
    {
        long snapshots = 0;
        long exact = 0;
        long slackBearing = 0;
        long slackBits = 0;
        List<string> firstFailures = [];

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            (long total, long matched, long bearing, long bits, string? failure) = Measure(path);
            snapshots += total;
            exact += matched;
            slackBearing += bearing;
            slackBits += bits;

            if (failure is not null && firstFailures.Count < 6)
            {
                firstFailures.Add($"{name}: {failure}");
            }

            if (total == 0)
            {
                continue;
            }

            TestContext.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {matched:N0} of {total:N0} snapshots re-encode exactly " +
                $"({100.0 * matched / total:F2}%)"));
        }

        foreach (string failure in firstFailures)
        {
            TestContext.Out.WriteLine($"    first mismatch - {failure}");
        }

        // Guards against the two ways this could report a clean run while measuring nothing: a
        // corpus that stopped being read, and a decoder that started failing every snapshot.
        snapshots.ShouldBeGreaterThan(1000);

        // **This was a REPORT until 2026-08-19, and that was the mistake.** It counted exact
        // re-encodes, printed a percentage, and asserted only the guard above — so entity
        // byte-fidelity, which is one of the larger claims this project makes, could have fallen
        // from 100% to 3% and the suite would have stayed green with the number in the log.
        //
        // The measured answer is 4,017 of 4,017 across every era in the corpus, protocols 11
        // through 24, point-of-view and SourceTV alike. That is not a threshold to be tuned; a
        // snapshot that does not re-encode to its own bits means a property was read and
        // discarded, and the reader stays aligned while the information is gone.
        //
        // So it is asserted at 100% rather than at a floor. A floor here would be a decision to
        // tolerate losing entity data, and there is no version of this project where that is
        // acceptable — see docs/memory/decode-must-be-total.md.
        exact.ShouldBe(
            snapshots,
            "every entity snapshot must re-encode to the bits it came from; anything less means a " +
            "property was read and discarded, which no length check can see");
        TestContext.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"total: {exact:N0} of {snapshots:N0} ({100.0 * exact / snapshots:F2}%) " +
            $"re-encode their content exactly"));

        // Reported rather than folded into the failure count, because it is a fact about the
        // format rather than about this decoder: a body is stated in bits and built in bytes, so
        // it can end before its stated end, and what sits in the gap is not reliably zero.
        TestContext.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{slackBearing:N0} snapshots end before their stated length, {slackBits:N0} bits " +
            $"in total - carried by the assembly writer on a slack line"));
    }

    private static (long Total, long Exact, long SlackBearing, long SlackBits, string? FirstFailure)
        Measure(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = Corpus.ProtocolOf(path);
        NetDecodeState state = new() { NetworkProtocol = protocol };
        EntityDecoder? decoder = null;

        long total = 0;
        long exact = 0;
        long slackBearing = 0;
        long slackBits = 0;
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
                    return (0, 0, 0, 0, null);
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

                // **Compared over the content, not over the stated length.** EncodeEntities
                // encodes entities; it is not given the bits the sender left after them and
                // cannot invent them. Comparing the padded region measured the caller's zero-fill
                // against the demo's leftovers and reported it as a decoder defect - which is
                // what made this instrument disagree with the assembly round trip, where those
                // bits travel explicitly on a `slack` line.
                if (snapshot.LengthBits > encodedBits)
                {
                    slackBearing++;
                    slackBits += snapshot.LengthBits - encodedBits;
                }

                // Clamped, because the encoder can also write MORE than the stated length - a
                // field wider than the one that was read. That is a mismatch to report, not an
                // index to run off the end of the original with.
                int comparable = Math.Min(encodedBits, snapshot.LengthBits);
                int difference = encodedBits > snapshot.LengthBits
                    ? comparable
                    : FirstDifferingBit(snapshot.Body.Span, rewritten, comparable);

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

        return (total, exact, slackBearing, slackBits, firstFailure);
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
