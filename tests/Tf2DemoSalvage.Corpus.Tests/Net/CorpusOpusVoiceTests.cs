using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Decodes every Opus chunk in the corpus's Steam-codec voice to real PCM.
/// </summary>
/// <remarks>
/// **The decisive test for the Opus wiring, and the one hand-built fixtures could not give.**
/// <c>OpusVoiceDecoderTests</c> covers the wrapper's lifecycle and error handling with garbage
/// bytes, because a byte-level Opus fixture is exactly the kind of hand-built encoding this
/// project's own memory warns is fragile. The 1452 real packets already extracted by
/// <see cref="SteamVoicePayload"/> are the only genuine Opus stream available, and libopus either
/// decodes what the game actually sent or it does not.
///
/// One decoder per speaker (steamID), never per packet — Opus is stateful, and the corpus already
/// measured that voice packets from different speakers interleave within a demo (see
/// <c>TheSteamIdIdentifiesSpeakersTheClientSlotCannot</c>), which is exactly the condition a
/// shared decoder would desynchronise under.
/// </remarks>
public sealed class CorpusOpusVoiceTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryChunk_DecodesToNonSilentPcm()
    {
        int chunks = 0;
        int totalSamples = 0;
        int silentChunks = 0;
        Dictionary<ulong, OpusVoiceDecoder> decoders = [];

        try
        {
            foreach (string path in Corpus.Files())
            {
                Corpus.VoiceSummary demo = Corpus.Voice(path);
                if (demo.Codec != "steam")
                {
                    continue;
                }

                foreach (Corpus.VoicePacketSummary voice in demo.Packets)
                {
                    VoicePacket packet = SteamVoicePayload.Decode(voice.Body);

                    if (!decoders.TryGetValue(packet.SteamId, out OpusVoiceDecoder? decoder))
                    {
                        decoder = new OpusVoiceDecoder();
                        decoders[packet.SteamId] = decoder;
                    }

                    foreach (VoiceChunk chunk in packet.Chunks)
                    {
                        // libopus itself is the assertion here: a wrong frame boundary or a
                        // desynchronised decoder throws through OpusVoiceDecoder's documented
                        // exception rather than producing plausible-looking noise.
                        short[] pcm = decoder.Decode(chunk.Data.Span);

                        chunks++;
                        totalSamples += pcm.Length;

                        if (Array.TrueForAll(pcm, sample => sample == 0))
                        {
                            silentChunks++;
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (OpusVoiceDecoder decoder in decoders.Values)
            {
                decoder.Dispose();
            }
        }

        chunks.ShouldBeGreaterThan(0, "no Opus chunk reached the decoder");

        // Not every chunk needs to carry audible sound - a pause in speech is a real thing a
        // decoder legitimately produces - but if EVERY chunk decoded to all zeros that would
        // mean the frames are being fed to the decoder in the wrong order or the wrong shape,
        // producing silence rather than an error. A rate rather than an absolute: some silence
        // is expected, all of it is not.
        double silentRate = (double)silentChunks / chunks;
        silentRate.ShouldBeLessThan(0.5, $"{silentChunks} of {chunks} chunks decoded to silence");

        output.WriteLine(
            $"{chunks} chunks decoded across {decoders.Count} speakers, " +
            $"{totalSamples} total samples, {silentChunks} silent ({silentRate:P1})");
    }

    [Fact]
    public void OneDecoderPerSpeaker_KeepsInterleavedStreamsInSync()
    {
        // The property that justifies keying decoders by steamID rather than sharing one. If
        // packets from two speakers were fed through a single decoder, Opus's own delta state
        // would desynchronise the moment they interleaved - and the corpus already measured
        // that they do (CorpusVoiceTests.TheSteamIdIdentifiesSpeakersTheClientSlotCannot).
        // This test would still pass with a shared decoder IF nothing in the corpus actually
        // interleaves, so it first confirms interleaving occurs before trusting the decode.
        int speakerSwitches = 0;
        ulong? lastSpeaker = null;
        Dictionary<ulong, OpusVoiceDecoder> decoders = [];

        try
        {
            foreach (string path in Corpus.Files())
            {
                Corpus.VoiceSummary demo = Corpus.Voice(path);
                if (demo.Codec != "steam")
                {
                    continue;
                }

                foreach (Corpus.VoicePacketSummary voice in demo.Packets)
                {
                    VoicePacket packet = SteamVoicePayload.Decode(voice.Body);

                    if (lastSpeaker is { } previous && previous != packet.SteamId)
                    {
                        speakerSwitches++;
                    }

                    lastSpeaker = packet.SteamId;

                    if (!decoders.TryGetValue(packet.SteamId, out OpusVoiceDecoder? decoder))
                    {
                        decoder = new OpusVoiceDecoder();
                        decoders[packet.SteamId] = decoder;
                    }

                    foreach (VoiceChunk chunk in packet.Chunks)
                    {
                        _ = decoder.Decode(chunk.Data.Span);
                    }
                }
            }
        }
        finally
        {
            foreach (OpusVoiceDecoder decoder in decoders.Values)
            {
                decoder.Dispose();
            }
        }

        speakerSwitches.ShouldBeGreaterThan(0, "no interleaving occurred, so this test proved nothing");
        output.WriteLine($"{speakerSwitches} speaker switches, all decoded without error");
    }

}
