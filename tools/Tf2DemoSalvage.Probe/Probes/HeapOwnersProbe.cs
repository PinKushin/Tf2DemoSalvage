using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Diagnostics.Runtime;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A full dump's live managed heap, by the field that retains it (finding 78).</summary>
/// <remarks>
/// **Why it exists:** a gcdump names types, and a type is not an owner. This walks the dump's real roots breadth first
/// with ClrMD. It charges every object once, to the first path that reaches it. The label is the nearest field of one of
/// this project's types on that path (<c>EntityModelSet._raw</c>), prefixed by the one above it. So the table reads as
/// owners, not as types. An object reached only through runtime or framework objects is charged to its root's kind.
///
/// **The control is the total:** the sum over labels is every object reachable from a root, and it must be about the
/// live total a gcdump reported for the same moment.
/// </remarks>
public sealed class HeapOwnersProbe : IProbe
{
    private const string Ours = "Tf2DemoSalvage.";

    /// <inheritdoc/>
    public string Name => "heap-owners";

    /// <inheritdoc/>
    public string Summary => "a dump's or a running process's live heap by retaining field, two owners deep: heap-owners <file.dmp|pid> [rows=40]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        bool live = arguments.Count > 0 && int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out _);

        if (arguments.Count == 0 || (!live && !File.Exists(arguments[0])))
        {
            output.WriteLine(Summary);
            return;
        }

        int rows = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 40;

        // A process id attaches to the running process, suspended for the walk, so no dump file is ever written.
        using DataTarget target = live
            ? DataTarget.AttachToProcess(int.Parse(arguments[0], CultureInfo.InvariantCulture), suspend: true)
            : DataTarget.LoadDump(arguments[0]);
        using ClrRuntime runtime = target.ClrVersions[0].CreateRuntime();
        ClrHeap heap = runtime.Heap;

        List<string> labels = [];
        Dictionary<string, int> labelIds = new(StringComparer.Ordinal);
        long[] bytes = new long[4096];
        HashSet<ulong> seen = [];
        Queue<(ulong Address, int Label, string? Owner)> pending = new();

        int Id(string label)
        {
            if (!labelIds.TryGetValue(label, out int id))
            {
                id = labels.Count;
                labelIds[label] = id;
                labels.Add(label);

                if (id >= bytes.Length)
                {
                    Array.Resize(ref bytes, bytes.Length * 2);
                }
            }

            return id;
        }

        foreach (ClrRoot root in heap.EnumerateRoots())
        {
            if (root.Object.Address != 0 && seen.Add(root.Object.Address))
            {
                pending.Enqueue((root.Object.Address, Id($"(root {root.RootKind})"), null));
            }
        }

        while (pending.TryDequeue(out (ulong Address, int Label, string? Owner) next))
        {
            ClrObject item = heap.GetObject(next.Address);

            if (!item.IsValid || item.Type is not { } type)
            {
                continue;
            }

            bytes[next.Label] += (long)item.Size;
            bool ours = type.Name?.StartsWith(Ours, StringComparison.Ordinal) == true;

            foreach (ClrReference reference in item.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: true))
            {
                ulong child = reference.Object.Address;

                if (child == 0 || !seen.Add(child))
                {
                    continue;
                }

                if (ours)
                {
                    // An object of ours names what it holds: `Owner > Type.field`, the owner being the nearest of ours above.
                    string field = reference.Field?.Name ?? "[]";
                    string here = $"{Short(type.Name!)}.{field}";
                    pending.Enqueue((child, Id(next.Owner is null ? here : $"{next.Owner} > {here}"), here));
                }
                else
                {
                    pending.Enqueue((child, next.Label, next.Owner));
                }
            }
        }

        long total = bytes.Sum();
        output.WriteLine($"{seen.Count:N0} objects reachable from {labels.Count} owners; {Mb(total)} MB (control: the dump's live total)");

        foreach ((string label, long size) in labels.Select((label, id) => (label, bytes[id])).OrderByDescending(pair => pair.Item2).Take(rows))
        {
            output.WriteLine($"{Mb(size),9} MB  {label}");
        }

        // The same bytes by the top-level owner alone, so a long tail of second-level labels still adds up somewhere.
        output.WriteLine();
        output.WriteLine("by first owner:");

        foreach (IGrouping<string, (string Label, long Size)> group in labels.Select((label, id) => (Label: label, Size: bytes[id]))
            .GroupBy(pair => pair.Label.Split(" > ")[0]).OrderByDescending(group => group.Sum(pair => pair.Size)).Take(rows))
        {
            output.WriteLine($"{Mb(group.Sum(pair => pair.Size)),9} MB  {group.Key}");
        }
    }

    private static string Short(string type) => type.StartsWith(Ours, StringComparison.Ordinal) ? type[Ours.Length..] : type;

    private static string Mb(long bytes) => (bytes / 1048576d).ToString("0.0", CultureInfo.InvariantCulture);
}
