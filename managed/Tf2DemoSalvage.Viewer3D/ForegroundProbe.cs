using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;


namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Says which window currently owns the foreground, for the log.
/// </summary>
/// <remarks>
/// **Full screen loses the foreground and nothing recorded where it went.** The symptom is the
/// owner's: "the window is losing focus or something on the first full screen test and it stalls
/// from there", cured by alt-tabbing away and back. Two plausible mechanisms were tested and both
/// were wrong — the menu strip hiding its F11 shortcut, and the border-style change recreating the
/// window handle — which is the point at which guessing stops being cheaper than measuring.
///
/// A log line naming the window that took the foreground turns "focus was lost" into "focus went
/// THERE", and those are different investigations. See
/// <c>docs/memory/measure-every-hop-before-blaming-one.md</c>.
///
/// Diagnostic only: nothing branches on what this returns.
/// </remarks>
internal static partial class ForegroundProbe
{
    /// <summary>Describes the foreground window relative to one of ours.</summary>
    /// <param name="ours">A window handle belonging to this application.</param>
    /// <returns>A single line naming the foreground window, its process, and whether it is ours.</returns>
    public static string Describe(IntPtr ours)
    {
        IntPtr foreground = GetForegroundWindow();

        if (foreground == IntPtr.Zero)
        {
            // A real state, not a failure to read: Windows leaves no window foreground while one
            // is being destroyed or while the desktop is locked.
            return "foreground: none (no window owns it)";
        }

        _ = GetWindowThreadProcessId(foreground, out uint process);

        // A plain span rather than a StringBuilder, which the analyzers reject for P/Invoke
        // (CA1838) — a StringBuilder marshals through a temporary copy on every call.
        // **The process, not the window title.** A title needs a character buffer across the
        // P/Invoke boundary, which the analyzers and the source-generated marshaller each reject in
        // a different way, and it answers a smaller question: "which application took the
        // foreground" is what decides where a keystroke went.
        string owner = process == (uint)Environment.ProcessId ? "ours" : ProcessName(process);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"foreground: {foreground:x} pid {process} ({owner}); " +
            $"this window {ours:x}, {(foreground == ours ? "has it" : "does NOT have it")}");
    }

    /// <summary>The name of a process, or its id when it cannot be read.</summary>
    private static string ProcessName(uint id)
    {
        try
        {
            using Process process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // Exited between the two calls. Named rather than swallowed, so a log reader can tell
            // "a process we could not identify" from "no process".
            return "exited";
        }
        catch (InvalidOperationException)
        {
            return "unreadable";
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
