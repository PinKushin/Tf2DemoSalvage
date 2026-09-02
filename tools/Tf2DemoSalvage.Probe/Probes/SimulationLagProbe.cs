using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// How far an entity's simulation tick sits from the packet that carried it.
/// </summary>
/// <remarks>
/// **The number a still-open decision should be made on** (B273). The engine timestamps an
/// interpolation history entry with the entity's SIMULATION time —
/// <c>C_BaseEntity::GetLastChangeTime</c> returns <c>GetSimulationTime()</c> for anything latched
/// as a simulation variable, origin and angles among them, and
/// <c>OnLatchInterpolatedVariables</c> hands it to every watcher (<c>c_baseentity.cpp:2806</c>).
/// This project stamps every keyframe with the packet's tick instead. This measures the gap.
///
/// <code>
///   simlag tf2-2013-build1729296-stv-cp_foundry
/// </code>
///
/// **Two things had to be right before the numbers meant anything, and both were wrong first.**
/// <c>m_flSimulationTime</c> is eight bits of offset with <c>SPROP_ENCODED_AGAINST_TICKCOUNT</c>, so
/// it must be converted at receipt rather than stored and read later; and the base is the SERVER's
/// tick from <c>net_Tick</c>, not the demo's own command tick, which starts near zero while a
/// server has been up for hours. With either wrong the histogram is bimodal at the clamp — noise
/// wearing the shape of a distribution.
///
/// The "no simulation time" line is the control: while it is zero, the distribution describes the
/// demo rather than describing which entities happened to answer.
/// </remarks>
public sealed class SimulationLagProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "simlag";

    /// <inheritdoc/>
    public string Summary =>
        "packet tick minus each entity's simulation tick: simlag <demo>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("simlag <demo>");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        output.WriteLine(Path.GetFileName(path));

        Report(output, "SIMULATION", timeline.SimulationLag, timeline.SimulationLagUnknown);
        Report(output, "ANIMATION", timeline.AnimationLag, timeline.AnimationLagUnknown);
        Report(output, "SIMULATION MINUS ANIMATION, where both were sent", timeline.ClockGap, 0);

        output.WriteLine("BY CLASS, simulation lag — the same bucket everywhere is a clock offset:");

        foreach ((string className, int[] counts) in timeline.SimulationLagByClass
            .OrderByDescending(entry => Total(entry.Value))
            .Take(12))
        {
            int total = Total(counts);
            List<string> parts = [];

            for (int bucket = 0; bucket < DemoTimeline.LagBuckets; bucket++)
            {
                if (counts[bucket] > 0)
                {
                    parts.Add(
                        $"{Label(bucket)}:{100.0 * counts[bucket] / Math.Max(1, total):0}%");
                }
            }

            output.WriteLine(
                $"  {className,-28} {total.ToString(CultureInfo.InvariantCulture),7} updates  "
                + string.Join("  ", parts));
        }
    }

    /// <summary>Everything in one class's histogram.</summary>
    private static int Total(int[] counts)
    {
        int total = 0;

        foreach (int count in counts)
        {
            total += count;
        }

        return total;
    }

    /// <summary>Prints one clock's histogram with its own control line.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="clock">Which of the engine's two latch clocks this is.</param>
    /// <param name="lag">The histogram.</param>
    /// <param name="unknown">Updates that carried no value for this clock.</param>
    private static void Report(
        TextWriter output, string clock, Func<int, int> lag, int unknown)
    {
        int total = 0;

        for (int bucket = 0; bucket < DemoTimeline.LagBuckets; bucket++)
        {
            total += lag(bucket);
        }

        output.WriteLine(
            $"{clock}: {total.ToString(CultureInfo.InvariantCulture)} entity updates carried a "
            + $"tick, {unknown.ToString(CultureInfo.InvariantCulture)} did not");

        for (int bucket = 0; bucket < DemoTimeline.LagBuckets; bucket++)
        {
            int count = lag(bucket);

            if (count == 0)
            {
                continue;
            }

            output.WriteLine(
                $"  lag {Label(bucket),5} ticks: "
                + $"{count.ToString(CultureInfo.InvariantCulture),9} "
                + $"({100.0 * count / Math.Max(1, total):0.##}%)");
        }
    }

    /// <summary>How a bucket is named, with the ends marked as the catch-alls they are.</summary>
    /// <remarks>
    /// The first and last buckets hold everything beyond ±8, so printing them as "−8" and "+8"
    /// would report a clamp as a measurement — which is exactly how the first run of this was
    /// misread.
    /// </remarks>
    private static string Label(int bucket)
    {
        if (bucket == 0)
        {
            return "<=-8";
        }

        if (bucket == DemoTimeline.LagBuckets - 1)
        {
            return ">=+8";
        }

        return (bucket - DemoTimeline.LagZero).ToString("+0;-0;0", CultureInfo.InvariantCulture);
    }
}
