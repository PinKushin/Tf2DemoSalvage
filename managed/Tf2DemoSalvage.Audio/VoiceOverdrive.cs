using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>The voice overdrive duck's two ConVars, as <c>VoiceSE_Idle</c> reads them each frame (B549).</summary>
/// <param name="Overdrive"><c>voice_overdrive</c>: what every other sound is divided by while voice plays.</param>
/// <param name="FadeTime"><c>voice_overdrivefadetime</c>: seconds to ramp in or out.</param>
public readonly record struct VoiceOverdriving(float Overdrive, float FadeTime)
{
    /// <summary>engine.dll's registered defaults: 2 and 0.4.</summary>
    public static VoiceOverdriving Engine { get; } = new(
        EngineConVars.ByName("voice_overdrive").Number,
        EngineConVars.ByName("voice_overdrivefadetime").Number);
}

/// <summary>
/// The engine's voice overdrive: while any voice channel is assigned, every sound that is not a voice source is turned
/// down by <c>voice_overdrive</c>, on a raised-cosine ramp (B549).
/// </summary>
/// <remarks>
/// **Read in disassembly**, x64 engine.dll build 11191711; the addresses are in <c>VoiceOverdriveConformanceTests</c>.
/// <c>Voice_AssignChannel</c> turns it on for every incoming speaker, so demo voice chat ducks the game; the paint takes
/// the smaller of <see cref="Scale"/> and the speech ducker's (<see cref="SpeechDucker"/>).
/// </remarks>
public sealed class VoiceOverdrive
{
    /// <summary>The ramp's time, 0 to the fade time (<c>DAT_180521120</c>).</summary>
    private float _time;

    /// <summary>The scale the paint reads, 0x100 at rest (<c>DAT_180463450</c>).</summary>
    public int Scale { get; private set; } = SpeechDucker.Unity;

    /// <summary>
    /// One frame of <c>Voice_Idle</c>'s overdrive: the end when no channel is assigned, then <c>VoiceSE_Idle</c>.
    /// </summary>
    /// <param name="overdriving">The ConVars.</param>
    /// <param name="assigned">How many voice channels are in use, their pad included.</param>
    /// <param name="frameTime">The host frame time: demo time, nothing while paused.</param>
    /// <remarks>
    /// <c>Voice_AssignChannel</c> sets the flag the moment it assigns, and only <c>Voice_Idle</c> clears it, when it counts
    /// no channel — so the flag (<c>DAT_180521124</c>) at this point is "a channel is assigned", and is not kept here.
    /// </remarks>
    public void Idle(VoiceOverdriving overdriving, int assigned, float frameTime)
    {
        Scale = SpeechDucker.Unity;

        if (assigned > 0)
        {
            _time += frameTime;

            if (overdriving.FadeTime <= _time)
            {
                _time = overdriving.FadeTime;
            }
        }
        else
        {
            // `== 0.0` in the engine; the time is never below zero, so this is the same test.
            if (_time <= 0f)
            {
                return;
            }

            _time -= frameTime;

            if (_time <= 0f)
            {
                _time = 0f;
            }
        }

        double cos = Math.Cos(_time / overdriving.FadeTime * 3.1415926535);
        Scale = (int)(256f / ((float)(0.5 - cos * 0.5) * (overdriving.Overdrive - 1f) + 1f));
    }

    /// <summary><c>voice_enable</c> 0: the scale back to unity, the ramp left where it was (<c>FUN_18005ba30</c>).</summary>
    public void Disable() => Scale = SpeechDucker.Unity;
}
