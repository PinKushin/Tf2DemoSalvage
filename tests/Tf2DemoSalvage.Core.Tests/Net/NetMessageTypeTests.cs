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
    [Theory]
    [InlineData(NetMessageType.Empty, 0)]
    [InlineData(NetMessageType.File, 2)]
    [InlineData(NetMessageType.NetTick, 3)]
    [InlineData(NetMessageType.StringCmd, 4)]
    [InlineData(NetMessageType.SetConVar, 5)]
    [InlineData(NetMessageType.SignOnState, 6)]
    [InlineData(NetMessageType.Print, 7)]
    [InlineData(NetMessageType.ServerInfo, 8)]
    [InlineData(NetMessageType.ClassInfo, 10)]
    [InlineData(NetMessageType.SetPause, 11)]
    [InlineData(NetMessageType.CreateStringTable, 12)]
    [InlineData(NetMessageType.UpdateStringTable, 13)]
    [InlineData(NetMessageType.VoiceInit, 14)]
    [InlineData(NetMessageType.VoiceData, 15)]
    [InlineData(NetMessageType.Sounds, 17)]
    [InlineData(NetMessageType.SetView, 18)]
    [InlineData(NetMessageType.FixAngle, 19)]
    [InlineData(NetMessageType.BspDecal, 21)]
    [InlineData(NetMessageType.UserMessage, 23)]
    [InlineData(NetMessageType.EntityMessage, 24)]
    [InlineData(NetMessageType.GameEvent, 25)]
    [InlineData(NetMessageType.PacketEntities, 26)]
    [InlineData(NetMessageType.TempEntities, 27)]
    [InlineData(NetMessageType.Prefetch, 28)]
    [InlineData(NetMessageType.Menu, 29)]
    [InlineData(NetMessageType.GameEventList, 30)]
    [InlineData(NetMessageType.GetCvarValue, 31)]
    [InlineData(NetMessageType.CmdKeyValues, 32)]
    public void MessageType_HasItsOnWireValue(NetMessageType type, int expected)
    {
        ((int)type).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(22)]
    public void UnusedIds_AreNotDefined(int unusedId)
    {
        // Absent on purpose. A stream producing one of these is malformed, and the decoder
        // must reject it rather than invent a meaning.
        Enum.IsDefined((NetMessageType)unusedId).ShouldBeFalse();
    }

    [Fact]
    public void EveryValue_FitsInTheSixBitWireField()
    {
        // The type field is 6 bits, so no id may exceed 63. A value that did would be
        // unreadable and the mistake would not show up until a real packet hit it.
        foreach (NetMessageType type in Enum.GetValues<NetMessageType>())
        {
            ((int)type).ShouldBeLessThan(1 << 6);
        }
    }

    [Fact]
    public void MessageIds_AreUnique()
    {
        NetMessageType[] all = Enum.GetValues<NetMessageType>();

        all.Select(t => (int)t).Distinct().Count().ShouldBe(all.Length);
    }
}
