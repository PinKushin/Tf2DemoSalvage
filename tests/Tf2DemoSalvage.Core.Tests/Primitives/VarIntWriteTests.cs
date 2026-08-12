using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Round-trip tests for the varint encoder.
/// </summary>
/// <remarks>
/// Stated as a round trip because there is no expected byte array to get wrong, which is the
/// failure mode hand-built fixtures in this project have actually had. The encoder exists to
/// re-encode messages back to the bits they came from, so "decodes to the same number" is not
/// enough on its own — a padded encoding decodes correctly and produces different bytes. The
/// canonical-length property is what pins that.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified, so the property " +
                    "delegate is the assertion. Sonar does not know the library.")]
public sealed class VarIntWriteTests
{
    [Test]
    public void AnyValue_SurvivesARoundTrip()
    {
        Gen.UInt.Sample(value =>
        {
            BitWriter writer = new();
            VarInt.WriteUInt32(writer, value);

            BitReader reader = new(writer.Build());
            return VarInt.ReadUInt32(ref reader) == value;
        });
    }

    [Test]
    public void EveryValue_EncodesInTheFewestGroupsThatHoldIt()
    {
        // A decoder accepts a padded encoding, so a writer that emitted five bytes for the value
        // 1 would round-trip through the previous test and still produce bytes no demo contains.
        // Byte-exact re-encoding needs the canonical form, not merely a decodable one.
        Gen.UInt.Sample(value =>
        {
            BitWriter writer = new();
            VarInt.WriteUInt32(writer, value);

            int expected = value switch
            {
                < 1u << 7 => 1,
                < 1u << 14 => 2,
                < 1u << 21 => 3,
                < 1u << 28 => 4,
                _ => 5,
            };

            return writer.BitCount == expected * 8;
        });
    }
}
