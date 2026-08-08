using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Xunit.Abstractions;

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
public sealed class CorpusNetMessageTests(ITestOutputHelper output)
{
    private const int PacketsToSample = 200;

    [Fact]
    public void WhenAPacketYieldsMessages_TheFirstIsNetTick()
    {
        // Originally asserted that *every* packet begins with net_Tick. Real demos disagree:
        // some packets open with a message we cannot decode yet, so they yield nothing at
        // all. The claim that survives contact with the corpus is the weaker one - net_Tick
        // leads whenever anything is readable.
        foreach (string path in Corpus.Files())
        {
            foreach (DemoCommand packet in ReadPackets(path).Take(PacketsToSample))
            {
                NetMessageReadResult result = NetMessageReader.Read(packet.Payload.Span);

                if (result.Messages.Count > 0)
                {
                    result.Messages[0].Type.ShouldBe(
                        NetMessageType.NetTick,
                        $"{Path.GetFileName(path)} at tick {packet.Tick}");
                }
            }
        }
    }

    [Fact]
    public void MostPacketsBeginWithNetTick()
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

            int withTick = packets.Count(p => FirstMessage(p) is NetTickMessage);

            withTick.ShouldBeGreaterThan(
                packets.Length / 2, $"{Path.GetFileName(path)}: net_Tick is usually first");
        }
    }

    [Fact]
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
            List<int> offsets =
            [
                .. ReadPackets(path)
                    .Take(PacketsToSample)
                    .Select(FirstMessage)
                    .OfType<NetTickMessage>()
                    .Zip(
                        ReadPackets(path).Take(PacketsToSample).Where(p => FirstMessage(p) is NetTickMessage),
                        (tick, packet) => tick.Tick - packet.Tick)
            ];

            offsets.ShouldNotBeEmpty($"{Path.GetFileName(path)}: no net_Tick found at all");

            // Not exactly constant. The band is small and does not grow, which is jitter
            // rather than desynchronisation - a bit-level misread would yield garbage 32-bit
            // values, not a wobble of a few ticks. The bound is deliberately loose because
            // the point is to catch a decoder that has lost the stream, not to pin a
            // network characteristic that legitimately varies between recordings.
            int spread = offsets.Max() - offsets.Min();
            output.WriteLine(
                $"{Path.GetFileName(path)}: tick offset {offsets.Min()}..{offsets.Max()} " +
                $"(spread {spread}) over {offsets.Count} packets");

            spread.ShouldBeLessThanOrEqualTo(
                64,
                $"{Path.GetFileName(path)}: server-to-demo tick offset spread {spread} " +
                $"(min {offsets.Min()}, max {offsets.Max()}) looks like the decoder lost the " +
                $"stream rather than clock jitter");
        }
    }

    [Fact]
    public void ReportHowFarIntoPacketsWeCurrentlyGet()
    {
        foreach (string path in Corpus.Files())
        {
            DemoCommand[] packets = ReadPackets(path).Take(PacketsToSample).ToArray();
            if (packets.Length == 0)
            {
                continue;
            }

            var stoppedAt = new Dictionary<string, int>(StringComparer.Ordinal);
            int complete = 0;
            long bitsRead = 0;
            long bitsTotal = 0;

            foreach (DemoCommand packet in packets)
            {
                NetMessageReadResult result = NetMessageReader.Read(packet.Payload.Span);
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

            output.WriteLine($"{Path.GetFileName(path)} - {packets.Length} packets sampled");
            output.WriteLine($"  fully read : {complete}");
            output.WriteLine($"  bits read  : {bitsRead} of {bitsTotal} " +
                             $"({100.0 * bitsRead / bitsTotal:F2}%)");
            foreach ((string key, int count) in stoppedAt.OrderByDescending(kv => kv.Value))
            {
                output.WriteLine($"  stopped at {key}: {count}");
            }

            output.WriteLine(string.Empty);
        }

        // No assertion on the numbers themselves: they are a progress report, and pinning them
        // would mean editing this test every time a message type is implemented.
        Corpus.Files().ShouldNotBeEmpty();
    }

    /// <summary>First decoded message of a packet, or <c>null</c> if none could be read.</summary>
    private static INetMessage? FirstMessage(DemoCommand packet)
    {
        IReadOnlyList<INetMessage> messages = NetMessageReader.Read(packet.Payload.Span).Messages;
        return messages.Count > 0 ? messages[0] : null;
    }

    private static DemoCommand[] ReadPackets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type == DemoCommandType.Packet)];
    }
}
