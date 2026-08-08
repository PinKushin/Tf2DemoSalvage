using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for walking the message stream inside a <c>dem_packet</c> payload.
/// </summary>
/// <remarks>
/// The defining constraint: messages are not length-prefixed, so the reader cannot skip a type
/// it does not understand. It must stop and say where. These tests pin that behaviour as much
/// as they pin the decoding, because "stopped cleanly at an unsupported message" and "silently
/// read garbage" look identical from the outside.
/// </remarks>
public sealed class NetMessageReaderTests
{
    [Fact]
    public void Read_SingleNetTick_DecodesEveryField()
    {
        byte[] packet = new BitWriter().NetTick(120935, 1500, 42).Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.Count.ShouldBe(1);
        NetTickMessage tick = result.Messages[0].ShouldBeOfType<NetTickMessage>();
        tick.Tick.ShouldBe(120935);
        tick.HostFrameTimeRaw.ShouldBe((ushort)1500);
        tick.HostFrameTimeStdDevRaw.ShouldBe((ushort)42);
    }

    [Fact]
    public void Read_NetTick_ConvertsFrameTimeUsingTheSourceScale()
    {
        byte[] packet = new BitWriter().NetTick(1, 1500, 250).Build();

        NetTickMessage tick =
            NetMessageReader.Read(packet).Messages[0].ShouldBeOfType<NetTickMessage>();

        tick.HostFrameTimeStdDevSeconds.ShouldBe(0.0025f, 0.0000001f);

        // Source scales host frame time by 100,000 on the wire.
        tick.HostFrameTimeSeconds.ShouldBe(0.015f, 0.0000001f);
    }

    [Fact]
    public void Read_ConsumesExactlySixtyFourBitsPerNetTick()
    {
        byte[] packet = new BitWriter().NetTick(5, 1, 2).NetTick(9, 3, 4).Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.Count.ShouldBe(2);
        // 2 x (6-bit type + 64-bit body). An off-by-one here would desynchronise the second
        // message rather than fail outright, which is why the count is asserted too.
        result.BitsConsumed.ShouldBe(2 * (NetMessage.TypeBits + 64));
        ((NetTickMessage)result.Messages[1]).Tick.ShouldBe(9);
    }

    [Fact]
    public void Read_EmptyMessage_HasNoBodyAndDoesNotStopTheStream()
    {
        // net_NOP is pure padding: the six type bits and nothing else.
        byte[] packet = new BitWriter()
            .Message(NetMessageType.Empty)
            .NetTick(7, 0, 0)
            .Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.Count.ShouldBe(2);
        result.StoppedAt.ShouldBeNull();
        ((NetTickMessage)result.Messages[1]).Tick.ShouldBe(7);
    }

    [Fact]
    public void Read_UnsupportedMessage_StopsAndReportsWhereAndWhy()
    {
        byte[] packet = new BitWriter()
            .NetTick(1, 0, 0)
            .Message(NetMessageType.PacketEntities)
            .Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.Count.ShouldBe(1);
        result.StoppedAt.ShouldBe(NetMessageType.PacketEntities);
        // Stopping position matters: it is the only way to tell how far into a packet we get.
        result.BitsConsumed.ShouldBe(NetMessage.TypeBits + 64);
        result.StopReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Read_UndefinedMessageId_StopsRatherThanGuessing()
    {
        // Id 22 is unused at this protocol.
        byte[] packet = new BitWriter().Write(22, NetMessage.TypeBits).Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.ShouldBeEmpty();
        result.StoppedAt.ShouldBeNull();
        result.StopReason.ShouldNotBeNull().ShouldContain("22");
    }

    [Fact]
    public void Read_TrailingZeroPadding_ReadsAsNopsAndEndsCleanly()
    {
        // Packets are padded to a byte boundary. net_NOP is message id 0, so trailing zero
        // bits are indistinguishable from a run of NOPs - almost certainly why NOP was given
        // id 0 in the first place. Both readings mean the same thing, namely nothing left to
        // do, so either is correct. What matters is that padding is never reported as damage.
        byte[] packet = new BitWriter().NetTick(3, 0, 0).Write(0, 3).Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.StopReason.ShouldBeNull();
        result.Messages[0].ShouldBeOfType<NetTickMessage>();
        result.Messages.Skip(1).ShouldAllBe(m => m.Type == NetMessageType.Empty);
    }

    [Fact]
    public void Read_MessageBodyRunningPastTheEnd_ReportsTruncation()
    {
        // A net_Tick type followed by only half its body.
        byte[] packet = new BitWriter()
            .Message(NetMessageType.NetTick)
            .Write(0, 32)
            .Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.ShouldBeEmpty();
        result.StopReason.ShouldNotBeNull().ShouldContain("truncat", Case.Insensitive);
    }

    [Fact]
    public void IsComplete_IsTrueOnlyWhenTheWholePacketWasRead()
    {
        // Callers branch on this to decide whether a packet's contents can be trusted as
        // exhaustive, so it needs asserting directly rather than inferred from StopReason.
        byte[] whole = new BitWriter().NetTick(1, 0, 0).Build();
        byte[] blocked = new BitWriter()
            .NetTick(1, 0, 0)
            .Message(NetMessageType.PacketEntities)
            .Build();

        NetMessageReader.Read(whole).IsComplete.ShouldBeTrue();
        NetMessageReader.Read(blocked).IsComplete.ShouldBeFalse();
        NetMessageReader.Read([]).IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void Read_EmptyPacket_YieldsNothingAndNoComplaint()
    {
        NetMessageReadResult result = NetMessageReader.Read([]);

        result.Messages.ShouldBeEmpty();
        result.StopReason.ShouldBeNull();
        result.BitsConsumed.ShouldBe(0);
    }
}
