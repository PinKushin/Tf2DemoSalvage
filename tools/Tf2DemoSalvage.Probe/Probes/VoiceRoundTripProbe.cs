using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Decodes an injected demo's voice and compares the PCM with the WAV that was injected.</summary>
/// <remarks>
/// **Its control**: the WAV against itself must correlate at 1.0, and the WAV against white noise must not
/// (reversing it was tried first and scored 0.996, because the sample is periodic). It asserts nothing — a measurement is not a test (D38). Mono 16-bit PCM WAV only.
/// </remarks>
public sealed class VoiceRoundTripProbe : IProbe
{
    private const int MaxLag = 2400;

    /// <inheritdoc/>
    public string Name => "voice-roundtrip";

    /// <inheritdoc/>
    public string Summary => "decode an injected demo's voice and correlate it with the source WAV: voice-roundtrip <demo> <mono-16-bit.wav>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("voice-roundtrip <demo> <mono-16-bit.wav>");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(arguments[0]));
        List<short> decoded = [];
        using OpusVoiceDecoder decoder = new();
        int packets = 0;
        int rejected = 0;

        foreach (SceneVoice voice in timeline.Voice)
        {
            if (!SteamVoicePayload.TryDecode(voice.Body.Span, out VoicePacket? packet))
            {
                rejected++;
                continue;
            }

            packets++;

            foreach (VoiceChunk chunk in packet.Chunks)
            {
                decoded.AddRange(decoder.Decode(chunk.Data.Span));
            }
        }

        float[] source = ReadWav(arguments[1], output);
        float[] heard = [.. decoded.ConvertAll(sample => sample / 32768f)];

        output.WriteLine($"  packets decoded  {packets} of {timeline.Voice.Count} ({rejected} rejected by TryDecode)");
        output.WriteLine($"  samples          decoded {heard.Length}, source {source.Length}");
        Report(output, "control: source vs itself", source, source);
        uint state = 2463534242;
        float[] noise = new float[source.Length];

        for (int i = 0; i < noise.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            noise[i] = (state / (float)uint.MaxValue * 2) - 1;
        }

        Report(output, "control: source vs white noise", source, noise);
        Report(output, "decoded vs source", heard, source);
    }

    private static void Report(TextWriter output, string label, float[] a, float[] b)
    {
        float best = 0;
        int bestLag = 0;

        for (int lag = -MaxLag; lag <= MaxLag; lag += 8)
        {
            float corr = Correlate(a, b, lag);

            if (Math.Abs(corr) > Math.Abs(best))
            {
                best = corr;
                bestLag = lag;
            }
        }

        output.WriteLine($"  {label,-30} best correlation {best:0.000} at lag {bestLag} samples");
    }

    private static float Correlate(float[] a, float[] b, int lag)
    {
        double dot = 0;
        double energyA = 0;
        double energyB = 0;

        for (int i = 0; i < b.Length; i++)
        {
            int j = i + lag;

            if (j < 0 || j >= a.Length)
            {
                continue;
            }

            dot += a[j] * b[i];
            energyA += a[j] * a[j];
            energyB += b[i] * b[i];
        }

        return energyA == 0 || energyB == 0 ? 0 : (float)(dot / Math.Sqrt(energyA * energyB));
    }

    private static float[] ReadWav(string path, TextWriter output)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int position = 12;
        int rate = 0;
        int channels = 0;
        int bits = 0;

        while (position + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            int size = BitConverter.ToInt32(bytes, position + 4);

            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(bytes, position + 10);
                rate = BitConverter.ToInt32(bytes, position + 12);
                bits = BitConverter.ToInt16(bytes, position + 22);
            }
            else if (id == "data")
            {
                output.WriteLine($"  wav              {rate} Hz, {channels} ch, {bits}-bit");
                int count = size / 2;
                float[] samples = new float[count];

                for (int i = 0; i < count; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, position + 8 + (i * 2)) / 32768f;
                }

                return rate == OpusVoiceDecoder.SampleRate ? samples : Resample(samples, rate);
            }

            position += 8 + size + (size & 1);
        }

        throw new InvalidDataException("no data chunk");
    }

    private static float[] Resample(float[] samples, int rate)
    {
        float[] output = new float[(int)((long)samples.Length * OpusVoiceDecoder.SampleRate / rate)];

        for (int i = 0; i < output.Length; i++)
        {
            double at = (double)i * rate / OpusVoiceDecoder.SampleRate;
            int index = (int)at;
            double fraction = at - index;
            float next = index + 1 < samples.Length ? samples[index + 1] : samples[index];
            output[i] = (float)((samples[index] * (1 - fraction)) + (next * fraction));
        }

        return output;
    }
}
