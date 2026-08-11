using System;
using System.Text;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Decodes a single networked property value, given the definition that describes it.
/// </summary>
/// <remarks>
/// Deliberately separated from entity iteration. These decoders can be verified in isolation by
/// round trip — encode an arbitrary value, decode it, require equality — whereas iteration
/// cannot, because a wrong property-index encoding produces plausible values rather than an
/// error. Building the half that can be checked first keeps the unverifiable half honest.
///
/// The coordinate encodings are **not implemented and throw rather than guess**. TF2 is a
/// multiplayer game, so player positions almost certainly use <c>SPROP_COORD_MP</c> — a flag
/// VDC does not document at all and which was only found by reading the SDK. Returning a
/// plausible number for one of these would be the worst available outcome: silently wrong
/// positions, in the one field a viewer exists to draw.
/// </remarks>
public static class SendPropDecoder
{
    internal const int UnsignedFlag = 1 << 0;
    internal const int CoordFlag = 1 << 1;
    internal const int NoScaleFlag = 1 << 2;
    internal const int NormalFlag = 1 << 5;

    /// <summary>
    /// The same bit as <see cref="NormalFlag"/>, which on an integer property means the value
    /// is varint-encoded rather than fixed width.
    /// </summary>
    internal const int VarIntFlag = 1 << 5;
    internal const int CoordMpFlag = 1 << 13;
    internal const int CoordMpLowPrecisionFlag = 1 << 14;
    internal const int CoordMpIntegralFlag = 1 << 15;

    /// <summary>Every coordinate encoding, in the order the engine tests them.</summary>
    internal const int CoordFlags =
        CoordFlag | CoordMpFlag | CoordMpLowPrecisionFlag | CoordMpIntegralFlag;

    /// <summary>Integer bits when the value is inside the world bounds: <c>COORD_INTEGER_BITS_MP</c>.</summary>
    internal const int CoordIntegerBitsInBounds = 11;

    /// <summary>Integer bits otherwise: <c>COORD_INTEGER_BITS</c>.</summary>
    internal const int CoordIntegerBits = 14;

    /// <summary>Fraction bits at normal precision: <c>COORD_FRACTIONAL_BITS</c>.</summary>
    internal const int CoordFractionBits = 5;

    /// <summary>Fraction bits at low precision: <c>COORD_FRACTIONAL_BITS_MP_LOWPRECISION</c>.</summary>
    internal const int CoordFractionBitsLowPrecision = 3;

    /// <summary>Bits a normal uses for magnitude, plus one for sign.</summary>
    internal const int NormalFractionBits = 11;

    /// <summary>Width of a networked string's length prefix: <c>DT_MAX_STRING_BITS</c>.</summary>
    internal const int StringLengthBits = 9;

    /// <summary>Reads an integer property.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing its width and signedness.</param>
    /// <returns>The decoded value.</returns>
    public static long ReadInt(ref BitReader reader, SendProperty property)
    {
        // Flag 32 is overloaded: SPROP_NORMAL on a float, SPROP_VARINT on an integer. Same bit,
        // entirely different encoding, and nothing in the schema disambiguates it but the
        // property's own type. Reading a varint as a fixed-width field consumes the wrong
        // number of bits and desynchronises every property after it in the entity.
        if ((property.Flags & VarIntFlag) != 0)
        {
            return (property.Flags & UnsignedFlag) != 0
                ? VarInt.ReadUInt32(ref reader)
                : VarInt.ReadInt32(ref reader);
        }

        uint raw = reader.ReadUInt32(property.BitCount);

        if ((property.Flags & UnsignedFlag) != 0)
        {
            // Widened deliberately. A 32-bit unsigned property does not fit in an int, and
            // reading one into an int reports 0xFFFFFFFF as -1 - the right bits, the wrong
            // number, which is this format's characteristic failure. The reference parser uses
            // a 64-bit integer for the same reason.
            return raw;
        }

        // Sign-extend from the property's width. Without this a negative value read at 11 bits
        // comes back as 2047 - not a crash, just a plausible number.
        //
        // Sign costs no storage here: the same bits are transmitted either way and the sign is
        // only how the top bit is read. What it costs is range, which is why SPROP_UNSIGNED is
        // a per-property flag - an 11-bit signed value spans -1024..1023 rather than 0..2047.
        // Range loss becomes a storage cost the moment the range is needed: holding 65,535
        // signed means moving to 32 bits, because 16-bit signed stops at 32,767.
        int shift = 32 - property.BitCount;
        return (int)raw << shift >> shift;   // int shift keeps sign extension at the wire width
    }

    /// <summary>Reads a float property.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing how the value is encoded.</param>
    /// <returns>The decoded value.</returns>
    public static float ReadFloat(ref BitReader reader, SendProperty property)
    {
        // Coordinate flags are tested before SPROP_NOSCALE because the engine tests them in
        // that order. Reversing it is not a wrong value but a desynchronisation: a coordinate
        // is 2 to 20 bits, a noscale float is always 32.
        if ((property.Flags & CoordFlags) != 0)
        {
            return ReadCoord(ref reader, property.Flags);
        }

        if ((property.Flags & NoScaleFlag) != 0)
        {
            return BitConverter.Int32BitsToSingle((int)reader.ReadUInt32(32));
        }

        if ((property.Flags & NormalFlag) != 0)
        {
            bool negative = reader.ReadBit();
            float magnitude = reader.ReadUInt32(NormalFractionBits) /
                              (float)((1 << NormalFractionBits) - 1);
            return negative ? -magnitude : magnitude;
        }

        // Range encoding: the stored value is a fraction of the way from low to high.
        uint raw = reader.ReadUInt32(property.BitCount);
        float fraction = raw / (float)((1L << property.BitCount) - 1);
        return property.LowValue + ((property.HighValue - property.LowValue) * fraction);
    }

    /// <summary>Reads a three-component vector.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <returns>The decoded components.</returns>
    public static (float X, float Y, float Z) ReadVector(ref BitReader reader, SendProperty property)
    {
        float x = ReadFloat(ref reader, property);
        float y = ReadFloat(ref reader, property);

        if ((property.Flags & NormalFlag) == 0)
        {
            return (x, y, ReadFloat(ref reader, property));
        }

        // A normal is unit length, so the third component is derived rather than sent - only
        // its sign is. Reading it as a float instead would consume bits that belong to the
        // next property.
        bool negative = reader.ReadBit();
        float squared = (x * x) + (y * y);

        // The clamp is not only for malformed input. x and y are quantised to 11 bits each, so
        // they are already approximations, and squaring amplifies that - float rounding alone
        // can push the sum just above 1 for a legitimately unit-length normal. sqrt of a small
        // negative is NaN, which does not throw and does not stop anything; it propagates
        // silently and surfaces much later as a position that will not render.
        // Stryker disable once Equality: at exactly 1 both comparisons give zero - the else
        // branch returns 0f and sqrt(1 - 1) is also 0. Equivalent mutant.
        float z = squared < 1f ? MathF.Sqrt(1f - squared) : 0f;

        return (x, y, negative ? -z : z);
    }

    /// <summary>Reads a two-component vector; the third is reconstructed elsewhere.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <returns>The decoded components.</returns>
    public static (float X, float Y) ReadVectorXY(ref BitReader reader, SendProperty property)
    {
        return (ReadFloat(ref reader, property), ReadFloat(ref reader, property));
    }

    /// <summary>Reads a length-prefixed string.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <returns>The decoded string.</returns>
    /// <remarks>
    /// Length-prefixed at nine bits, unlike the NUL-terminated strings the message layer uses.
    /// Mixing the two conventions up would desynchronise the entity rather than fail.
    /// </remarks>
    public static string ReadString(ref BitReader reader)
    {
        int length = (int)reader.ReadUInt32(StringLengthBits);
        byte[] bytes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            bytes[i] = reader.ReadByte();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Whether this property's encoding is implemented.</summary>
    /// <param name="property">The definition to check.</param>
    /// <returns><c>true</c> when it can be decoded.</returns>
    /// <remarks>
    /// Every value encoding is implemented now, so the only unsupported type is
    /// <see cref="SendPropType.DataTable"/> — and that one is structure rather than a value,
    /// so it never reaches a flattened list at all. Kept because callers report schema
    /// coverage with it, and because a new encoding would reintroduce a false case here rather
    /// than at every call site.
    /// </remarks>
    public static bool IsSupported(SendProperty property) =>
        property.Type != SendPropType.DataTable;

    /// <summary>
    /// Reads a coordinate: presence flags, then an integer part, then a fraction.
    /// </summary>
    /// <remarks>
    /// The layout is the one thing here that VDC does not document — <c>SPROP_COORD_MP</c> and
    /// its variants appear only in the SDK's <c>dt_common.h</c> and <c>bf_read</c>.
    ///
    /// Two details are the whole risk, and both decode to a plausible position rather than an
    /// error when wrong:
    ///
    /// - **The integer part is stored minus one.** A present integer is never zero — that case
    ///   is carried by the presence bit — so the encoder subtracts one to buy back a value.
    ///   Forgetting to add it back shifts every coordinate by one unit.
    /// - **The in-bounds bit selects the integer width**, 11 bits inside the world bounds and
    ///   14 outside. Inverting it misreads the integer *and* every bit that follows.
    ///
    /// The integral variant is not a subset of the others: it has no fraction at all, and it
    /// reads the sign bit only when an integer is present, where the non-integral variants
    /// always read it.
    /// </remarks>
    private static float ReadCoord(ref BitReader reader, int flags)
    {
        // Strict first-match precedence, as the engine's FloatDefinition does it: COORD, then
        // COORD_MP, then LOWPRECISION, then INTEGRAL. These are not independent modifiers, and
        // treating them as such is a width bug rather than a cosmetic one - a property
        // carrying COORD_MP and LOWPRECISION together reads five fraction bits, not three.
        bool plainCoord = (flags & CoordFlag) != 0;
        bool multiplayer = !plainCoord && (flags & CoordMpFlag) != 0;
        bool lowPrecision = !plainCoord && !multiplayer &&
                            (flags & CoordMpLowPrecisionFlag) != 0;
        // Stryker disable once Bitwise: this block is only entered when at least one of the four
        // coord flags is set, so once the other three are ruled out this one must be — and & and
        // | agree. Equivalent by construction, not a missing test.
        bool integral = !plainCoord && !multiplayer && !lowPrecision &&
                        (flags & CoordMpIntegralFlag) != 0;

        // SPROP_COORD has no in-bounds bit; its first two bits say which parts are present.
        bool inBounds = !plainCoord && reader.ReadBit();
        bool hasInteger = reader.ReadBit();
        bool hasFraction = plainCoord ? reader.ReadBit() : !integral;

        if (plainCoord && !hasInteger && !hasFraction)
        {
            return 0f;
        }

        if (integral && !hasInteger)
        {
            return 0f;
        }

        bool negative = reader.ReadBit();
        float value = 0f;

        if (hasInteger)
        {
            int width = plainCoord || !inBounds ? CoordIntegerBits : CoordIntegerBitsInBounds;

            // Plus one: the encoder stored it minus one, because a present integer is never
            // zero. Dropping this is a one-unit shift on every coordinate in the demo.
            value = reader.ReadUInt32(width) + 1;
        }

        if (hasFraction)
        {
            int width = lowPrecision ? CoordFractionBitsLowPrecision : CoordFractionBits;
            value += reader.ReadUInt32(width) / (float)(1 << width);
        }

        return negative ? -value : value;
    }
}
