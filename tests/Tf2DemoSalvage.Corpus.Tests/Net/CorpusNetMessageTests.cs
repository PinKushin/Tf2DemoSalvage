using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Runs the message reader over real packets, and reports how far into them it gets.
/// </summary>
/// <remarks>
/// Because messages carry no length prefix, "how far we get" is the honest measure of layer 2
/// progress — and it only goes up as message types are implemented. These tests assert the
/// floor rather than the ceiling, so they keep passing as coverage improves rather than
/// needing an edit every time.
/// </remarks>
public sealed class CorpusNetMessageTests
{
    private const int PacketsToSample = 200;

    // Removed: an assertion that the first decodable message is always net_Tick. It held
    // only because packets opening with svc_GameEvent used to yield nothing at all; once
    // GameEvent was implemented they decoded, and some packets genuinely do lead with an
    // event rather than a tick. Packets_TheCorpus_MostlyBeginWithNetTick below carries the real claim.

    [Test]
    public void Packets_TheCorpus_MostlyBeginWithNetTick()
    {
        // A floor, not an equality: it guards against a regression that stops decoding
        // net_Tick entirely, without pinning a number that shifts as more messages land.
        foreach (string path in Corpus.Files())
        {
            DemoCommand[] packets = [.. ReadPackets(path).Take(PacketsToSample)];
            if (packets.Length == 0)
            {
                continue;
            }

            ushort protocol = ProtocolOf(path);
            int withTick = packets.Count(p => FirstMessage(p, protocol) is NetTickMessage);

            withTick.ShouldBeGreaterThan(
                packets.Length / 2, $"{Path.GetFileName(path)}: net_Tick is usually first");
        }
    }

    [Test]
    public void NetTickRunsOnTheServerClock_AtAConstantOffsetFromTheDemoClock()
    {
        // This began as "the two ticks are equal". They are not, and the difference is the
        // finding: the container's command header counts from the start of the recording,
        // while net_Tick carries the server's own absolute tick. The same reason a demo's
        // dem_signon commands sit at an implausible tick.
        //
        // Constancy is the stronger assertion anyway. Equality would only prove the two
        // agree; a fixed offset across hundreds of packets proves both decoders advance in
        // lockstep, and they are encoded completely differently - a 32-bit little-endian
        // field in the container versus 32 bits read from a bit stream at an arbitrary
        // offset. Any desynchronisation in either shows up immediately.
        foreach (string path in Corpus.Files())
        {
            ushort protocol = ProtocolOf(path);
            List<int> offsets =
            [
                .. ReadPackets(path)
                    .Take(PacketsToSample)
                    .Select(packet => (packet, first: FirstMessage(packet, protocol)))
                    .Where(pair => pair.first is NetTickMessage)
                    .Select(pair => ((NetTickMessage)pair.first!).Tick - pair.packet.Tick)
            ];

            offsets.ShouldNotBeEmpty($"{Path.GetFileName(path)}: no net_Tick found at all");

            // Not exactly constant. The band is small and does not grow, which is jitter
            // rather than desynchronisation - a bit-level misread would yield garbage 32-bit
            // values, not a wobble of a few ticks.
            //
            // **Measured across an unbroken stretch, not across the whole sample**, and the
            // difference is a real distinction rather than a tolerance. A recording can contain
            // gaps: the client stalls, records nothing for a while, and resumes. That moves the
            // offset permanently, because server time passed and demo time did not.
            //
            // A stall and a desynchronisation look identical if you only measure min-to-max, and
            // they are trivially separable if you look at the demo clock as well:
            //
            //   stall  - BOTH clocks gap. Consecutive packets sit seconds apart on the demo
            //            clock too, and the offset is rock stable either side of the step.
            //   desync - only the server tick misbehaves. The demo clock keeps advancing one to
            //            three ticks per packet while the decoded value goes wrong.
            //
            // So the sample is split at demo-clock gaps and each unbroken run is measured on its
            // own. Found the hard way: a pub demo recorded while this repo's own mutation suite
            // was saturating the machine froze the game for about 36 seconds mid-match, stepping
            // the offset by 3,500 ticks. The old assertion called that "the decoder lost the
            // stream". It had not - the same file decodes every entity snapshot it is offered.
            List<List<int>> runs = [[]];
            int previousTick = int.MinValue;

            foreach ((DemoCommand packet, INetMessage? first) in Sampled(path, protocol))
            {
                if (first is not NetTickMessage tick)
                {
                    continue;
                }

                if (previousTick != int.MinValue && packet.Tick - previousTick > GapTicks)
                {
                    runs.Add([]);
                }

                runs[^1].Add(tick.Tick - packet.Tick);
                previousTick = packet.Tick;
            }

            List<int> longest = runs.OrderByDescending(run => run.Count).First();
            int gaps = runs.Count - 1;
            int spread = longest.Count == 0 ? 0 : longest.Max() - longest.Min();

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: tick offset {offsets.Min()}..{offsets.Max()} over " +
                $"{offsets.Count} packets; longest unbroken run {longest.Count} packets, " +
                $"spread {spread}, recording gaps {gaps}");

            // A garbage 32-bit read lands here whatever the gaps did. Server ticks are bounded by
            // how long a server has been up, so an offset in the millions is a misread and not a
            // recording that paused - no stall can manufacture one.
            offsets.Max(Math.Abs).ShouldBeLessThan(
                ImplausibleOffset,
                $"{Path.GetFileName(path)}: offset {offsets.Min()}..{offsets.Max()} is outside " +
                $"anything a server clock produces, so the field was misread");

            spread.ShouldBeLessThanOrEqualTo(
                64,
                $"{Path.GetFileName(path)}: within an unbroken run of {longest.Count} packets " +
                $"the server-to-demo offset still spreads {spread}. Gaps are excluded, so this " +
                $"is the decoder losing the stream rather than the recording pausing");
        }
    }

    /// <summary>A demo-clock jump this large is a recording gap, not the usual one to three.</summary>
    /// <remarks>
    /// Half a second at TF2's tick rate. Normal packet spacing is one to three ticks, and the
    /// stalls this separates out are hundreds — so the threshold sits in a wide empty band rather
    /// than near either population, which is what makes it a classification and not a tolerance.
    /// </remarks>
    private const int GapTicks = 32;

    /// <summary>Beyond any real server uptime, so only a misread reaches it.</summary>
    private const int ImplausibleOffset = 100_000_000;

    private static IEnumerable<(DemoCommand Packet, INetMessage? First)> Sampled(
        string path, ushort protocol) =>
        ReadPackets(path)
            .Take(PacketsToSample)
            .Select(packet => (packet, FirstMessage(packet, protocol)));

    [Test]
    public void PacketProgress_TheCorpus_IsReported()
    {
        foreach (string path in Corpus.Files())
        {
            DemoCommand[] packets = ReadPackets(path).Take(PacketsToSample).ToArray();
            if (packets.Length == 0)
            {
                continue;
            }

            Dictionary<string, int> stoppedAt = new(StringComparer.Ordinal);
            int complete = 0;
            long bitsRead = 0;
            long bitsTotal = 0;

            foreach (DemoCommand packet in packets)
            {
                NetMessageReadResult result = NetMessageReader.Read(
                    packet.Payload.Span, new NetDecodeState { NetworkProtocol = ProtocolOf(path) });
                bitsRead += result.BitsConsumed;
                bitsTotal += packet.Payload.Length * 8L;

                if (result.IsComplete)
                {
                    complete++;
                }
                else
                {
                    string key = result.StoppedAt?.ToString() ?? "undefined id";
                    if (result.Messages.Count == 0)
                    {
                        key += " (first message)";
                    }

                    stoppedAt.TryGetValue(key, out int count);
                    stoppedAt[key] = count + 1;
                }
            }

            TestContext.Out.WriteLine($"{Path.GetFileName(path)} - {packets.Length} packets sampled");
            TestContext.Out.WriteLine($"  fully read : {complete}");
            TestContext.Out.WriteLine($"  bits read  : {bitsRead} of {bitsTotal} " +
                             $"({100.0 * bitsRead / bitsTotal:F2}%)");
            foreach ((string key, int count) in stoppedAt.OrderByDescending(kv => kv.Value))
            {
                TestContext.Out.WriteLine($"  stopped at {key}: {count}");
            }

            TestContext.Out.WriteLine(string.Empty);
        }

        // No assertion on the numbers themselves: they are a progress report, and pinning them
        // would mean editing this test every time a message type is implemented.
        Corpus.Files().ShouldNotBeEmpty();
    }

    /// <summary>First decoded message of a packet, or <c>null</c> if none could be read.</summary>
    /// <remarks>
    /// The protocol is not optional. The message type field is five bits at protocol 15 and below
    /// and six above (RISKS B17), so decoding an old demo with the default state reads one bit too
    /// many and everything after it is noise.
    ///
    /// This silently passed for a long time. Reading six bits where five were written yields the
    /// SAME value whenever the sixth bit happens to be zero, which for the 2009 demo's first
    /// message it usually was - so the omission looked correct until a protocol-14 demo arrived
    /// and the coincidence stopped holding.
    /// </remarks>
    private static INetMessage? FirstMessage(DemoCommand packet, ushort networkProtocol)
    {
        NetDecodeState state = new() { NetworkProtocol = networkProtocol };
        IReadOnlyList<INetMessage> messages =
            NetMessageReader.Read(packet.Payload.Span, state).Messages;
        return messages.Count > 0 ? messages[0] : null;
    }

    /// <summary>The demo's network protocol, from its header.</summary>
    private static ushort ProtocolOf(string path) => Corpus.ProtocolOf(path);

    private static DemoCommand[] ReadPackets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type == DemoCommandType.Packet)];
    }
}
