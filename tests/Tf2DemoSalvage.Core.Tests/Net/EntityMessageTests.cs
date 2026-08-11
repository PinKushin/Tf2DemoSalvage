using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for the one field of <c>svc_EntityMessage</c> that can be read without knowing the class.
/// </summary>
/// <remarks>
/// **This message was written off as undecodable in principle, and that was wrong.** Its body is
/// handled by the receiving entity's class rather than by the schema, so there is no generic
/// layout — but the SDK contains only 18 <c>ReceiveMessage</c> implementations in total, most of
/// them HL2 and episodic, and <c>game/client/tf/</c> overrides it not at all. Every one of them
/// opens by reading a message-type byte and switching on it.
///
/// So the class id selects the handler and the first byte selects the case. The byte is reported
/// and deliberately not named: <c>BASEENTITY_MSG_REMOVE_DECALS</c> and <c>PLAY_PLAYER_JINGLE</c>
/// are both 1, for different classes, so naming it would assert a handler that the class id has
/// not been resolved to.
/// </remarks>
public sealed class EntityMessageTests
{
    [Fact]
    public void TheLeadingByteIsReportedAsTheMessageType()
    {
        // Every svc_EntityMessage in the corpus is exactly this: 8 bits, class 1, type 1 - one
        // byte selecting RemoveAllDecals, with no payload after it.
        EntityMessage message = new(519, 1, 8, new byte[] { 1 });

        message.MessageType.ShouldBe(1);
    }

    [Fact]
    public void ABodyLongerThanOneByte_StillReportsOnlyTheLeadingByte()
    {
        // The type byte is the dispatch, and whatever follows belongs to the case it selects.
        // Reading further would need the handler, which needs the class resolved to a name.
        EntityMessage message = new(42, 7, 40, new byte[] { 3, 0xAA, 0xBB, 0xCC, 0xDD });

        message.MessageType.ShouldBe(3);
    }

    [Fact]
    public void AnEmptyBody_ReportsNoMessageType()
    {
        // A zero-length body has no byte to dispatch on. Reporting 0 would invent a message type,
        // and 0 is a plausible one - the switch cases start at 1, so a fabricated 0 looks like a
        // legitimate "no case matched" rather than like missing data.
        new EntityMessage(1, 1, 0, Array.Empty<byte>()).MessageType.ShouldBeNull();
        new EntityMessage(1, 1, 0).MessageType.ShouldBeNull();
    }

    [Fact]
    public void ABodyShorterThanAByte_ReportsNoMessageType()
    {
        // A stated length below 8 bits cannot contain the dispatch byte. The buffer may still
        // hold a byte - CopyBits rounds up to whole bytes - so reading it would report padding
        // as a message type.
        new EntityMessage(1, 1, 5, new byte[] { 0xFF }).MessageType.ShouldBeNull();
    }
}
