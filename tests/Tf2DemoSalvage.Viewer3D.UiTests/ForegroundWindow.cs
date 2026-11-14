using System;
using System.Runtime.InteropServices;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// One reading of the foreground: the window and the process that owns it. The suite's one way to ask "who is in front",
/// which the focus guards poll and the desktop check reads (B230).
/// </summary>
/// <param name="Window">The foreground window, or zero when there is none (a secure desktop, the lock screen).</param>
/// <param name="ProcessId">Its owner, or zero when the window was gone before its owner could be read.</param>
/// <remarks>
/// **A refusal names the reading that failed, carried from the check** (B243): reading the foreground again after the
/// guard gives up can name a window that arrived a moment later, which is the race B230 is about.
/// </remarks>
internal readonly record struct ForegroundWindow(nint Window, uint ProcessId)
{
    /// <summary>Reads the foreground now: two Win32 calls, microseconds.</summary>
    /// <returns>The reading.</returns>
    public static ForegroundWindow Read()
    {
        nint window = GetForegroundWindow();

        if (window == 0)
        {
            return default;
        }

        // A thread id of 0 is failure: the window went between the two calls, and the zero pid is not process 0 (Idle).
        return GetWindowThreadProcessId(window, out uint processId) == 0 ? new ForegroundWindow(window, 0) : new ForegroundWindow(window, processId);
    }

    /// <summary>The owning process's name, or null when it cannot be read: gone, or not ours to read.</summary>
    /// <returns>The name, or null.</returns>
    public string? ProcessName()
    {
        if (ProcessId == 0)
        {
            return null;
        }

        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)ProcessId);
            return process.ProcessName;
        }
        catch (Exception unread) when (unread is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Gone between the reads, or protected. A refusal's message must never be replaced by this lookup's exception.
            return null;
        }
    }

    /// <summary>This reading in words, with its process's name and its window's title read now.</summary>
    /// <returns>One line.</returns>
    public string Describe()
    {
        char[] title = new char[256];
        int length = Window == 0 ? 0 : GetWindowTextW(Window, title, title.Length);

        return Describe(this, ProcessName(), new string(title, 0, length));
    }

    /// <summary>The words for a reading, given what was read about it.</summary>
    /// <param name="at">The reading.</param>
    /// <param name="name">Its process's name, or null when unreadable.</param>
    /// <param name="title">Its window's title.</param>
    /// <returns>One line.</returns>
    public static string Describe(ForegroundWindow at, string? name, string title) =>
        at.Window == 0
            ? "no foreground window"
            : $"process {at.ProcessId} ({name ?? "unreadable or gone"}), window 0x{at.Window:X} \"{title}\"";

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, [Out] char[] text, int capacity);
}
