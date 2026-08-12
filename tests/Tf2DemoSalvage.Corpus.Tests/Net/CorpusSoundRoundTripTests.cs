using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Re-encodes every decoded sound body and compares the bits against the demo.
/// </summary>
/// <remarks>
/// **The independent check <see cref="SoundDecoder"/> never had.** Every other decoder here can be
/// cross-checked against demostf/parser; that one cannot, because demostf leaves sound bodies
/// opaque. Its layout came from Valve's <c>soundinfo.h</c> and was accepted on the strength of the
/// values looking plausible — and plausible is precisely what a wrong delta base produces, since
/// the flag bits rather than the values decide how much is read.
///
/// Reproducing the bits closes that in a way no plausibility check can. It also tests the encoder
/// harder than the decoder in one respect: the wire permits a field to be sent or omitted, so the
/// encoder has to make the same choice the sender made. Every one of those choices is a claim
/// about the engine, and a wrong claim shows up as a mismatch rather than as a nicer number.
/// </remarks>
public sealed class CorpusSoundRoundTripTests
{
    [Test]
    public void EverySoundBody_ReEncodesToTheBitsItCameFrom()
    {
        int bodies = 0;
        int sounds = 0;
        List<string> mismatches = [];

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            string name = Path.GetFileName(path);

            foreach (SoundsMessage message in Messages(path))
            {
                IReadOnlyList<DecodedSound> decoded;
                try
                {
                    decoded = SoundDecoder.Decode(
                        message.Body.Span, message.Count, message.BodyBits, protocol);
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
                {
                    continue;
                }

                bodies++;
                sounds += decoded.Count;

                (byte[] rewritten, int bits) = SoundEncoder.Encode(decoded, protocol);
                if (bits != message.BodyBits || !SameBits(message.Body.Span, rewritten, bits))
                {
                    mismatches.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{name}: {decoded.Count} sounds re-encoded to {bits} bits, not " +
                        $"{message.BodyBits}"));
                }
            }
        }

        TestContext.Out.WriteLine($"{bodies:N0} bodies, {sounds:N0} sounds re-encoded");

        // Both guards matter. A corpus that stopped being read would otherwise report a clean run,
        // and so would a decoder that started failing every body - the catch above skips those.
        bodies.ShouldBeGreaterThan(1000);
        mismatches.ShouldBeEmpty();
    }

    private static bool SameBits(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int index = bit / 8;
            int shift = bit % 8;
            if (((left[index] >> shift) & 1) != ((right[index] >> shift) & 1))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<SoundsMessage> Messages(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(2000))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is SoundsMessage sounds && sounds.BodyBits > 0)
                {
                    yield return sounds;
                }
            }
        }
    }
}
