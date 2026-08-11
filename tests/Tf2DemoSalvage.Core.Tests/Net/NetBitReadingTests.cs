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
    [Fact]
    public void ReadString_StopsAtTheTerminator()
    {
        BitReader reader = new([(byte)'h', (byte)'i', 0, (byte)'X']);

        NetBitReading.ReadString(ref reader).ShouldBe("hi");

        // The terminator is consumed, so whatever follows it is untouched - the next thing to
        // read is 'X', not the byte after it.
        reader.ReadByte().ShouldBe((byte)'X');
    }

    [Fact]
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

    [Fact]
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
