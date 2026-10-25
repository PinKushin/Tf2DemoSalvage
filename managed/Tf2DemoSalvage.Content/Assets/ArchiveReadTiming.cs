using System.Globalization;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A thread's <see cref="GameArchives.Read"/> calls, counted and timed by the call itself (B595 part 5).</summary>
/// <param name="Reads">Calls made.</param>
/// <param name="Ticks">Stopwatch ticks spent inside them.</param>
/// <param name="SlowReads">Calls that took longer than <see cref="SlowMilliseconds"/>.</param>
public readonly partial record struct ArchiveReadTiming(long Reads, long Ticks, long SlowReads)
{
    /// <summary>The threshold a read must exceed to count as slow.</summary>
    public const double SlowMilliseconds = 5d;

    /// <summary>This reading with one more call of <paramref name="ticks"/> added.</summary>
    /// <param name="ticks">The call's length in Stopwatch ticks.</param>
    /// <returns>The new total.</returns>
    public ArchiveReadTiming Add(long ticks) => new(
        Reads + 1,
        Ticks + ticks,
        SlowReads + (ticks * 1000d / System.Diagnostics.Stopwatch.Frequency > SlowMilliseconds ? 1 : 0));

    /// <summary>The calls between an earlier reading and this one.</summary>
    /// <param name="left">This reading.</param>
    /// <param name="right">The earlier one.</param>
    /// <returns>The difference.</returns>
    public static ArchiveReadTiming operator -(ArchiveReadTiming left, ArchiveReadTiming right) => left.Subtract(right);

    /// <summary>The calls between an earlier reading and this one.</summary>
    /// <param name="earlier">The earlier reading.</param>
    /// <returns>The difference.</returns>
    public ArchiveReadTiming Subtract(ArchiveReadTiming earlier) =>
        new(Reads - earlier.Reads, Ticks - earlier.Ticks, SlowReads - earlier.SlowReads);

    /// <inheritdoc/>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Reads} reads {Ticks * 1000d / System.Diagnostics.Stopwatch.Frequency:0.0} ms, {SlowReads} over {SlowMilliseconds:0} ms");

    /// <summary>The process's page faults so far, soft and hard together, from <c>GetProcessMemoryInfo</c>; 0 off Windows.</summary>
    /// <returns>The count.</returns>
    public static long ProcessPageFaults()
    {
        if (!System.OperatingSystem.IsWindows())
        {
            return 0;
        }

        MemoryCounters counters = new() { Size = (uint)Marshal.SizeOf<MemoryCounters>() };

        return K32GetProcessMemoryInfo(-1, ref counters, counters.Size) ? counters.PageFaultCount : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool K32GetProcessMemoryInfo(nint process, ref MemoryCounters counters, uint size);
}
