using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A demo whose file ends inside a command compiles back with its tail (B448).
/// </summary>
/// <remarks>
/// **157 of D200's 429 pool demos end mid-command** — ESEA's writer flushed whole 4 KiB blocks and
/// never the last. The engine stops reading there (<c>CDemoFile::ReadRawData</c> fails on a short
/// read), so nothing past the last whole command is decoded; but D200 asks for every byte back, so
/// the assembly carries those bytes verbatim and the compiler writes them out again.
/// </remarks>
public sealed class CutDemoAssemblyTests
{
    /// <summary>dem_stop's type byte and its three tick bytes, which a cut demo never reaches.</summary>
    private const int StopBytes = 4;

    /// <summary>A type byte plus an int32 tick.</summary>
    private const int CommandHeaderBytes = 5;

    /// <summary>The command header, <c>democmdinfo_t</c>, two sequence numbers and the length.</summary>
    private const int PacketPayloadOffset = CommandHeaderBytes + 76 + 8 + 4;

    [TestCase(2, TestName = "RoundTrip_CutTwoBytesIntoTheLastCommand_ReproducesBytes")]
    [TestCase(CommandHeaderBytes - 1, TestName = "RoundTrip_CutInsideTheCommandHeader_ReproducesBytes")]
    [TestCase(CommandHeaderBytes + 40, TestName = "RoundTrip_CutInsideThePrologue_ReproducesBytes")]
    [TestCase(PacketPayloadOffset + 3, TestName = "RoundTrip_CutMidPacketPayload_ReproducesBytes")]
    public void RoundTrip_CutDemo_ReproducesBytes(int tailBytes)
    {
        byte[] cut = Cut(tailBytes);

        byte[] rebuilt = Compile(Decompile(cut));

        rebuilt.ShouldBe(cut);
    }

    [Test]
    public void Write_CutDemo_CarriesTheTailAsItsOwnLine()
    {
        // The construct is named, so a reader of the text can see the file was cut and by how much.
        byte[] cut = Cut(2);

        Decompile(cut).ShouldContain("\ntail " + Convert.ToHexString(cut.AsSpan(cut.Length - 2)) + "\n");
    }

    [Test]
    public void ReadWhole_CutDemo_ReturnsTheBytesAfterTheLastWholeCommand()
    {
        byte[] cut = Cut(PacketPayloadOffset + 3);

        (IReadOnlyList<DemoCommand> commands, ReadOnlyMemory<byte> tail) =
            DemoCommandReader.ReadWhole(cut.AsMemory(DemoHeader.SizeBytes));

        commands.Count.ShouldBe(1);
        tail.ToArray().ShouldBe(cut[^(PacketPayloadOffset + 3)..]);
    }

    [Test]
    public void ReadWhole_WholeDemo_HasNoTail()
    {
        byte[] whole = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1));

        DemoCommandReader.ReadWhole(whole.AsMemory(DemoHeader.SizeBytes)).Tail.IsEmpty.ShouldBeTrue();
    }

    /// <summary>
    /// Two packets with the stop removed and the second cut so that <paramref name="tailBytes"/> of it remain.
    /// </summary>
    private static byte[] Cut(int tailBytes)
    {
        byte[] one = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1));
        byte[] two = SyntheticDemo.From(SyntheticDemo.DefaultProtocol, Packet(1), Packet(2));
        int wholeEnd = one.Length - StopBytes;

        return two[..(wholeEnd + tailBytes)];
    }

    private static DemoCommand Packet(int tick) => SyntheticDemo.Packet(
        SyntheticDemo.DefaultProtocol, tick, new PrintMessage("cut off by a 4 KiB flush"));

    private static string Decompile(byte[] demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        (IReadOnlyList<DemoCommand> commands, ReadOnlyMemory<byte> tail) =
            DemoCommandReader.ReadWhole(demo.AsMemory(DemoHeader.SizeBytes));

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands, tail);
        return text.ToString();
    }

    private static byte[] Compile(string text)
    {
        using StringReader reader = new(text);
        AssembledDemo demo = DemoAssembly.Parse(reader);
        return DemoWriter.Write(demo.Header, demo.Commands, demo.Tail);
    }
}
