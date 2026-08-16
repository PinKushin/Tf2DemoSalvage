using System;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The remaining bit primitives against transcriptions of Valve's, over random bits.
/// </summary>
/// <remarks>
/// **Three readers that share a failure mode with the coordinate ones and are easier to overlook.**
/// An angle, a normal and a variable-width integer are each small enough to look obviously right,
/// and each is read from a stream with no length prefix — so a width error propagates rather than
/// staying local.
///
/// <c>ReadUBitVar</c> is the one worth the effort. Valve reads SIX bits, and then, if the low two
/// select a wider encoding, **seeks backwards four bits** and reads the full field. Net consumption
/// is two bits plus the payload, not six plus the payload, and an implementation that reads a
/// two-bit selector followed by the payload is equivalent only because the rewind exactly cancels
/// the four value bits it already consumed. That equivalence is worth establishing rather than
/// assuming, which is what the differential does.
/// </remarks>
public sealed class BitPrimitiveDifferentialTests
{
    /// <summary><c>NORMAL_FRACTIONAL_BITS</c> from <c>public/coordsize.h</c>.</summary>
    private const int NormalFractionBits = 11;

    /// <summary><c>NORMAL_RESOLUTION</c>: <c>1.0 / ((1 &lt;&lt; 11) - 1)</c>, a double as in coordsize.h.</summary>
    private const double NormalResolution = 1.0 / ((1 << NormalFractionBits) - 1);

    /// <summary>The width <c>svc_FixAngle</c> sends an angle at.</summary>
    private const int AngleBits = 16;

    [Test]
    public void TheAngleReaderAgreesWithValve()
    {
        Random random = new(20260816);
        byte[] bits = new byte[8];

        for (int trial = 0; trial < 20_000; trial++)
        {
            random.NextBytes(bits);

            BitReader ours = new(bits);
            BitReader theirs = new(bits);

            float mine = NetMessageReader.ReadAngle(ref ours);
            float valve = ValveReadBitAngle(ref theirs, AngleBits);

            mine.ShouldBe(valve, $"trial {trial}: angle decode differs");
            ours.BitsRead.ShouldBe(theirs.BitsRead);
        }
    }

    [Test]
    public void TheNormalReaderAgreesWithValve()
    {
        Random random = new(20260816);
        byte[] bits = new byte[8];

        SendProperty normal = new(
            SendPropType.Float, "normal", SendPropDecoder.NormalFlag, string.Empty, 0f, 1f, 0, 0);

        for (int trial = 0; trial < 20_000; trial++)
        {
            random.NextBytes(bits);

            BitReader ours = new(bits);
            BitReader theirs = new(bits);

            float mine = SendPropDecoder.ReadFloat(ref ours, normal);
            float valve = ValveReadBitNormal(ref theirs);

            mine.ShouldBe(valve, $"trial {trial}: normal decode differs");
            ours.BitsRead.ShouldBe(theirs.BitsRead);
        }
    }

    [Test]
    public void TheVariableWidthIntegerAgreesWithValve()
    {
        Random random = new(20260816);
        byte[] bits = new byte[8];

        for (int trial = 0; trial < 20_000; trial++)
        {
            random.NextBytes(bits);

            BitReader ours = new(bits);

            uint mine = UBitVar.Read(ref ours);
            (uint valve, int consumed) = ValveReadUBitVar(bits);

            mine.ShouldBe(
                valve,
                $"trial {trial}: variable-width integer differs on {Convert.ToHexString(bits)}");

            ours.BitsRead.ShouldBe(
                consumed,
                $"trial {trial}: same value, different width — the rewind does not line up");
        }
    }

    [Test]
    public void TheNarrowestEncodingConsumesSixBitsAndTheWidestThirtyFour()
    {
        // **The rewind, stated as bit counts.** Valve reads six then seeks back four, so the widest
        // encoding costs two selector bits plus thirty-two, not six plus thirty-two. A reader that
        // forgot the rewind would be four bits ahead on every non-trivial value — and entity
        // indices are sent this way, so it would lose the whole snapshot rather than one number.
        Consumed(0b00).ShouldBe(6);
        Consumed(0b01).ShouldBe(2 + 8);
        Consumed(0b10).ShouldBe(2 + 12);
        Consumed(0b11).ShouldBe(2 + 32);
    }

    /// <summary>How many bits this project's reader consumes for a given selector.</summary>
    private static int Consumed(int selector)
    {
        BitWriter writer = new();
        writer.Write((uint)selector, 2);
        writer.Write(0, 32);
        writer.Write(0, 8);

        BitReader reader = new(writer.Build());
        UBitVar.Read(ref reader);

        return reader.BitsRead;
    }

    /// <summary><c>bf_read::ReadBitAngle</c>, from <c>tier1/bitbuf.cpp:948</c>.</summary>
    private static float ValveReadBitAngle(ref BitReader buffer, int numbits)
    {
        // BitForBitnum(n) is 1 << n, from GetBitForBitnum in public/bitvec.h.
        float shift = 1 << numbits;
        int i = (int)buffer.ReadUInt32(numbits);

        return (float)(i * (360.0 / shift));
    }

    /// <summary><c>bf_read::ReadBitNormal</c>, from <c>tier1/bitbuf.cpp:1291</c>.</summary>
    private static float ValveReadBitNormal(ref BitReader buffer)
    {
        bool signbit = buffer.ReadBit();
        uint fractval = buffer.ReadUInt32(NormalFractionBits);

        float value = (float)(fractval * NormalResolution);

        return signbit ? -value : value;
    }

    /// <summary>
    /// <c>bf_read::ReadUBitVar</c> with <c>ReadUBitVarInternal</c>, from
    /// <c>public/tier1/bitbuf.h:757</c> and <c>tier1/bitbuf.cpp:1000</c>.
    /// </summary>
    /// <remarks>
    /// **The rewind is expressed as the position it lands on**, because this project's reader has
    /// no seek and adding one purely for a test would be production surface bought with a test's
    /// money. Valve reads six bits and then does <c>m_iCurBit -= 4</c>, which leaves the cursor at
    /// two — so a fresh reader that discards two bits is at the same place, and the bits consumed
    /// are two plus the payload. That restatement is the one liberty taken here, and it is stated
    /// rather than hidden because everything else is deliberately verbatim.
    ///
    /// Valve's width expression is kept as arithmetic:
    /// <c>4 + encoding*4 + (((2 - encoding) &gt;&gt; 31) &amp; 16)</c>, which yields 8, 12 and 32 by a
    /// sign-bit trick rather than a table.
    /// </remarks>
    private static (uint Value, int BitsConsumed) ValveReadUBitVar(byte[] data)
    {
        BitReader first = new(data);

        // "six bits: low 2 bits for encoding + first 4 bits of value"
        uint sixbits = first.ReadUInt32(6);
        uint encoding = sixbits & 3;

        if (encoding == 0)
        {
            return (sixbits >> 2, 6);
        }

        // ReadUBitVarInternal: "m_iCurBit -= 4" leaves the cursor two bits in.
        BitReader after = new(data);
        after.ReadUInt32(2);

        int bits = 4 + ((int)encoding * 4) + (((2 - (int)encoding) >> 31) & 16);

        return (after.ReadUInt32(bits), 2 + bits);
    }
}
