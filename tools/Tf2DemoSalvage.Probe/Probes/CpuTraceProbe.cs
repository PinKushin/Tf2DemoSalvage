using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Where one thread's time goes in a window of a <c>dotnet-trace</c> CPU sample (finding 82).</summary>
/// <remarks>
/// **A window, because a whole-process trace of the viewer is mostly its load**: the timeline build alone is minutes of
/// samples, and a frame question is about the last seconds. The EventPipe sampler samples every managed thread whether
/// it runs or waits, so the thread is chosen by id; with none given, each thread's sample count and top leaf are listed
/// so the render thread can be picked. **Control:** the inclusive share of the frame's own entry point should be near
/// 100% of the chosen thread's samples while it renders.
/// </remarks>
public sealed class CpuTraceProbe : IProbe
{
    private const string SampleProvider = "Microsoft-DotNETCore-SampleProfiler";

    public string Name => "cpu-trace";

    public string Summary =>
        "inclusive and leaf time by method for one thread in a window of a .nettrace: cpu-trace <file.nettrace> <from-s> <to-s> [thread] [top=40] [leaf-substring]";

    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3 || !File.Exists(arguments[0]))
        {
            output.WriteLine(Summary);
            return;
        }

        double from = double.Parse(arguments[1], CultureInfo.InvariantCulture) * 1000d;
        double to = double.Parse(arguments[2], CultureInfo.InvariantCulture) * 1000d;
        int? thread = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : null;
        int top = arguments.Count > 4 ? int.Parse(arguments[4], CultureInfo.InvariantCulture) : 40;
        string? focus = arguments.Count > 5 ? arguments[5] : null;
        Dictionary<string, long> callers = [];

        string etlx = TraceLog.CreateFromEventPipeDataFile(arguments[0]);

        using TraceLog log = new(etlx);

        output.WriteLine($"trace lasts {log.SessionDuration.TotalSeconds:0.0} s");

        Dictionary<int, (long Samples, Dictionary<string, long> Leaves)> threads = [];
        Dictionary<string, long> inclusive = [];
        Dictionary<string, long> leaf = [];
        long samples = 0;

        foreach (TraceEvent sample in log.Events)
        {
            if (sample.ProviderName != SampleProvider || sample.TimeStampRelativeMSec < from || sample.TimeStampRelativeMSec > to)
            {
                continue;
            }

            List<string> frames = [];

            for (TraceCallStack? at = sample.CallStack(); at is not null; at = at.Caller)
            {
                string name = at.CodeAddress.FullMethodName;
                frames.Add(string.IsNullOrEmpty(name) ? "?" + at.CodeAddress.ModuleName : name);
            }

            string top0 = frames.Count > 0 ? frames[0] : "(no stack)";

            if (thread is null)
            {
                (long count, Dictionary<string, long> leaves) = threads.TryGetValue(sample.ThreadID, out (long Samples, Dictionary<string, long> Leaves) seen) ? seen : (0, []);
                leaves[top0] = leaves.GetValueOrDefault(top0) + 1;
                threads[sample.ThreadID] = (count + 1, leaves);
                continue;
            }

            if (sample.ThreadID != thread)
            {
                continue;
            }

            samples++;
            leaf[top0] = leaf.GetValueOrDefault(top0) + 1;

            // With a leaf named, who reached it: the four frames above it, so a wait can be traced to its allocator.
            if (focus is not null && top0.Contains(focus, StringComparison.Ordinal))
            {
                string path = string.Join(" <- ", frames.Skip(1).Take(4).Select(static name => name.Split('(')[0]));
                callers[path] = callers.GetValueOrDefault(path) + 1;
            }

            foreach (string name in frames.Distinct())
            {
                inclusive[name] = inclusive.GetValueOrDefault(name) + 1;
            }
        }

        if (thread is null)
        {
            foreach ((int id, (long count, Dictionary<string, long> leaves)) in threads.OrderByDescending(pair => pair.Value.Samples))
            {
                (string name, long n) = leaves.OrderByDescending(pair => pair.Value).First();
                output.WriteLine($"thread {id}: {count} samples; top leaf {n} {name}");
            }

            return;
        }

        output.WriteLine($"thread {thread}: {samples} samples in {(to - from) / 1000d:0.#} s");
        output.WriteLine("inclusive:");

        foreach ((string name, long n) in inclusive.OrderByDescending(pair => pair.Value).Take(top))
        {
            output.WriteLine($"{100d * n / Math.Max(samples, 1),6:0.0}%  {name}");
        }

        output.WriteLine("leaf:");

        foreach ((string name, long n) in leaf.OrderByDescending(pair => pair.Value).Take(top))
        {
            output.WriteLine($"{100d * n / Math.Max(samples, 1),6:0.0}%  {name}");
        }

        if (focus is not null)
        {
            output.WriteLine($"reaching a leaf containing '{focus}':");

            foreach ((string path, long n) in callers.OrderByDescending(pair => pair.Value).Take(top))
            {
                output.WriteLine($"{100d * n / Math.Max(samples, 1),6:0.0}%  {path}");
            }
        }
    }
}
