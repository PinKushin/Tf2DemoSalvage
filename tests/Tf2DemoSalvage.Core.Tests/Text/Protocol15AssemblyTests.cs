using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// A protocol-15 demo written at six bits, decompiled to text and compiled back (B440).
/// </summary>
/// <remarks>
/// **The text has to say which width it was, because the compiler has no bytes to ask.** Reading
/// decides a protocol-15 demo's type width from its first packet; compiling writes that packet, so
/// the width must already be in the text when the packet is assembled — and a header saying
/// protocol 15 says nothing about it. B440's CEVO demo compiled back to 10,914,237 bytes from
/// 10,914,193 before this existed.
/// </remarks>
public sealed class Protocol15AssemblyTests
{
    /// <summary>The protocol both builds announced.</summary>
    private const ushort Protocol = 15;

    [Test]
    public void RoundTrip_AProtocol15DemoWrittenAtSixBits_ReproducesItsBytes()
    {
        ServerInfoMessage info = Info();
        byte[] demo = SyntheticDemo.From(
            Protocol,
            SyntheticDemo.SixBitPacket(
                null, 0, info, new NetTickMessage(4242, 7, 8),
                new SetConVarMessage([new KeyValuePair<string, string>("tv_transmitall", "1")])),
            SyntheticDemo.SixBitPacket(
                info, 1, new NetTickMessage(4243, 0, 0), new PrefetchMessage(1234)));

        string assembly = Decompile(demo);

        // The width is stated, and ServerInfo is TEXT restating the header — a line that only
        // assembles back to its own bits when it was read at the width it was written at.
        assembly.ShouldContain("\n  messagetypebits 6\n");
        assembly.ShouldContain("\n  svc_serverinfo 15 ");

        using StringReader reader = new(assembly);
        (DemoHeader header, IReadOnlyList<DemoCommand> commands) = DemoAssembly.Parse(reader);

        DemoWriter.Write(header, commands).ShouldBe(demo);
    }

    /// <summary>The assembly text a demo decompiles to.</summary>
    private static string Decompile(byte[] demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands = [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoAssembly.Write(text, header, commands);
        return text.ToString();
    }

    /// <summary>A protocol-15 SourceTV server's <c>svc_ServerInfo</c>: a four-byte map CRC, no replay flag.</summary>
    private static ServerInfoMessage Info() => new(
        NetworkProtocol: Protocol,
        ServerCount: 11,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0xFFFF_FFFF,
        MaxClasses: 249,
        MapHash: [0x3D, 0x8E, 0x60, 0x75],
        PlayerSlot: 4,
        MaxPlayers: 15,
        IntervalPerTick: 0.015f,
        Platform: 'l',
        GameDirectory: "tf",
        Map: "cp_badlands",
        Skybox: "sky_badlands_01",
        ServerName: "SourceTV",
        IsReplay: false);
}
