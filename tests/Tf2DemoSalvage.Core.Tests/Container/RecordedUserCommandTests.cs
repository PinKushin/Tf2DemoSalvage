using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// What prediction needs from the stream (D205): each <c>dem_usercmd</c>'s outgoing sequence and command, and each
/// packet's second sequence number — the last command the state in it already includes.
/// </summary>
/// <remarks>
/// <c>CDemoRecorder</c> writes a usercmd as its outgoing sequence then the delta-coded command, and a packet's
/// <c>democmdinfo_t</c> (76 bytes) then two sequence numbers. *Evidence class for the second number being the
/// acknowledged command: measured* — on <c>tf2-2013-build1729296-pov-cp_badlands</c> the packet at tick 3511 carries
/// 6109 and its velocity already shows command 6109's release of the forward key (300 to 282, one tick of friction).
/// </remarks>
public sealed class RecordedUserCommandTests
{
    [Test]
    public void From_AUserCmd_CarriesItsTickSequenceAndCommand()
    {
        UserCommand command = new(6109, 6176, 0f, 38.5f, 0f, 450f, 0f, 0f, 0x8, 0, 0, 0, 0, 0, 0);
        byte[] sequence = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(sequence, 6109);

        RecordedUserCommand recorded = RecordedUserCommand.From(
            new DemoCommand(DemoCommandType.UserCmd, 3511, command.Encode(), sequence));

        recorded.Tick.ShouldBe(3511);
        recorded.Sequence.ShouldBe(6109);
        recorded.Command.ForwardMove.ShouldBe(450f);
        recorded.Command.Yaw.ShouldBe(38.5f);
    }

    [Test]
    public void Acknowledged_APacketPrologue_IsTheSecondSequenceNumber()
    {
        byte[] prologue = new byte[RecordedView.SizeBytes + 8];
        BinaryPrimitives.WriteInt32LittleEndian(prologue.AsSpan(RecordedView.SizeBytes), 5967);
        BinaryPrimitives.WriteInt32LittleEndian(prologue.AsSpan(RecordedView.SizeBytes + 4), 6109);

        RecordedUserCommand.Acknowledged(prologue).ShouldBe(6109);
    }

    [Test]
    public void Acknowledged_APrologueWithoutSequenceNumbers_IsNull() =>
        RecordedUserCommand.Acknowledged(new byte[RecordedView.SizeBytes]).ShouldBeNull();
}
