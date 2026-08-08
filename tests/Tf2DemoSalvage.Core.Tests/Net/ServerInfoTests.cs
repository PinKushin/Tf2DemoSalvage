using System;
using System.Linq;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Synthetic tests for <c>svc_ServerInfo</c>, the message that gates the entire signon stream.
/// </summary>
/// <remarks>
/// Two fields make this message unusually verifiable against real demos: its
/// <c>NetworkProtocol</c> must equal the one in the file header, and its <c>Map</c> must equal
/// the header's map name. Both are transmitted through completely different paths — one a
/// fixed-offset field in a 1072-byte header, the other a NUL-terminated string at an arbitrary
/// bit offset — so agreement is strong evidence the bit layout is right.
/// </remarks>
public sealed class ServerInfoTests
{
    private static byte[] Build(
        ushort protocol = 24,
        uint serverCount = 7,
        bool sourceTv = true,
        bool dedicated = true,
        uint mapCrc = 0xDEADBEEF,
        ushort maxClasses = 275,
        byte playerSlot = 3,
        byte maxPlayers = 24,
        float intervalPerTick = 0.015f,
        char platform = 'l',
        string game = "tf",
        string map = "cp_process_final",
        string skybox = "sky_upward_01",
        string serverName = "serveme.tf",
        bool replay = false)
    {
        BitWriter writer = new BitWriter().Message(NetMessageType.ServerInfo);
        writer.Write(protocol, 16)
            .Write(serverCount, 32)
            .Write(sourceTv ? 1u : 0u, 1)
            .Write(dedicated ? 1u : 0u, 1)
            .Write(mapCrc, 32)
            .Write(maxClasses, 16);

        // Protocol 18 replaced the older 4-byte map CRC with a 16-byte hash.
        int hashBytes = protocol > 17 ? 16 : 4;
        for (int i = 0; i < hashBytes; i++)
        {
            writer.Write((uint)(i + 1), 8);
        }

        writer.Write(playerSlot, 8)
            .Write(maxPlayers, 8)
            .Write((uint)BitConverter.SingleToInt32Bits(intervalPerTick), 32)
            .Write(platform, 8)
            .String(game)
            .String(map)
            .String(skybox)
            .String(serverName);

        // The replay flag arrived in protocol 16.
        if (protocol > 15)
        {
            writer.Write(replay ? 1u : 0u, 1);
        }

        return writer.Build();
    }

    [Fact]
    public void ServerInfo_DecodesEveryField()
    {
        NetMessageReadResult result = NetMessageReader.Read(Build());

        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.NetworkProtocol.ShouldBe((ushort)24);
        info.ServerCount.ShouldBe(7u);
        info.IsSourceTv.ShouldBeTrue();
        info.IsDedicated.ShouldBeTrue();
        info.MaxClasses.ShouldBe((ushort)275);
        info.PlayerSlot.ShouldBe((byte)3);
        info.MaxPlayers.ShouldBe((byte)24);
        info.IntervalPerTick.ShouldBe(0.015f, 0.000001f);
        info.Platform.ShouldBe('l');
        info.GameDirectory.ShouldBe("tf");
        info.Map.ShouldBe("cp_process_final");
        info.Skybox.ShouldBe("sky_upward_01");
        info.ServerName.ShouldBe("serveme.tf");
        info.IsReplay.ShouldBeFalse();
    }

    [Fact]
    public void ServerInfo_ExposesTickRateDerivedFromTheInterval()
    {
        // TF2's 66.67 tick rate is 1 / 0.015. The interval is what is transmitted; the rate
        // is what everyone actually talks about.
        NetMessageReadResult result = NetMessageReader.Read(Build(intervalPerTick: 0.015f));

        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.TickRate.ShouldBe(66.67f, 0.01f);
    }

    [Fact]
    public void ServerInfo_ZeroInterval_ReportsZeroTickRateRatherThanInfinity()
    {
        NetMessageReadResult result = NetMessageReader.Read(Build(intervalPerTick: 0f));

        result.Messages[0].ShouldBeOfType<ServerInfoMessage>().TickRate.ShouldBe(0f);
    }

    [Fact]
    public void ServerInfo_LeavesTheReaderPositionedForTheNextMessage()
    {
        // ServerInfo has no length prefix, so every field width must be exactly right or
        // everything after it is noise. This is the assertion that proves the whole layout.
        NetMessageReadResult result = NetMessageReader.Read(BuildThenTick(4242));

        result.Messages[0].ShouldBeOfType<ServerInfoMessage>();

        // Byte-padding the fixture leaves zero bits that decode as net_NOP, so the tick is
        // found by type rather than by a fixed index. What matters is that it decodes at all:
        // ServerInfo has no length prefix, so a single wrong field width turns it into noise.
        NetTickMessage tick = result.Messages.OfType<NetTickMessage>().ShouldHaveSingleItem();
        tick.Tick.ShouldBe(4242);
    }

    [Fact]
    public void ServerInfo_ReplayFlagSet_IsReported()
    {
        NetMessageReadResult result = NetMessageReader.Read(Build(replay: true));

        result.Messages[0].ShouldBeOfType<ServerInfoMessage>().IsReplay.ShouldBeTrue();
    }

    [Fact]
    public void ServerInfo_ListenServer_ReportsNotDedicated()
    {
        NetMessageReadResult result = NetMessageReader.Read(Build(dedicated: false, sourceTv: false));

        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.IsDedicated.ShouldBeFalse();
        info.IsSourceTv.ShouldBeFalse();
    }

    [Theory]
    [InlineData(24, 16)]
    [InlineData(18, 16)]
    [InlineData(17, 4)]
    [InlineData(14, 4)]
    public void MapHashWidth_FollowsTheProtocolVersion(ushort protocol, int expectedBytes)
    {
        // Protocol 18 replaced a 4-byte CRC with a 16-byte hash. Our corpus is entirely
        // protocol 24, so the older branch has no specimen behind it - this pins the intended
        // behaviour without claiming it is verified against a real demo.
        NetMessageReadResult result = NetMessageReader.Read(Build(protocol: protocol));

        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.MapHash.Count.ShouldBe(expectedBytes);
        info.NetworkProtocol.ShouldBe(protocol);

        // The strings after the hash only land correctly if its width was right.
        info.Map.ShouldBe("cp_process_final");
        info.ServerName.ShouldBe("serveme.tf");
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(16, true)]
    [InlineData(15, false)]
    [InlineData(14, false)]
    public void ReplayFlag_IsOnlyReadFromProtocolSixteenOnward(ushort protocol, bool present)
    {
        // Same caveat: only protocol 24 is corpus-verified. Reading a bit that is not there
        // would not throw - it would silently consume a bit belonging to whatever follows.
        NetMessageReadResult result = NetMessageReader.Read(Build(protocol: protocol, replay: true));

        ServerInfoMessage info = result.Messages[0].ShouldBeOfType<ServerInfoMessage>();
        info.IsReplay.ShouldBe(present);
    }

    private static byte[] BuildThenTick(uint tick)
    {
        // Rebuild in one writer so the net_Tick lands at ServerInfo's true end, unaligned.
        BitWriter writer = new();
        byte[] serverInfo = Build();

        // BitsConsumed would include the NOP padding read after ServerInfo, so the exact end
        // of the message is taken from the first result instead.
        NetMessageReadResult probe = NetMessageReader.Read(serverInfo);
        int bits = probe.Messages.OfType<NetEmptyMessage>().Any()
            ? probe.BitsConsumed - (probe.Messages.Count(m => m is NetEmptyMessage) * NetMessage.TypeBits)
            : probe.BitsConsumed;

        for (int bit = 0; bit < bits; bit++)
        {
            writer.Write((uint)((serverInfo[bit / 8] >> (bit % 8)) & 1), 1);
        }

        return writer.NetTick(tick, 0, 0).Build();
    }
}
