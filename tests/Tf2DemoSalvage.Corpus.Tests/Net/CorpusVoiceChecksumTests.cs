using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Identifies the four trailing bytes of a <c>steam</c>-codec voice payload.
/// </summary>
/// <remarks>
/// **A differential finding, verified rather than adopted.** This project derived the Steam voice
/// framing independently, by exact consumption — steamID, typed sub-packets, then four bytes it
/// could account for positionally but not explain, and so carried uninterpreted rather than
/// guessed at. <c>demostf/steam-audio-codec</c>, an unrelated open-source implementation, arrives
/// at a byte-identical structure and names those four bytes a CRC32 over everything preceding
/// them.
///
/// Agreement between two independent readings is worth something, but it is still someone else's
/// claim until this project checks it against its own data — which is what this test does, over
/// every Steam-codec voice packet the corpus holds. A wrong guess about *which* bytes are covered
/// would pass no packets at all; being right passes all of them.
/// </remarks>
public sealed class CorpusVoiceChecksumTests(ITestOutputHelper output)
{
    [Fact]
    public void TheTrailingFourBytesAreACrc32OverEverythingBeforeThem()
    {
        int checked_ = 0;
        int matched = 0;

        foreach (string path in Corpus.Files())
        {
            foreach (VoiceDataMessage voice in SteamVoice(path))
            {
                ReadOnlySpan<byte> body = voice.Body.Span;

                if (body.Length < 12)
                {
                    continue;
                }

                VoicePacket packet = SteamVoicePayload.Decode(body);

                // Everything except the trailing four bytes: the steamID and every sub-packet.
                uint computed = Crc32.HashToUInt32(body[..^4]);

                checked_++;
                if (computed == packet.Tail)
                {
                    matched++;
                }
            }
        }

        checked_.ShouldBeGreaterThan(0, "no steam-codec voice packet reached the check");

        // All or nothing: a CRC either covers the right range or it does not, and a partial match
        // rate would mean the range is wrong rather than that some packets are corrupt.
        matched.ShouldBe(
            checked_,
            $"{matched} of {checked_} payload tails matched a CRC32 of the preceding bytes");

        output.WriteLine($"{matched}/{checked_} voice payload tails are a CRC32 of the body");
    }

    private static IEnumerable<VoiceDataMessage> SteamVoice(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };
        bool steam = false;

        foreach (DemoCommand command in
            DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))
                .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state)
                .Messages)
            {
                if (message is VoiceInitMessage init)
                {
                    steam = string.Equals(init.Codec, "steam", StringComparison.Ordinal);
                }

                if (steam && message is VoiceDataMessage voice && voice.BodyBits > 0)
                {
                    yield return voice;
                }
            }
        }
    }
}
