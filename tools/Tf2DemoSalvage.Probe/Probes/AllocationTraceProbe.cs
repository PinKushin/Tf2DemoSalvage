using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Large-object allocations in an EventPipe trace, by type and by the frame that allocated them (finding 78).</summary>
/// <remarks>
/// **Why it exists:** the viewer's own `--measure` line names the TYPE behind 1.7–1.9 GB of large <c>float[]</c> per 20 s
/// of playback, but not the code. An in-process listener cannot see the stack, since runtime events reach it on another
/// thread. A trace taken with <c>dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5</c> carries a
/// stack on every <c>GCAllocationTick</c>, and TraceLog resolves those stacks to methods.
///
/// **The control is the total.** The sum of large ticks here, scaled to 20 s, must be about the rate the `--measure`
/// line reported for the same kind of run. A trace that falls well short dropped events and cannot name an owner.
///
/// The report groups each type by its first frame outside the runtime's own collection and array helpers
/// (<c>Array.Resize</c>, <c>List`1.set_Capacity</c>, …), because those name the mechanism and not the owner.
/// </remarks>
public sealed class AllocationTraceProbe : IProbe
{
    /// <summary>Frames that name how an array was made rather than who made it.</summary>
    private static readonly string[] Plumbing =
    [
        "System.Array.", "System.Collections.", "System.Linq.", "System.Runtime.", "System.GC.", "System.Buffer",
        "System.Span", "System.ReadOnlySpan", "System.Memory", "System.SZArrayHelper", "System.Private.CoreLib!System.Array",
    ];

    /// <inheritdoc/>
    public string Name => "alloc-trace";

    /// <inheritdoc/>
    public string Summary => "large allocations in a .nettrace by type and allocating frame: alloc-trace <file.nettrace> [frames=3]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || !File.Exists(arguments[0]))
        {
            output.WriteLine(Summary);
            return;
        }

        int depth = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 3;
        string etlx = TraceLog.CreateFromEventPipeDataFile(arguments[0]);

        using TraceLog log = new(etlx);
        using TraceLogEventSource source = log.Events.GetSource();

        Dictionary<(string Type, string Owner), long> bySite = [];
        Dictionary<string, long> byType = [];
        double first = double.NaN;
        double last = 0;
        long ticks = 0;
        long unresolved = 0;
        long everything = 0;

        source.Clr.GCAllocationTick += tick =>
        {
            everything += tick.AllocationAmount64;

            if (tick.AllocationKind != GCAllocationKind.Large)
            {
                return;
            }

            first = double.IsNaN(first) ? tick.TimeStampRelativeMSec : first;
            last = tick.TimeStampRelativeMSec;
            ticks++;

            long bytes = tick.AllocationAmount64;
            string type = tick.TypeName ?? "?";
            string owner = Owner(tick.CallStack(), depth);

            if (owner.Length == 0)
            {
                unresolved += bytes;
                owner = "(no stack)";
            }

            byType[type] = byType.GetValueOrDefault(type) + bytes;
            bySite[(type, owner)] = bySite.GetValueOrDefault((type, owner)) + bytes;
        };

        source.Process();

        double seconds = Math.Max((last - first) / 1000d, 1e-3);
        double scale = 20d / seconds;
        long total = byType.Values.Sum();

        output.WriteLine(
            $"{ticks} large ticks over {seconds:0.0} s; {Mb(total)} MB, {Mb((long)(total * scale))} MB per 20 s " +
            $"(control: compare with the --measure line's large allocations); unresolved stacks {Mb(unresolved)} MB; " +
            $"all ticks, every heap, {Mb(everything)} MB (control: the viewer's allocated-since-start for a whole-process trace)");

        foreach ((string type, long bytes) in byType.OrderByDescending(pair => pair.Value).Take(8))
        {
            output.WriteLine($"{Mb((long)(bytes * scale)),8} MB/20s  {type}");

            foreach (((_, string owner), long at) in bySite.Where(pair => pair.Key.Type == type).OrderByDescending(pair => pair.Value).Take(6))
            {
                output.WriteLine($"{Mb((long)(at * scale)),16} MB/20s  {owner}");
            }
        }
    }

    /// <summary>The first <paramref name="depth"/> frames that are not array or collection plumbing, nearest first.</summary>
    private static string Owner(TraceCallStack? stack, int depth)
    {
        List<string> frames = [];

        for (TraceCallStack? at = stack; at is not null && frames.Count < depth; at = at.Caller)
        {
            string name = at.CodeAddress.FullMethodName;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (frames.Count == 0 && Plumbing.Any(name.StartsWith))
            {
                continue;
            }

            frames.Add(name);
        }

        return string.Join(" <- ", frames);
    }

    private static string Mb(long bytes) => (bytes / 1048576d).ToString("0", CultureInfo.InvariantCulture);
}
