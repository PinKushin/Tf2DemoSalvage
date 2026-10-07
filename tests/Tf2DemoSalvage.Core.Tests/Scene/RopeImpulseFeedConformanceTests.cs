using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>C_RopeKeyframe::ReceiveMessage</c> (`c_rope.cpp:2082-2095`): the three floats
/// <c>CRopeKeyframe::PropagateForce</c> writes (`rope.cpp:520-526`) become the rope's <c>m_flImpulse</c> (B478).
/// </summary>
public sealed class RopeImpulseFeedConformanceTests
{
    private const int RopeClass = 112;

    /// <remarks>
    /// **No type byte**: unlike every other handler the rope reads <c>x</c>, <c>y</c>, <c>z</c> straight off the message,
    /// each a 32-bit <c>ReadFloat</c> — the <c>WRITE_FLOAT</c>s of <c>PropagateForce</c>.
    /// </remarks>
    [Test]
    public void Record_ARopesOwnMessage_IsItsImpulse()
    {
        RopeImpulseFeed feed = new();

        feed.Record(Message(120, RopeClass, 1.5f, -2f, 40f), "CRopeKeyframe", RopeClass, tick: 900).ShouldBeTrue();

        feed.All.ShouldBe([new SceneRopeImpulse(900, 120, (1.5f, -2f, 40f))]);
    }

    /// <remarks>
    /// **Another class id goes to the base handler**: <c>if ( classID != GetClientClass()-&gt;m_ClassID )
    /// BaseClass::ReceiveMessage</c> (`c_rope.cpp:2084-2089`) — and an entity that is not a rope never reaches the rope's
    /// handler at all.
    /// </remarks>
    [TestCase("CRopeKeyframe", RopeClass + 1)]
    [TestCase("CBaseAnimating", RopeClass)]
    public void Record_AMessageNotForTheRopesHandler_IsNoImpulse(string entityClass, int entityClassId)
    {
        RopeImpulseFeed feed = new();

        feed.Record(Message(120, RopeClass, 1f, 1f, 1f), entityClass, entityClassId, tick: 1).ShouldBeFalse();

        feed.All.ShouldBeEmpty();
    }

    /// <remarks>
    /// **A body too short reads zero past its end**: <c>bf_read</c> sets its overflow flag and every later read
    /// returns 0 (`bitbuf.cpp`, <c>ReadUBitLong</c> under <c>IsOverflowed</c>). Only whole floats are read here, so a
    /// 64-bit body is <c>( x, y, 0 )</c>.
    /// </remarks>
    [Test]
    public void Record_AShortBody_ReadsZeroPastItsEnd()
    {
        RopeImpulseFeed feed = new();
        EntityMessage full = Message(120, RopeClass, 3f, 4f, 5f);

        feed.Record(full with { BodyBits = 64 }, "CRopeKeyframe", RopeClass, tick: 2);

        feed.All[0].Impulse.ShouldBe((3f, 4f, 0f));
    }

    private static EntityMessage Message(int entity, int classId, float x, float y, float z)
    {
        byte[] body = new byte[12];

        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(0), x);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(4), y);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(8), z);

        return new EntityMessage(entity, classId, 96, body);
    }
}
