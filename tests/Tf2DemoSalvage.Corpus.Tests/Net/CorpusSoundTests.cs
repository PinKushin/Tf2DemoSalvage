using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Decodes <c>svc_Sounds</c> bodies out of real demos, across every protocol in the corpus.
/// </summary>
/// <remarks>
/// **No second implementation exists to check this against.** demostf/parser does not decode
/// sounds, so unlike every other message in this project there is no differential available and
/// the layout rests on Valve's <c>soundinfo.h</c> alone. That makes the corpus the only evidence,
/// and it makes the *kind* of evidence matter.
///
/// Exact bit consumption validates the field WIDTHS. It cannot validate the delta base, because
/// every field is preceded by a flag bit and it is the flags — not the values — that decide how
/// much is read: deltas against the wrong sound consume identical bits and produce wrong values.
///
/// So the values are checked for plausibility as well, and the checks are chosen to be ones a
/// misread cannot pass by luck: entity indices inside MAX_EDICTS, sound indices inside the
/// precache table's own size, origins inside the world, volume in 0..1. A decoder reading the
/// wrong bits produces coordinates in the tens of thousands and entity indices in the thousands,
/// which is the characteristic failure of this format rather than a crash.
/// </remarks>
public sealed class CorpusSoundTests
{
    private const int MaxEdicts = 2048;
    private const int MaxSounds = 1 << 14;
    private const float WorldHalfExtent = 16384f;

    [Test]
    public void Sounds_DecodeWithoutOverrunningTheirStatedLength()
    {
        int demos = 0;

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            int bodies = 0;
            int sounds = 0;
            int failed = 0;

            foreach (SoundsMessage message in Messages(path))
            {
                bodies++;
                try
                {
                    sounds += SoundDecoder.Decode(
                        message.Body.Span, message.Count, message.BodyBits, protocol).Count;
                }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
                {
                    failed++;
                }
            }

            if (bodies == 0)
            {
                continue;
            }

            demos++;
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)} (protocol {protocol}): {bodies} messages, " +
                $"{sounds} sounds, {failed} failed");

            failed.ShouldBe(0, Path.GetFileName(path));
            sounds.ShouldBeGreaterThan(0, Path.GetFileName(path));
        }

        demos.ShouldBeGreaterThan(0, "no demo carried svc_Sounds");
    }

    [Test]
    public void EverySoundIsPlausible()
    {
        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            string name = Path.GetFileName(path);

            foreach (DecodedSound sound in Sounds(path, protocol).Take(3000))
            {
                // A wrong bit offset shows up here long before it shows up as an exception.
                sound.EntityIndex.ShouldBeInRange(0, MaxEdicts - 1, name);
                sound.SoundNumber.ShouldBeInRange(0, MaxSounds - 1, name);
                sound.Volume.ShouldBeInRange(0f, 1f, name);
                sound.Channel.ShouldBeInRange(0, 7, name);

                MathF.Abs(sound.OriginX).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
                MathF.Abs(sound.OriginY).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
                MathF.Abs(sound.OriginZ).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
            }
        }
    }

    [Test]
    public void SoundNumbers_AddressTheSoundPrecacheTable()
    {
        // The sharpest available check, and the one closest to a differential: sound indices come
        // from the bit stream and the precache table comes from svc_CreateStringTable, by
        // completely independent paths. An index past the end of that table means the bits were
        // read wrong - there is no way to land inside it by accident across thousands of sounds.
        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            int precacheSize = PrecacheSize(path, protocol);
            if (precacheSize == 0)
            {
                continue;
            }

            string name = Path.GetFileName(path);
            int checked_ = 0;

            foreach (DecodedSound sound in Sounds(path, protocol).Take(2000))
            {
                sound.SoundNumber.ShouldBeLessThan(
                    precacheSize, $"{name}: sound index outside its {precacheSize}-entry table");
                checked_++;
            }

            if (checked_ > 0)
            {
                TestContext.Out.WriteLine($"{name}: {checked_} sounds inside a {precacheSize}-entry table");
            }
        }
    }

    private static int PrecacheSize(string path, ushort protocol)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = protocol };

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(400))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is CreateStringTableMessage { Name: "soundprecache" } table)
                {
                    return table.MaxEntries;
                }
            }
        }

        return 0;
    }

    private static IEnumerable<DecodedSound> Sounds(string path, ushort protocol)
    {
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

            foreach (DecodedSound sound in decoded)
            {
                yield return sound;
            }
        }
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
                if (message is SoundsMessage sounds)
                {
                    yield return sounds;
                }
            }
        }
    }
}
