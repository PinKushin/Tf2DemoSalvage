using System;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>The engine's <c>CAutoGain</c>: per-block level normalisation of one voice channel, scaled by <c>voice_scale</c>.</summary>
/// <remarks>
/// **Read from TF2's x64 engine.dll**; the SDK does not carry the voice engine. <c>Voice_AssignChannel</c>
/// (<c>FUN_180057350</c>) calls <c>Reset</c> (<c>FUN_1800591d0</c>) with <c>128, voice_maxgain, voice_avggain,
/// voice_scale</c> each time a channel is assigned, and <c>Voice_AddIncomingData</c> (<c>FUN_180056eb0</c>) runs every
/// decompressed packet through <c>ProcessSamples</c> (<c>FUN_180058fe0</c>) before it is buffered. Field for field:
///
/// <code>
/// [0] block size 128     [1] max gain   [2] avg-to-max   [3] block offset   [4] block |s| total   [5] block |s| max
/// [6] scale              [7] cur gain   [8] next gain    [9] fixed cur gain (×128, starts 0x80)   [10] fixed step
/// </code>
///
/// **The scale lands twice, and that is the engine's**: a block's current gain is <c>next × scale</c> where <c>next</c>
/// was already <c>min(32767 / level, max) × scale</c>. Ported as found rather than "fixed" (VoiceConformanceTests).
/// </remarks>
public sealed class VoiceAutoGain
{
    private const int BlockSize = 128;

    private readonly float _maxGain;
    private readonly float _avgToMax;
    private readonly float _scale;
    private int _blockOffset;
    private int _blockTotal;
    private int _blockMax;
    private float _nextGain = 1f;
    private int _fixedGain = 0x80;
    private int _fixedStep;

    /// <summary>A channel's gain as <c>Voice_AssignChannel</c> resets it, with the engine's <c>voice_maxgain</c> and <c>voice_avggain</c>.</summary>
    /// <param name="scale"><c>voice_scale</c>.</param>
    public VoiceAutoGain(float scale)
    {
        _maxGain = EngineConVars.ByName("voice_maxgain").Number;
        _avgToMax = EngineConVars.ByName("voice_avggain").Number;
        _scale = scale;
    }

    /// <summary>Applies the gain in place, as <c>ProcessSamples</c> does.</summary>
    /// <param name="samples">16-bit PCM, rewritten.</param>
    public void Process(Span<short> samples)
    {
        while (samples.Length > 0)
        {
            int count = Math.Min(samples.Length, BlockSize - _blockOffset);

            for (int i = 0; i < count; i++)
            {
                int sample = samples[i];
                int magnitude = Math.Abs(sample);

                _blockTotal += magnitude;
                _blockMax = Math.Max(_blockMax, magnitude);

                int scaled = ((_blockOffset * _fixedStep) + _fixedGain) * sample >> 7;
                _blockOffset++;

                samples[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
            }

            samples = samples[count..];

            if (_blockOffset % BlockSize == 0)
            {
                float current = _scale * _nextGain;
                _fixedGain = (int)(current * 128f);

                int average = _blockTotal / BlockSize;
                float next = Math.Min(32767f / (((_blockMax - average) * _avgToMax) + average), _maxGain);

                _blockTotal = 0;
                _blockMax = 0;
                _blockOffset = 0;

                _nextGain = next * _scale;
                _fixedStep = (int)((_nextGain - current) / (BlockSize - 1) * 128f);
            }
        }
    }
}
