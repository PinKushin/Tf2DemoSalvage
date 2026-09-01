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

        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}: "
            + $"{count.ToString(CultureInfo.InvariantCulture)} props from "
            + $"{timeline.Props.Count.ToString(CultureInfo.InvariantCulture)} tracks, "
            + $"{repeats.ToString(CultureInfo.InvariantCulture)} repeats");

        output.WriteLine($"  PropsAt whole   {Each(whole, repeats, count):0.0} ns/prop"
            + $"   ({Ms(whole, repeats):0.000} ms a call)");

        // **The same call, advancing like playback, because stage C made the two different
        // questions** (B259 fix 3). Repeating one tick measures a rebuild where nothing changed —
        // the parked path, all collate. Playback crosses keyframe boundaries and drags the lerp
        // list along, so this steps a quarter tick per call, which at 66.7 ticks a second is
        // roughly a 270 fps cadence. Fresh timeline so the first probe's sampling state does not
        // pre-pay this one's wakes.
        DemoTimeline advancing = DemoTimeline.Build(File.ReadAllBytes(path));

        advancing.PropsAt(tick, props);

        long steppedAt = Stopwatch.GetTimestamp();

        for (int run = 0; run < repeats; run++)
        {
            advancing.PropsAt(tick + ((run + 1) * 0.25d), props);
        }

        long steppedTicks = Stopwatch.GetTimestamp() - steppedAt;

        output.WriteLine($"  PropsAt stepped {Each(steppedTicks, repeats, count):0.0} ns/prop"
            + $"   ({Ms(steppedTicks, repeats):0.000} ms a call, +0.25 tick each)");

        // **A split between `At` and the rest USED to be reported here and has been removed,
        // because it was an instrument that lied.** Timing a bare loop over `track.At(tick)` and
        // subtracting it from the whole call gave 357 ns on one run, 696 on the next, and finally
        // −221 — a negative share of a total that contains it, which is impossible. The two loops
        // touch memory in different orders and the second runs against a warmed cache, so the
        // difference measured the benchmark rather than the code.
        //
        // It was believed long enough to redirect a design. What survives is what does not move
        // between runs: the size of the record, and how many tracks can never change.

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

        // **What a single prop weighs, because the engine's equivalent weighs eight bytes.** Valve
        // passes `C_BaseEntity*` between its systems; every pass here takes `SceneProp` BY VALUE, so
        // the same journey is a memcpy. This is the number that says whether that matters.
        output.WriteLine(
            $"  one SceneProp is "
            + $"{System.Runtime.CompilerServices.Unsafe.SizeOf<SceneProp>().ToString(CultureInfo.InvariantCulture)}"
            + $" bytes (a ScenePose alone is "
            + $"{System.Runtime.CompilerServices.Unsafe.SizeOf<ScenePose>().ToString(CultureInfo.InvariantCulture)}"
            + "); the engine passes an 8-byte pointer");

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
