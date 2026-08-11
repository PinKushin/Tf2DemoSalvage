using System;
using System.Globalization;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Writes property values back in the encodings <see cref="SendPropDecoder"/> reads.
/// </summary>
/// <remarks>
/// **The inverse of the decoder, written so entity snapshots can be re-encoded and compared
/// against the demo.** That comparison is the only evidence available that the decoder reads every
/// field rather than merely staying aligned — a property read at the wrong width still leaves the
/// walk in step when the width happens to be right for the next one, and the value it produced is
/// simply wrong.
///
/// **Most of these encodings are recoverable from their values, and the exceptions are stated.**
/// A coordinate's presence bits are determined — an integer part is stored minus one, so a present
/// one is never zero, and a zero fraction is not sent. A range-encoded float quantises to a fixed
/// number of steps, so rounding back recovers the raw. The one genuine choice is the in-bounds bit
/// on the multiplayer coordinate variants, which selects an 11-bit or 14-bit integer field; this
/// picks the narrow form whenever the value fits, and the corpus round trip is what says whether
/// the sender agrees.
///
/// Losses that are inherent rather than incidental: the wire cannot express negative zero, because
/// zero is written as clear presence bits with no sign bit after them.
/// </remarks>
public static class SendPropEncoder
{
    /// <summary>Writes an integer property.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing its width and signedness.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteInt(BitWriter writer, SendProperty property, long value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Flag 32 is overloaded - SPROP_NORMAL on a float, SPROP_VARINT on an integer - and only
        // the property's type disambiguates it, exactly as on the way in.
        if ((property.Flags & SendPropDecoder.VarIntFlag) != 0)
        {
            if ((property.Flags & SendPropDecoder.UnsignedFlag) != 0)
            {
                VarInt.WriteUInt32(writer, (uint)value);
                return;
            }

            VarInt.WriteInt32(writer, (int)value);
            return;
        }

        // Masked to the property's width, which is what makes a negative come back sign-extended:
        // the same bits are transmitted either way and the sign is only how the top one is read.
        writer.Write((uint)value & Mask(property.BitCount), property.BitCount);
    }

    /// <summary>Writes a float property.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing how the value is encoded.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteFloat(BitWriter writer, SendProperty property, float value) =>
        WriteFloat(writer, property, value, null);

    /// <summary>Writes a float property, honouring a recorded encoding choice.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing how the value is encoded.</param>
    /// <param name="value">The value.</param>
    /// <param name="inBounds">
    /// The in-bounds bit the sender used, or <c>null</c> to derive it. Honoured rather than
    /// derived because a sender may use the wide field for a value the narrow one would hold, and
    /// both decode to the same number.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteFloat(
        BitWriter writer, SendProperty property, float value, bool? inBounds)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Same precedence as the decoder: coordinate flags before SPROP_NOSCALE, because the
        // engine tests them in that order and a coordinate is 2 to 20 bits where a noscale float
        // is always 32.
        if ((property.Flags & SendPropDecoder.CoordFlags) != 0)
        {
            WriteCoord(writer, value, property.Flags, inBounds);
            return;
        }

        if ((property.Flags & SendPropDecoder.NoScaleFlag) != 0)
        {
            writer.Write((uint)BitConverter.SingleToInt32Bits(value), 32);
            return;
        }

        if ((property.Flags & SendPropDecoder.NormalFlag) != 0)
        {
            const int steps = (1 << SendPropDecoder.NormalFractionBits) - 1;
            writer.WriteBit(float.IsNegative(value))
                .Write(
                    (uint)MathF.Round(MathF.Abs(value) * steps),
                    SendPropDecoder.NormalFractionBits);
            return;
        }

        // Range encoding. The stored value is a fraction of the way from low to high, so the
        // inverse is the fraction times the number of steps, rounded - truncating would lose the
        // round trip on most values, since the decoded float is rarely exactly on a step.
        float span = property.HighValue - property.LowValue;
        long steps2 = (1L << property.BitCount) - 1;
        uint raw = span == 0
            ? 0
            : (uint)Math.Clamp(
                MathF.Round((value - property.LowValue) / span * steps2), 0, steps2);

        writer.Write(raw, property.BitCount);
    }

    /// <summary>Writes a three-component vector.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <param name="value">The components.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteVector(
        BitWriter writer, SendProperty property, (float X, float Y, float Z) value) =>
        WriteVector(writer, property, value, 0);

    /// <summary>Writes a vector, honouring each component's recorded encoding choice.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <param name="value">The components.</param>
    /// <param name="inBounds">Bit per component, as <c>ReadVector</c> reported them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteVector(
        BitWriter writer, SendProperty property, (float X, float Y, float Z) value, int inBounds)
    {
        ArgumentNullException.ThrowIfNull(writer);

        WriteFloat(writer, property, value.X, (inBounds & 1) != 0);
        WriteFloat(writer, property, value.Y, (inBounds & 2) != 0);

        if ((property.Flags & SendPropDecoder.NormalFlag) == 0)
        {
            WriteFloat(writer, property, value.Z, (inBounds & 4) != 0);
            return;
        }

        // A normal is unit length, so Z is derived from X and Y on the way back in and only its
        // sign is on the wire. Writing it as a float would add bits the decoder does not read.
        writer.WriteBit(float.IsNegative(value.Z));
    }

    /// <summary>Writes a two-component vector.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <param name="value">The components.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteVectorXY(
        BitWriter writer, SendProperty property, (float X, float Y) value) =>
        WriteVectorXY(writer, property, value, 0);

    /// <summary>Writes a two-component vector, honouring the recorded encoding choices.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <param name="value">The components.</param>
    /// <param name="inBounds">Bit per component, as <c>ReadVectorXY</c> reported them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteVectorXY(
        BitWriter writer, SendProperty property, (float X, float Y) value, int inBounds)
    {
        ArgumentNullException.ThrowIfNull(writer);

        WriteFloat(writer, property, value.X, (inBounds & 1) != 0);
        WriteFloat(writer, property, value.Y, (inBounds & 2) != 0);
    }

    /// <summary>Writes a length-prefixed string.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The string.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/> or <paramref name="value"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// Length-prefixed at nine bits, unlike the NUL-terminated strings the message layer uses.
    /// The length counts BYTES, so a UTF-8 name is measured after encoding rather than in
    /// characters — measuring the string's length would truncate every non-ASCII name.
    /// </remarks>
    public static void WriteString(BitWriter writer, string value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write((uint)bytes.Length, SendPropDecoder.StringLengthBits).WriteBytes(bytes);
    }

    /// <summary>Writes a coordinate: presence flags, then an integer part, then a fraction.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The coordinate.</param>
    /// <param name="flags">The property's flags, which select the variant.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <remarks>
    /// The four variants are not independent modifiers; the engine takes the first that matches
    /// and so does this. The integral variant is the odd one: no fraction at all, and no sign bit
    /// when there is no integer part either, where the others always write one.
    /// </remarks>
    public static void WriteCoord(BitWriter writer, float value, int flags) =>
        WriteCoord(writer, value, flags, null);

    /// <summary>Writes a coordinate, honouring a recorded in-bounds choice.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The coordinate.</param>
    /// <param name="flags">The property's flags, which select the variant.</param>
    /// <param name="recordedInBounds">The bit the sender used, or <c>null</c> to derive it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    public static void WriteCoord(
        BitWriter writer, float value, int flags, bool? recordedInBounds)
    {
        ArgumentNullException.ThrowIfNull(writer);

        bool plainCoord = (flags & SendPropDecoder.CoordFlag) != 0;
        bool multiplayer = !plainCoord && (flags & SendPropDecoder.CoordMpFlag) != 0;
        bool lowPrecision = !plainCoord && !multiplayer &&
                            (flags & SendPropDecoder.CoordMpLowPrecisionFlag) != 0;
        bool integral = !plainCoord && !multiplayer && !lowPrecision;

        int fractionBits = lowPrecision
            ? SendPropDecoder.CoordFractionBitsLowPrecision
            : SendPropDecoder.CoordFractionBits;
        int fractionSteps = 1 << fractionBits;

        float magnitude = MathF.Abs(value);
        int integer = (int)magnitude;
        int fraction = integral
            ? 0
            : (int)MathF.Round((magnitude - integer) * fractionSteps);

        // A fraction that rounds up to a whole step is a carry. Left alone it would be written
        // into the fraction field as zero and read back a whole unit short.
        if (fraction == fractionSteps)
        {
            integer++;
            fraction = 0;
        }

        if (integral)
        {
            integer = (int)MathF.Round(magnitude);
        }

        bool hasInteger = integer != 0;
        bool hasFraction = fraction != 0;

        if (!plainCoord)
        {
            // Valve's own predicate is `intval < (1 << COORD_INTEGER_BITS_MP)`, and it is derived
            // from the value the SENDER had rather than the one that came back - so it is honoured
            // when recorded and derived only when there is nothing to honour.
            writer.WriteBit(
                recordedInBounds
                    ?? integer < 1 << SendPropDecoder.CoordIntegerBitsInBounds);
        }

        writer.WriteBit(hasInteger);

        if (plainCoord)
        {
            writer.WriteBit(hasFraction);
            if (!hasInteger && !hasFraction)
            {
                // Zero is two clear bits and no sign. A sign bit nobody reads would shift
                // everything after it.
                return;
            }
        }
        else if (integral && !hasInteger)
        {
            return;
        }

        writer.WriteBit(float.IsNegative(value));

        if (hasInteger)
        {
            bool narrow = !plainCoord &&
                (recordedInBounds ?? integer < 1 << SendPropDecoder.CoordIntegerBitsInBounds);

            int width = narrow
                ? SendPropDecoder.CoordIntegerBitsInBounds
                : SendPropDecoder.CoordIntegerBits;

            if (integer > 1 << width)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A coordinate's integer part is {width} bits, so it stops at " +
                        $"{1 << width}."));
            }

            // Minus one, matching the decoder's plus one: a present integer part is never zero,
            // so the encoding spends that value on one it could not otherwise reach.
            writer.Write((uint)(integer - 1), width);
        }

        // On the non-plain paths a fraction is always present unless the variant is integral -
        // its absence is implied by the flags rather than signalled, so a zero fraction is still
        // written.
        if (plainCoord ? hasFraction : !integral)
        {
            writer.Write((uint)fraction, fractionBits);
        }
    }

    /// <summary>Low <paramref name="bits"/> bits set.</summary>
    private static uint Mask(int bits) => bits >= 32 ? uint.MaxValue : (1u << bits) - 1;
}
