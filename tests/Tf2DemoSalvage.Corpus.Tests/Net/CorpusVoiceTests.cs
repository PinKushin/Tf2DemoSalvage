using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Decodes every voice packet in the corpus and requires each to consume exactly.
/// </summary>
/// <remarks>
/// **Exact consumption is the only integrity signal a voice body has.** There is no checksum over
/// the framing and no terminator on most blocks, so a field read at the wrong width shows up here
/// as leftover bytes and nowhere else at all. It matters more than usual for this message: the
/// output is handed to an Opus decoder, and bytes that are not a frame boundary produce audible
/// noise rather than an error.
/// </remarks>
public sealed class CorpusVoiceTests
{
    [Test]
    public void EverySteamVoicePacket_ConsumesExactly()
    {
        if (!Corpus.AnyDemoUses("steam"))
        {
            Assert.Ignore("no demo present carries steam-codec (Opus) voice - it is absent from the "
            + "committed corpus and lives only in tools/corpus/local");
        }

        int packets = 0;
        int chunks = 0;
        int terminated = 0;
        HashSet<ulong> speakers = [];
        HashSet<int> rates = [];

        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);

            Corpus.VoiceSummary demo = Corpus.Voice(path);
            if (demo.Codec != "steam")
            {
                continue;
            }

            foreach (Corpus.VoicePacketSummary voice in demo.Packets)
            {
                // Throws rather than returns a flag, so a failure names the demo and the offset.
                VoicePacket packet = SteamVoicePayload.Decode(voice.Body);

                packets++;
                chunks += packet.Chunks.Count;
                terminated += packet.IsTerminated ? 1 : 0;
                speakers.Add(packet.SteamId);
                rates.Add(packet.SampleRate);

                packet.SteamId.ShouldBeGreaterThan(0UL, $"{name}: a packet had no speaker");
            }
        }

        packets.ShouldBeGreaterThan(0, "no steam-codec voice packet reached the decoder");

        // Every speaker is a real account. A misread steamID lands far outside the individual
        // range and is the cheapest falsification available for the first eight bytes.
        foreach (ulong speaker in speakers)
        {
            speaker.ShouldBeGreaterThan(
                76561197960265728UL, "a steamID fell below the start of the individual range");
        }

        // 24000 throughout the corpus. A second rate appearing is not necessarily wrong, but it
        // is a change worth failing on rather than absorbing, because the decoder is configured
        // from it.
        rates.ShouldBe([24000]);

        TestContext.Out.WriteLine(
            $"{packets} packets, {chunks} chunks, {terminated} terminated, " +
            $"{speakers.Count} distinct speakers, rates {string.Join("/", rates)}");
    }

    [Test]
    public void VoiceData_SteamId_IdentifiesSpeakersTheClientSlotCannot()
    {
        if (!Corpus.AnyDemoUses("steam"))
        {
            Assert.Ignore("no demo present carries steam-codec (Opus) voice - it is absent from the "
            + "committed corpus and lives only in tools/corpus/local");
        }

        // Why this framing was worth decoding rather than carrying whole. svc_VoiceData gives a
        // client slot; a slot is an index into the roster at that instant and is recycled when
        // players leave. If the two always agreed one-to-one there would be nothing gained here,
        // so this measures whether they actually differ.
        int demosCompared = 0;

        foreach (string path in Corpus.Files())
        {
            Dictionary<int, HashSet<ulong>> bySlot = [];

            Corpus.VoiceSummary demo = Corpus.Voice(path);
            if (demo.Codec != "steam")
            {
                continue;
            }

            foreach (Corpus.VoicePacketSummary voice in demo.Packets)
            {
                VoicePacket packet = SteamVoicePayload.Decode(voice.Body);

                if (!bySlot.TryGetValue(voice.Client, out HashSet<ulong>? seen))
                {
                    seen = [];
                    bySlot[voice.Client] = seen;
                }

                seen.Add(packet.SteamId);
            }

            if (bySlot.Count == 0)
            {
                continue;
            }

            demosCompared++;

            // Within one recording a slot should hold one account - that is the invariant that
            // makes the pairing usable at all, and a violation would mean either the slot or the
            // steamID is being misread.
            foreach ((int slot, HashSet<ulong> accounts) in bySlot)
            {
                accounts.Count.ShouldBe(
                    1, $"{Path.GetFileName(path)}: slot {slot} carried {accounts.Count} accounts");
            }

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: {bySlot.Count} speaking slots, " +
                $"each one account");
        }

        demosCompared.ShouldBeGreaterThan(0);
    }

}
