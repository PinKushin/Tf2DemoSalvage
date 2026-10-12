using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every medigun beam alive in a span of ticks, as the timeline's feed made them (B523).</summary>
/// <remarks>
/// **`timeline.HealBeams.All`, which is what `MainForm.AddHealBeams` walks** — so a beam missing here is missing on
/// screen, and one present here and not drawn is the drawing's fault. Written to answer whether the glow on a
/// patient in a TF2 picture is a beam that had only just begun at the tick we shot.
///
/// <code>
///   heal-beams f12tf.dem 63400 63700
/// </code>
/// </remarks>
public sealed class HealBeamsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "heal-beams";

    /// <inheritdoc/>
    public string Summary => "medigun beams alive in a span of ticks, with medic, patient and item: heal-beams <demo> <fromTick> <toTick>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine(Summary);
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int from = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        int shown = 0;

        foreach (SceneHealBeam beam in timeline.HealBeams.All)
        {
            if (beam.Start <= to && (beam.End ?? int.MaxValue) >= from)
            {
                output.WriteLine(beam);
                shown++;
            }
        }

        // The control: a match has hundreds of beams, so a zero total is the feed's fault and not this span's.
        output.WriteLine($"{shown} of {timeline.HealBeams.All.Count} beams touch ticks {from}-{to}");
    }
}
