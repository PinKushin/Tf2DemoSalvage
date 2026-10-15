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
/// **Its controls**: the value count beside each comparison (a hash of nothing matches anything). The reach is the
/// default settings' (<c>WindowReach</c>, D219), never a number chosen here.
///
/// It asserts nothing — a measurement is not a test (D38).
/// </remarks>
public sealed class TimelineWindowsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "timeline-windows";

    /// <inheritdoc/>
    public string Summary =>
        "checkpoint a demo, answer sample ticks through windows, compare with a whole build, and say what checkpoints cost: timeline-windows <demo> [every s=5] [samples=60] [cost]; the reach is the default settings' (D219)";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("timeline-windows <demo> [every seconds] [samples] [cost]");
            return;
        }

        double every = arguments.Count > 1 ? double.Parse(arguments[1], CultureInfo.InvariantCulture) : 5;
        int samples = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : 60;
        bool costOnly = arguments.Count > 3 && arguments[3] == "cost";
        byte[] demo = File.ReadAllBytes(path);
        const double Interval = 0.015;
        int everyTicks = (int)(every / Interval);

        long heapBefore = Live();
        Stopwatch walk = Stopwatch.StartNew();
        Dictionary<Type, (long Objects, long Elements)>? created = costOnly ? [] : null;
        DemoTimeline.Checkpoints checkpoints = DemoTimeline.Checkpoints.Walk(demo, everyTicks, new WindowSettings(), created)!;
        walk.Stop();
        long heapAfter = Live();

        // The reach the walk KEPT, carried from it rather than recomputed here.
        output.WriteLine($"{Path.GetFileName(path)} ({demo.Length / 1048576d:0.0} MB): a checkpoint every {everyTicks} ticks, " +
            $"reach: tracks {checkpoints.Reach.TrackTicks}, output {checkpoints.Reach.OutputTicks}");

        // What checkpoints after the first CREATE rather than share — the source of the retained heap — by type.
        foreach ((Type type, (long objects, long elements)) in (created ?? []).OrderByDescending(entry => entry.Value.Elements).Take(15))
        {
            output.WriteLine($"    created {objects,12:N0} objects {elements,14:N0} elements  {type}");
        }
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
        (double Copy, double Decode, double Finish) phases = default;

        foreach (double tick in order)
        {
            windows.Start();
            DemoTimeline window = checkpoints.Window((int)tick, (int)tick + 1, out (double Copy, double Decode, double Finish) spent);
            windows.Stop();
            phases = (phases.Copy + spent.Copy, phases.Decode + spent.Decode, phases.Finish + spent.Finish);
            List<(string Question, object? Answer)> expected = TimelineAnswers.At(whole, tick);
            List<(string Question, object? Answer)> actual = TimelineAnswers.At(window, tick);
            bool differs = false;

            for (int index = 0; index < expected.Count; index++)
            {
                (string hash, long count) = TimelineFingerprint.OfAnswer(expected[index].Answer);
                values += count;

                if (hash != TimelineFingerprint.OfAnswer(actual[index].Answer).Hash)
                {
                    if (!byQuestion.Keys.Any(key => key.StartsWith(expected[index].Question + " ", StringComparison.Ordinal)))
                    {
                        // The first difference per question, as values: what to look at, not a count.
                        output.WriteLine($"    first {expected[index].Question} at {tick:0.00} (window {window.FirstTick}..{window.LastTick}): " +
                            $"whole {FirstDifference(expected[index].Answer, actual[index].Answer)} / window {FirstDifference(actual[index].Answer, expected[index].Answer)}");
                    }

                    differs = true;

                    // Keyed by the first differing FIELD too, so a single cause is told from several.
                    string field = System.Text.RegularExpressions.Regex.Match(
                        FirstDifference(expected[index].Answer, actual[index].Answer), @": (\w+)(=|\.\{)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
                    string key = $"{expected[index].Question} {field}";
                    byQuestion[key] = byQuestion.GetValueOrDefault(key) + 1;
                }
            }

            differing += differs ? 1 : 0;
        }

        output.WriteLine($"  {at.Count * 2} seeks, {values} values compared; {differing} ticks differ; window build mean {windows.Elapsed.TotalMilliseconds / (at.Count * 2):0} ms " +
            $"(copy {phases.Copy / (at.Count * 2):0}, decode {phases.Decode / (at.Count * 2):0}, finish {phases.Finish / (at.Count * 2):0})");

        foreach ((string question, int count) in byQuestion.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"    {question,-30} differs at {count}");
        }

        output.WriteLine(differing == 0 ? "  IDENTICAL" : "  DIFFERENT");
    }

    private static string FirstDifference(object? answer, object? other)
    {
        // Two lists: the first element that differs, which is what to look at.
        if (answer is System.Collections.IList items && other is System.Collections.IList others)
        {
            for (int index = 0; index < Math.Min(items.Count, others.Count); index++)
            {
                if (TimelineFingerprint.OfAnswer(items[index]).Hash != TimelineFingerprint.OfAnswer(others[index]).Hash)
                {
                    return $"[{index}] of {items.Count}: {Fields(items[index], others[index])}";
                }
            }
        }

        return Describe(answer);
    }

    // The public properties, nested one level, whose hashes differ — named, with this side's value.
    private static string Fields(object? item, object? other)
    {
        if (item is null || other is null || item.GetType() != other.GetType())
        {
            return Describe(item);
        }

        List<string> differing = [];

        foreach (System.Reflection.PropertyInfo property in item.GetType().GetProperties()
                     .Where(property => property.GetIndexParameters().Length == 0 && !property.PropertyType.IsByRefLike))
        {
            object? mine = property.GetValue(item);
            object? theirs = property.GetValue(other);

            if (TimelineFingerprint.OfAnswer(mine).Hash != TimelineFingerprint.OfAnswer(theirs).Hash)
            {
                differing.Add(mine is not null && !mine.GetType().IsPrimitive && !mine.GetType().IsEnum && mine is not string && mine is not System.Collections.IEnumerable
                    ? $"{property.Name}.{{{Fields(mine, theirs)}}}"
                    : $"{property.Name}={Describe(mine)}");
            }
        }

        return $"entity {item.GetType().GetProperty("EntityIndex")?.GetValue(item)}: {string.Join("; ", differing)}";
    }

    private static string Describe(object? answer)
    {
        string text = answer is System.Collections.ICollection items ? $"{items.Count} items" : $"{answer}";
        return text.Length > 700 ? text[..700] : text;
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
