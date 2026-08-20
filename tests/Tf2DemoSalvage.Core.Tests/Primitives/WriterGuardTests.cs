using System;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>
/// The width guards on the bit writer, and the encoder overloads that state no encoding choice.
/// </summary>
/// <remarks>
/// **A width outside 1–32 is a caller bug, not a wire condition**, so nothing that reads a demo can
/// produce one — which is precisely why these lines sit uncovered while the writer is exercised by
/// every round trip in the suite. They are here because the alternative to throwing is a silent
/// truncation: <c>Write(value, 0)</c> writing nothing at all leaves the reader a bit behind for the
/// rest of the message, and a stream that desynchronises one bit in still decodes into numbers.
///
/// The overloads are the other half. Several encoders take an optional record of *how* a value was
/// encoded — which of several representations the demo used — and the short form that omits it is
/// what a caller writing a fresh demo uses. Two entry points, one of them only ever reached when
/// the value is being invented rather than reproduced.
/// </remarks>
public sealed class WriterGuardTests
{
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(33)]
    public void Write_AWidthOutsideOneToThirtyTwo_IsRefused(int bits)
    {
        // Both ends and the zero, because the guard is a range and a test of one end passes
        // against a comparison written the wrong way round at the other.
        BitWriter writer = new();

        Should.Throw<ArgumentOutOfRangeException>(() => writer.Write(1u, bits));
    }

    [Test]
    public void Write_AWidthAtEitherEndOfTheRange_IsAccepted()
    {
        // **The control.** A Write that refused every width would satisfy the three cases above,
        // and the boundaries are where an off-by-one lives.
        BitWriter writer = new();

        writer.Write(1u, 1).Write(uint.MaxValue, 32);

        writer.BitCount.ShouldBe(33);
    }

    [Test]
    public void AppendBits_MoreBitsThanTheBufferHolds_IsRefused()
    {
        // A length-prefixed body is copied in by this method, so the stated length and the buffer
        // come from different places. Copying past the end would append whatever followed the
        // array in memory.
        BitWriter writer = new();

        Should.Throw<ArgumentOutOfRangeException>(
            () => writer.AppendBits(new byte[2], 17));
    }

    [Test]
    public void AppendBits_ExactlyAsManyBitsAsTheBufferHolds_IsAccepted()
    {
        // The boundary the guard is written around: 16 bits of a two-byte buffer is the whole of
        // it and must be allowed.
        BitWriter writer = new();

        writer.AppendBits(new byte[] { 0xAB, 0xCD }, 16);

        writer.BitCount.ShouldBe(16);
    }

    [Test]
    public void WriteFloat_TheOverloadWithNoRecordedChoice_MatchesTheOneThatStatesNone()
    {
        // The short overload exists for a caller writing a value the demo never carried, so it has
        // no encoding choice to reproduce. It must agree with passing "no choice" explicitly —
        // otherwise a written demo and a rewritten one differ for a reason nothing records.
        SendProperty property = new(
            SendPropType.Float, "m_flCycle", 0, string.Empty, 0f, 1f, 10, 0);

        BitWriter shortForm = new();
        SendPropEncoder.WriteFloat(shortForm, property, 0.25f);

        BitWriter longForm = new();
        SendPropEncoder.WriteFloat(longForm, property, 0.25f, null);

        shortForm.Build().ShouldBe(longForm.Build());
        shortForm.BitCount.ShouldBe(longForm.BitCount);
    }

    [Test]
    public void WriteVector_TheOverloadWithNoRecordedChoices_MatchesTheOneThatStatesNone()
    {
        SendProperty property = new(
            SendPropType.Vector, "m_vecOrigin", SendPropDecoder.CoordFlag,
            string.Empty, 0f, 0f, 0, 0);

        BitWriter shortForm = new();
        SendPropEncoder.WriteVector(shortForm, property, (16f, -32f, 64f));

        BitWriter longForm = new();
        SendPropEncoder.WriteVector(longForm, property, (16f, -32f, 64f), 0);

        shortForm.Build().ShouldBe(longForm.Build());
    }

    [Test]
    public void WriteVectorXY_TheOverloadWithNoRecordedChoices_MatchesTheOneThatStatesNone()
    {
        SendProperty property = new(
            SendPropType.VectorXY, "m_vecOrigin", SendPropDecoder.CoordFlag,
            string.Empty, 0f, 0f, 0, 0);

        BitWriter shortForm = new();
        SendPropEncoder.WriteVectorXY(shortForm, property, (16f, -32f));

        BitWriter longForm = new();
        SendPropEncoder.WriteVectorXY(longForm, property, (16f, -32f), 0);

        shortForm.Build().ShouldBe(longForm.Build());
    }

    [Test]
    public void WriteVector_ANormal_SendsOnlyTheSignOfZ()
    {
        // **A normal is unit length, so Z is derived from X and Y on the way back in** and only
        // its sign travels. Writing it as a float would add bits the decoder never reads, which
        // desynchronises everything after it in the same entity.
        //
        // The observable is the width: a normal's Z costs one bit, so two normals differing only
        // in the sign of Z must encode to the same length and different bits.
        SendProperty property = new(
            SendPropType.Vector, "m_vecNormal", SendPropDecoder.NormalFlag,
            string.Empty, 0f, 0f, 11, 0);

        BitWriter up = new();
        SendPropEncoder.WriteVector(up, property, (0.6f, 0f, 0.8f), 0);

        BitWriter down = new();
        SendPropEncoder.WriteVector(down, property, (0.6f, 0f, -0.8f), 0);

        up.BitCount.ShouldBe(down.BitCount);
        up.Build().ShouldNotBe(down.Build());
    }
}
