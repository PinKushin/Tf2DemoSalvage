using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>One sound event as <c>svc_Sounds</c> described it.</summary>
/// <param name="EntityIndex">Entity the sound is attached to.</param>
/// <param name="SoundNumber">Index into the <c>soundprecache</c> string table.</param>
/// <param name="Flags">SND_* flags.</param>
/// <param name="Channel">Which channel it plays on.</param>
/// <param name="IsAmbient">Whether it is an ambient sound.</param>
/// <param name="IsSentence">Whether it is a sentence rather than a sound file.</param>
/// <param name="SequenceNumber">Distinguishes repeats of the same sound.</param>
/// <param name="Volume">0 to 1.</param>
/// <param name="SoundLevel">Attenuation level.</param>
/// <param name="Pitch">Playback pitch, 100 being normal.</param>
/// <param name="DelaySeconds">Delay before it plays.</param>
/// <param name="OriginX">Where it plays, X.</param>
/// <param name="OriginY">Where it plays, Y.</param>
/// <param name="OriginZ">Where it plays, Z.</param>
/// <param name="SpeakerEntity">Speaker re-broadcasting it, or -1.</param>
public readonly record struct DecodedSound(
    int EntityIndex,
    int SoundNumber,
    int Flags,
    int Channel,
    bool IsAmbient,
    bool IsSentence,
    int SequenceNumber,
    float Volume,
    int SoundLevel,
    int Pitch,
    float DelaySeconds,
    float OriginX,
    float OriginY,
    float OriginZ,
    int SpeakerEntity);

/// <summary>
/// Decodes a <c>svc_Sounds</c> body into the sound events it describes.
/// </summary>
/// <remarks>
/// **The last undeciphered part of the codec, and the one with no second implementation to check
/// against.** demostf/parser does not decode these — there is no <c>sounds.rs</c>, and its
/// <c>ParseSoundsMessage</c> carries the lifetime its raw-body messages use. So the layout here
/// comes from Valve's own <c>public/soundinfo.h</c> in <c>alliedmodders/hl2sdk</c>, branch
/// <c>tf2</c>, where <c>SoundInfo_t::ReadDelta</c> is an inline header function.
///
/// **Every field is delta-coded against the previous sound in the same message**, with a leading
/// bit per field saying whether a new value follows. The first sound deltas against engine
/// defaults. That structure has a consequence worth stating: **a wrong delta base produces wrong
/// values while consuming exactly the right number of bits**, because the flag bits — not the
/// values — determine the width. Exact consumption therefore validates the field widths and not
/// the base, which is why the corpus tests also check that entity indices, sound indices and
/// origins are plausible.
///
/// Four fields are protocol-conditional, and this is the first decoder in the project whose
/// boundaries sit *inside* the corpus's era range rather than at its edges:
///
/// | Field | Above | At or below |
/// |---|---|---|
/// | sound index | 14 bits (proto &gt; 22) | 13 bits |
/// | flags | 11 bits (proto &gt; 18) | 9 bits |
/// | special DSP | present (proto &gt; 21) | absent |
/// </remarks>
public static class SoundDecoder
{
    private const int EntityShortBits = 5;
    private const int MaxEdictBits = 11;
    private const int SoundIndexBitsModern = 14;
    private const int SoundIndexBitsLegacy = 13;
    private const int FlagBitsModern = 11;
    private const int FlagBitsLegacy = 9;
    private const int ChannelBits = 3;
    private const int SequenceNumberBits = 10;
    private const int VolumeBits = 7;
    private const int SoundLevelBits = 9;
    private const int PitchBits = 8;
    private const int SpecialDspBits = 8;
    private const int DelayBits = 13;
    private const int SpeakerEntityBits = MaxEdictBits + 1;

    /// <summary>Origin components are sent scaled down by eight, in two fewer bits than a coord.</summary>
    private const int OriginBits = 14 - 2;
    private const float OriginScale = 8f;

    /// <summary>Protocol boundaries, from <c>soundinfo.h</c>'s own comments.</summary>
    private const int SoundIndexWidthProtocol = 22;
    private const int FlagWidthProtocol = 18;
    private const int SpecialDspProtocol = 21;

    /// <summary><c>SND_STOP</c>: a stop carries none of the fields that describe playback.</summary>
    private const int StopFlag = 1 << 2;

    /// <summary>Bias the engine applies so precision is lost only on large skip-aheads.</summary>
    private const float DelayOffset = 0.100f;

    /// <summary>Engine defaults the first sound in a message deltas against.</summary>
    private static DecodedSound Default => new(
        EntityIndex: 0,
        SoundNumber: 0,
        Flags: 0,
        Channel: 6,
        IsAmbient: false,
        IsSentence: false,
        SequenceNumber: 0,
        Volume: 1f,
        SoundLevel: 75,
        Pitch: 100,
        DelaySeconds: 0f,
        OriginX: 0f,
        OriginY: 0f,
        OriginZ: 0f,
        SpeakerEntity: -1);

    /// <summary>Decodes a sounds body.</summary>
    /// <param name="body">The message's body bytes.</param>
    /// <param name="count">How many sounds the message declares.</param>
    /// <param name="lengthBits">The body's stated length in bits.</param>
    /// <param name="networkProtocol">The demo's protocol, which sizes three fields.</param>
    /// <returns>The sounds, in order.</returns>
    /// <exception cref="InvalidDataException">The decode overran the stated body length.</exception>
    public static IReadOnlyList<DecodedSound> Decode(
        ReadOnlySpan<byte> body, int count, int lengthBits, ushort networkProtocol)
    {
        BitReader reader = new(body);
        List<DecodedSound> sounds = new(count);
        DecodedSound previous = Default;

        for (int i = 0; i < count; i++)
        {
            previous = ReadSound(ref reader, previous, networkProtocol);
            sounds.Add(previous);
        }

        // The message states its own body length, so a correct set of field widths lands on or
        // before it. This catches a wrong width; it cannot catch a wrong delta base, because the
        // flag bits rather than the values decide how much is read.
        if (reader.BitsRead > lengthBits)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"Decoding {count} sounds consumed {reader.BitsRead} bits of a stated {lengthBits}."));
        }

        return sounds;
    }

    private static DecodedSound ReadSound(
        ref BitReader reader, DecodedSound previous, ushort protocol)
    {
        // The entity index is the one field that is not a plain delta: a set bit means a new
        // value follows, and a second bit chooses between a short five-bit form and a full one.
        int entity = previous.EntityIndex;
        if (reader.ReadBit())
        {
            entity = reader.ReadBit()
                ? (int)reader.ReadUInt32(EntityShortBits)
                : (int)reader.ReadUInt32(MaxEdictBits);
        }

        int soundNumber = DeltaUInt(
            ref reader, previous.SoundNumber,
            protocol > SoundIndexWidthProtocol ? SoundIndexBitsModern : SoundIndexBitsLegacy);

        int flags = DeltaUInt(
            ref reader, previous.Flags,
            protocol > FlagWidthProtocol ? FlagBitsModern : FlagBitsLegacy);

        int channel = DeltaUInt(ref reader, previous.Channel, ChannelBits);
        bool ambient = reader.ReadBit();
        bool sentence = reader.ReadBit();

        if (flags == StopFlag)
        {
            // A stop carries nothing else at all. Reading the playback fields anyway would take
            // bits belonging to the next sound.
            return previous with
            {
                EntityIndex = entity,
                SoundNumber = soundNumber,
                Flags = flags,
                Channel = channel,
                IsAmbient = ambient,
                IsSentence = sentence,
            };
        }

        // Three-way rather than two: unchanged, incremented, or stated outright. The middle case
        // is what makes repeated sounds cheap on the wire.
        int sequence = previous.SequenceNumber;
        if (!reader.ReadBit())
        {
            sequence = reader.ReadBit()
                ? previous.SequenceNumber + 1
                : (int)reader.ReadUInt32(SequenceNumberBits);
        }

        float volume = reader.ReadBit()
            ? reader.ReadUInt32(VolumeBits) / 127f
            : previous.Volume;

        int soundLevel = reader.ReadBit()
            ? (int)reader.ReadUInt32(SoundLevelBits)
            : previous.SoundLevel;

        int pitch = DeltaUInt(ref reader, previous.Pitch, PitchBits);

        // Absent below protocol 22, and reading it there would consume eight bits belonging to
        // the delay flag and origin that follow.
        int specialDsp = protocol > SpecialDspProtocol
            ? DeltaUInt(ref reader, 0, SpecialDspBits)
            : 0;
        _ = specialDsp;

        float delay = previous.DelaySeconds;
        if (reader.ReadBit())
        {
            delay = ReadSigned(ref reader, DelayBits) / 1000f;
            if (delay < 0)
            {
                delay *= 10f;
            }

            delay -= DelayOffset;
        }

        float x = DeltaScaled(ref reader, previous.OriginX);
        float y = DeltaScaled(ref reader, previous.OriginY);
        float z = DeltaScaled(ref reader, previous.OriginZ);

        int speaker = reader.ReadBit()
            ? ReadSigned(ref reader, SpeakerEntityBits)
            : previous.SpeakerEntity;

        return new DecodedSound(
            entity, soundNumber, flags, channel, ambient, sentence, sequence,
            volume, soundLevel, pitch, delay, x, y, z, speaker);
    }

    /// <summary>A flag bit, then a value if it is set, otherwise the previous sound's.</summary>
    private static int DeltaUInt(ref BitReader reader, int previous, int bits) =>
        reader.ReadBit() ? (int)reader.ReadUInt32(bits) : previous;

    private static float DeltaScaled(ref BitReader reader, float previous) =>
        reader.ReadBit() ? OriginScale * ReadSigned(ref reader, OriginBits) : previous;

    /// <summary>Reads a two's-complement value of the given width, sign-extended.</summary>
    /// <remarks>
    /// The engine's <c>bf_read::ReadSBitLong</c>. Skipping the extension does not fail — a
    /// negative twelve-bit coordinate comes back as a positive number near 4096, which scaled by
    /// eight puts a sound tens of thousands of units outside the map. Plausible arithmetic,
    /// impossible position, and nothing reports an error. Sound origins and the speaker entity
    /// are the signed fields here; the speaker's -1 for "none" is exactly the value that
    /// disappears without this.
    /// </remarks>
    private static int ReadSigned(ref BitReader reader, int bits)
    {
        uint raw = reader.ReadUInt32(bits);
        int shift = 32 - bits;
        return (int)raw << shift >> shift;
    }
}
