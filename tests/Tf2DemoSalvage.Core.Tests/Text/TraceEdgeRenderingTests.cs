using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// Messages and commands the trace renders through their fallback shapes.
/// </summary>
/// <remarks>
/// **Several messages have two renderings and the interesting one is the second.** An entity
/// message with a leading type byte names the byte; one with an empty body has no byte to name. A
/// sound list with bits is expanded; one with none is a header line. Each pair exists because
/// reporting a zero where nothing was stated is a claim the demo did not make — and in every case
/// the demos take the first branch, so the second is the one nothing has ever exercised.
///
/// The odd one out is <c>SkippedMessage</c>. It is not a message on the wire at all: it is what
/// the reader records when a case consumes bits and produces nothing, so that the message list
/// stays parallel to the stream. <c>svc_SetPause</c> is one bit and no object, and without the
/// placeholder the trace would show consumed bits with nothing to attribute them to.
/// </remarks>
public sealed class TraceEdgeRenderingTests
{
    [Test]
    public void Trace_AnEntityMessageWithNoBody_NamesItWithoutAMessageType()
    {
        // The leading byte selects the case inside the receiving class's ReceiveMessage, so a body
        // with no byte has no case to name. Printing `type 0` would claim it said
        // BASEENTITY_MSG_REMOVE_DECALS.
        string trace = Trace(SyntheticDemo.Containing(
            new EntityMessage(EntityIndex: 12, ClassId: 5, BodyBits: 0)));

        trace.ShouldContain("svc_entitymessage entity 12");
        trace.ShouldNotContain("type ");
    }

    [Test]
    public void Trace_AnEntityMessageWithATypeByte_NamesTheType()
    {
        // **The control, and it is the branch every demo takes.** Without it, an assertion that
        // the type is absent could pass because the renderer never prints a type at all.
        string trace = Trace(SyntheticDemo.Containing(
            new EntityMessage(EntityIndex: 12, ClassId: 5, BodyBits: 8, Body: new byte[] { 1 })));

        trace.ShouldContain("svc_entitymessage entity 12");
        trace.ShouldContain("type 1");
    }

    [Test]
    public void Trace_ASoundListWithNoBits_IsAHeaderLineRatherThanAnExpansion()
    {
        // A sounds message whose body is empty has nothing to expand, and the expander would read
        // past the end trying. The header line still reports the count the message declared.
        string trace = Trace(SyntheticDemo.Containing(
            new SoundsMessage(IsReliable: false, Count: 0, BodyBits: 0)));

        trace.ShouldContain("svc_sounds unreliable count 0 bits 0");
    }

    [Test]
    public void Trace_AMessageTheReaderConsumesWithoutProducing_IsStillAccountedFor()
    {
        // **svc_SetPause is one bit and no object.** Every message accounts for itself or the
        // trace shows consumed bits with nothing attached to them — which reads as a decoder that
        // lost its place rather than as a message with no content.
        BitWriter packet = new();
        packet.Write((uint)NetMessageType.SetPause, NetMessage.TypeBits).WriteBit(true);

        string trace = Trace(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            new DemoCommand(
                DemoCommandType.Packet, 66, packet.Build(), new byte[PacketPrologueBytes])));

        trace.ShouldContain("svc_setpause");
    }

    [Test]
    public void Trace_ACommandTypeTheContainerDoesNotDefine_IsNamedRatherThanOmitted()
    {
        // **This one cannot come from a file, and the reader is why**: an unrecognised command
        // byte stops the read outright rather than being handed on, because the container has no
        // length prefix and there is nothing to skip to. So the command is handed to the writer
        // directly, which is the only way this rendering is reachable at all.
        //
        // It is still worth having. The trace is a complete account of what it was given, and a
        // command silently dropped from it would leave a gap that reads as a shorter demo.
        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            text,
            "synthetic.dem",
            SyntheticDemo.Header(),
            [new DemoCommand((DemoCommandType)9, 66, ReadOnlyMemory<byte>.Empty)]);

        text.ToString().ShouldContain("dem_unknown");
    }

    /// <summary>
    /// Bytes of <c>democmdinfo_t</c> and the sequence numbers a packet command carries.
    /// </summary>
    private const int PacketPrologueBytes = 76 + 8;

    private static string Trace(byte[] demo)
    {
        DemoHeader header = DemoHeader.Parse(demo.AsSpan(0, DemoHeader.SizeBytes));
        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(demo.AsMemory(DemoHeader.SizeBytes))];

        StringWriter text = new() { NewLine = "\n" };
        DemoTraceWriter.Write(text, "synthetic.dem", header, commands);
        return text.ToString();
    }
}
