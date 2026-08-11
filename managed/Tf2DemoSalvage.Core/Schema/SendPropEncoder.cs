using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Schema;

/// <summary>
/// Writes property values back in the encodings <see cref="SendPropDecoder"/> reads.
/// </summary>
/// <remarks>
/// **Deliberately one encoding, not all of them.** Only <c>SPROP_COORD</c> is here, because only
/// <c>svc_BspDecal</c> needs it — that message carries three bare coordinates outside any entity,
/// and re-encoding it is what closes the last gap in the message round trip. The rest of the
/// encodings will arrive when something needs them; a speculative encoder for each would be a
/// large body of code with nothing checking it.
///
/// <c>SPROP_COORD</c> is one of the few whose encoding shape *is* recoverable from its value,
/// which is why it can be written without the decoder recording anything. The two presence bits
/// say whether an integer part and a fraction follow, and both are determined: an integer part is
/// stored minus one, so a present one is never zero, and a fraction of zero is simply not sent.
/// Contrast a sound's origin, where the sender's choice is genuinely lost.
/// </remarks>
public static class SendPropEncoder
{
    /// <summary>Integer bits: <c>COORD_INTEGER_BITS</c>.</summary>
    private const int CoordIntegerBits = 14;

    /// <summary>Fraction bits at normal precision: <c>COORD_FRACTIONAL_BITS</c>.</summary>
    private const int CoordFractionBits = 5;

    /// <summary>Fraction steps in one unit.</summary>
    private const int CoordFractionSteps = 1 << CoordFractionBits;

    /// <summary>Writes a plain <c>SPROP_COORD</c> value.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="value">The coordinate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The magnitude does not fit the 14-bit integer field.
    /// </exception>
    public static void WriteCoord(BitWriter writer, float value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        float magnitude = MathF.Abs(value);
        int integer = (int)magnitude;
        int fraction = (int)MathF.Round((magnitude - integer) * CoordFractionSteps);

        // A fraction that rounds up to a whole step is a carry, not a 32nd. Leaving it would
        // write a fraction field the decoder reads back as 1.0 added to the integer part.
        if (fraction == CoordFractionSteps)
        {
            integer++;
            fraction = 0;
        }

        bool hasInteger = integer != 0;
        bool hasFraction = fraction != 0;

        writer.WriteBit(hasInteger).WriteBit(hasFraction);
        if (!hasInteger && !hasFraction)
        {
            // Zero is two clear bits and no sign. Writing a sign here would add a bit the
            // decoder does not read, shifting everything after it.
            return;
        }

        if (integer > 1 << CoordIntegerBits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A coordinate's integer part is {CoordIntegerBits} bits, so it stops at " +
                    $"{1 << CoordIntegerBits}."));
        }

        writer.WriteBit(float.IsNegative(value));

        if (hasInteger)
        {
            // Minus one, matching the decoder's plus one: a present integer part is never zero,
            // so the encoding spends the zero on the value it cannot otherwise reach.
            writer.Write((uint)(integer - 1), CoordIntegerBits);
        }

        if (hasFraction)
        {
            writer.Write((uint)fraction, CoordFractionBits);
        }
    }
}
