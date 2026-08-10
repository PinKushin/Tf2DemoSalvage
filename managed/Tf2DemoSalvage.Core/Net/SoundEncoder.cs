using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Re-encodes decoded sounds back to a <c>svc_Sounds</c> body.
/// </summary>
/// <remarks>
/// **The only independent check available for <see cref="SoundDecoder"/>.** Every other decoder in
/// this project can be cross-checked against demostf/parser; that one cannot, because demostf
/// leaves sound bodies opaque. Its layout rests on Valve's <c>public/soundinfo.h</c> and on the
/// values looking plausible, and plausible is exactly what a wrong delta base produces — the flag
/// bits decide how much is read, so a wrong base consumes the right number of bits and yields
/// wrong numbers.
///
/// A round trip closes that. If the decoder read a field at the wrong offset or width, the values
/// it produced cannot be written back into the same bits.
///
/// **Which fields to send is read off <see cref="SoundFields"/> rather than inferred.** The first
/// attempt inferred it — write a field when it differs from the previous sound — and that is not
/// recoverable from the values. It came out exactly 12 bits short per occurrence on hundreds of
/// corpus bodies, always a multiple of an origin's width: the engine compares positions at full
/// precision, this sees them after quantisation to an 8-unit grid, and two sounds from one moving
/// entity therefore look identical here while the sender thought otherwise.
///
/// That is a real finding about the format rather than an encoder bug, and the fix belongs on the
/// decode side: a demo cannot be rebuilt from values alone, so the decoder now records which
/// fields were present and which of the wire's forms each one used.
/// </remarks>
public static class SoundEncoder
{
    /// <summary>Encodes a sequence of sounds as a message body.</summary>
    /// <param name="sounds">The sounds, in order.</param>
    /// <param name="networkProtocol">The demo's protocol, which sizes three fields.</param>
    /// <returns>The body, and how many bits of it are meaningful.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sounds"/> is <c>null</c>.</exception>
    public static (byte[] Body, int BitCount) Encode(
        IReadOnlyList<DecodedSound> sounds, ushort networkProtocol)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        BitWriter writer = new();
        foreach (DecodedSound sound in sounds)
        {
            WriteSound(writer, sound, networkProtocol);
        }

        return (writer.Build(), writer.BitCount);
    }

    private static void WriteSound(BitWriter writer, DecodedSound sound, ushort protocol)
    {
        SoundFields sent = sound.Sent;

        // The entity index is the one field that is not a plain delta: a bit saying a value
        // follows, then a bit choosing the five-bit form over the full one.
        if (Has(sent, SoundFields.Entity))
        {
            bool narrow = Has(sent, SoundFields.EntityNarrow);
            writer.WriteBit(true).WriteBit(narrow).Write(
                (uint)sound.EntityIndex,
                narrow ? SoundDecoder.EntityShortBits : SoundDecoder.MaxEdictBits);
        }
        else
        {
            writer.WriteBit(false);
        }

        WriteDelta(
            writer, sent, SoundFields.SoundNumber, sound.SoundNumber,
            SoundDecoder.SoundNumberBits(protocol));
        WriteDelta(
            writer, sent, SoundFields.Flags, sound.Flags, SoundDecoder.FlagsBits(protocol));
        WriteDelta(writer, sent, SoundFields.Channel, sound.Channel, SoundDecoder.ChannelBits);

        writer.WriteBit(sound.IsAmbient);
        writer.WriteBit(sound.IsSentence);

        if (sound.Flags == SoundDecoder.StopFlag)
        {
            // A stop carries nothing else at all.
            return;
        }

        // Three-way, and the sense is inverted from every other flag here: a SET bit means the
        // sequence number did not change.
        if (Has(sent, SoundFields.SequenceIncrement))
        {
            writer.WriteBit(false).WriteBit(true);
        }
        else if (Has(sent, SoundFields.SequenceExplicit))
        {
            writer.WriteBit(false).WriteBit(false)
                .Write((uint)sound.SequenceNumber, SoundDecoder.SequenceNumberBits);
        }
        else
        {
            writer.WriteBit(true);
        }

        // The decoder divides by 127, so the inverse multiplies and rounds. Truncating loses the
        // round trip on most values: 0.24 came from 30/127, and 0.24 x 127 is 30 minus an epsilon.
        WriteOptional(
            writer, sent, SoundFields.Volume,
            () => writer.Write(
                (uint)MathF.Round(sound.Volume * SoundDecoder.VolumeScale),
                SoundDecoder.VolumeBits));

        WriteDelta(
            writer, sent, SoundFields.SoundLevel, sound.SoundLevel, SoundDecoder.SoundLevelBits);
        WriteDelta(writer, sent, SoundFields.Pitch, sound.Pitch, SoundDecoder.PitchBits);

        // Absent below protocol 22, where the flag bit itself is not on the wire either.
        if (protocol > SoundDecoder.SpecialDspProtocol)
        {
            WriteDelta(
                writer, sent, SoundFields.SpecialDsp, sound.SpecialDsp,
                SoundDecoder.SpecialDspBits);
        }

        WriteOptional(
            writer, sent, SoundFields.Delay, () => WriteDelay(writer, sound.DelaySeconds));

        WriteOrigin(writer, sent, SoundFields.OriginX, sound.OriginX);
        WriteOrigin(writer, sent, SoundFields.OriginY, sound.OriginY);
        WriteOrigin(writer, sent, SoundFields.OriginZ, sound.OriginZ);

        WriteOptional(
            writer, sent, SoundFields.Speaker,
            () => writer.Write(
                (uint)sound.SpeakerEntity & Mask(SoundDecoder.SpeakerEntityBits),
                SoundDecoder.SpeakerEntityBits));
    }

    /// <summary>A flag bit, and the value when the sound transmitted one.</summary>
    private static void WriteDelta(
        BitWriter writer, SoundFields sent, SoundFields field, int value, int bits) =>
        WriteOptional(writer, sent, field, () => writer.Write((uint)value, bits));

    private static void WriteOptional(
        BitWriter writer, SoundFields sent, SoundFields field, Action write)
    {
        writer.WriteBit(Has(sent, field));
        if (Has(sent, field))
        {
            write();
        }
    }

    private static void WriteDelay(BitWriter writer, float delay)
    {
        // Undoing the decoder in reverse order: add the bias back, then undo the ten-fold
        // expansion the negative branch applied, then scale to milliseconds.
        float biased = delay + SoundDecoder.DelayOffset;
        if (biased < 0)
        {
            biased /= 10f;
        }

        writer.Write(
            (uint)(int)MathF.Round(biased * 1000f) & Mask(SoundDecoder.DelayBits),
            SoundDecoder.DelayBits);
    }

    private static void WriteOrigin(
        BitWriter writer, SoundFields sent, SoundFields field, float value) =>
        WriteOptional(
            writer, sent, field,
            () => writer.Write(
                (uint)(int)MathF.Round(value / SoundDecoder.OriginScale)
                    & Mask(SoundDecoder.OriginBits),
                SoundDecoder.OriginBits));

    private static bool Has(SoundFields sent, SoundFields field) => (sent & field) != 0;

    /// <summary>Low <paramref name="bits"/> bits set, for writing a negative in two's complement.</summary>
    private static uint Mask(int bits) => bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
}
