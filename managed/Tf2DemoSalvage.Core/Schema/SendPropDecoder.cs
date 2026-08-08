using System;
using System.Globalization;
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
    private const int UnsignedFlag = 1 << 0;
    private const int CoordFlag = 1 << 1;
    private const int NoScaleFlag = 1 << 2;
    private const int NormalFlag = 1 << 5;
    private const int CoordMpFlag = 1 << 13;
    private const int CoordMpLowPrecisionFlag = 1 << 14;
    private const int CoordMpIntegralFlag = 1 << 15;

    /// <summary>Flags whose encodings are not implemented yet.</summary>
    private const int UnsupportedFlags =
        CoordFlag | CoordMpFlag | CoordMpLowPrecisionFlag | CoordMpIntegralFlag;

    /// <summary>Bits a normal uses for magnitude, plus one for sign.</summary>
    private const int NormalFractionBits = 11;

    /// <summary>Width of a networked string's length prefix: <c>DT_MAX_STRING_BITS</c>.</summary>
    private const int StringLengthBits = 9;

    /// <summary>Reads an integer property.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing its width and signedness.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadInt(ref BitReader reader, SendProperty property)
    {
        uint raw = reader.ReadUInt32(property.BitCount);

        if ((property.Flags & UnsignedFlag) != 0)
        {
            return (int)raw;
        }

        // Sign-extend from the property's width. Without this a negative value read at 11 bits
        // comes back as a large positive one, which is wrong in a way that still looks numeric.
        int shift = 32 - property.BitCount;
        return (int)raw << shift >> shift;
    }

    /// <summary>Reads a float property.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing how the value is encoded.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="NotSupportedException">
    /// The property uses a coordinate encoding, which is not implemented.
    /// </exception>
    public static float ReadFloat(ref BitReader reader, SendProperty property)
    {
        ThrowIfUnsupported(property);

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
    /// <exception cref="NotSupportedException">The property uses a coordinate encoding.</exception>
    public static (float X, float Y, float Z) ReadVector(ref BitReader reader, SendProperty property)
    {
        // Stryker disable once Statement: removing this changes nothing observable - the
        // per-component ReadFloat below guards the same property and throws the same
        // exception. Kept so the failure names the vector rather than a component.
        ThrowIfUnsupported(property);

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
        float z = squared < 1f ? MathF.Sqrt(1f - squared) : 0f;

        return (x, y, negative ? -z : z);
    }

    /// <summary>Reads a two-component vector; the third is reconstructed elsewhere.</summary>
    /// <param name="reader">Reader positioned at the value.</param>
    /// <param name="property">The definition describing each component.</param>
    /// <returns>The decoded components.</returns>
    /// <exception cref="NotSupportedException">The property uses a coordinate encoding.</exception>
    public static (float X, float Y) ReadVectorXY(ref BitReader reader, SendProperty property)
    {
        // Stryker disable once Statement: as above, ReadFloat guards the same property.
        ThrowIfUnsupported(property);

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
    public static bool IsSupported(SendProperty property) =>
        (property.Flags & UnsupportedFlags) == 0;

    private static void ThrowIfUnsupported(SendProperty property)
    {
        if (IsSupported(property))
        {
            return;
        }

        // Thrown rather than approximated. A wrong coordinate is a plausible position, and a
        // viewer drawing plausible-but-wrong positions is worse than one that refuses to draw.
        throw new NotSupportedException(string.Create(
            CultureInfo.InvariantCulture,
            $"Property '{property.Name}' uses a coordinate encoding (flags 0x{property.Flags:X}) " +
            $"that is not implemented. SPROP_COORD_MP and its variants are undocumented in the " +
            $"Valve Developer Community wiki and were found only in the SDK headers."));
    }
}
