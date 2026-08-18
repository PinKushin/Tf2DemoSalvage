using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// The message-stream walk, driven with arbitrary bytes.
/// </summary>
/// <remarks>
/// **This is the widest untrusted surface in the project and nothing fuzzed it.** The four existing
/// targets are primitives — a bit reader, a varint, the container envelope, Snappy. Behind
/// <see cref="NetMessageReader.Read(ReadOnlySpan{byte}, NetDecodeState)"/> sits every message
/// decoder there is: server info, string tables, game events, entity snapshots, sounds, voice,
/// user messages. A single call reaches all of it.
///
/// **The property is TOTALITY, and it is the reader's own documented claim rather than one invented
/// here.** `Read` catches a walk that runs off the end and reports it — "That is reported rather
/// than thrown: the messages already read are good, and the caller decides whether a partial packet
/// is usable." So the target asserts the strongest thing that claim implies: **no input of any shape
/// makes `Read` throw.** An exception escaping is a defect by the reader's own definition, and it
/// is the failure a malformed or truncated demo would produce in a user's hands.
///
/// The structural invariants are checked alongside, because "did not throw" is satisfied by a
/// method that returns nonsense:
///
/// <list type="bullet">
/// <item><c>Messages</c> and <c>MessageStartBits</c> are parallel, so their counts must agree;</item>
/// <item>no message starts before the buffer or after its end;</item>
/// <item>message starts strictly increase — two messages cannot begin at one bit, and a walk that
/// stopped advancing would otherwise loop for ever without tripping anything;</item>
/// <item><c>BitsConsumed</c> stays within the buffer.</item>
/// </list>
///
/// **Protocol is taken from the input rather than fixed**, because the type field is five bits at or
/// below protocol 15 and six above it. Fixing it would leave half the dispatch table unreachable —
/// the same blindness recorded on <see cref="BitReaderFuzzTarget"/> about choosing widths.
/// </remarks>
public static class NetMessageFuzzTarget
{
    /// <summary>Walks <paramref name="data"/> as a packet payload and checks the result holds.</summary>
    /// <param name="data">Arbitrary bytes.</param>
    /// <exception cref="FuzzPropertyViolationException">The reader broke its contract.</exception>
    public static void Consume(ReadOnlySpan<byte> data) => _ = ConsumeAndCountMessages(data);

    /// <summary>
    /// <see cref="Consume"/>, additionally reporting how many messages came back.
    /// </summary>
    /// <remarks>
    /// Exists for the same reason <c>BitReaderFuzzTarget.ConsumeAndCountReads</c> does: a
    /// target that quietly stopped reaching the decoder would make every property hold vacuously,
    /// and the deterministic suite needs to be able to prove work happened.
    /// </remarks>
    /// <returns>The number of messages decoded before the walk stopped.</returns>
    public static int ConsumeAndCountMessages(ReadOnlySpan<byte> data)
    {
        // The first byte picks the protocol, so both type-field widths are reachable. Protocols
        // this project knows run 7..24; anything outside that has no era to belong to.
        ushort protocol = data.Length > 0 ? (ushort)(7 + (data[0] % 18)) : (ushort)24;

        NetDecodeState state = new() { NetworkProtocol = protocol };

        NetMessageReadResult result;

        try
        {
            result = NetMessageReader.Read(data, state);
        }
        catch (Exception error)
        {
            // **Every exception is a finding, with no allowed list.** Unlike the primitive targets,
            // where EndOfStreamException is a documented outcome, this entry point promises to
            // report a truncated or malformed walk instead of throwing. Catching a specific type
            // here would be encoding an exception the contract does not have.
            throw new FuzzPropertyViolationException(
                $"NetMessageReader.Read threw {error.GetType().Name} on {data.Length} bytes at " +
                $"protocol {protocol.ToString(CultureInfo.InvariantCulture)}: {error.Message}",
                error);
        }

        int totalBits = data.Length * 8;

        if (result.Messages.Count != result.MessageStartBits.Count)
        {
            throw new FuzzPropertyViolationException(
                $"{result.Messages.Count} messages but {result.MessageStartBits.Count} start " +
                "offsets; the two lists are documented as parallel.");
        }

        if (result.BitsConsumed < 0 || result.BitsConsumed > totalBits)
        {
            throw new FuzzPropertyViolationException(
                $"BitsConsumed {result.BitsConsumed} is outside the {totalBits}-bit buffer.");
        }

        int previousStart = -1;

        for (int index = 0; index < result.MessageStartBits.Count; index++)
        {
            int start = result.MessageStartBits[index];

            if (start < 0 || start >= totalBits)
            {
                throw new FuzzPropertyViolationException(
                    $"Message {index} starts at bit {start}, outside the {totalBits}-bit buffer.");
            }

            if (start <= previousStart)
            {
                // Not merely untidy: a walk whose position stops advancing is how a reader spins
                // for ever on a malformed packet, and the count check alone would not see it.
                throw new FuzzPropertyViolationException(
                    $"Message {index} starts at bit {start}, not after the previous message's " +
                    $"{previousStart}; the walk did not advance.");
            }

            previousStart = start;
        }

        return result.Messages.Count;
    }
}
