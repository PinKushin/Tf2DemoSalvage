using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The B602 hybrid's margin and equivalence: how far the cheap speeds stray from prediction's, and what it costs.</summary>
/// <remarks>
/// **Fit, then test on other landings**: the margin is read off one set of seeks and the equivalence counted on a disjoint
/// set, so it is not graded on its own sample. The number that sets the margin is the largest |cheap − 0.5| over probes
/// where cheap and exact pick DIFFERENT activities — the distribution of |exact − cheap| is reported beside it.
/// `docs/measurements/2026-10-26-pov-recorder-seek-scan.md`.
/// </remarks>
public sealed class RecorderSpeedGateProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "recorder-speed-gate";

    /// <inheritdoc/>
    public string Summary => "B602 hybrid: cheap-vs-predicted speed error, margin, equivalence and ms: recorder-speed-gate <demo> [seeks] [margin]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        // The margin is validated before the demo is opened, so a typo costs nothing.
        if (!ProbeArguments.TryDemo(arguments, Summary, output, out string path) ||
            !ProbeArguments.TryCount(arguments, 1, 40, 2, output, out int seeks) ||
            !ProbeArguments.TryNonNegative(arguments, 2, output, out float? asked) ||
            PovMatch.Open(path, output) is not { } match)
        {
            return;
        }

        RecorderGate gate = new(match);

        // Fit: the exact route's probes on seek-scan's own landings.
        RouteRun fit = gate.Run(RecorderRoute.Exact, gate.Landings(seeks, 0d));
        List<RecorderSpeeds> speeds = [.. fit.ProbeTicks.Select(gate.Speeds).OfType<RecorderSpeeds>()];

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)}: recorder {match.Recorder}; fit {seeks} landings, {fit.ProbeTicks.Count} recorder probes, {speeds.Count} viewed and alive; " +
            $"item hooks {(match.Game.Weapons.Items is null ? "ABSENT" : "wired")}, ground surfaces {(match.Map.ImpactDecals is null ? "ABSENT" : "wired")}"));

        if (speeds.Count == 0)
        {
            output.WriteLine("error: control failed: no probe had the recorder viewed and alive.");
            Environment.ExitCode = 1;
            return;
        }

        float needed = 0f;

        foreach ((string name, Func<RecorderSpeeds, float> cheap) in new (string, Func<RecorderSpeeds, float>)[]
                 {
                     ("networked", s => s.Networked),
                     ("track", s => s.Track),
                 })
        {
            float[] error = [.. speeds.Select(s => MathF.Abs(s.Exact - cheap(s))).Order()];
            RecorderSpeeds[] flips = [.. speeds.Where(s => !RecorderGate.SameSide(s.Exact, cheap(s)))];
            float flipMargin = flips.Length == 0 ? 0f : flips.Max(s => MathF.Abs(cheap(s) - PlayerActivityState.MovingMinimumSpeed));
            needed = name == "networked" ? flipMargin : needed;

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {name}: |exact - cheap| mean {error.Average():0.###}, median {Quantile(error, 0.5):0.###}, p99 {Quantile(error, 0.99):0.###}, max {error[^1]:0.###} u/s; " +
                $"{flips.Length} probes on the other side of 0.5, the farthest |cheap - 0.5| among them {flipMargin:0.###} u/s"));

            // Where the flips sit: the margin each would need, and how many predicted "still" against a moving cheap speed.
            float[] flipNeeds = [.. flips.Select(s => MathF.Abs(cheap(s) - PlayerActivityState.MovingMinimumSpeed)).Order()];

            if (flipNeeds.Length > 0)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"    flips' needed margin: median {Quantile(flipNeeds, 0.5):0.###}, p90 {Quantile(flipNeeds, 0.9):0.###}, p99 {Quantile(flipNeeds, 0.99):0.###}; " +
                    $"over 10: {flipNeeds.Count(need => need > 10f)}, over 50: {flipNeeds.Count(need => need > 50f)}, over 300: {flipNeeds.Count(need => need > 300f)}; " +
                    $"exact still and cheap moving: {flips.Count(s => s.Exact <= PlayerActivityState.MovingMinimumSpeed)}, the reverse: {flips.Count(s => s.Exact > PlayerActivityState.MovingMinimumSpeed)}"));
                // Which side was right: the server's own answer is the networked speed the NEXT packet brings.
                int truthWithExact = 0;
                int truthWithCheap = 0;

                foreach (RecorderSpeeds flip in flips)
                {
                    if (gate.NextPacketSpeed(flip.Tick) is { } truth)
                    {
                        truthWithExact += RecorderGate.SameSide(truth, flip.Exact) ? 1 : 0;
                        truthWithCheap += RecorderGate.SameSide(truth, cheap(flip)) ? 1 : 0;
                    }
                }

                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"    against the next packet's networked speed: {truthWithExact} flips side with exact, {truthWithCheap} with {name}"));

                // And the frame: whether the viewer DRAWS him at exact's speed there (the scan's reference) or the cheap one.
                IReadOnlyDictionary<int, float> drawn = gate.FrameSpeeds(flips.Select(flip => flip.Tick));
                int frameWithExact = flips.Count(flip => drawn.TryGetValue(flip.Tick, out float at) && RecorderGate.SameSide(at, flip.Exact));
                int frameWithCheap = flips.Count(flip => drawn.TryGetValue(flip.Tick, out float at) && RecorderGate.SameSide(at, cheap(flip)));
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"    the frame's drawn speed there: {frameWithExact} flips side with exact, {frameWithCheap} with {name}, {flips.Count(flip => !drawn.ContainsKey(flip.Tick))} not drawn alive"));
                output.WriteLine("    worst five (tick: exact, networked, track): " + string.Join("; ", flips
                    .OrderByDescending(s => MathF.Abs(cheap(s) - PlayerActivityState.MovingMinimumSpeed))
                    .DistinctBy(s => s.Tick)
                    .Take(5)
                    .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Tick}: {s.Exact:0.##}, {s.Networked:0.##}, {s.Track:0.##}"))));
            }
        }

        // The plan's rule: the fit's farthest flip, times four, at least 10 u/s; or the margin asked for.
        float margin = asked ?? MathF.Max(10f, needed * 4f);

        foreach (float swept in new[] { 0f, 10f, 50f, 100f, 300f, needed, margin }.Distinct())
        {
            int wrong = speeds.Count(s => RecorderGate.Decides(s.Networked, swept) && !RecorderGate.SameSide(s.Exact, s.Networked));
            int predicts = speeds.Count(s => !RecorderGate.Decides(s.Networked, swept));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  margin {swept:0.###}: {wrong} fit probes decided wrong, {predicts} of {speeds.Count} ({100d * predicts / speeds.Count:0.0}%) predict"));
        }

        gate.Margin = margin;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  test half: the hybrid emulated at margin {margin:0.###} u/s"));

        // Test: other landings, half a stride on; every route, timed; then the untimed comparisons.
        int[] test = gate.Landings(seeks, 0.5d);
        Dictionary<RecorderRoute, RouteRun> runs = Enum.GetValues<RecorderRoute>().ToDictionary(route => route, route => gate.Run(route, test));
        RouteRun reference = runs[RecorderRoute.Exact];

        foreach ((RecorderRoute route, RouteRun run) in runs)
        {
            int differ = run.Sequences.Zip(reference.Sequences).Count(pair => pair.First != pair.Second);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  test {route}: recorder probes per seek mean {run.RecorderMs.Skip(1).Average():0.000} ms, max {run.RecorderMs.Skip(1).Max():0.000} ms; " +
                $"{run.ProbeTicks.Count} probes; {differ} of {test.Length} landings chose another sequence than Exact"));
        }

        RouteRun hybrid = runs[RecorderRoute.Hybrid];
        int compared = 0;
        int mismatched = 0;

        foreach (int tick in hybrid.ProbeTicks)
        {
            if (gate.Speeds(tick) is { } exact && gate.Recorder(RecorderRoute.Hybrid, tick) is { } answered)
            {
                compared++;
                mismatched += RecorderGate.SameSide(exact.Exact, answered.Speed) ? 0 : 1;
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  test Hybrid per probe: {mismatched} of {compared} viewed, living probes on another side of 0.5 than Exact"));
    }

    /// <summary>A sorted sample's value at a fraction, nearest rank.</summary>
    private static float Quantile(float[] sorted, double fraction) =>
        sorted[Math.Clamp((int)Math.Ceiling(fraction * sorted.Length) - 1, 0, sorted.Length - 1)];
}
