using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The sound decode claims that only real engine bytes can settle.
/// </summary>
/// <remarks>
/// **Trimmed rather than deleted, and the line where it was trimmed is the rule this project now
/// applies to the whole corpus suite.** The layout, the field widths, the protocol boundaries and
/// the delta base all moved to <c>SoundCodecTests</c>, where sounds are written rather than found:
/// a synthetic sound can be encoded at protocol 18 or 21, which no recording in existence can do,
/// and its expected values are known by construction instead of bounded by a plausibility range.
///
/// What could not move is here. A synthetic fixture cannot corroborate a decode against anything,
/// because both sides of the comparison would be written by the same hand — the test would be
/// checking this project against its own beliefs. The corroboration below comes from two decoders
/// that share no code reaching the same conclusion about a real file, and that is evidence of a
/// different kind rather than more of the same.
///
/// The other half of the real-bytes evidence for sounds is not here either: it is in
/// <c>CorpusAssemblyRoundTripTests</c>, which decompiles every demo to text and compiles it back
/// byte for byte. <c>MessageAssembly</c> expands sound bodies into fields and re-encodes them, so
/// that test already puts every sound in the corpus through both codecs. <c>CorpusSoundRoundTripTests</c>
/// did the same thing over a subset and was deleted as a duplicate.
/// </remarks>
public sealed class CorpusSoundTests
{
    [Test]
    public void SoundNumbers_AddressTheSoundPrecacheTable()
    {
        // **The sharpest check available on this decoder, and the closest thing it has to a
        // differential.** demostf/parser does not decode sound bodies at all, so there is no
        // second implementation to compare against — the layout rests on Valve's soundinfo.h
        // alone.
        //
        // This substitutes for one. Sound indices come out of a delta-coded bit stream; the
        // precache table comes out of svc_CreateStringTable. The two paths share no code and no
        // assumptions, so an index landing inside that table is a fact about the file rather than
        // about the parser. Across thousands of sounds there is no way to land inside it by
        // accident.
        //
        // This is exactly what a synthetic fixture cannot do. Writing the sound AND the table
        // would make both sides say whatever this project already believes.
        int demos = 0;

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            int precacheSize = PrecacheSize(path, protocol);
            if (precacheSize == 0)
            {
                continue;
            }

            string name = Path.GetFileName(path);
            int examined = 0;

            foreach (DecodedSound sound in Sounds(path, protocol).Take(2000))
            {
                sound.SoundNumber.ShouldBeLessThan(
                    precacheSize, $"{name}: sound index outside its {precacheSize}-entry table");
                examined++;
            }

            if (examined == 0)
            {
                continue;
            }

            demos++;
            TestContext.Out.WriteLine(
                $"{name}: {examined} sounds inside a {precacheSize}-entry table");
        }

        // Asserted, not assumed. A loop over an empty corpus passes identically to one that ran
        // and was satisfied — RISKS B20 is that mistake, where a helper stopped yielding anything
        // and every test built on it kept passing.
        demos.ShouldBeGreaterThan(0, "no demo yielded both a precache table and a sound");
    }

    [Test]
    public void Decode_EveryRealSoundBody_StaysWithinItsStatedLength()
    {
        // The claim that decoding must be TOTAL, which is a property of the corpus rather than of
        // the codec: the engine wrote these bytes and the engine reads them back, so anything
        // this cannot read is our defect. A synthetic body proves the decoder handles the shapes
        // this project thought to write; only real files can show a shape nobody thought of.
        //
        // Kept narrow deliberately — the values are no longer checked here, because
        // SoundCodecTests checks them against known answers instead of against a range.
        int demos = 0;

        foreach (string path in Corpus.Files())
        {
            ushort protocol = Corpus.ProtocolOf(path);
            int bodies = 0;
            int sounds = 0;
            List<string> failures = [];

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
                    failures.Add(error.Message);
                }
            }

            if (bodies == 0)
            {
                continue;
            }

            demos++;
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)} (protocol {protocol}): {bodies} messages, " +
                $"{sounds} sounds");

            // The message itself, not just the count. A failure-only log that says "3 failed"
            // costs a re-run to find out what failed.
            failures.ShouldBeEmpty(Path.GetFileName(path));
            sounds.ShouldBeGreaterThan(0, Path.GetFileName(path));
        }

        demos.ShouldBeGreaterThan(0, "no demo carried svc_Sounds");
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
                // Reported as a failure by the test above rather than silently here, so a decoder
                // that started failing every body cannot make this one report a clean run.
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
