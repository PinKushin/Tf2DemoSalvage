using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A demo's voice through the production mixer with the speech ducker on, and the gain it applied (B719).</summary>
/// <remarks>
/// **The production path end to end:** the demo's <see cref="VoicePlan"/>, <see cref="VoicePresenter"/> starting each
/// spurt as the viewer does, into a real <see cref="AudioOutput"/> on an OpenAL Soft loopback device rendered tick by
/// tick, stepped by <see cref="AudioOutput.Duck"/> as <c>PlaySoundsOver</c> steps it. A looping non-speech tone plays
/// throughout as the subject; its gain is READ BACK FROM THE DEVICE each tick, the value the mixer used.
///
/// <code>
///   speech-duck [demo]        — default z1800, which carries voice
/// </code>
/// </remarks>
public sealed class SpeechDuckProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "speech-duck";

    /// <inheritdoc/>
    public string Summary =>
        "a demo's voice through the production mixer with snd_ducking_off 0, and the duck it applied: speech-duck [demo]";

    private const int Rate = 44100;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (DemoCorpus.Find(arguments.Count > 0 ? arguments[0] : "z1800", output) is not { } path)
        {
            output.WriteLine("demo not found");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        VoicePlanResult plan = VoicePlan.From(timeline.VoiceInits, timeline.Voice, timeline.IntervalPerTick);
        output.WriteLine($"{Path.GetFileName(path)}: {plan.Spurts.Count} spurts{(plan.Refusal is { } r ? "; " + r : string.Empty)}");

        foreach (bool off in new[] { true, false })
        {
            Play(output, timeline, plan, SpeechDucking.Engine with { Off = off });
        }
    }

    private static void Play(TextWriter output, DemoTimeline timeline, VoicePlanResult plan, SpeechDucking ducking)
    {
        using AudioOutput? device = AudioOutput.TryCreateLoopback(Rate, NullLogger.Instance);

        if (device is null)
        {
            output.WriteLine("no OpenAL loopback device");
            return;
        }

        int view = timeline.RecorderEntityIndex ?? 0;
        using VoicePresenter voice = new()
        {
            Plan = plan.Spurts,
            Interval = timeline.IntervalPerTick,
            Renderer = render => System.Threading.Tasks.Task.FromResult(render()),
        };

        // The subject: a second of tone, looping, on an entity no voice uses.
        float[] tone = new float[Rate];

        for (int at = 0; at < tone.Length; at++)
        {
            tone[at] = MathF.Sin(at * 0.05f) * 0.25f;
        }

        int subject = device.Play(new SoundSample(Rate, 1, tone, Loops: true), 1f, 1f, gain: 1f, entity: -1, channel: 1);
        int frames = (int)Math.Round(Rate * (double)timeline.IntervalPerTick);
        int first = timeline.Frames[0].Tick;
        int last = timeline.Frames[^1].Tick;
        int ducked = 0;
        int firstDucked = -1;
        float lowest = 1f;
        int lowestAt = -1;

        for (int tick = first; tick <= last; tick++)
        {
            voice.Update(device, tick, jumped: false, (0f, 0f, 0f), (0f, -1f, 0f), (tick - first) * (double)timeline.IntervalPerTick, view, _ => null);
            device.Duck(ducking, timeline.IntervalPerTick);
            _ = device.Render(frames);

            float used = device.GainOf(subject);

            if (used < 1f)
            {
                ducked++;
                firstDucked = firstDucked < 0 ? tick : firstDucked;
            }

            if (used < lowest)
            {
                lowest = used;
                lowestAt = tick;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"snd_ducking_off {(ducking.Off ? 1 : 0)}: subject gain read back below 1 on {ducked} of {last - first + 1} ticks" +
            $" (first at {firstDucked}); lowest {lowest:0.######} at tick {lowestAt}"));
    }
}
