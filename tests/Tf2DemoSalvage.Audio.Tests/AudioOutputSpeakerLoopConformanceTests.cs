using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A `SND_SPEAKER` start whose wave loops plays nothing, after it has taken its channel (B724).</summary>
/// <remarks>
/// x64 <c>engine.dll</c> build 11191711. <c>S_StartDynamicSound</c> (<c>FUN_18002e6e0</c>) picks its channel
/// (<c>FUN_18002a480</c>), keeps a loop already playing there (<c>0x18002e8a4-0x18002e8df</c>) or frees what the channel
/// held (<c>FUN_180045cc0</c>), sets the channel up and loads its wave, and only then tests bit 3 of <c>+0x13c</c> with the
/// source's IsLooped (<c>0x18002ec0a</c>): "DynamicSound : Speaker ignored looping sound", the channel freed, 0 returned.
/// So the sound the entity's channel was playing has already stopped. <c>S_StartStaticSound</c> (<c>FUN_18002f4c0</c>)
/// makes the same test on a channel that replaced nothing: "StaticSound : Speaker ignored looping sound".
/// </remarks>
[NonParallelizable]
public sealed class AudioOutputSpeakerLoopConformanceTests
{
    private const int Body = 4;
    private const int Static = 6;

    private static readonly SoundPlace ThroughSpeaker = new(null, (0f, 0f, 0f), false) { Speaker = true };

    private static SoundSample Tone(string key, bool loops = false) =>
        AudioOutputDeviceTests.Tone(44100) with { Key = key, Loops = loops };

    [Test]
    public void Play_ALoopThroughASpeakerOnABusyChannel_StopsWhatItHeldAndStartsNothing()
    {
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();
        int held = output.Play(Tone("line"), 1f, 1f, entity: 1, channel: Body);

        output.Play(Tone("hum", loops: true), 1f, 1f, entity: 1, channel: Body, place: ThroughSpeaker).ShouldBe(0);

        output.Refusal.ShouldBe(StartRefusal.SpeakerLoop);
        output.SetVoicePitch(held, 1f).ShouldBeFalse("the pick took the channel before the speaker test freed it");
        output.Playing.ShouldBe(0);
    }

    [Test]
    public void Play_AOneShotThroughASpeaker_Plays()
    {
        // The control: the flag alone refuses nothing.
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();

        output.Play(Tone("line"), 1f, 1f, entity: 1, channel: Body, place: ThroughSpeaker).ShouldNotBe(0);
        output.Playing.ShouldBe(1);
    }

    [Test]
    public void Play_ALoopWithoutTheSpeakerFlag_Plays()
    {
        // The control: a looping wave alone refuses nothing.
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();

        output.Play(Tone("hum", loops: true), 1f, 1f, entity: 1, channel: Body).ShouldNotBe(0);
    }

    [Test]
    public void Play_TheSameLoopThroughASpeakerWhereItAlreadyPlays_KeepsItPlaying()
    {
        // The loop keep returns before the speaker test (0x18002e8a4 against 0x18002ec0a).
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();
        SoundSample hum = Tone("hum", loops: true);
        int first = output.Play(hum, 1f, 1f, entity: 1, channel: Body);

        output.Play(hum, 1f, 1f, entity: 1, channel: Body, place: ThroughSpeaker).ShouldBe(0);

        output.Refusal.ShouldBe(StartRefusal.LoopKept);
        output.SetVoicePitch(first, 1f).ShouldBeTrue();
    }

    [Test]
    public void Play_AStaticLoopThroughASpeaker_StartsNothingAndStopsNothing()
    {
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();
        int playing = output.Play(Tone("line"), 1f, 1f, entity: 1, channel: Static);

        output.Play(Tone("hum", loops: true), 1f, 1f, entity: 1, channel: Static, place: ThroughSpeaker).ShouldBe(0);

        output.Refusal.ShouldBe(StartRefusal.SpeakerLoop);
        output.SetVoicePitch(playing, 1f).ShouldBeTrue("a static start replaces nothing");
    }
}
