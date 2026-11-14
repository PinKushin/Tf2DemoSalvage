using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>When a sound sent with `SND_DELAY` starts on the mixer's clock (B732).</summary>
/// <remarks>
/// x64 <c>engine.dll</c> build 11191711. <c>CL_DispatchSound</c> (<c>FUN_18008caf0</c>) hands a sound with `SND_DELAY`
/// (0x10) whose delay is above -0.1 to <c>S_ComputeDelayForSoundtime</c> (<c>FUN_18002b110</c>) with
/// <c>CLOCK_SYNC_SERVER</c> (1), the sound's time being the last server tick's (<c>DAT_1805414c0</c>, written at
/// <c>0x180141771</c> as the tick count × the tick interval) + its delay − (ticks this frame − 1) × the interval; and a
/// result at or below 0 becomes 1e-6. <c>S_ComputeDelayForSoundtime</c>:
/// <code>
/// if (base[i] == 0) { paintedBase[i] = paintedtime; base[i] = soundtime - interval; }   // DAT_1804a7570, DAT_1804a7578
/// delay = ((int)(rate * (soundtime - base[i])) - paintedtime + paintedBase[i]) / (float)rate;   // rate: shm +0x18
/// if (soundtime - base[i] &gt;= 0 &amp;&amp; |delay| &lt;= 0.2) return delay + snd_delay_sound_shift;
/// base[i] = 0; paintedBase[i] = 0; return 0;
/// </code>
/// The base is also cleared by <c>FUN_180030850</c>, which the mixer's channel list (<c>FUN_180040420</c>) calls for the
/// server clock whenever no active channel was started with a delay from the server (flags 0x20 and 0x40 at +0x13c).
/// </remarks>
public sealed class ServerSoundClockConformanceTests
{
    private const int Rate = 44100;
    private const float Interval = 0.015f;

    [Test]
    public void Shift_Default_Is001()
    {
        // FUN_180001960: ConVar("snd_delay_sound_shift", 0x18035fdc0 = "0.01", flags 0).
        EngineConVars.ByName("snd_delay_sound_shift").Default.ShouldBe("0.01");
        ServerSoundClock.Shift.ShouldBe(0.01f);
    }

    [Test]
    public void Delay_FirstSound_IsOneTickOnTheMixerPlusTheShift()
    {
        ServerSoundClock clock = new();

        // base = 10 - 0.015; (int)(44100 × 0.015) = 661 samples, the painted time just latched.
        clock.Delay(new ServerSoundTime(10f, Interval), painted: 5000, Rate)
            .ShouldBe((661f / Rate) + 0.01f, 1e-6f);
    }

    [Test]
    public void Delay_ALaterSoundAsTheMixerAdvances_IsItsServerTimeLessTheMixersProgress()
    {
        ServerSoundClock clock = new();
        _ = clock.Delay(new ServerSoundTime(10f, Interval), painted: 5000, Rate);

        // 0.115 s of server time past the base (5071 samples, truncated) against 2205 painted since the latch.
        float expected = (((int)(Rate * (10.1f - (10f - Interval))) - 2205) / (float)Rate) + 0.01f;

        clock.Delay(new ServerSoundTime(10.1f, Interval), painted: 5000 + 2205, Rate).ShouldBe(expected, 1e-6f);
        expected.ShouldBe(0.075f, 0.0001f, "the control: about 65 ms ahead of the mixer, plus 10");
    }

    [TestCase(10.3f)] // more than a fifth of a second ahead
    [TestCase(9.9f)] // before the base
    public void Delay_OutOfStep_ResetsAndIsZero(float soundTime)
    {
        ServerSoundClock clock = new();
        _ = clock.Delay(new ServerSoundTime(10f, Interval), painted: 0, Rate);

        clock.Delay(new ServerSoundTime(soundTime, Interval), painted: 0, Rate).ShouldBe(0f);

        // Reset: the next sound latches the clock afresh, one tick ahead.
        clock.Delay(new ServerSoundTime(50f, Interval), painted: 9000, Rate).ShouldBe((661f / Rate) + 0.01f, 1e-6f);
    }

    [Test]
    public void Delay_AUserShift_IsAddedInPlaceOfTheDefault()
    {
        ServerSoundClock clock = new();

        clock.Delay(new ServerSoundTime(10f, Interval, Shift: 0.05f), painted: 0, Rate).ShouldBe((661f / Rate) + 0.05f, 1e-6f);
    }

    /// <remarks>Past an int of painted samples (13.5 hours at 44100), the latch's painted base must not wrap.</remarks>
    [Test]
    public void Delay_PastAnIntOfPaintedTime_KeepsItsBase()
    {
        ServerSoundClock clock = new();
        const long late = 14L * 3600 * Rate;
        _ = clock.Delay(new ServerSoundTime(10f, Interval), late, Rate);

        float expected = (((int)(Rate * (10.1f - (10f - Interval))) - 2205) / (float)Rate) + 0.01f;

        clock.Delay(new ServerSoundTime(10.1f, Interval), late + 2205, Rate).ShouldBe(expected, 1e-6f);
    }

    [Test]
    public void Reset_ThenASound_LatchesAfresh()
    {
        ServerSoundClock clock = new();
        _ = clock.Delay(new ServerSoundTime(10f, Interval), painted: 0, Rate);

        clock.Delay(new ServerSoundTime(50f, Interval), painted: 0, Rate).ShouldBe(0f, "the control: 40 s off, unlatched");
        _ = clock.Delay(new ServerSoundTime(10f, Interval), painted: 0, Rate);
        clock.Reset();

        clock.Delay(new ServerSoundTime(50f, Interval), painted: 0, Rate).ShouldBe((661f / Rate) + 0.01f, 1e-6f);
    }
}
