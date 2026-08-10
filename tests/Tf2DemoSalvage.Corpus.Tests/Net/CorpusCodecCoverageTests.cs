using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Measures how much of the message stream is actually deciphered, rather than merely walked.
/// </summary>
/// <remarks>
/// **The scoreboard that decides when the codec is done, because every softer measure has already
/// been passed while the codec was incomplete.** "Decodes end to end with zero stops" is satisfied
/// by a reader that steps over a body it does not understand — the length is known, so the walk
/// stays aligned and nothing looks wrong. That is exactly how <c>svc_Sounds</c>,
/// <c>svc_TempEntities</c> and the voice payload have sat unread while every other test reported
/// success.
///
/// So this counts bits, in two buckets:
///
/// - **Modelled** — every field decoded into a value. The message could be re-encoded from what
///   was kept.
/// - **Opaque** — consumed, its length known, its content discarded or held as raw bytes. The
///   stream stays aligned and the content is not understood.
///
/// A demo is fully deciphered when its opaque share is zero. Nothing else is a completion
/// criterion, and in particular "the trace looks right" is not — the opaque bodies are precisely
/// the ones a trace cannot show, so a readable trace and an undeciphered codec are the same
/// picture.
///
/// This test asserts nothing about the ratio. It reports, because the number is meant to be
/// watched moving rather than gated — and because a gate would be set to today's value and then
/// defended.
/// </remarks>
public sealed class CorpusCodecCoverageTests(ITestOutputHelper output)
{
    /// <summary>Message types whose body is consumed without being understood.</summary>
    /// <remarks>
    /// Judged by what the decoded record retains, not by whether the reader stayed aligned. A
    /// <see cref="SoundsMessage"/> knows its reliability flag and how many sounds it holds and
    /// nothing about any of them; a <see cref="PacketEntitiesMessage"/> keeps its body as raw
    /// bytes, which is enough to re-emit but not enough to say it is understood without the
    /// schema that interprets it.
    /// </remarks>
    private static int OpaqueBits(INetMessage message) => message switch
    {
        SkippedMessage skipped => skipped.BodyBits,
        SoundsMessage sounds => sounds.BodyBits,
        TempEntitiesMessage temp => temp.BodyBits,
        VoiceDataMessage voice => voice.BodyBits,
        EntityMessage entity => entity.BodyBits,
        UserMessage { Fields: null } user => user.BodyBits,
        _ => 0,
    };

    [Fact]
    public void ReportHowMuchOfTheCodecIsDeciphered()
    {
        foreach (string path in Corpus.Files())
        {
            byte[] bytes = File.ReadAllBytes(path);
            ushort protocol = Corpus.ProtocolOf(path);
            NetDecodeState state = new() { NetworkProtocol = protocol };

            long payloadBits = 0;
            long opaque = 0;
            Dictionary<string, long> byType = new(StringComparer.Ordinal);

            foreach (DemoCommand command in
                DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(3000))
            {
                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
                {
                    continue;
                }

                payloadBits += command.Payload.Length * 8L;

                foreach (INetMessage message in
                    NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    int bits = OpaqueBits(message);
                    if (bits <= 0)
                    {
                        continue;
                    }

                    opaque += bits;
                    string key = message is UserMessage named
                        ? $"svc_UserMessage/{named.Name ?? "?"}"
                        : message.Type.ToString();
                    byType[key] = byType.TryGetValue(key, out long seen) ? seen + bits : bits;
                }
            }

            double share = payloadBits == 0 ? 0 : 100.0 * opaque / payloadBits;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {opaque:N0} of {payloadBits:N0} payload bits opaque " +
                $"({share:F2}%)"));

            foreach ((string type, long bits) in byType.OrderByDescending(e => e.Value).Take(6))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture, $"    {bits,12:N0}  {type}"));
            }
        }

        // The corpus has to be present for the numbers above to mean anything. Nothing else is
        // asserted: this is an instrument, and gating it would freeze whatever it reads today.
        Corpus.Files().ShouldNotBeEmpty();
    }
}
