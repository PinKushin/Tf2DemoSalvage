using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

using NUnit.Framework.Interfaces;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>What the machine has free, read from <c>GlobalMemoryStatusEx</c>.</summary>
/// <remarks>
/// **Added for B558.** The first UI failure with a readable tree (run 39478390153) showed a Windows system dialog open on
/// the hosted runner: *"Windows created a temporary paging file on your computer because of a problem that occurred
/// with your paging file configuration"*. That points at memory pressure, and nothing in the log said how much memory
/// the runner had. This logs the answer before every test, so the next failure shows whether the pressure was there.
/// </remarks>
internal static class MachineMemory
{
    /// <summary>One reading, in bytes.</summary>
    /// <param name="TotalPhysicalBytes">Installed RAM the OS can use.</param>
    /// <param name="AvailablePhysicalBytes">RAM free now.</param>
    /// <param name="TotalCommitBytes">The commit limit: RAM plus page file.</param>
    /// <param name="AvailableCommitBytes">Commit still available (what a process can still allocate).</param>
    /// <param name="LoadPercent">The OS's own memory-load figure.</param>
    internal readonly record struct Reading(
        ulong TotalPhysicalBytes,
        ulong AvailablePhysicalBytes,
        ulong TotalCommitBytes,
        ulong AvailableCommitBytes,
        uint LoadPercent);

    /// <summary>Reads the machine's memory now.</summary>
    /// <returns>The reading.</returns>
    /// <exception cref="Win32Exception">The call failed; the error code is in the exception, so a failed read is never reported as zeros.</exception>
    internal static Reading Read()
    {
        MemoryStatus status = new() { Length = (uint)Marshal.SizeOf<MemoryStatus>() };

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return new Reading(status.TotalPhysical, status.AvailablePhysical, status.TotalPageFile, status.AvailablePageFile, status.MemoryLoad);
    }

    /// <summary>One line for the test log.</summary>
    /// <param name="reading">A reading.</param>
    /// <returns>For example <c>physical 6144 of 16384 MB free, commit 3072 of 18432 MB free, memory load 62%</c>.</returns>
    internal static string Describe(Reading reading)
    {
        const ulong Megabyte = 1024UL * 1024;

        return $"physical {reading.AvailablePhysicalBytes / Megabyte} of {reading.TotalPhysicalBytes / Megabyte} MB free, " +
            $"commit {reading.AvailableCommitBytes / Megabyte} of {reading.TotalCommitBytes / Megabyte} MB free, memory load {reading.LoadPercent}%";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    // **`System32` only, which is CA5392** (see TestNameInTitle): the search path is pinned rather than trusting the working directory.
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GlobalMemoryStatusEx")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}

/// <summary>Logs the machine's free memory before every test, so a UI failure shows whether the runner was short (B558).</summary>
/// <remarks>
/// An assembly-level <c>ITestAction</c> with <see cref="ActionTargets.Test"/>, the same shape as <c>TestNameInTitle</c>
/// (one concern each). The line goes through <see cref="ViewerApplication.Log"/> so it lands beside the focus and window dumps.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
internal sealed class MachineMemoryLogAttribute : Attribute, ITestAction
{
    /// <inheritdoc/>
    public ActionTargets Targets => ActionTargets.Test;

    /// <inheritdoc/>
    public void BeforeTest(ITest test)
    {
        ArgumentNullException.ThrowIfNull(test);

        ViewerApplication.Log($"memory before {test.Name}: {MachineMemory.Describe(MachineMemory.Read())}");
    }

    /// <inheritdoc/>
    public void AfterTest(ITest test)
    {
    }
}
