using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What one <c>PropsAt</c> costs, split between the sampling and the handing-back.
/// </summary>
/// <remarks>
/// **Written to settle where `sample` goes before rebuilding anything** (B259 fix 3). The plan was
/// per-track change detection, on the reasoning that a static prop should be cheap; but a static
/// prop has ONE keyframe, so `At` early-outs after two searches on a one-element array, and 1.4
/// microseconds a track is far too much for that. The suspicion is the boundary rather than the
/// algorithm: `PropsAt` takes `ICollection&lt;SceneProp&gt;` and `IReadOnlySet&lt;int&gt;`, so every
/// prop costs an interface dispatch copying a sixteen-field struct, plus a second dispatch and a
/// hash lookup. `docs/memory/linq-is-a-test-tool.md` is the same rule from the other side.
///
/// **A probe rather than production instrumentation** (D126), because this is one question about one
/// demo at one tick, and the last attempt to time it inside `PropsAt` was refused by the analyzers
/// for using a mutable static.
///
/// Reports nanoseconds per prop for each half. It asserts nothing — a measurement is not a test.
/// </remarks>
public sealed class SampleCostProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sample-cost";

    /// <inheritdoc/>
    public string Summary =>
        "where PropsAt's time goes, per prop: sample-cost <demo> <tick> [repeats]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("sample-cost <demo> <tick> [repeats]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        if (!int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int tick))
        {
            output.WriteLine($"'{arguments[1]}' is not a tick.");
            return;
        }

        int repeats = arguments.Count > 2
            && int.TryParse(arguments[2], NumberStyles.None, CultureInfo.InvariantCulture, out int asked)
            && asked > 0
                ? asked
                : 500;

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        List<SceneProp> props = [];

        // Warm: the first call pays for JIT and for growing the list, and a measurement that
        // includes those describes the first frame of a run rather than the steady state.
        timeline.PropsAt(tick, props);

        int count = props.Count;

        if (count == 0)
        {
            output.WriteLine($"No props at tick {tick.ToString(CultureInfo.InvariantCulture)}.");
            return;
        }

        // **The whole call, through the interface it really has.**
        long wholeAt = Stopwatch.GetTimestamp();

        for (int run = 0; run < repeats; run++)
        {
            timeline.PropsAt(tick, props);
        }

        long whole = Stopwatch.GetTimestamp() - wholeAt;

        // **The sampling alone**, asked track by track through the concrete type — no interface, no
        // record construction, no list. Whatever separates this from the line above is the cost of
        // the boundary rather than of deciding where anything is.
        long sampledAt = Stopwatch.GetTimestamp();

        for (int run = 0; run < repeats; run++)
        {
            foreach (ScenePropTrack track in timeline.Props)
            {
                _ = track.At(tick);
            }
        }

        long sampled = Stopwatch.GetTimestamp() - sampledAt;

        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}: "
            + $"{count.ToString(CultureInfo.InvariantCulture)} props from "
            + $"{timeline.Props.Count.ToString(CultureInfo.InvariantCulture)} tracks, "
            + $"{repeats.ToString(CultureInfo.InvariantCulture)} repeats");

        output.WriteLine($"  PropsAt whole   {Each(whole, repeats, count):0.0} ns/prop"
            + $"   ({Ms(whole, repeats):0.000} ms a call)");

        output.WriteLine($"  At() alone      {Each(sampled, repeats, count):0.0} ns/prop"
            + $"   ({Ms(sampled, repeats):0.000} ms a call)");

        output.WriteLine($"  the boundary    {Each(whole - sampled, repeats, count):0.0} ns/prop"
            + $"   ({Ms(whole - sampled, repeats):0.000} ms a call)"
            + "   <- record construction, ICollection.Add, IReadOnlySet.Contains");

        // **How much of the work is provably constant.** A track holds every keyframe the demo ever
        // stated for it — nothing is added during playback — so one with a single keyframe answers
        // the same pose at every tick of its life, and its whole `SceneProp` could be built once.
        // This is the size of the prize for caching the finished record rather than re-deriving it.
        int constant = 0;

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.KeyframeCount <= 1)
            {
                constant++;
            }
        }

        output.WriteLine(
            $"  of {timeline.Props.Count.ToString(CultureInfo.InvariantCulture)} tracks, "
            + $"{constant.ToString(CultureInfo.InvariantCulture)} have one keyframe and can never "
            + "change; their pose and their record are constant for the whole demo");
    }

    private static double Each(long ticks, int repeats, int props) =>
        ticks / (double)Stopwatch.Frequency * 1_000_000_000d / (repeats * (double)props);

    private static double Ms(long ticks, int repeats) =>
        ticks / (double)Stopwatch.Frequency * 1000d / repeats;
}
