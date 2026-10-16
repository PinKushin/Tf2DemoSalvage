using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>The voice a timeline keeps for playback: every <c>svc_VoiceData</c> with its speaker, and the codec <c>svc_VoiceInit</c> named.</summary>
/// <remarks>
/// **The speaker is the client slot plus one.** engine.dll (x64), <c>CClientState::ProcessVoiceData</c> at
/// <c>FUN_1801fa0d0</c>: <c>iVar1 = *(int *)(msg + 0x28) + 1</c>, and that <c>iVar1</c> is what reaches
/// <c>GetPlayerInfo</c>, <c>Voice_GetChannel</c> (<c>FUN_180057a60</c>) and <c>Voice_AssignChannel</c>
/// (<c>FUN_180057350</c>) — the entity index the sound is then started from. The proximity flag (<c>msg + 0x2c</c>)
/// is handed to <c>Voice_AssignChannel</c> unchanged.
/// </remarks>
public sealed class SceneVoiceConformanceTests
{
    [Test]
    public void Voice_ADataMessage_IsKeptWithItsTickBodyAndProximity()
    {
        byte[] body = [1, 2, 3, 4];

        DemoTimeline timeline = Watching(new VoiceDataMessage(Client: 2, Proximity: 1, BodyBits: 32, body));

        SceneVoice voice = timeline.Voice.ShouldHaveSingleItem();
        (voice.Tick, voice.Client, voice.Proximity).ShouldBe((7, 2, true));
        voice.Body.ToArray().ShouldBe(body);
    }

    [Test]
    public void Entity_ForClientSlot2_IsEntity3() =>
        new SceneVoice(0, Client: 2, Proximity: false, default).Entity.ShouldBe(3, "ProcessVoiceData: m_nFromClient + 1");

    [Test]
    public void Voice_AnEmptyBody_IsNotKept() =>
        Watching(new VoiceDataMessage(Client: 0, Proximity: 0, BodyBits: 0)).Voice.ShouldBeEmpty();

    [Test]
    public void VoiceInits_AnInit_IsKeptWithItsCodecAndQuality()
    {
        SceneVoiceInit init = Watching(new VoiceInitMessage("vaudio_celt", 255, 22050)).VoiceInits.ShouldHaveSingleItem();

        // The reader overwrites the 255 escape with the rate behind it (VoiceInitMessage), so both read 22050.
        (init.Tick, init.Codec, init.Quality, init.SampleRate).ShouldBe((7, "vaudio_celt", 22050, (int?)22050));
    }

    /// <summary>By stream order: the engine applies each message as it reads it, whatever the tick says.</summary>
    [Test]
    public void VoiceCodecOf_PacketsBeforeAndAfterAnInit_AreNullThenTheInitsCodec()
    {
        DemoTimeline timeline = Watching(
            new VoiceDataMessage(1, 0, 8, new byte[1]),
            new VoiceInitMessage("steam", 5),
            new VoiceDataMessage(1, 0, 8, new byte[1]));

        timeline.Voice.Select(timeline.VoiceCodecOf).ShouldBe([null, "steam"]);
    }

    private static DemoTimeline Watching(params INetMessage[] messages) =>
        DemoTimeline.Build(SyntheticDemo.From(
            SyntheticDemo.DefaultProtocol,
            SyntheticDemo.DataTables(new Core.Schema.DemoSchema([], [])),
            SyntheticDemo.Packet(SyntheticDemo.DefaultProtocol, 7, messages)));
}
