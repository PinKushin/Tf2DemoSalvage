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
/// <param name="SpecialDsp">DSP preset, above protocol 21.</param>
/// <param name="Sent">
/// Which fields this sound actually transmitted, and which encoding forms it chose.
/// </param>
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
    int SpeakerEntity,
    int SpecialDsp = 0,
    SoundFields Sent = SoundFields.None);

/// <summary>
/// Which fields a sound put on the wire, and which of the wire's forms it used.
/// </summary>
/// <remarks>
/// **Not decoration: without this a sound cannot be re-encoded to the bits it came from.** Every
/// field is optional and inherits the previous sound's value when absent, so a decoder that keeps
/// only the values cannot tell "sent, and happened to be identical" from "not sent". Those produce
/// different bit streams.
///
/// It is not a theoretical distinction. Re-encoding the corpus by the obvious rule — write the
/// field when it differs from the previous sound — came out 12 bits short per occurrence on
/// hundreds of bodies, always a multiple of an origin's 12-bit width. The engine compares
/// positions at full precision and this decoder sees them after quantisation, so two sounds from
/// one moving entity land in the same grid cell and the field looks redundant when the sender did
/// not think so.
///
/// The three form flags are the same problem in a different dress: an entity index below 32 fits
/// the narrow form but nothing forces the sender to use it, and a sequence number one higher than
/// the last can be sent either as the increment form or in full.
/// </remarks>
[System.Flags]
public enum SoundFields
{
    /// <summary>Nothing transmitted; every value inherited.</summary>
    None = 0,

    /// <summary>An entity index followed.</summary>
    Entity = 1 << 0,

    /// <summary>That index used the five-bit form rather than the full one.</summary>
    EntityNarrow = 1 << 1,

    /// <summary>A sound index followed.</summary>
    SoundNumber = 1 << 2,

    /// <summary>A flags value followed.</summary>
    Flags = 1 << 3,

    /// <summary>A channel followed.</summary>
    Channel = 1 << 4,

    /// <summary>The sequence number was sent as "one higher than the last".</summary>
    SequenceIncrement = 1 << 5,

    /// <summary>The sequence number was sent in full.</summary>
    SequenceExplicit = 1 << 6,

    /// <summary>A volume followed.</summary>
    Volume = 1 << 7,

    /// <summary>A sound level followed.</summary>
    SoundLevel = 1 << 8,

    /// <summary>A pitch followed.</summary>
    Pitch = 1 << 9,

    /// <summary>A DSP preset followed.</summary>
    SpecialDsp = 1 << 10,

    /// <summary>A delay followed.</summary>
    Delay = 1 << 11,

    /// <summary>An X coordinate followed.</summary>
    OriginX = 1 << 12,

    /// <summary>A Y coordinate followed.</summary>
    OriginY = 1 << 13,

    /// <summary>A Z coordinate followed.</summary>
    OriginZ = 1 << 14,

    /// <summary>A speaker entity followed.</summary>
    Speaker = 1 << 15,
}

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
    // Internal rather than private because SoundEncoder writes the same fields back, and a
    // second copy of a width is a second chance for the two halves to disagree - which is
    // precisely what the round trip exists to detect, so it must not be able to hide here.
    internal const int EntityShortBits = 5;
    internal const int MaxEdictBits = 11;
    private const int SoundIndexBitsModern = 14;
    private const int SoundIndexBitsLegacy = 13;
    private const int FlagBitsModern = 11;
    private const int FlagBitsLegacy = 9;
    internal const int ChannelBits = 3;
    internal const int SequenceNumberBits = 10;
    internal const int VolumeBits = 7;
    internal const int SoundLevelBits = 9;
    internal const int PitchBits = 8;
    internal const int SpecialDspBits = 8;
    internal const int DelayBits = 13;
    internal const int SpeakerEntityBits = MaxEdictBits + 1;

    /// <summary>Volume is sent as a seventh of a 127-step scale.</summary>
    internal const float VolumeScale = 127f;

    /// <summary>Origin components are sent scaled down by eight, in two fewer bits than a coord.</summary>
    internal const int OriginBits = 14 - 2;
    internal const float OriginScale = 8f;

    /// <summary>Protocol boundaries, from <c>soundinfo.h</c>'s own comments.</summary>
    private const int SoundIndexWidthProtocol = 22;
    private const int FlagWidthProtocol = 18;
    internal const int SpecialDspProtocol = 21;

    /// <summary>Width of a sound index at this protocol.</summary>
    internal static int SoundNumberBits(int protocol) =>
        protocol > SoundIndexWidthProtocol ? SoundIndexBitsModern : SoundIndexBitsLegacy;

    /// <summary>Width of the flags field at this protocol.</summary>
    internal static int FlagsBits(int protocol) =>
        protocol > FlagWidthProtocol ? FlagBitsModern : FlagBitsLegacy;

    /// <summary><c>SND_STOP</c>: a stop carries none of the fields that describe playback.</summary>
    internal const int StopFlag = 1 << 2;

    /// <summary>Bias the engine applies so precision is lost only on large skip-aheads.</summary>
    internal const float DelayOffset = 0.100f;

    /// <summary>Engine defaults the first sound in a message deltas against.</summary>
    internal static DecodedSound Default => new(
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
        SoundFields sent = SoundFields.None;

        int entity = previous.EntityIndex;
        if (reader.ReadBit())
        {
            sent |= SoundFields.Entity;
            bool narrow = reader.ReadBit();
            sent |= narrow ? SoundFields.EntityNarrow : SoundFields.None;
            entity = (int)reader.ReadUInt32(narrow ? EntityShortBits : MaxEdictBits);
        }

        int soundNumber = DeltaUInt(
            ref reader, previous.SoundNumber, SoundNumberBits(protocol),
            ref sent, SoundFields.SoundNumber);

        int flags = DeltaUInt(
            ref reader, previous.Flags, FlagsBits(protocol), ref sent, SoundFields.Flags);

        int channel = DeltaUInt(
            ref reader, previous.Channel, ChannelBits, ref sent, SoundFields.Channel);
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
                Sent = sent,
            };
        }

        // Three-way rather than two: unchanged, incremented, or stated outright. The middle case
        // is what makes repeated sounds cheap on the wire.
        int sequence = previous.SequenceNumber;
        if (!reader.ReadBit())
        {
            if (reader.ReadBit())
            {
                sent |= SoundFields.SequenceIncrement;
                sequence = previous.SequenceNumber + 1;
            }
            else
            {
                sent |= SoundFields.SequenceExplicit;
                sequence = (int)reader.ReadUInt32(SequenceNumberBits);
            }
        }

        float volume = previous.Volume;
        if (reader.ReadBit())
        {
            sent |= SoundFields.Volume;
            volume = reader.ReadUInt32(VolumeBits) / VolumeScale;
        }

        int soundLevel = previous.SoundLevel;
        if (reader.ReadBit())
        {
            sent |= SoundFields.SoundLevel;
            soundLevel = (int)reader.ReadUInt32(SoundLevelBits);
        }

        int pitch = DeltaUInt(ref reader, previous.Pitch, PitchBits, ref sent, SoundFields.Pitch);

        // Absent below protocol 22, and reading it there would consume eight bits belonging to
        // the delay flag and origin that follow.
        int specialDsp = protocol > SpecialDspProtocol
            ? DeltaUInt(ref reader, 0, SpecialDspBits, ref sent, SoundFields.SpecialDsp)
            : 0;

        float delay = previous.DelaySeconds;
        if (reader.ReadBit())
        {
            sent |= SoundFields.Delay;
            delay = ReadSigned(ref reader, DelayBits) / 1000f;
            if (delay < 0)
            {
                delay *= 10f;
            }

            delay -= DelayOffset;
        }

        float x = DeltaScaled(ref reader, previous.OriginX, ref sent, SoundFields.OriginX);
        float y = DeltaScaled(ref reader, previous.OriginY, ref sent, SoundFields.OriginY);
        float z = DeltaScaled(ref reader, previous.OriginZ, ref sent, SoundFields.OriginZ);

        int speaker = previous.SpeakerEntity;
        if (reader.ReadBit())
        {
            sent |= SoundFields.Speaker;
            speaker = ReadSigned(ref reader, SpeakerEntityBits);
        }

        return new DecodedSound(
            entity, soundNumber, flags, channel, ambient, sentence, sequence,
            volume, soundLevel, pitch, delay, x, y, z, speaker, specialDsp, sent);
    }

    /// <summary>A flag bit, then a value if it is set, otherwise the previous sound's.</summary>
    private static int DeltaUInt(
        ref BitReader reader, int previous, int bits, ref SoundFields sent, SoundFields field)
    {
        if (!reader.ReadBit())
        {
            return previous;
        }

        sent |= field;
        return (int)reader.ReadUInt32(bits);
    }

    private static float DeltaScaled(
        ref BitReader reader, float previous, ref SoundFields sent, SoundFields field)
    {
        if (!reader.ReadBit())
        {
            return previous;
        }

        sent |= field;
        return OriginScale * ReadSigned(ref reader, OriginBits);
    }

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
