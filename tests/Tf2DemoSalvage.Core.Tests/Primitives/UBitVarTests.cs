using System;
using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Tests.Net;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// Tests for Source's variable-width unsigned integer.
/// </summary>
/// <remarks>
/// Written before the implementation, and mostly as round trips — the encoder is four lines
/// and the property "whatever went in comes out" covers every selector without picking values
/// by hand.
/// </remarks>
[SuppressMessage("Major Code Smell", "S2699:Tests should include assertions",
    Justification = "CsCheck's Sample throws when a property is falsified.")]
public sealed class UBitVarTests
{
    /// <summary>Encodes at the narrowest selector that fits, as Source does.</summary>
    private static void Write(BitWriter writer, uint value)
    {
        (int selector, int bits) = value switch
        {
            < 1u << 4 => (0, 4),
            < 1u << 8 => (1, 8),
            < 1u << 12 => (2, 12),
            _ => (3, 32),
        };

        writer.Write((uint)selector, UBitVar.SelectorBits).Write(value, bits);
    }
    [TestCase(0u, 0, 4)]
    [TestCase(15u, 0, 4)]
    [TestCase(16u, 1, 8)]
    [TestCase(255u, 1, 8)]
    [TestCase(256u, 2, 12)]
    [TestCase(4095u, 2, 12)]
    [TestCase(4096u, 3, 32)]
    [TestCase(uint.MaxValue, 3, 32)]
    public void EachSelector_DecodesItsWidth(uint value, int selector, int bits)
    {
        BitWriter writer = new();
        writer.Write((uint)selector, 2).Write(value, bits);
        BitReader reader = new(writer.Build());

        UBitVar.Read(ref reader).ShouldBe(value);
    }

    [Test]
    public void AnyValue_SurvivesARoundTrip()
    {
        Gen.UInt.Sample(value =>
        {
            BitWriter writer = new();
            Write(writer, value);
            BitReader reader = new(writer.Build());

            return UBitVar.Read(ref reader) == value;
        });
    }

    [Test]
    public void ASequence_ReadsBackInOrderAtUnalignedOffsets()
    {
        // Entity index deltas arrive in long runs at arbitrary bit offsets, so consuming one
        // bit too few or too many would corrupt every index after it rather than fail.
        Gen.UInt.Array[1, 40].Sample(values =>
        {
            BitWriter writer = new();
            writer.Write(1, 3);   // deliberately misalign the start
            foreach (uint value in values)
            {
                Write(writer, value);
            }

            BitReader reader = new(writer.Build());
            _ = reader.ReadUInt32(3);

            foreach (uint value in values)
            {
                if (UBitVar.Read(ref reader) != value)
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Test]
    public void ConsumesExactlyTheWidthItReports()
    {
        Gen.UInt.Sample(value =>
        {
            BitWriter writer = new();
            Write(writer, value);
            BitReader reader = new(writer.Build());

            int before = reader.BitsRead;
            _ = UBitVar.Read(ref reader);

            return reader.BitsRead - before == UBitVar.EncodedBits(value);
        });
    }

    [Test]
    public void SmallValuesAreCheaperThanAVarint()
    {
        // The reason this encoding exists: an entity index delta of 3 costs six bits here
        // against a varint's eight, and a demo carries tens of thousands of them.
        UBitVar.EncodedBits(3).ShouldBe(6);

        // Every width boundary, from both sides. Mutation testing found the 256 edge
        // untested, and an off-by-one there would mis-size one value in every 256.
        UBitVar.EncodedBits(15).ShouldBe(6);
        UBitVar.EncodedBits(16).ShouldBe(10);
        UBitVar.EncodedBits(255).ShouldBe(10);
        UBitVar.EncodedBits(256).ShouldBe(14);
        UBitVar.EncodedBits(4095).ShouldBe(14);
        UBitVar.EncodedBits(4096).ShouldBe(34);
        UBitVar.EncodedBits(uint.MaxValue).ShouldBe(34);
    }
}
