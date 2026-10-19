using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Another process's memory, region by region, sampled until it exits (finding 78).</summary>
/// <remarks>
/// **This counts the whole process, which a census of one object graph cannot see.** The viewer keeps 0.83 GB for a map
/// out of a 7.6 GB working set. This probe walks the viewer's address space with <c>VirtualQueryEx</c> and sums the
/// COMMITTED bytes by region type: image, mapped and private. It also lists the largest private allocations by their
/// allocation base, which are the managed heap's regions, the driver's and the native heaps'. The process must already
/// be running. Each sample waits on the process's exit, never on a sleep.
///
/// **Every sample prints its control**: the committed private bytes from the walk beside the process's own
/// <c>PrivateMemorySize64</c>. The two count the same thing by different routes and must agree. A walk that misses regions
/// falls short. GPU-side memory and the managed heap's own split come from outside the process, from performance
/// counters and <c>dotnet-gcdump</c>, and are not measured here.
/// </remarks>
public sealed partial class ProcessMemoryProbe : IProbe
{
    private const uint MemCommit = 0x1000;
    private const uint MemImage = 0x1000000;
    private const uint MemMapped = 0x40000;
    private const uint MemPrivate = 0x20000;
    private const uint QueryInformation = 0x0400;
    private const uint VmRead = 0x0010;

    /// <inheritdoc/>
    public string Name => "process-memory";

    /// <inheritdoc/>
    public string Summary => "a running process's committed memory by region type, sampled until it exits: process-memory <name> [seconds=5]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        TimeSpan every = TimeSpan.FromSeconds(arguments.Count > 1 ? double.Parse(arguments[1], CultureInfo.InvariantCulture) : 5);

        if (Process.GetProcessesByName(arguments[0]).FirstOrDefault() is not { } process)
        {
            output.WriteLine($"No process named '{arguments[0]}' is running.");
            return;
        }

        using (process)
        {
            output.WriteLine($"pid {process.Id}");
            nint handle = OpenProcess(QueryInformation | VmRead, 0, (uint)process.Id);

            if (handle == 0)
            {
                output.WriteLine($"OpenProcess refused (error {Marshal.GetLastPInvokeError()}).");
                return;
            }

            try
            {
                do
                {
                    Sample(output, process, handle);
                    output.Flush();
                }
                while (!process.WaitForExit(every));
            }
            finally
            {
                _ = CloseHandle(handle);
            }
        }
    }

    private static void Sample(TextWriter output, Process process, nint handle)
    {
        Dictionary<uint, long> byType = [];
        Dictionary<nint, long> privateByAllocation = [];
        nint address = 0;

        while (VirtualQueryEx(handle, address, out MemoryBasicInformation region, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) != 0)
        {
            if (region.State == MemCommit)
            {
                byType[region.Type] = byType.GetValueOrDefault(region.Type) + (long)region.RegionSize;

                if (region.Type == MemPrivate)
                {
                    privateByAllocation[region.AllocationBase] = privateByAllocation.GetValueOrDefault(region.AllocationBase) + (long)region.RegionSize;
                }
            }

            nint next = region.BaseAddress + (nint)region.RegionSize;

            if (next <= address)
            {
                break;
            }

            address = next;
        }

        process.Refresh();

        if (process.HasExited)
        {
            return;
        }

        output.WriteLine(
            $"{DateTime.Now:HH:mm:ss} working set {Mb(process.WorkingSet64)}, private {Mb(process.PrivateMemorySize64)} " +
            $"(walk: private {Mb(byType.GetValueOrDefault(MemPrivate))}, mapped {Mb(byType.GetValueOrDefault(MemMapped))}, image {Mb(byType.GetValueOrDefault(MemImage))}); " +
            $"{privateByAllocation.Count} private allocations, largest: " +
            string.Join(", ", privateByAllocation.Values.OrderDescending().Take(8).Select(Mb)) +
            $"; under 64 MB each: {Mb(privateByAllocation.Values.Where(size => size < 64L << 20).Sum())}");
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("F0", CultureInfo.InvariantCulture);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    // Pinned to System32 so the loader cannot be pointed at a kernel32.dll dropped beside the probe (CA5392).
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, int inherit, uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation information, nuint length);
}
