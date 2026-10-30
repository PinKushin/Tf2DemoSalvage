using System;
using System.IO;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests the two shared readers used across the message decoders.
/// </summary>
public sealed class NetBitReadingTests
{
    [Test]
    public void ReadString_StopsAtTheTerminator()
    {
        BitReader reader = new([(byte)'h', (byte)'i', 0, (byte)'X']);

        NetBitReading.ReadString(ref reader).ShouldBe("hi");

        // The terminator is consumed, so whatever follows it is untouched - the next thing to
        // read is 'X', not the byte after it.
        reader.ReadByte().ShouldBe((byte)'X');
    }

    [TestCase(new byte[] { 0xD0, 0x20, 0x9F }, TestName = "ReadStringBytes_ALeadByteCutByASpace_ReturnsTheWireBytes")]
    [TestCase(new byte[] { 0x80, 0xFF, 0xC0, 0xAF }, TestName = "ReadStringBytes_BytesUtf8NeverUses_ReturnsTheWireBytes")]
    [TestCase(new byte[] { 0xE5, 0xA4 }, TestName = "ReadStringBytes_ASequenceCutByTheTerminator_ReturnsTheWireBytes")]
    public void ReadStringBytes_WireBytes_ReturnsThemAndConsumesTheTerminator(byte[] wire)
    {
        // bf_read::ReadString copies raw chars up to the zero and decodes nothing
        // (src/tier1/bitbuf.cpp), so the bytes are the engine's form of the string.
        BitReader reader = new([.. wire, 0, (byte)'X']);

        NetBitReading.ReadStringBytes(ref reader).ShouldBe(wire);
        reader.ReadByte().ShouldBe((byte)'X');
    }

    [Test]
    public void ReadString_BytesThatAreNotUtf8_ReplacesEachStrayByteForDisplay()
    {
        // The display text is unchanged by keeping the bytes: what is not UTF-8 shows as U+FFFD.
        BitReader reader = new([0xD0, 0x20, 0x9F, 0]);

        NetBitReading.ReadString(ref reader).ShouldBe("� �");
    }

    [Test]
    public void ReadString_ValidUtf8_DecodesAsText()
    {
        BitReader reader = new([.. System.Text.Encoding.UTF8.GetBytes("miałker Пётр大将"), 0]);

        NetBitReading.ReadString(ref reader).ShouldBe("miałker Пётр大将");
    }

    [Test]
    public void ReadString_ThatNeverTerminates_ThrowsRatherThanReadingForever()
    {
        // The property this class exists to pin. ReadString's loop has no bound of its own -
        // only the underlying reader running out stops it - so a stream with no zero byte
        // anywhere in it must fail at the buffer edge rather than spin. Reachable in practice:
        // GameEventCodec reads a string inside a length-bounded body, and a malformed body with
        // no terminator inside its declared length is exactly this case.
        BitReader reader = new([1, 2, 3, 4]);
        Exception? thrown = null;

        try
        {
            NetBitReading.ReadString(ref reader);
        }
        catch (EndOfStreamException error)
        {
            thrown = error;
        }

        thrown.ShouldNotBeNull();
    }

    [Test]
    public void ReadString_EmptyBuffer_ThrowsImmediately()
    {
        BitReader reader = new([]);
        Exception? thrown = null;

        try
        {
            NetBitReading.ReadString(ref reader);
        }
        catch (EndOfStreamException error)
        {
            thrown = error;
        }

        thrown.ShouldNotBeNull();
    }
}
