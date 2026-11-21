using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>A sound on <c>CHAN_AUTO</c> is stopped by name, never by key (B782 review rounds 5 and 6).</summary>
/// <remarks>
/// The engine's stop of a patch names its sound — `CSoundPatch::Shutdown` → `StopSound` by name
/// (`soundenvelope.cpp:477-491`), `S_AlterChannel` matching `ch->sfx` (engine.dll FUN_18002aa20, B416) — so it reaches a
/// patch on <c>CHAN_AUTO</c>, which the by-key stop cannot name (the engine picked the channel). The level change's stop
/// of a kept patch takes this path.
/// </remarks>
[NonParallelizable]
public sealed class AudioOutputAutoChannelStopConformanceTests
{
    private const int Auto = SoundScript.AutoChannel;

    [Test]
    public void Silence_ByNameOnTheAutoChannel_StopsTheSound()
    {
        using AudioOutput output = AudioOutputDeviceTests.OpenLoopback();
        IAudioSink sink = output;
        SoundSample hum = AudioOutputDeviceTests.Tone(44100) with { Key = "hum", Loops = true };
        int voice = output.Play(hum, 1f, 1f, entity: 1, channel: Auto);
        voice.ShouldNotBe(0, "the condition: it plays");

        sink.Silence(1, Auto);
        output.Playing.ShouldBe(1, "the control: by key, CHAN_AUTO names nothing");

        sink.Silence(1, Auto, hum).ShouldBe(voice);
        output.Playing.ShouldBe(0);
    }
}
