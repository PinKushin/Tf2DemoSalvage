using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many `CHAN_STATIC` sounds a demo starts, one-shots against loops, and what a skip from its start would hand 64 static channels (B550).</summary>
/// <remarks>
/// A skip dispatches only static sounds and stops (`CL_DispatchSound`, engine.dll FUN_18008caf0), and `S_StartStaticSound` has 64
/// channels. This asks whether a skip to the end from the start could fill them. The control is the count of waves read at all:
/// none read means the install was not found, not that the demo has no sound.
/// </remarks>
public sealed class StaticSoundsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "static-sounds";

    /// <inheritdoc/>
    public string Summary => "a demo's CHAN_STATIC starts, one-shots and loops, and the loops still live at its end: static-sounds <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("usage: static-sounds <demo>, with TF2 installed");
            return;
        }

        GameArchives archives = GameArchives.Open(game);
        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(arguments[0]));
        Dictionary<string, SoundSample?> read = new(StringComparer.OrdinalIgnoreCase);
        HashSet<(int Entity, string Name)> live = [];
        int oneShots = 0;
        int loops = 0;
        int stops = 0;
        int unread = 0;
        double oneShotSeconds = 0d;

        foreach (SceneSound sound in timeline.Sounds.Where(static sound => sound.Channel == SoundScript.StaticChannel))
        {
            if (sound.IsStop)
            {
                stops++;
                live.Remove((sound.EntityIndex, sound.Name));
                continue;
            }

            if (!read.TryGetValue(sound.Name, out SoundSample? sample))
            {
                sample = SoundFile.Open("sound/" + SoundName.Parse(sound.Name).Path, archives.Read) is { } file &&
                    SoundSampleReader.Read(file.Bytes) is { Sample: { } decoded }
                    ? decoded
                    : null;
                read[sound.Name] = sample;
            }

            if (sample is not { } opened)
            {
                unread++;
                continue;
            }

            if (opened.Loops)
            {
                loops++;
                live.Add((sound.EntityIndex, sound.Name));
            }
            else
            {
                oneShots++;
                oneShotSeconds += opened.FrameCount / (double)opened.SampleRate;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(arguments[0])}: {timeline.Sounds.Count} sounds; static: {oneShots} one-shots ({oneShotSeconds:0.0} s of sound), " +
            $"{loops} loop starts, {stops} stops, {unread} unread; {live.Count} loops live at the end; waves read {read.Values.Count(static s => s is not null)} of {read.Count}"));
    }
}
