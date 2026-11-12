using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The speech ducker as engine.dll's <c>S_Update</c> runs it (B719), and the scale its paint loop applies to a channel.
/// </summary>
/// <remarks>
/// Read in disassembly, x64 <c>engine.dll</c> build 11191711, <c>FUN_180030870</c>:
/// <code>
/// 0x180030ce0-0x180030d69  per active channel: SND_Spatialize (FUN_180029440); if its source's IsVoiceSource (vtable
///                          +0x30), count it and keep the largest (int)max(0, its twelve speaker volumes)
/// 0x180030e97              snd_ducking_off's int nonzero: duck = 1.0 (0x18035e93c), scale = 0x100
/// 0x180030ea8-0x180030ee4  target = 1.0; with a speech channel counted, the loudest clamped to 0..255 (negative to 0)
///                          and over (int)(snd_duckerthreshold * 255.0) (0x180361434): target = snd_ducktovolume
/// 0x180030ee7-0x180030f02  time = snd_duckerattacktime when target &lt; duck, else snd_duckerreleasetime
/// 0x180030f07-0x180030f16  duck = Approach(target, duck, (1.0 - snd_ducktovolume) / time * frametime) (FUN_1802780b0)
/// 0x180030f21-0x180030f3c  scale = (int)(duck * 256.0) (0x180361438)
/// </code>
/// and in the paint's channel volume (<c>FUN_180041fb0</c>, <c>0x180042032-0x1800420a6</c>): below 0x100, a channel
/// whose source is not a voice source has its volumes multiplied by scale × 1/256 (<c>0x1803622b0</c>). Both start at
/// 1.0 and 0x100 (<c>.data 0x1804620c8</c>). Every prediction is with TF2's defaults (0.55, 0.5, 2.5, 0.15) at a 0.1 s
/// frame, where an attack moves 0.09 a frame and a release 0.018.
/// </remarks>
public sealed class SpeechDuckerConformanceTests
{
    private const float Frame = 0.1f;

    private static readonly SpeechDucking On = SpeechDucking.Engine with { Off = false };

    [Test]
    public void Engine_TheRegisteredDefaults_AreOffWithTf2sFourValues()
    {
        SpeechDucking.Engine.ShouldBe(new SpeechDucking(Off: true, ToVolume: 0.55f, AttackTime: 0.5f, ReleaseTime: 2.5f, Threshold: 0.15f));
    }

    [Test]
    public void Update_ASpeechChannelAboveThreshold_AttacksToTheDuckVolumeAt009AFrame()
    {
        SpeechDucker ducker = new();
        (float Duck, int Scale, float Gain)[] expected =
        [
            (0.91f, 232, 0.90625f),
            (0.82f, 209, 0.81640625f),
            (0.73f, 186, 0.7265625f),
            (0.64f, 163, 0.63671875f),
            (0.55f, 140, 0.546875f),
            (0.55f, 140, 0.546875f),
        ];

        foreach ((float duck, int scale, float gain) in expected)
        {
            ducker.Update(On, speechChannels: 1, loudest: 200, Frame);

            ducker.Duck.ShouldBe(duck, 1e-5f);
            ducker.Scale.ShouldBe(scale);
            ducker.ChannelGain.ShouldBe(gain);
        }
    }

    [Test]
    public void Update_TheSpeechEnding_ReleasesAt0018AFrameByTheReleaseTime()
    {
        SpeechDucker ducker = Ducked();
        (float Duck, int Scale)[] expected = [(0.568f, 145), (0.586f, 150), (0.604f, 154)];

        foreach ((float duck, int scale) in expected)
        {
            ducker.Update(On, speechChannels: 0, loudest: 0, Frame);

            ducker.Duck.ShouldBe(duck, 1e-5f);
            ducker.Scale.ShouldBe(scale);
        }
    }

    [TestCase(38, 1f)]
    [TestCase(39, 0.91f)]
    public void Update_TheLoudestAgainstTheThreshold_DucksOnlyAbove38(int loudest, float duck)
    {
        // (int)(0.15 × 255) = (int)38.25 = 38, and the test is strictly above it.
        SpeechDucker ducker = new();

        ducker.Update(On, speechChannels: 1, loudest, Frame);

        ducker.Duck.ShouldBe(duck, 1e-5f);
    }

    [Test]
    public void Update_NoSpeechChannelCounted_DoesNotDuckWhateverTheLoudest()
    {
        SpeechDucker ducker = new();

        ducker.Update(On, speechChannels: 0, loudest: 200, Frame);

        ducker.Duck.ShouldBe(1f);
        ducker.Scale.ShouldBe(SpeechDucker.Unity);
    }

    [Test]
    public void Update_ALoudestAbove255_IsClampedBeforeTheThreshold()
    {
        // Threshold 1.1 is (int)280.5 = 280: 300 unclamped would duck, 255 does not.
        SpeechDucker ducker = new();

        ducker.Update(On with { Threshold = 1.1f }, speechChannels: 1, loudest: 300, Frame);

        ducker.Duck.ShouldBe(1f);
    }

    [Test]
    public void Update_ANegativeLoudest_IsTakenAsZero()
    {
        // Threshold -0.1 is (int)-25.5 = -25: -30 unclamped would not duck, 0 does.
        SpeechDucker ducker = new();

        ducker.Update(On with { Threshold = -0.1f }, speechChannels: 1, loudest: -30, Frame);

        ducker.Duck.ShouldBe(0.91f, 1e-5f);
    }

    [Test]
    public void Update_DuckingTurnedOffWhileDucked_IsUnityAtOnce()
    {
        SpeechDucker ducker = Ducked();

        ducker.Update(SpeechDucking.Engine, speechChannels: 1, loudest: 200, Frame);

        ducker.Duck.ShouldBe(1f);
        ducker.Scale.ShouldBe(SpeechDucker.Unity);
        ducker.ChannelGain.ShouldBe(1f);
    }

    [Test]
    public void Update_TheEngineDefaultOff_NeverDucks()
    {
        SpeechDucker ducker = new();

        ducker.Update(SpeechDucking.Engine, speechChannels: 3, loudest: 255, Frame);

        ducker.Duck.ShouldBe(1f);
        ducker.ChannelGain.ShouldBe(1f);
    }

    /// <summary>Ducked all the way, at 0.55 and 140.</summary>
    private static SpeechDucker Ducked()
    {
        SpeechDucker ducker = new();

        for (int frame = 0; frame < 6; frame++)
        {
            ducker.Update(On, speechChannels: 1, loudest: 200, Frame);
        }

        ducker.Scale.ShouldBe(140, "the control: ducked before the case runs");

        return ducker;
    }
}
