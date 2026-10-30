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
public sealed class CorpusCeltSpeexVoiceTests
{
    private const int CeltFrameBytes = 64;
    private const int SpeexNarrowbandFrameBytes = 28;

    [Test]
    public void EveryCeltFrame_DecodesToPcm()
    {
        if (!CeltVoiceDecoder.IsAvailable)
        {
            Assert.Ignore("celt is not built on this machine - run tools/native-audio/build.ps1");
        }

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

        TestContext.Out.WriteLine($"{frames} CELT frames decoded, {silentFrames} silent ({silentRate:P1})");
    }

    [Test]
    public void EverySpeexFrame_DecodesToPcm()
    {
        if (!SpeexVoiceDecoder.IsAvailable)
        {
            Assert.Ignore("speex is not built on this machine - run tools/native-audio/build.ps1");
        }

        int frames = 0;
        int silentFrames = 0;
        int steamPackets = 0;
        int silkFrames = 0;

        // A silent decode is the decoder's to answer for only when the INPUT was not silent. libspeex at quality 5
        // encodes 160 zero samples to exactly SilenceFrame (the 3420 pair: the microphone returned zeros, so every one
        // of its frames is that one), so a frame that IS the silence frame decodes silent correctly; a silent decode
        // of any other frame is a decoder or framing fault. Per frame, not per demo: a demo can mix speech with a
        // stretch of zeros (the lcor 4231 and 4269 runs do), and the evidence is the frame's own bytes.
        Dictionary<string, int> unexplainedByDemo = [];
        Dictionary<string, HashSet<string>> distinctByDemo = [];
        Dictionary<string, SpeexVoiceDecoder> decoders = [];
        Dictionary<(string, ulong), SilkVoiceDecoder> silk = [];

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

                    // B441: a vaudio_speex session from 2011 on carries Steam Voice (SILK). The
                    // engine chose by sv_use_steam_voice, which the demo does not record; the
                    // payload's own CRC32 tail is what it does show.
                    if (SteamVoicePayload.TryDecode(body, out VoicePacket? steam))
                    {
                        steamPackets++;

                        if (!silk.TryGetValue((path, steam.SteamId), out SilkVoiceDecoder? speaker))
                        {
                            speaker = new SilkVoiceDecoder();
                            silk[(path, steam.SteamId)] = speaker;
                        }

                        foreach (VoiceChunk chunk in steam.Chunks)
                        {
                            speaker.Decode(chunk.Data.Span).Length.ShouldBeGreaterThan(0);
                            silkFrames++;
                        }

                        continue;
                    }

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
                        ReadOnlySpan<byte> encoded = body.Slice(at, SpeexNarrowbandFrameBytes);
                        short[] pcm = decoder.Decode(encoded);

                        frames++;

                        if (!distinctByDemo.TryGetValue(path, out HashSet<string>? distinct))
                        {
                            distinct = [];
                            distinctByDemo[path] = distinct;
                        }

                        distinct.Add(Convert.ToHexString(encoded));

                        if (Array.TrueForAll(pcm, sample => sample == 0))
                        {
                            silentFrames++;

                            if (!IsSilenceFrame(encoded))
                            {
                                unexplainedByDemo[path] = unexplainedByDemo.GetValueOrDefault(path) + 1;
                            }
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

            foreach (SilkVoiceDecoder decoder in silk.Values)
            {
                decoder.Dispose();
            }
        }

        frames.ShouldBeGreaterThan(0, "no Speex frame reached the decoder");

        // The premise is the decoder's: a frame that is not the silence frame must not decode to silence. (The earlier
        // rule, under half the frames silent, asserted that the corpus's voice was audible, which is a claim about what
        // was recorded and was false for a whole pair; D38.)
        List<string> unexplained =
        [
            .. unexplainedByDemo.Select(demo => $"{Path.GetFileName(demo.Key)}: {demo.Value} silent decodes of frames that are not the silence frame"),
        ];

        unexplained.ShouldBeEmpty("a silent decode from a frame that is not digital silence");

        // The controls that keep the rule above from passing vacuously: if every Speex demo were one repeated frame, or
        // everything decoded to zeros, `unexplained` would be empty for the wrong reason.
        distinctByDemo.Values.Count(distinct => distinct.Count > 1).ShouldBeGreaterThan(
            0, "control: at least one demo carries varied encoded frames, so the rule above has input to judge");
        (frames - silentFrames).ShouldBeGreaterThan(0, "control: some Speex frame decodes to audible PCM");

        TestContext.Out.WriteLine(
            $"{frames} Speex frames decoded across {decoders.Count} demos, " +
            $"{silentFrames} silent, every one the silence frame (digital silence in); " +
            $"{steamPackets} Steam Voice packets, {silkFrames} SILK frames");
    }

    /// <summary>
    /// libspeex 1.2.1 at quality 5 encoding 160 zero samples (the specimen-triage probe's <c>speex-encode</c>, 2026-10-29).
    /// 220 payload bits in 28 bytes, so the last nibble is padding and only the first 55 hex digits are compared.
    /// </summary>
    private const string SilenceFrameHex = "269D6600001B366CD9B0001B366CD9B0001B366CD9B0001B366CD9B7";

    private static bool IsSilenceFrame(ReadOnlySpan<byte> encoded) =>
        Convert.ToHexString(encoded).AsSpan(0, SilenceFrameHex.Length - 1)
            .SequenceEqual(SilenceFrameHex.AsSpan(0, SilenceFrameHex.Length - 1));

}
