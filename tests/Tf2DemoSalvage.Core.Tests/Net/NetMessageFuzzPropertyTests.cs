using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Fuzz;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The deterministic layer for the message-stream fuzz target.
/// </summary>
/// <remarks>
/// Same shape as <c>BitReaderFuzzPropertyTests</c>: the property the libFuzzer target enforces,
/// driven over seeded reproducible inputs so it runs in the normal suite in milliseconds and a
/// failure names a fixed seed rather than an input nobody can reproduce.
///
/// **The claim under test is the reader's own.** `NetMessageReader.Read` catches a walk that runs
/// off the end and reports it through `StopReason` — "the messages already read are good, and the
/// caller decides whether a partial packet is usable". So no input should make it throw, and that
/// is exactly what a truncated or corrupt demo in a user's hands would exercise.
///
/// **Truncation is the most valuable generator here and is deliberately exhaustive.** Random bytes
/// mostly stop at the first unimplemented message type; a VALID packet cut at every possible byte
/// boundary walks real decoders right up to the point where the body runs out, which is where a
/// reader that trusts a declared length rather than the buffer will fail.
/// </remarks>
public sealed class NetMessageFuzzPropertyTests
{
    /// <summary>Fixed so a failure is reproducible.</summary>
    private const int Seed = 20260818;

    private const int RandomCaseCount = 2000;
    private const int MaxRandomLength = 128;

    [Test]
    public void Consume_SeededRandomBuffers_NeverViolatesTheProperty()
    {
        Random random = new(Seed);

        for (int i = 0; i < RandomCaseCount; i++)
        {
            byte[] data = new byte[random.Next(0, MaxRandomLength + 1)];
            random.NextBytes(data);

            Should.NotThrow(() => NetMessageFuzzTarget.Consume(data));
        }
    }

    [Test]
    public void Consume_EveryTruncationOfARealPacket_NeverViolatesTheProperty()
    {
        byte[] packet = Packet();

        for (int length = 0; length <= packet.Length; length++)
        {
            byte[] truncated = packet[..length];

            Should.NotThrow(
                () => NetMessageFuzzTarget.Consume(truncated),
                $"a packet truncated to {length} bytes should be reported, not thrown");
        }
    }

    [Test]
    public void Consume_EverySingleBitFlipOfARealPacket_NeverViolatesTheProperty()
    {
        // **Corruption rather than truncation**, which reaches a different failure: a flipped bit
        // can turn a known message type into an unimplemented one, invert a length, or make a
        // count enormous, all while the buffer stays exactly as long as the reader expects.
        byte[] packet = Packet();

        for (int index = 0; index < packet.Length; index++)
        {
            for (int bit = 0; bit < 8; bit++)
            {
                byte[] corrupted = [.. packet];
                corrupted[index] ^= (byte)(1 << bit);

                Should.NotThrow(
                    () => NetMessageFuzzTarget.Consume(corrupted),
                    $"flipping bit {bit} of byte {index} should be reported, not thrown");
            }
        }
    }

    [Test]
    public void Consume_AValidPacket_ActuallyDecodesMessages()
    {
        // **The control, and the reason it is here.** Every other test asserts that nothing was
        // thrown, which a target reaching no decoder at all satisfies perfectly. This one proves
        // the harness does work - the same guard BitReaderFuzzTarget's read count exists for.
        NetMessageFuzzTarget.ConsumeAndCountMessages(Packet())
            .ShouldBeGreaterThan(0, "the fuzz target should be reaching real decoders");
    }

    [TestCaseSource(nameof(StructuredBuffers))]
    public void Consume_StructuredEdgeCaseBuffers_NeverViolatesTheProperty(byte[] data)
    {
        Should.NotThrow(() => NetMessageFuzzTarget.Consume(data));
    }

    private static IEnumerable<byte[]> StructuredBuffers()
    {
        yield return [];
        yield return [0x00];
        yield return [0xFF];

        // All bits set and all clear at several lengths: the first is every message type at once
        // as far as the walk gets, the second is a run of net_NOP.
        yield return new byte[64];
        yield return [.. new byte[64].AsSpan().ToArray().AsSpan()];

        byte[] ones = new byte[64];
        Array.Fill(ones, (byte)0xFF);
        yield return ones;

        // A single byte short of a type field, which is the boundary the walk's own "fewer bits
        // left than a type field means padding" check sits on.
        yield return [0x01];
    }

    /// <summary>A packet carrying several real messages, built by this project's own writer.</summary>
    /// <remarks>
    /// Built rather than taken from a demo so this suite needs no corpus and runs anywhere,
    /// including the measurement box. The messages are ones with no schema dependency, so the
    /// packet decodes fully in isolation.
    /// </remarks>
    private static byte[] Packet()
    {
        NetDecodeState state = new() { NetworkProtocol = 24 };
        BitWriter writer = new();

        NetMessageWriter.TryWrite(writer, new NetTickMessage(120935, 1500, 42), state);
        NetMessageWriter.TryWrite(writer, new SetViewMessage(3), state);
        NetMessageWriter.TryWrite(writer, new PrintMessage("a message"), state);
        NetMessageWriter.TryWrite(writer, new PrefetchMessage(77), state);
        NetMessageWriter.TryWrite(writer, new SignOnStateMessage(6, 12345), state);

        return writer.Build();
    }
}
