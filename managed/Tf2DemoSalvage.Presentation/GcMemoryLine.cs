using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The memory line `--measure` prints when it ends: the GC's own account beside the process's (finding 80).</summary>
/// <remarks>
/// **Read, not inferred.** Finding 80 identified a 6 GB private allocation as the GC's from its growth alone, and
/// estimated "committed but not live" as that allocation minus a heap dump taken seconds apart. This line reports the
/// GC's committed, live and fragmented bytes as one reading (<c>GC.GetGCMemoryInfo</c>), taken with no forced
/// collection so it shows what the process actually holds. The slack is committed minus live minus fragmented.
/// </remarks>
public static class GcMemoryLine
{
    private const double Megabyte = 1048576d;

    /// <summary>Formats one reading.</summary>
    /// <param name="privateBytes">The process's private bytes.</param>
    /// <param name="workingSet">The process's working set.</param>
    /// <param name="heapSize">The GC's heap size at its last collection.</param>
    /// <param name="committed">The GC's total committed bytes.</param>
    /// <param name="fragmented">The GC's fragmented bytes.</param>
    /// <param name="generations">Each generation's size after the last collection: gen0, gen1, gen2, LOH, POH.</param>
    /// <returns>The line.</returns>
    public static string Format(
        long privateBytes, long workingSet, long heapSize, long committed, long fragmented, IReadOnlyList<long> generations)
    {
        ArgumentNullException.ThrowIfNull(generations);

        static string Mb(long bytes) => (bytes / Megabyte).ToString("0", CultureInfo.InvariantCulture);
        string Generation(int index) => index < generations.Count ? Mb(generations[index]) : "?";

        return $"memory: private {Mb(privateBytes)} MB, working set {Mb(workingSet)} MB; GC heap {Mb(heapSize)} MB, " +
            $"committed {Mb(committed)} MB, fragmented {Mb(fragmented)} MB, slack {Mb(committed - heapSize - fragmented)} MB; " +
            $"generations {Generation(0)}, {Generation(1)}, {Generation(2)}, LOH {Generation(3)}, POH {Generation(4)} MB";
    }

    /// <summary>The line for this process, now.</summary>
    /// <returns>The line.</returns>
    public static string Now()
    {
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long[] generations = new long[info.GenerationInfo.Length];

        for (int index = 0; index < generations.Length; index++)
        {
            generations[index] = info.GenerationInfo[index].SizeAfterBytes;
        }

        using System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();
        return Format(self.PrivateMemorySize64, self.WorkingSet64, info.HeapSizeBytes, info.TotalCommittedBytes, info.FragmentedBytes, generations);
    }
}
