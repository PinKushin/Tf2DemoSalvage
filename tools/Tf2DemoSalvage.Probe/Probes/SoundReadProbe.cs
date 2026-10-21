using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Times a sound's first and second read through the production search path, split into the read and the decode.</summary>
/// <remarks>
/// **Written for the ~395 ms first-play stall** (finding 87): a time that does not vary with a file's length is a wait, not
/// a decode, and this separates the two. Each name is read twice, so the second read is the control on the first: what
/// a cold read costs over a warm one is the first-touch cost, whatever it is.
/// <code>
///   sound-read weapons/draw_primary.wav weapons/fx/nearmiss/bulletLtoR08.wav …
/// </code>
/// </remarks>
public sealed class SoundReadProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sound-read";

    /// <inheritdoc/>
    public string Summary =>
        "a sound's first and second read through the install, read and decode timed apart: sound-read <name>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);

        foreach (string name in arguments)
        {
            for (int pass = 1; pass <= 2; pass++)
            {
                long at = Stopwatch.GetTimestamp();
                (byte[] Bytes, string Path)? opened = SoundFile.Open("sound/" + SoundName.Parse(name).Path, archives.Read);
                double read = Stopwatch.GetElapsedTime(at).TotalMilliseconds;

                at = Stopwatch.GetTimestamp();
                SoundSampleResult decoded = opened is { } file ? SoundSampleReader.Read(file.Bytes) : default;
                double decode = Stopwatch.GetElapsedTime(at).TotalMilliseconds;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{name,-48} pass {pass}: read {read,7:0.0} ms  decode {decode,6:0.0} ms  " +
                    $"{opened?.Bytes.Length ?? -1} bytes, {decoded.Sample?.FrameCount ?? 0} frames"));
            }
        }
    }
}
