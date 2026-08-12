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
public sealed class CorpusVoiceChecksumTests
{
    [Test]
    public void TheTrailingFourBytesAreACrc32OverEverythingBeforeThem()
    {
        if (!Corpus.AnyDemoUses("steam"))
        {
            Assert.Ignore("no demo present carries steam-codec (Opus) voice - it is absent from the "
            + "committed corpus and lives only in tools/corpus/local");
        }

        int checked_ = 0;
        int matched = 0;

        foreach (string path in Corpus.Files())
        {
            Corpus.VoiceSummary demo = Corpus.Voice(path);
            if (demo.Codec != "steam")
            {
                continue;
            }

            foreach (Corpus.VoicePacketSummary voice in demo.Packets)
            {
                ReadOnlySpan<byte> body = voice.Body;

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

        TestContext.Out.WriteLine($"{matched}/{checked_} voice payload tails are a CRC32 of the body");
    }

}
