using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <c>SND_SPEAKER</c>, bit 6 of a sound's flags (<c>soundflags.h:125</c>), reaches the timeline's sound (B724).
/// </summary>
/// <remarks>
/// <c>env_microphone</c> sets it on what it plays again through its speaker (<c>envmicrophone.cpp:479</c>), the flags
/// field is 11 bits wide (<c>SND_FLAG_BITS_ENCODE</c>), and engine.dll build 11191711 copies the bit into the channel
/// at both starts (<c>FUN_18002e6e0</c>, <c>FUN_18002f4c0</c>: bit 3 of <c>+0x13c</c>), from the flags
/// <c>CL_DispatchSound</c> (<c>FUN_18008caf0</c>) passes unchanged.
/// </remarks>
public sealed class SceneSoundSpeakerFlagConformanceTests
{
    private const ushort Modern = 24;

    [TestCase(1 << 6, true)]
    [TestCase(0, false)]
    [TestCase(1 << 8, false)]
    [TestCase((1 << 6) | (1 << 8), true)]
    public void Build_ASoundsFlagsOnTheWire_CarryTheSpeakerBitAlone(int flags, bool expected)
    {
        DecodedSound sound = new(
            EntityIndex: 55,
            SoundNumber: 0,
            Flags: flags,
            Channel: 6,
            IsAmbient: false,
            IsSentence: false,
            SequenceNumber: 0,
            Volume: 1f,
            SoundLevel: 75,
            Pitch: 100,
            DelaySeconds: 0f,
            OriginX: 0f,
            OriginY: 0f,
            OriginZ: 0f,
            SpeakerEntity: -1,
            Sent: SoundFields.Entity | SoundFields.Flags);

        (byte[] body, int bits) = SoundEncoder.Encode([sound], Modern);

        // After the signon's server info, as the timeline reads it: a packet written without one is read at protocol 0
        // and its sound never arrives. A demo with no dem_datatables builds an empty timeline, so it carries a schema,
        // which nothing here uses.
        ServerInfoMessage serverInfo = ServerInfo();
        DemoTimeline timeline = DemoTimeline.Build(SyntheticDemo.From(
            Modern,
            SyntheticDemo.Packet(Modern, 0, serverInfo),
            SyntheticDemo.DataTables(SyntheticBeam.Schema()),
            SyntheticDemo.PacketAfter(serverInfo, 10, new SoundsMessage(IsReliable: false, Count: 1, BodyBits: bits, Body: body))));

        // The entity index is the control: the sound arrived, and decoded at the width it was written at.
        SceneSound played = timeline.Sounds.ShouldHaveSingleItem();
        played.EntityIndex.ShouldBe(55);
        played.ThroughSpeaker.ShouldBe(expected);
    }

    private static ServerInfoMessage ServerInfo() => new(
        NetworkProtocol: Modern,
        ServerCount: 1,
        IsSourceTv: true,
        IsDedicated: true,
        MapCrc: 0,
        MaxClasses: 1,
        MapHash: new byte[16],
        PlayerSlot: 0,
        MaxPlayers: 2,
        IntervalPerTick: 0.015f,
        Platform: 'w',
        GameDirectory: "tf",
        Map: "cp_process_f12",
        Skybox: "sky_tf2_04",
        ServerName: "synthetic",
        IsReplay: false);
}
