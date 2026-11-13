using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A demo's sounds through the production mixer, and each channel's volume ramp as the device held it (B724).</summary>
/// <remarks>
/// **The production path:** <see cref="SoundPresenter"/> over the demo's schedule with TF2's own sounds, the ears at the
/// recorder's view each tick, into a real <see cref="AudioOutput"/> on an OpenAL Soft loopback device, stepped by
/// <see cref="AudioOutput.Update"/> as <c>PlaySoundsOver</c> steps it, one tick a frame. Each tick, every voice's volume
/// is read from the mixer's own state (<c>VolumeOf</c>, what <c>Update</c> stepped) and its gain back from the device.
///
/// The control is the count of re-aims after a voice's first frame: none means the demo changed no playing channel's
/// volume, and a zero ramp count would then say nothing about the ramp.
/// <code>
///   volume-ramp [demo]        — default z1800
/// </code>
/// </remarks>
public sealed class VolumeRampProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "volume-ramp";

    /// <inheritdoc/>
    public string Summary =>
        "a demo's sounds through the production mixer, and each channel's volume ramp read from it each tick: volume-ramp [demo]";

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

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("TF2 is not installed: no sounds to play");
            return;
        }

        using AudioOutput? device = AudioOutput.TryCreateLoopback(Rate, NullLogger.Instance);

        if (device is null)
        {
            output.WriteLine("no OpenAL loopback device");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        SoundCache sounds = new(NullLogger.Instance);
        sounds.Level(GameArchives.Open(game).Read);
        ActiveLoops loops = new();
        SoundPresenter presenter = new(new SoundscapeSystem(loops, sounds.Sample, NullLogger.Instance), loops, sounds.Sample, NullLogger.Instance)
        {
            Loading = sounds.Loading,
            Held = sounds.HeldSample,
            Schedule = new SoundSchedule(timeline.Sounds),
        };

        float interval = timeline.IntervalPerTick;
        int frames = (int)Math.Round(Rate * (double)interval);
        int first = timeline.Frames[0].Tick;
        int last = timeline.Frames[^1].Tick;
        // Each ear's volume is its own channel speaker (B724), so each is followed on its own.
        Dictionary<(int Voice, bool Right), (float Current, int Target)> seen = [];
        int viewed = 0;
        int started = 0;
        int startedOffTarget = 0;
        int reaimed = 0;
        int rampFrames = 0;
        int fullSteps = 0;
        int snapped = 0;
        float largest = 0f;
        string? example = null;

        for (int tick = first; tick <= last; tick++)
        {
            RecordedView? view = timeline.RecordedViewAt(tick);
            viewed += view is null ? 0 : 1;
            (float X, float Y, float Z) ears = view?.ViewOrigin ?? (0f, 0f, 0f);
            float yaw = (view?.ViewAngles.Yaw ?? 0f) * MathF.PI / 180f;

            _ = presenter.Update(device, tick, ears, (MathF.Sin(yaw), -MathF.Cos(yaw), 0f), (tick - first) * (double)interval);
            device.Update(SpeechDucking.Engine, interval);
            _ = device.Render(frames);

            foreach ((int voice, bool right) in device.Voices.SelectMany(static voice => new[] { (voice, false), (voice, true) }).ToArray())
            {
                SpeakerVolume volume = right ? device.VolumesOf(voice).Right : device.VolumesOf(voice).Left;

                if (!seen.TryGetValue((voice, right), out (float Current, int Target) before))
                {
                    started++;
                    startedOffTarget += volume.Current.CompareTo(volume.Target) != 0 ? 1 : 0;
                }
                else
                {
                    reaimed += volume.Target != before.Target ? 1 : 0;
                    float step = Math.Abs(volume.Current - before.Current);

                    if (volume.Current.CompareTo(volume.Target) != 0)
                    {
                        rampFrames++;
                        fullSteps += step.CompareTo(20f) == 0 ? 1 : 0;
                        example ??= string.Create(
                            CultureInfo.InvariantCulture,
                            $"tick {tick} voice {voice} {(right ? "right" : "left")} ear: {before.Current:0.###} -> {volume.Current:0.###} toward {volume.Target}, device gain {(right ? device.GainsOf(voice).Right : device.GainsOf(voice).Left):0.######}");
                    }
                    else if (step > 0f)
                    {
                        snapped++;
                    }

                    largest = Math.Max(largest, step);
                }

                seen[(voice, right)] = (volume.Current, volume.Target);
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: {timeline.Sounds.Count} sounds ({timeline.Sounds.Count(static s => s.ChangesVolume)} SND_CHANGE_VOL) " +
            $"over {last - first + 1} ticks, {viewed} with a recorded view; {started} ears started (two a voice), " +
            $"{startedOffTarget} off their target on their first frame; {reaimed} ear re-aims after the first frame (the control); " +
            $"{rampFrames} ear-frames mid-ramp, {fullSteps} of them a full 20-unit step; {snapped} landings; largest step {largest:0.###}"));
        output.WriteLine(example ?? "no voice was seen mid-ramp");
    }
}
