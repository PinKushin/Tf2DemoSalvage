using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Re-encodes decoded messages and compares the bits against the demo they came from.
/// </summary>
/// <remarks>
/// **The check that can see a field read and thrown away.** Everything else in this suite is
/// blind to that: the reader stays aligned because the field was consumed, the trace looks
/// complete because the message is named, and the length self-check passes because the length was
/// right. The information is simply gone, and nothing reports it. Writing the message back and
/// comparing bits has no such blind spot — a dropped field is a bit that cannot be reproduced.
///
/// Two tests, and the difference matters:
///
/// - <see cref="EveryWritableMessage_ReproducesItsOwnBitsExactly"/> is a **gate**. Any message the
///   writer claims it can write has to come back identical, on every demo in the corpus. It fails
///   naming the demo, the message type and the bit offset.
/// - <see cref="ReportHowMuchOfThePayloadRoundTrips"/> is an **instrument**, like the codec
///   coverage report. It says how much of each demo's payload is reproducible today, which is what
///   turns "the decode is lossless" from an opinion into a number that moves.
///
/// The instrument does not assert a threshold, for the reason the codec one does not: a threshold
/// gets set to today's value and then defended.
/// </remarks>
public sealed class CorpusMessageRoundTripTests(ITestOutputHelper output)
{
    /// <summary>Commands read per demo, so the suite stays inside a normal test run.</summary>
    private const int CommandLimit = 1500;

    [Fact]
    public void EveryWritableMessage_ReproducesItsOwnBitsExactly()
    {
        int checkedMessages = 0;

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);

            foreach (Packet packet in Packets(path))
            {
                // Written one at a time against a fresh writer, so a mismatch names the message
                // rather than "somewhere in this packet". The offset each message starts at comes
                // from re-reading the packet, which is why the reader is asked for it below.
                foreach (WrittenMessage written in packet.Messages)
                {
                    if (!NetMessageWriter.CanWrite(written.Message))
                    {
                        continue;
                    }

                    BitWriter writer = new();
                    NetMessageWriter.TryWrite(writer, written.Message, written.State)
                        .ShouldBeTrue(name);

                    writer.BitCount.ShouldBe(
                        written.BitLength,
                        $"{name}: {written.Message.Type} at bit {written.StartBit} " +
                        $"re-encoded to a different length");

                    BitsAt(packet.Payload, written.StartBit, written.BitLength)
                        .ShouldBe(
                            BitsAt(writer.Build(), 0, written.BitLength),
                            $"{name}: {written.Message.Type} at bit {written.StartBit}");

                    checkedMessages++;
                }
            }
        }

        // A filter that matches nothing passes silently, and so does a corpus that stopped being
        // read. The count is the guard against both.
        checkedMessages.ShouldBeGreaterThan(10000);
        output.WriteLine($"{checkedMessages:N0} messages re-encoded bit for bit");
    }

    [Fact]
    public void ReportHowMuchOfThePayloadRoundTrips()
    {
        foreach (string path in Corpus.Files())
        {
            long total = 0;
            long written = 0;
            Dictionary<string, long> missing = new(StringComparer.Ordinal);

            foreach (Packet packet in Packets(path))
            {
                foreach (WrittenMessage message in packet.Messages)
                {
                    total += message.BitLength;
                    if (NetMessageWriter.CanWrite(message.Message))
                    {
                        written += message.BitLength;
                        continue;
                    }

                    string key = message.Message.Type.ToString();
                    missing[key] = missing.TryGetValue(key, out long seen)
                        ? seen + message.BitLength
                        : message.BitLength;
                }
            }

            double share = total == 0 ? 0 : 100.0 * written / total;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {written:N0} of {total:N0} message bits round-trip " +
                $"({share:F2}%)"));

            foreach ((string type, long bits) in missing.OrderByDescending(e => e.Value).Take(5))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"    {bits,12:N0}  {type}"));
            }
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    /// <summary>One decoded message, with where it sat and the state it was read under.</summary>
    private sealed record WrittenMessage(
        INetMessage Message, int StartBit, int BitLength, NetDecodeState State);

    private sealed record Packet(byte[] Payload, IReadOnlyList<WrittenMessage> Messages);

    /// <summary>
    /// Reads a demo's packets, recording each message's bit extent.
    /// </summary>
    /// <remarks>
    /// The extents come from reading the packet one message at a time and asking the reader where
    /// it stopped, rather than from a parallel implementation of the framing. A second copy of the
    /// framing would agree with the first about a message it read wrongly.
    ///
    /// A separate write-side state is carried because the reader's own state has already advanced
    /// by the time a packet is finished: <c>svc_ServerInfo</c> sets the protocol that sizes later
    /// fields, so the writer has to see it arrive at the same point in the stream rather than
    /// being handed the finished article.
    /// </remarks>
    private static IEnumerable<Packet> Packets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort protocol = Corpus.ProtocolOf(path);
        NetDecodeState readState = new() { NetworkProtocol = protocol };
        NetDecodeState writeState = new() { NetworkProtocol = protocol };

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(CommandLimit))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            byte[] payload = command.Payload.ToArray();
            NetMessageReadResult result = NetMessageReader.Read(payload, readState);
            List<WrittenMessage> messages = new(result.Messages.Count);

            for (int i = 0; i < result.Messages.Count; i++)
            {
                INetMessage message = result.Messages[i];
                int start = result.MessageStartBits[i];
                int end = i + 1 < result.Messages.Count
                    ? result.MessageStartBits[i + 1]
                    : result.BitsConsumed;

                messages.Add(new WrittenMessage(
                    message, start, end - start, Snapshot(writeState)));

                // ServerInfo has to reach the write state at the point in the stream it arrived,
                // not at the end of the packet: it sets the protocol that sizes later fields, and
                // a message before it is read at protocol 0.
                // The write state has to see what the read state saw, at the point it saw it.
                // ServerInfo sets the protocol that sizes later fields; a game event list supplies
                // the field ORDER every later event is written in.
                if (message is ServerInfoMessage info)
                {
                    writeState.ServerInfo = info;
                }
                else if (message is GameEventListMessage list)
                {
                    writeState.AddEventDefinitions(list.Definitions);
                }
            }

            yield return new Packet(payload, messages);
        }
    }

    private static NetDecodeState Snapshot(NetDecodeState state)
    {
        NetDecodeState copy = new()
        {
            NetworkProtocol = state.NetworkProtocol,
            ServerInfo = state.ServerInfo,
        };

        copy.AddEventDefinitions(state.EventDefinitions.Values);
        return copy;
    }

    /// <summary>Copies <paramref name="bits"/> bits starting at <paramref name="startBit"/>.</summary>
    private static byte[] BitsAt(byte[] source, int startBit, int bits)
    {
        BitWriter writer = new();
        for (int i = 0; i < bits; i++)
        {
            int bit = startBit + i;
            writer.Write((uint)((source[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.Build();
    }
}
