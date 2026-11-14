using Tf2DemoSalvage.Audio;

namespace Tf2DemoSalvage.Audio.Tests;

/// <summary>
/// The voice overdrive duck as engine.dll's <c>VoiceSE_Idle</c> runs it (B549): while a voice channel is assigned, every
/// sound that is not a voice source is turned down, to half at the defaults, over <c>voice_overdrivefadetime</c>.
/// </summary>
/// <remarks>
/// Read in disassembly, x64 <c>engine.dll</c> build 11191711, 2026-11-14:
/// <code>
/// FUN_180058640  Voice_AssignChannel, from ProcessVoiceData (FUN_1801fba10): on assigning, VoiceSE_StartOverdrive
/// FUN_18005ba20  VoiceSE_StartOverdrive: active (DAT_180521124) = 1
/// FUN_180059380  Voice_Idle(frametime): counts the channels in use; none, VoiceSE_EndOverdrive (FUN_18005b880,
///                active = 0); then VoiceSE_Idle(frametime) — XMM0 from XMM7, the frame time (0x18005981b)
/// FUN_18005b890  VoiceSE_Idle: scale (DAT_180463450) = 0x100. Active: t += frametime, at most fadetime. Not active and
///                t == 0: return. Not active: t -= frametime, at least 0. Then
///                scale = (int)(256 / ((0.5 - 0.5 cos(t / fadetime × π)) × (voice_overdrive - 1) + 1))
/// FUN_180058800  voice_enable 0 in Voice_Idle: scale = 0x100 (FUN_18005ba30), nothing else
/// </code>
/// and in the paint's channel volume (<c>FUN_180041fb0</c>, <c>0x180042032-</c>): the smaller of this scale and the speech
/// ducker's, below 0x100, multiplies a non-voice channel's volumes by scale / 256.
/// </remarks>
public sealed class VoiceOverdriveConformanceTests
{
    private static readonly VoiceOverdriving Defaults = VoiceOverdriving.Engine;

    [Test]
    public void Engine_TheRegisteredDefaults_AreTwoAndFourTenths()
    {
        Defaults.ShouldBe(new VoiceOverdriving(Overdrive: 2f, FadeTime: 0.4f));
    }

    [Test]
    public void Idle_NoVoiceEverAssigned_LeavesTheScaleAtUnity()
    {
        VoiceOverdrive overdrive = new();

        overdrive.Idle(Defaults, assigned: 0, frameTime: 0.1f);

        overdrive.Scale.ShouldBe(256);
    }

    [TestCase(1, 0.4f, 128)]       // cos(π) = -1: 256 / 2
    [TestCase(1, 0.2f, 170)]       // cos(π/2) = 0: 256 / 1.5 = 170.67, truncated
    [TestCase(3, 0.1f, 223)]       // cos(π/4): 0.5 - 0.35355 = 0.14645, 256 / 1.14645 = 223.3; any count above 0 alike
    public void Idle_AVoiceAssigned_RampsTheScaleDownOverTheFadeTime(int assigned, float elapsed, int expected)
    {
        VoiceOverdrive overdrive = new();

        overdrive.Idle(Defaults, assigned, elapsed);

        overdrive.Scale.ShouldBe(expected);
    }

    [Test]
    public void Idle_LongerThanTheFadeTime_HoldsAtHalf()
    {
        VoiceOverdrive overdrive = new();

        overdrive.Idle(Defaults, assigned: 1, frameTime: 0.3f);
        overdrive.Idle(Defaults, assigned: 1, frameTime: 0.3f);

        overdrive.Scale.ShouldBe(128);
    }

    [Test]
    public void Idle_TheLastVoiceFreed_RampsBackUpAndRestsAtUnity()
    {
        VoiceOverdrive overdrive = new();
        overdrive.Idle(Defaults, assigned: 1, frameTime: 0.4f);

        overdrive.Idle(Defaults, assigned: 0, frameTime: 0.2f);
        overdrive.Scale.ShouldBe(170, "halfway back: the same curve");

        overdrive.Idle(Defaults, assigned: 0, frameTime: 0.3f);
        overdrive.Scale.ShouldBe(256);
    }

    [Test]
    public void Idle_APausedFrame_HoldsTheScale()
    {
        // Voice_Idle is passed the host frame time, which a paused demo does not advance.
        VoiceOverdrive overdrive = new();
        overdrive.Idle(Defaults, assigned: 1, frameTime: 0.2f);

        overdrive.Idle(Defaults, assigned: 1, frameTime: 0f);

        overdrive.Scale.ShouldBe(170);
    }

    [Test]
    public void Idle_AnOverdriveOfOne_NeverDucks()
    {
        VoiceOverdrive overdrive = new();

        overdrive.Idle(Defaults with { Overdrive = 1f }, assigned: 1, frameTime: 0.4f);

        overdrive.Scale.ShouldBe(256);
    }

    [Test]
    public void Disable_WhileDucked_RestoresUnityButKeepsTheRamp()
    {
        // FUN_180058800 resets the scale only: the timer is untouched, so the next Idle picks up where it was.
        VoiceOverdrive overdrive = new();
        overdrive.Idle(Defaults, assigned: 1, frameTime: 0.4f);

        overdrive.Disable();
        overdrive.Scale.ShouldBe(256);

        overdrive.Idle(Defaults, assigned: 1, frameTime: 0f);
        overdrive.Scale.ShouldBe(128);
    }
}
