using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A real demo walked into checkpoints, then asked at many ticks through windows, against a whole build (D217 step 3).
/// </summary>
/// <remarks>
/// **Seeks in a scrubber's order**: the sample ticks are visited latest-first, then earliest-first, so every window after
/// the first is a seek back or a seek forward past one already made — the back-then-forward case marks refused.
///
/// **Its controls**: the value count beside each comparison (a hash of nothing matches anything), and a run with a
/// lookback of 0 — it must differ somewhere, or the comparison cannot see a trim at all.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineWindowsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-windows";

    /// <inheritdoc/>
    public string Summary =>
        "checkpoint a demo, answer sample ticks through windows, compare with a whole build, and say what checkpoints cost: timeline-windows <demo> [every s=5] [lookback s=4] [samples=60] [cost]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("timeline-windows <demo> [every seconds] [lookback seconds] [samples] [cost]");
            return;
        }

        double every = arguments.Count > 1 ? double.Parse(arguments[1], CultureInfo.InvariantCulture) : 5;
        double lookback = arguments.Count > 2 ? double.Parse(arguments[2], CultureInfo.InvariantCulture) : 4;
        int samples = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 60;
        bool costOnly = arguments.Count > 4 && arguments[4] == "cost";
        byte[] demo = File.ReadAllBytes(path);
        const double Interval = 0.015;
        int everyTicks = (int)(every / Interval);
        int lookbackTicks = (int)(lookback / Interval);

        output.WriteLine($"{Path.GetFileName(path)} ({demo.Length / 1048576d:0.0} MB): a checkpoint every {everyTicks} ticks, lookback {lookbackTicks}");

        long heapBefore = Live();
        Stopwatch walk = Stopwatch.StartNew();
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, everyTicks, lookbackTicks)!;
        walk.Stop();
        long heapAfter = Live();
        int[] ticks = [.. checkpoints.Ticks];
        (long total, long largest) = checkpoints.Cost();

        output.WriteLine($"  walk {walk.Elapsed.TotalSeconds:0.0} s; {ticks.Length} checkpoints; retained heap {(heapAfter - heapBefore) / 1048576d:0.0} MB");
        // Retained heap is what the checkpoints HOLD (they share unchanged objects); this is what a resume ALLOCATES.
        output.WriteLine($"  a resume copies {total / ticks.Length / 1048576d:0.0} MB on average, largest {largest / 1048576d:0.0} MB");

        foreach ((string field, long bytes) in checkpoints.CostByField(ticks.Length / 2).Take(10))
        {
            output.WriteLine($"    mid checkpoint {field,-30} {bytes / 1048576d,8:0.0} MB");
        }

        if (costOnly)
        {
            return;
        }

        DemoTimeline whole = DemoTimeline.Build(demo);
        int first = whole.FirstTick;
        int last = whole.LastTick;
        List<double> at = [.. Enumerable.Range(0, samples).Select(index => first + ((last - first) * (index + 0.5) / samples) + 0.37)];

        // Latest first, then earliest first: every window is a seek back, then a seek forward, from the one before.
        IEnumerable<double> order = at.AsEnumerable().Reverse().Concat(at);
        int differing = 0;
        long values = 0;
        Dictionary<string, int> byQuestion = [];
        Stopwatch windows = new();

        foreach (double tick in order)
        {
            windows.Start();
            DemoTimeline window = checkpoints.Window((int)tick, (int)tick + 1);
            windows.Stop();
            List<(string Question, object? Answer)> expected = TimelineAnswers.At(whole, tick);
            List<(string Question, object? Answer)> actual = TimelineAnswers.At(window, tick);
            bool differs = false;

            for (int index = 0; index < expected.Count; index++)
            {
                (string hash, long count) = TimelineFingerprint.OfAnswer(expected[index].Answer);
                values += count;

                if (hash != TimelineFingerprint.OfAnswer(actual[index].Answer).Hash)
                {
                    if (!byQuestion.ContainsKey(expected[index].Question))
                    {
                        // The first difference per question, as values: what to look at, not a count.
                        output.WriteLine($"    first {expected[index].Question} at {tick:0.00} (window {window.FirstTick}..{window.LastTick}): " +
                            $"whole {Describe(expected[index].Answer)} / window {Describe(actual[index].Answer)}");
                    }

                    differs = true;
                    byQuestion[expected[index].Question] = byQuestion.GetValueOrDefault(expected[index].Question) + 1;
                }
            }

            differing += differs ? 1 : 0;
        }

        output.WriteLine($"  {at.Count * 2} seeks, {values} values compared; {differing} ticks differ; window build mean {windows.Elapsed.TotalMilliseconds / (at.Count * 2):0} ms");

        foreach ((string question, int count) in byQuestion.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"    {question,-16} differs at {count}");
        }

        output.WriteLine(differing == 0 ? "  IDENTICAL" : "  DIFFERENT");
    }

    private static string Describe(object? answer)
    {
        string text = answer is System.Collections.ICollection items ? $"{items.Count} items" : $"{answer}";
        return text.Length > 160 ? text[..160] : text;
    }

    // As `timeline-holders` measures: the heap after a full compacting collection.
    private static long Live()
    {
#pragma warning disable S1215
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
        GC.WaitForPendingFinalizers();
        return GC.GetGCMemoryInfo(GCKind.Any).HeapSizeBytes;
    }
}
