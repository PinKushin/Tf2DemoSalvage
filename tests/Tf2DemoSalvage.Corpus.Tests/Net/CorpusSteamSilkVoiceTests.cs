using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// B441 on real bytes: two matches that announce <c>vaudio_speex</c> carry Steam Voice with SILK
/// inside, and every packet frames, checksums and decodes.
/// </summary>
/// <remarks>
/// Only what real bytes alone can prove (D38): the record layout and the decoder are pinned by
/// synthetic tests in Core.Tests and Audio.Tests. Two lcor POV matches, 2012 at protocol 22 and
/// 2015 at protocol 24, chosen because B441 counted them before the layout was known.
/// </remarks>
public sealed class CorpusSteamSilkVoiceTests
{
    [TestCase("20120909_1804_cp_gullywash_final1_red_fags.dem", 454)]
    [TestCase("20150120_2113_cp_process_final_red_blu.dem", 522)]
    public void EveryPacket_FramesChecksumsAndDecodesAsSilk(string demo, int expectedPackets)
    {
        if (!SilkVoiceDecoder.IsAvailable)
        {
            Assert.Ignore("silk is not built on this machine - run tools/native-audio/build.ps1");
        }

        Corpus.VoiceSummary voice = Corpus.Voice(Corpus.Demo(demo));

        // The control on the premise: the session says Speex, which is why nothing else decoded them.
        voice.Codec.ShouldBe("vaudio_speex");
        voice.Packets.Count.ShouldBe(expectedPackets);

        int frames = 0;
        int samples = 0;
        int silentFrames = 0;
        int emptyFrames = 0;
        int noAudio = 0;
        Dictionary<ulong, SilkVoiceDecoder> decoders = [];

        try
        {
            foreach (Corpus.VoicePacketSummary summary in voice.Packets)
            {
                SteamVoicePayload.TryDecode(summary.Body, out VoicePacket? packet)
                    .ShouldBeTrue($"a {summary.Body.Length}-byte packet is not Steam Voice with a CRC32 tail");

                packet.Codec.ShouldNotBe(SteamVoiceCodec.Opus);

                if (packet.Codec == SteamVoiceCodec.None)
                {
                    // The 15-byte packets: steamID, one silence record, tail — no rate record.
                    noAudio++;
                    continue;
                }

                packet.SampleRate.ShouldBe(SilkVoiceDecoder.SampleRate);

                if (!decoders.TryGetValue(packet.SteamId, out SilkVoiceDecoder? decoder))
                {
                    decoder = new SilkVoiceDecoder();
                    decoders[packet.SteamId] = decoder;
                }

                foreach (VoiceChunk chunk in packet.Chunks)
                {
                    short[] pcm = decoder.Decode(chunk.Data.Span);

                    // Whole 20 ms frames at 16 kHz, or the frame boundary is wrong.
                    (pcm.Length % 320).ShouldBe(0);
                    pcm.Length.ShouldBeGreaterThan(0);

                    frames++;
                    emptyFrames += chunk.Data.IsEmpty ? 1 : 0;
                    samples += pcm.Length;
                    if (Array.TrueForAll(pcm, sample => sample == 0))
                    {
                        silentFrames++;
                    }
                }
            }
        }
        finally
        {
            foreach (SilkVoiceDecoder decoder in decoders.Values)
            {
                decoder.Dispose();
            }
        }

        frames.ShouldBeGreaterThan(0);
        ((double)silentFrames / frames).ShouldBeLessThan(0.5, $"{silentFrames} of {frames} SILK frames were silent");

        TestContext.Out.WriteLine(
            $"{demo}: {expectedPackets} packets ({noAudio} without audio), {frames} SILK frames decoded " +
            $"({emptyFrames} empty, concealed), {samples} samples " +
            $"({samples / (double)SilkVoiceDecoder.SampleRate:F1} s), {decoders.Count} speakers, {silentFrames} silent");
    }
}
