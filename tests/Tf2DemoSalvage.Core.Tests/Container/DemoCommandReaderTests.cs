using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// Tests for the command stream that follows the header. Layout CONFIRMED against three corpus
/// demos; see <c>docs/SPEC.md</c>.
/// </summary>
public sealed class DemoCommandReaderTests
{
    [Test]
    public void PacketCommand_CarriesTheCameraItWasRecordedFrom()
    {
        // democmdinfo_t was read and discarded on every packet - 76 bytes per command, skipped
        // to reach the payload. It holds the view origin and angles of the recording client,
        // which is the only record in a demo of where the camera actually was, and it is plain
        // floats rather than anything bit-packed.
        //
        // The 2D and 3D viewers need exactly this. Entity origins say where players stood; this
        // says what the person recording was looking at, which is a different question and one
        // nothing else in the file answers.
        byte[] info = new byte[76];
        BitConverter.GetBytes(1).CopyTo(info, 0);                    // flags
        BitConverter.GetBytes(128.5f).CopyTo(info, 4);               // view origin x
        BitConverter.GetBytes(-64.25f).CopyTo(info, 8);              // y
        BitConverter.GetBytes(32f).CopyTo(info, 12);                 // z
        BitConverter.GetBytes(10f).CopyTo(info, 16);                 // view angle pitch
        BitConverter.GetBytes(-170.5f).CopyTo(info, 20);             // yaw
        BitConverter.GetBytes(0f).CopyTo(info, 24);                  // roll

        List<byte> command = [(byte)DemoCommandType.Packet];
        command.AddRange(BitConverter.GetBytes(99));                 // tick
        command.AddRange(info);
        command.AddRange(new byte[8]);                               // sequence numbers
        command.AddRange(BitConverter.GetBytes(4));                  // payload length
        command.AddRange(new byte[4]);

        DemoCommand packet = DemoCommandReader.Read(command.ToArray()).First();

        ViewInfo view = packet.View.ShouldNotBeNull();
        view.Flags.ShouldBe(1);
        view.OriginX.ShouldBe(128.5f);
        view.OriginY.ShouldBe(-64.25f);
        view.OriginZ.ShouldBe(32f);
        view.Pitch.ShouldBe(10f);
        view.Yaw.ShouldBe(-170.5f);
        view.Roll.ShouldBe(0f);
    }

    [Test]
    public void CommandsWithoutCameraInformation_ReportNone()
    {
        // Only dem_signon and dem_packet carry democmdinfo_t. Reporting a zeroed camera for the
        // others would be inventing one, and a viewer cannot tell an invented origin from a real
        // one at the origin.
        List<byte> command = [(byte)DemoCommandType.ConsoleCmd];
        command.AddRange(BitConverter.GetBytes(7));
        command.AddRange(BitConverter.GetBytes(4));
        command.AddRange(new byte[4]);

        DemoCommandReader.Read(command.ToArray()).First().View.ShouldBeNull();
    }

    private const int CommandInfoBytes = 76;

    /// <summary>Assembles a synthetic command stream (header bytes excluded).</summary>
    private sealed class StreamBuilder
    {
        private readonly List<byte> _bytes = [];

        public StreamBuilder Command(DemoCommandType type, int tick)
        {
            _bytes.Add((byte)type);
            _bytes.AddRange(BitConverter.GetBytes(tick));
            return this;
        }

        public StreamBuilder RawData(params byte[] payload)
        {
            _bytes.AddRange(BitConverter.GetBytes(payload.Length));
            _bytes.AddRange(payload);
            return this;
        }

        public StreamBuilder CommandInfo()
        {
            _bytes.AddRange(new byte[CommandInfoBytes]);
            _bytes.AddRange(BitConverter.GetBytes(0));  // sequence in
            _bytes.AddRange(BitConverter.GetBytes(0));  // sequence out
            return this;
        }

        /// <summary>
        /// The terminator TF2 actually writes: dem_stop plus only three of the tick's four
        /// bytes. Every corpus demo ends this way.
        /// </summary>
        public StreamBuilder ShortStop(int tick)
        {
            _bytes.Add((byte)DemoCommandType.Stop);
            _bytes.AddRange(BitConverter.GetBytes(tick)[..3]);
            return this;
        }

        public StreamBuilder Raw(params byte[] bytes)
        {
            _bytes.AddRange(bytes);
            return this;
        }

        public byte[] Build() => [.. _bytes];
    }

    private static List<DemoCommand> ReadAll(byte[] stream) =>
        [.. DemoCommandReader.Read(stream)];

    [Test]
    public void Read_PacketThenStop_YieldsBothWithTicksAndPayload()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.Packet, 100).CommandInfo().RawData(0xAA, 0xBB, 0xCC)
            .Command(DemoCommandType.Stop, 200)
            .Build();

        List<DemoCommand> commands = ReadAll(stream);

        commands.Count.ShouldBe(2);
        commands[0].Type.ShouldBe(DemoCommandType.Packet);
        commands[0].Tick.ShouldBe(100);
        commands[0].Payload.ToArray().ShouldBe([0xAA, 0xBB, 0xCC]);
        commands[1].Type.ShouldBe(DemoCommandType.Stop);
        commands[1].Tick.ShouldBe(200);
        commands[1].Payload.Length.ShouldBe(0);
    }

    [Test]
    public void Read_ShortStopTerminator_EndsCleanlyAndRecoversTheTick()
    {
        // The case that matters most: every real TF2 demo ends with dem_stop followed by three
        // tick bytes, not four. A reader demanding the full header rejects every valid demo.
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.Packet, 10).CommandInfo().RawData(0x01)
            .ShortStop(57551)
            .Build();

        List<DemoCommand> commands = ReadAll(stream);

        commands.Count.ShouldBe(2);
        commands[1].Type.ShouldBe(DemoCommandType.Stop);
        commands[1].Tick.ShouldBe(57551);
    }

    [Test]
    public void Read_StopWithNoTickBytesAtAll_StillEndsCleanly()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.SyncTick, 0)
            .Raw((byte)DemoCommandType.Stop)
            .Build();

        List<DemoCommand> commands = ReadAll(stream);

        commands[^1].Type.ShouldBe(DemoCommandType.Stop);
    }

    [Test]
    public void Read_StopsAtStopEvenWhenBytesFollow()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.Stop, 5)
            .Command(DemoCommandType.Packet, 6).CommandInfo().RawData(0xFF)
            .Build();

        ReadAll(stream).Count.ShouldBe(1);
    }

    [Test]
    public void Read_SyncTick_HasNoPayload()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.SyncTick, 42)
            .Command(DemoCommandType.Stop, 43)
            .Build();

        List<DemoCommand> commands = ReadAll(stream);

        commands[0].Tick.ShouldBe(42);
        commands[0].Payload.Length.ShouldBe(0);
    }

    [Test]
    public void Read_UserCmd_SkipsTheOutgoingSequenceBeforeThePayload()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.UserCmd, 7)
            .Raw(BitConverter.GetBytes(99))
            .RawData(0xDE, 0xAD)
            .Command(DemoCommandType.Stop, 8)
            .Build();

        List<DemoCommand> commands = ReadAll(stream);

        commands[0].Type.ShouldBe(DemoCommandType.UserCmd);
        commands[0].Payload.ToArray().ShouldBe([0xDE, 0xAD]);
    }
    [TestCase(DemoCommandType.ConsoleCmd)]
    [TestCase(DemoCommandType.DataTables)]
    [TestCase(DemoCommandType.StringTables)]
    public void Read_LengthPrefixedCommands_ExposeTheirPayload(DemoCommandType type)
    {
        byte[] stream = new StreamBuilder()
            .Command(type, 1).RawData(0x11, 0x22)
            .Command(DemoCommandType.Stop, 2)
            .Build();

        ReadAll(stream)[0].Payload.ToArray().ShouldBe([0x11, 0x22]);
    }

    [Test]
    public void Read_SignonCarriesCommandInfoLikePacket()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.Signon, 0).CommandInfo().RawData(0x5A)
            .Command(DemoCommandType.Stop, 1)
            .Build();

        ReadAll(stream)[0].Payload.ToArray().ShouldBe([0x5A]);
    }

    [Test]
    public void Read_UnknownCommandByte_ThrowsInvalidData()
    {
        byte[] stream = new StreamBuilder().Raw(0x63).Raw(BitConverter.GetBytes(0)).Build();

        InvalidDataException exception = Should.Throw<InvalidDataException>(() => ReadAll(stream));

        exception.Message.ShouldContain("99");
    }

    [Test]
    public void Read_PayloadLongerThanTheBuffer_ThrowsEndOfStream()
    {
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.DataTables, 1)
            .Raw(BitConverter.GetBytes(9999))
            .Raw(0x01, 0x02)
            .Build();

        Should.Throw<EndOfStreamException>(() => ReadAll(stream));
    }

    [Test]
    public void Read_NegativePayloadLength_ThrowsInvalidData()
    {
        // A negative length would otherwise rewind the cursor and loop forever.
        byte[] stream = new StreamBuilder()
            .Command(DemoCommandType.DataTables, 1)
            .Raw(BitConverter.GetBytes(-8))
            .Build();

        Should.Throw<InvalidDataException>(() => ReadAll(stream));
    }

    [Test]
    public void Read_TruncatedMidCommandHeader_ThrowsEndOfStreamForNonStopCommands()
    {
        // Only dem_stop gets the short-header accommodation; anything else ending mid-header
        // is genuine damage and must be reported.
        byte[] stream = new StreamBuilder()
            .Raw((byte)DemoCommandType.Packet, 0x01, 0x02)
            .Build();

        Should.Throw<EndOfStreamException>(() => ReadAll(stream));
    }

    [Test]
    public void Read_EmptyStream_YieldsNothing()
    {
        ReadAll([]).ShouldBeEmpty();
    }

    [Test]
    public void Read_IsLazy_AndDoesNotThrowUntilEnumerated()
    {
        byte[] stream = new StreamBuilder().Raw(0x63).Build();

        IEnumerable<DemoCommand> lazy = DemoCommandReader.Read(stream);

        Should.Throw<InvalidDataException>(() => lazy.ToList());
    }
}
