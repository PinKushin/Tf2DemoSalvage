using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>The speech ducker's five ConVars, as <c>S_Update</c> reads them each frame (B719).</summary>
/// <param name="Off"><c>snd_ducking_off</c>'s int nonzero: no ducking, the duck held at 1.</param>
/// <param name="ToVolume"><c>snd_ducktovolume</c>: the duck while speech plays.</param>
/// <param name="AttackTime"><c>snd_duckerattacktime</c>: seconds to move the whole range down.</param>
/// <param name="ReleaseTime"><c>snd_duckerreleasetime</c>: seconds to move it back up.</param>
/// <param name="Threshold"><c>snd_duckerthreshold</c>: the loudest speech, as a fraction of 255, that does not duck.</param>
public readonly record struct SpeechDucking(bool Off, float ToVolume, float AttackTime, float ReleaseTime, float Threshold)
{
    /// <summary>engine.dll's registered defaults: off, 0.55, 0.5, 2.5, 0.15.</summary>
    public static SpeechDucking Engine { get; } = new(
        (int)EngineConVars.ByName("snd_ducking_off").Number != 0,
        EngineConVars.ByName("snd_ducktovolume").Number,
        EngineConVars.ByName("snd_duckerattacktime").Number,
        EngineConVars.ByName("snd_duckerreleasetime").Number,
        EngineConVars.ByName("snd_duckerthreshold").Number);
}

/// <summary>
/// The engine's speech ducker: while a voice source plays loudly enough, every other channel is turned down (B719).
/// </summary>
/// <remarks>
/// **Read in disassembly**, x64 engine.dll build 11191711; the addresses are in <c>SpeechDuckerConformanceTests</c>.
/// <c>S_Update</c> (<c>FUN_180030870</c>, <c>0x180030ce0-0x180030f3c</c>) moves <see cref="Duck"/> once a frame and
/// stores <see cref="Scale"/>; the paint's channel volume (<c>FUN_180041fb0</c>, <c>0x180042032-0x1800420a6</c>)
/// multiplies a non-voice channel by <see cref="ChannelGain"/>. A voice source is <c>CAudioSourceVoice</c> (always,
/// <c>0x180027340</c>) or a wave whose sentence says <c>voice_duck</c> (<c>0x180053f10</c>); MP3 sources never are
/// (<c>0x180017cb0</c>), so TF2's MP3 voice lines do not duck.
///
/// The paint takes the smaller of this scale and the voice-overdrive scale (<c>DAT_180463450</c>, <c>FUN_18005b890</c>),
/// which is below 256 while any voice channel is assigned — incoming demo voice included (<see cref="VoiceOverdrive"/>,
/// B549); the output combines the two.
/// </remarks>
public sealed class SpeechDucker
{
    /// <summary>The scale with nothing ducked: 0x100.</summary>
    public const int Unity = 256;

    /// <summary>The duck, 1 at rest (<c>DAT_1804620c8</c>, initialised 1.0).</summary>
    public float Duck { get; private set; } = 1f;

    /// <summary>The duck × 256, truncated, as the paint reads it (<c>DAT_1804620cc</c>, initialised 0x100).</summary>
    public int Scale { get; private set; } = Unity;

    /// <summary>What a non-voice channel's volume is multiplied by: <see cref="Scale"/> / 256 below unity, else 1.</summary>
    public float ChannelGain => Scale < Unity ? Scale * (1f / Unity) : 1f;

    /// <summary>One frame of <c>S_Update</c>'s ducking.</summary>
    /// <param name="ducking">The ConVars.</param>
    /// <param name="speechChannels">How many active channels play a voice source.</param>
    /// <param name="loudest">The largest of their speaker volumes on the engine's 0-255 scale, truncated.</param>
    /// <param name="frameTime">The frame's length in seconds.</param>
    public void Update(SpeechDucking ducking, int speechChannels, int loudest, float frameTime)
    {
        if (ducking.Off)
        {
            Duck = 1f;
            Scale = Unity;
            return;
        }

        float target = 1f;
        int threshold = (int)(ducking.Threshold * 255f);

        if (speechChannels > 0 && Math.Clamp(loudest, 0, 255) > threshold)
        {
            target = ducking.ToVolume;
        }

        float time = target < Duck ? ducking.AttackTime : ducking.ReleaseTime;

        Duck = Approach(target, Duck, (1f - ducking.ToVolume) / time * frameTime);
        Scale = (int)(Duck * 256f);
    }

    /// <summary><c>Approach</c> (<c>FUN_1802780b0</c>): <paramref name="value"/> moved toward <paramref name="target"/> by at most <paramref name="speed"/>.</summary>
    private static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (speed < delta)
        {
            return value + speed;
        }

        return delta < -speed ? value - speed : target;
    }
}
