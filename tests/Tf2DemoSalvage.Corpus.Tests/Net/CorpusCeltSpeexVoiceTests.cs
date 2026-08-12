using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Decodes the corpus's CELT and Speex voice — bare, unframed codec streams — to real PCM.
/// </summary>
/// <remarks>
/// Both codecs' frames arrive with no framing of their own: <c>findings/02-net-messages.md</c>
/// measured every <c>vaudio_celt</c> packet as an exact multiple of 64 bytes and every
/// pre-2016 <c>vaudio_speex</c> packet as an exact multiple of 28, with nothing marking where one
/// frame ends and the next begins beyond that fixed width. So "the corpus" here means walking
/// each packet in fixed-size slices, not parsing a header the way <see cref="SteamVoicePayload"/>
/// does.
///
/// **CELT's mode parameters are two integers recovered from TF2's own shipped
/// <c>vaudio_celt.dll</c>** — see <see cref="NativeCelt"/> for how. They are confirmed correct as
/// far as the binary can confirm them: <c>celt_mode_create</c> and
/// <c>celt_decoder_create_custom</c> both succeed with these exact values, and the mono channel
/// count was independently confirmed from the same binary's call site. What is NOT yet confirmed
/// is that they are the right parameters for the specific bytes <c>svc_VoiceData</c> carries — see
/// <c>RISKS.md</c> B33. <see cref="EveryCeltFrame_DecodesToPcm"/> is <c>Skip</c>ped rather than
/// deleted so the moment this is resolved, removing the skip is the whole fix.
/// </remarks>
public sealed class CorpusCeltSpeexVoiceTests(ITestOutputHelper output)
{
    private const int CeltFrameBytes = 64;
    private const int SpeexNarrowbandFrameBytes = 28;

    [Fact]
    public void EveryCeltFrame_DecodesToPcm()
    {
        int frames = 0;
        int silentFrames = 0;

        foreach (string path in Corpus.Files())
        {
            Corpus.VoiceSummary demo = Corpus.Voice(path);
            if (demo.Codec != "vaudio_celt")
            {
                continue;
            }

            foreach (Corpus.VoicePacketSummary voice in demo.Packets)
            {
                ReadOnlySpan<byte> body = voice.Body;

                (body.Length % CeltFrameBytes).ShouldBe(
                    0, $"{Path.GetFileName(path)}: a {body.Length}-byte CELT payload is not a " +
                       $"whole number of {CeltFrameBytes}-byte frames");

                // Fresh decoder per packet, matching what B33's investigation established as the
                // most forgiving condition: intra-packet concatenation is guaranteed contiguous
                // by construction, so this cannot fail from cross-packet state carried over a
                // real network gap.
                using CeltVoiceDecoder decoder = new();

                for (int at = 0; at < body.Length; at += CeltFrameBytes)
                {
                    // The decisive check. A wrong mode parameter throws here, on real bytes the
                    // game itself produced - there is no plausible-wrong-answer failure mode for
                    // this test the way there could be for a hand-built fixture.
                    short[] pcm = decoder.Decode(body.Slice(at, CeltFrameBytes));

                    frames++;
                    if (Array.TrueForAll(pcm, sample => sample == 0))
                    {
                        silentFrames++;
                    }
                }
            }
        }

        frames.ShouldBeGreaterThan(0, "no CELT frame reached the decoder");

        double silentRate = (double)silentFrames / frames;
        silentRate.ShouldBeLessThan(0.5, $"{silentFrames} of {frames} CELT frames were silent");

        output.WriteLine($"{frames} CELT frames decoded, {silentFrames} silent ({silentRate:P1})");
    }

    [Fact]
    public void EverySpeexFrame_DecodesToPcm()
    {
        int frames = 0;
        int silentFrames = 0;
        Dictionary<string, SpeexVoiceDecoder> decoders = [];

        try
        {
            foreach (string path in Corpus.Files())
            {
                Corpus.VoiceSummary demo = Corpus.Voice(path);
                if (demo.Codec != "vaudio_speex")
                {
                    continue;
                }

                foreach (Corpus.VoicePacketSummary voice in demo.Packets)
                {
                    ReadOnlySpan<byte> body = voice.Body;

                    (body.Length % SpeexNarrowbandFrameBytes).ShouldBe(
                        0, $"{Path.GetFileName(path)}: a {body.Length}-byte Speex payload is " +
                           $"not a whole number of {SpeexNarrowbandFrameBytes}-byte frames");

                    if (!decoders.TryGetValue(path, out SpeexVoiceDecoder? decoder))
                    {
                        // One decoder per demo, not per speaker: the corpus's pre-2016 demos are
                        // single-viewpoint recordings of a small listen-server session, and
                        // splitting by client slot would need the roster - unnecessary for what
                        // this test checks, which is only that real frames decode.
                        decoder = new SpeexVoiceDecoder();
                        decoders[path] = decoder;
                    }

                    for (int at = 0; at < body.Length; at += SpeexNarrowbandFrameBytes)
                    {
                        short[] pcm = decoder.Decode(
                            body.Slice(at, SpeexNarrowbandFrameBytes));

                        frames++;
                        if (Array.TrueForAll(pcm, sample => sample == 0))
                        {
                            silentFrames++;
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (SpeexVoiceDecoder decoder in decoders.Values)
            {
                decoder.Dispose();
            }
        }

        frames.ShouldBeGreaterThan(0, "no Speex frame reached the decoder");

        double silentRate = (double)silentFrames / frames;
        silentRate.ShouldBeLessThan(0.5, $"{silentFrames} of {frames} Speex frames were silent");

        output.WriteLine(
            $"{frames} Speex frames decoded across {decoders.Count} demos, " +
            $"{silentFrames} silent ({silentRate:P1})");
    }

}
