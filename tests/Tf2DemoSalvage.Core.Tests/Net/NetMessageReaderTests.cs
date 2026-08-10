using System;
using System.Collections.Generic;
using System.Linq;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;

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
    public void Read_EveryDefinedMessageType_IsImplemented()
    {
        // This test was rehomed four times - PacketEntities, TempEntities, SetView, Menu - as
        // each gained support, and has now run out of subjects: every defined type is
        // implemented. So it asserts the property that made it useful rather than naming a
        // victim, and it fails the moment a type is added to the enum without a case.
        //
        // Bodies are garbage, so most of these stop on truncation. That is fine and not what is
        // being checked. What must never appear is the default arm's reason, because an
        // unimplemented type discards the rest of its packet - the defect behind RISKS B13.
        foreach (NetMessageType type in Enum.GetValues<NetMessageType>())
        {
            byte[] packet = new BitWriter().Message(type).Write(0, 32).Build();
            string? reason = NetMessageReader.Read(packet).StopReason;

            if (reason is not null)
            {
                reason.ShouldNotContain("is not decoded yet", Case.Insensitive, type.ToString());
            }
        }
    }

    [Fact]
    public void Read_StoppingReportsHowFarItGot()
    {
        // The behaviour the test above used to cover, kept against an undefined id since no
        // defined type stops any more. Messages carry no length prefix, so an unknown one
        // cannot be stepped over - the rest of the packet is unreachable, not empty.
        byte[] packet = new BitWriter().NetTick(1, 0, 0).Write(22, NetMessage.TypeBits).Build();

        NetMessageReadResult result = NetMessageReader.Read(packet);

        result.Messages.Count.ShouldBe(1);
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
            .Message(NetMessageType.Menu)
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

    [Theory]
    [InlineData(4, "SayText2")]
    [InlineData(18, "Damage")]
    [InlineData(0, "Geiger")]
    [InlineData(78, "BuiltObject")]
    public void UserMessage_IsReportedWithItsRegisteredName(int type, string expected)
    {
        // A user message is where TF2 puts most of what a reader wants - damage numbers,
        // announcements, vote results - and every one of them used to appear in a trace as an
        // anonymous skipped message. Naming the type is most of the readability.
        //
        // The ids come from the registration order in tf_usermessages.cpp, read from the SDK
        // rather than recalled. SayText2 at 4 is the cross-check: it was already proven correct
        // against real chat before the table existed.
        BitWriter writer = new();
        writer.Message(NetMessageType.UserMessage)
            .Write((uint)type, 8)
            .Write(16, 11)
            .Write(0xBEEF, 16);
        writer.NetTick(4242, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        UserMessage user = result.Messages.OfType<UserMessage>().ShouldHaveSingleItem();
        user.UserMessageType.ShouldBe(type);
        user.Name.ShouldBe(expected);
        user.BodyBits.ShouldBe(16);

        // The tick behind it: naming a message must not change how many bits it consumes.
        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(4242);
    }

    [Theory]
    [InlineData(79)]    // exactly one past the last entry
    [InlineData(200)]   // far past it
    public void UnknownUserMessageType_IsReportedWithoutAName(int type)
    {
        // The table is TF2's registration order at one point in its history, so an id past the
        // end is expected on other eras or other games. Reporting the number with no name is
        // honest; inventing one would be worse than saying nothing.
        //
        // 79 is the case that matters. The bound is `type < Names.Length`, and 200 satisfies a
        // broken `<=` just as well as a correct `<` — it cannot see an off-by-one at the end of
        // the table. Paired with the id-78 row above, these two pin the boundary exactly.
        BitWriter writer = new();
        writer.Message(NetMessageType.UserMessage).Write((uint)type, 8).Write(8, 11).Write(0xAB, 8);

        UserMessage user = NetMessageReader.Read(writer.Build())
            .Messages.OfType<UserMessage>().ShouldHaveSingleItem();

        user.UserMessageType.ShouldBe(type);
        user.Name.ShouldBeNull();
    }

    [Fact]
    public void ChatUserMessages_AreStillDecodedAsChat()
    {
        // The control. SayText2 was decoded before this change and must not regress into a
        // merely-named message - naming the rest is an addition, not a replacement.
        // Body shape copied from ChatMessageTests rather than invented: client index, a flag,
        // then NUL-terminated localisation key, sender and text. An earlier version of this
        // fixture guessed the layout and produced bytes that parsed to nothing.
        byte[] bytes =
        [
            3, 1,
            .. System.Text.Encoding.UTF8.GetBytes("TF_Chat_All"), 0,
            .. System.Text.Encoding.UTF8.GetBytes("Sassy"), 0,
            .. System.Text.Encoding.UTF8.GetBytes("hello"), 0,
        ];

        BitWriter writer = new();
        writer.Message(NetMessageType.UserMessage)
            .Write(4, 8)
            .Write((uint)(bytes.Length * 8), 11);
        foreach (byte b in bytes)
        {
            writer.Write(b, 8);
        }

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        result.Messages.OfType<ChatMessage>().ShouldHaveSingleItem();
    }


    [Fact]
    public void UnreliableSounds_ReportCountAndLength()
    {
        // svc_Sounds was the single most common skipped message in the corpus - 231 in one 2009
        // demo. Reported by header now, not decoded: the reference implementation keeps the body
        // opaque too, and four of proto_version.h's boundaries are sound-related with no demo
        // between protocols 15 and 24 to exercise them.
        BitWriter writer = new();
        writer.Message(NetMessageType.Sounds)
            .Write(0, 1)                       // not reliable
            .Write(3, 8)                       // three sounds
            .Write(24, 16)                     // 24 bits of body
            .Write(0xABCDEF, 24);
        writer.NetTick(77, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        SoundsMessage sounds = result.Messages.OfType<SoundsMessage>().ShouldHaveSingleItem();
        sounds.IsReliable.ShouldBeFalse();
        sounds.Count.ShouldBe(3);
        sounds.BodyBits.ShouldBe(24);

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(77);
    }

    [Fact]
    public void ReliableSounds_ImplyOneSoundAndAShorterLengthField()
    {
        // The trap in this message: the reliable flag changes *two* fields at once. A reliable
        // message sends no count and an eight-bit length; an unreliable one sends a count byte
        // and a sixteen-bit length. Reading one shape for the other desynchronises the packet,
        // which is why the tick behind it is asserted rather than just the fields.
        BitWriter writer = new();
        writer.Message(NetMessageType.Sounds)
            .Write(1, 1)                       // reliable
            .Write(16, 8)                      // eight-bit length, no count byte
            .Write(0xBEEF, 16);
        writer.NetTick(88, 0, 0);

        NetMessageReadResult result = NetMessageReader.Read(writer.Build());

        SoundsMessage sounds = result.Messages.OfType<SoundsMessage>().ShouldHaveSingleItem();
        sounds.IsReliable.ShouldBeTrue();
        sounds.Count.ShouldBe(1);
        sounds.BodyBits.ShouldBe(16);

        result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem().Tick.ShouldBe(88);
    }

}
