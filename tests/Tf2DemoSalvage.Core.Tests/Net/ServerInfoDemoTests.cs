using System.Linq;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// <c>svc_ServerInfo</c> through a whole demo, without needing a real one.
/// </summary>
/// <remarks>
/// **Replaces <c>CorpusServerInfoTests</c>, and asserts something stronger than it could.** That
/// suite compared ServerInfo's map name against the demo header's, because with a real recording
/// nobody knows what the map name ought to be and the header is an independently-encoded copy of
/// the same fact — a good substitute oracle. A synthetic demo has the answer by construction, so
/// these assert the values directly, which is a stricter claim than "two fields agree".
///
/// **It also covers what the corpus cannot.** The committed demos are protocols 11, 14, 15, 16 and
/// 24; 12–13 and 17–23 have no specimen and community demos of that age are genuinely rare. A
/// written demo can be any of them.
///
/// The path is the real one: the message is encoded, wrapped in a packet, written into a demo with
/// its header, and read back out through <c>DemoCommandReader</c> and <c>NetMessageReader</c>. What
/// is synthetic is the CONTENT, not the plumbing.
/// </remarks>
public sealed class ServerInfoDemoTests
{
    [Test]
    public void RoundTrip_MapName_SurvivesTheWholeDemo()
    {
        // The corpus version of this compared against the header because it had no other oracle.
        // Here the expected value is simply known.
        ServerInfoMessage sent = Info(map: "koth_harvest_final");

        Read(SyntheticDemo.Containing(sent)).Map.ShouldBe("koth_harvest_final");
    }

    [Test]
    public void RoundTrip_EveryServerInfoField_ReturnsTheValueSent()
    {
        // **Distinct values in every field, which is what the corpus could not arrange.** A real
        // demo's ServerInfo has whatever the server had, and several fields are small integers that
        // collide — two zeroes look the same however they are transposed. Chosen values differ, so
        // a swapped pair cannot pass.
        ServerInfoMessage sent = Info(
            map: "pl_upward",
            maxPlayers: 33,
            maxClasses: 11,
            tickRate: 66.67f,
            platform: 'l',
            dedicated: true,
            sourceTv: false,
            skybox: "sky_upward_01");

        ServerInfoMessage read = Read(SyntheticDemo.Containing(sent));

        read.Map.ShouldBe("pl_upward");
        read.MaxPlayers.ShouldBe((byte)33);
        read.MaxClasses.ShouldBe((ushort)11);
        read.TickRate.ShouldBe(66.67f, 0.001f);
        read.Platform.ShouldBe('l');
        read.IsDedicated.ShouldBeTrue();
        read.IsSourceTv.ShouldBeFalse();
        read.Skybox.ShouldBe("sky_upward_01");
        read.GameDirectory.ShouldBe("tf");
    }

    [Test]
    public void Decode_SourceTvAndDedicatedFlags_AreNotTransposed()
    {
        // Two adjacent booleans, which is exactly the pair a bit-level decode transposes. The
        // corpus could only ever show one combination per demo; both are asserted here in one run.
        Read(SyntheticDemo.Containing(Info(sourceTv: true, dedicated: false)))
            .IsSourceTv.ShouldBeTrue();

        Read(SyntheticDemo.Containing(Info(sourceTv: true, dedicated: false)))
            .IsDedicated.ShouldBeFalse();

        Read(SyntheticDemo.Containing(Info(sourceTv: false, dedicated: true)))
            .IsSourceTv.ShouldBeFalse();

        Read(SyntheticDemo.Containing(Info(sourceTv: false, dedicated: true)))
            .IsDedicated.ShouldBeTrue();
    }

    [Test]
    public void Decode_AtProtocol18_StillReadsServerInfo()
    {
        // **Protocol 18 has no specimen anywhere in this project**, and community demos from that
        // window are rare enough that one may never turn up. The corpus axis stops at what someone
        // recorded; this does not.
        //
        // 17–23 are the gap. If a field width changed in that range this would not know it — but a
        // decoder that cannot READ the era at all is a different and detectable failure, and that
        // is what this pins.
        ServerInfoMessage read = Read(
            SyntheticDemo.Containing(18, Info(map: "cp_gravelpit", protocol: 18)));

        read.NetworkProtocol.ShouldBe((ushort)18);
        read.Map.ShouldBe("cp_gravelpit");
    }

    [Test]
    public void RoundTrip_LongMapName_IsNotTruncated()
    {
        // A community map name of real length, which the committed corpus does not contain — every
        // demo in it is a stock map with a short name. A length field read one bit narrow keeps
        // working on `cp_badlands` and loses the end of this.
        const string Long = "workshop/cp_mossrock_final_v3_beta_candidate.ugc1234567890";

        Read(SyntheticDemo.Containing(Info(map: Long))).Map.ShouldBe(Long);
    }

    /// <summary>A ServerInfo with sensible defaults, overridden per test.</summary>
    private static ServerInfoMessage Info(
        string map = "cp_process_final",
        byte maxPlayers = 24,
        ushort maxClasses = 9,
        float tickRate = 66.67f,
        char platform = 'w',
        bool dedicated = true,
        bool sourceTv = false,
        string skybox = "sky_tf2_04",
        ushort protocol = SyntheticDemo.DefaultProtocol) => new(
            NetworkProtocol: protocol,
            ServerCount: 7,
            IsSourceTv: sourceTv,
            IsDedicated: dedicated,
            MapCrc: 0x9ABC_DEF0,
            MaxClasses: maxClasses,

            // Sixteen bytes, which is the width the message reserves. Distinct values rather than
            // zeroes so a hash read at the wrong offset shows as wrong bytes rather than as more
            // zeroes.
            MapHash: [.. Enumerable.Range(1, 16).Select(value => (byte)value)],
            PlayerSlot: 1,
            MaxPlayers: maxPlayers,

            // The wire carries the interval, and TickRate is derived from it — so the value set
            // here is the reciprocal of the rate the test names.
            IntervalPerTick: 1f / tickRate,
            Platform: platform,
            GameDirectory: "tf",
            Map: map,
            Skybox: skybox,
            ServerName: "synthetic",
            IsReplay: false);

    /// <summary>The single ServerInfo a synthetic demo carries.</summary>
    private static ServerInfoMessage Read(byte[] demo) =>
        SyntheticDemo.MessagesIn(demo).OfType<ServerInfoMessage>().ShouldHaveSingleItem();
}
