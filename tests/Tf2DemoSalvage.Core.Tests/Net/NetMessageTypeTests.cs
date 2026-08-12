using System;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Pins the network message id table. These values come from prior art rather than an
/// authoritative Valve header (RISKS B3), so they are asserted explicitly — a silent drift
/// here would misread every packet in every demo.
/// </summary>
public sealed class NetMessageTypeTests
{
    [TestCase(NetMessageType.Empty, 0)]
    [TestCase(NetMessageType.File, 2)]
    [TestCase(NetMessageType.NetTick, 3)]
    [TestCase(NetMessageType.StringCmd, 4)]
    [TestCase(NetMessageType.SetConVar, 5)]
    [TestCase(NetMessageType.SignOnState, 6)]
    [TestCase(NetMessageType.Print, 7)]
    [TestCase(NetMessageType.ServerInfo, 8)]
    [TestCase(NetMessageType.ClassInfo, 10)]
    [TestCase(NetMessageType.SetPause, 11)]
    [TestCase(NetMessageType.CreateStringTable, 12)]
    [TestCase(NetMessageType.UpdateStringTable, 13)]
    [TestCase(NetMessageType.VoiceInit, 14)]
    [TestCase(NetMessageType.VoiceData, 15)]
    [TestCase(NetMessageType.Sounds, 17)]
    [TestCase(NetMessageType.SetView, 18)]
    [TestCase(NetMessageType.FixAngle, 19)]
    [TestCase(NetMessageType.BspDecal, 21)]
    [TestCase(NetMessageType.UserMessage, 23)]
    [TestCase(NetMessageType.EntityMessage, 24)]
    [TestCase(NetMessageType.GameEvent, 25)]
    [TestCase(NetMessageType.PacketEntities, 26)]
    [TestCase(NetMessageType.TempEntities, 27)]
    [TestCase(NetMessageType.Prefetch, 28)]
    [TestCase(NetMessageType.Menu, 29)]
    [TestCase(NetMessageType.GameEventList, 30)]
    [TestCase(NetMessageType.GetCvarValue, 31)]
    [TestCase(NetMessageType.CmdKeyValues, 32)]
    public void MessageType_HasItsOnWireValue(NetMessageType type, int expected)
    {
        ((int)type).ShouldBe(expected);
    }
    [TestCase(1)]
    [TestCase(9)]
    [TestCase(16)]
    [TestCase(20)]
    [TestCase(22)]
    public void UnusedIds_AreNotDefined(int unusedId)
    {
        // Absent on purpose. A stream producing one of these is malformed, and the decoder
        // must reject it rather than invent a meaning.
        Enum.IsDefined((NetMessageType)unusedId).ShouldBeFalse();
    }

    [Test]
    public void EveryValue_FitsInTheSixBitWireField()
    {
        // The type field is 6 bits, so no id may exceed 63. A value that did would be
        // unreadable and the mistake would not show up until a real packet hit it.
        foreach (NetMessageType type in Enum.GetValues<NetMessageType>())
        {
            ((int)type).ShouldBeLessThan(1 << 6);
        }
    }

    [Test]
    public void MessageIds_AreUnique()
    {
        NetMessageType[] all = Enum.GetValues<NetMessageType>();

        all.Select(t => (int)t).Distinct().Count().ShouldBe(all.Length);
    }
}
