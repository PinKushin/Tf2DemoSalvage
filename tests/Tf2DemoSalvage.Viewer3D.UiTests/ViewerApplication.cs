using System;
using System.IO;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Launches the viewer and finds elements in it.
/// </summary>
/// <remarks>
/// **The single seam onto the automation library.** Every test speaks to this rather than to
/// FlaUI directly, so a change of driver is a change in one file. That is insurance rather than a
/// plan: FlaUI is the deliberate choice here, being in-process and therefore the speed floor for
/// UIA work.
///
/// **Synchronised on the condition, never on the clock.** Waiting is unavoidable in a UI test
/// because the application is a real process, but a sleep long enough to be reliable is also long
/// enough to waste minutes across a suite - and it converts a deterministic failure into a
/// probabilistic one. Everything here waits for an element to exist.
/// </remarks>
internal sealed class ViewerApplication : IDisposable
{
    /// <summary>How long to wait for an element before failing the test.</summary>
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(20);

    private readonly UIA3Automation _automation;
    private readonly Application _application;

    private ViewerApplication(Application application, UIA3Automation automation, Window window)
    {
        _application = application;
        _automation = automation;
        Window = window;
    }

    /// <summary>The viewer's main window.</summary>
    public Window Window { get; }

    /// <summary>Launches the viewer, optionally opening paths at startup.</summary>
    /// <param name="arguments">Files or folders, as a file association would pass them.</param>
    /// <returns>The running application.</returns>
    /// <exception cref="FileNotFoundException">The viewer has not been built.</exception>
    public static ViewerApplication Launch(params string[] arguments)
    {
        string executable = LocateExecutable();

        Application application = arguments.Length == 0
            ? Application.Launch(executable)
            : Application.Launch(executable, string.Join(' ', arguments));

        UIA3Automation automation = new();

        // GetMainWindow polls until the window exists rather than assuming it is up, which
        // matters most on the first run after a build when the process starts cold.
        Window window = application.GetMainWindow(automation, FindTimeout);

        return new ViewerApplication(application, automation, window);
    }

    /// <summary>Finds a control by the automation id the shell gives it.</summary>
    /// <param name="automationId">The id, taken from the shell's own constants.</param>
    /// <returns>The element.</returns>
    /// <exception cref="InvalidOperationException">No such element appeared in time.</exception>
    /// <remarks>
    /// Retries until the timeout rather than failing on the first miss. A control that exists but
    /// has not yet been laid out is a real state, and treating it as absence is the classic UI
    /// test flake - which is a synchronisation defect, never noise.
    /// </remarks>
    public AutomationElement Find(string automationId)
    {
        AutomationElement? found = Window.FindFirstDescendant(
            search => search.ByAutomationId(automationId));

        if (found is not null)
        {
            return found;
        }

        bool appeared = Retry.WhileNull(
            () => Window.FindFirstDescendant(search => search.ByAutomationId(automationId)),
            FindTimeout).Success;

        return appeared
            ? Window.FindFirstDescendant(search => search.ByAutomationId(automationId))!
            : throw new InvalidOperationException(
                $"No element with automation id '{automationId}' appeared within {FindTimeout}.");
    }

    /// <summary>Whether a control with the given automation id is present.</summary>
    /// <param name="automationId">The id to look for.</param>
    /// <returns>True if present right now; this does not wait.</returns>
    public bool Exists(string automationId) =>
        Window.FindFirstDescendant(search => search.ByAutomationId(automationId)) is not null;

    /// <summary>The text currently in the status bar.</summary>
    /// <returns>The status text, or an empty string.</returns>
    public string StatusText()
    {
        AutomationElement? status = Window.FindFirstDescendant(
            search => search.ByControlType(ControlType.StatusBar));

        return status?.FindFirstChild()?.Name ?? string.Empty;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Closed rather than killed where possible, so the form's Dispose runs and the swap chain
        // is released - a killed process leaves the device to the driver to clean up, which is
        // exactly the path a crash-on-exit bug would hide in.
        try
        {
            _application.Close();
        }
        catch (InvalidOperationException)
        {
            // Already gone; nothing to close.
        }

        _automation.Dispose();
        _application.Dispose();
    }

    /// <summary>Finds the built viewer next to these tests.</summary>
    /// <remarks>
    /// Resolved from this assembly's own output directory rather than from a hard-coded
    /// configuration, so a Release run tests the Release build. The project reference guarantees
    /// it was rebuilt: driving a stale binary is the same failure as `dotnet test --no-build`, and
    /// it presents as a passing suite.
    /// </remarks>
    private static string LocateExecutable()
    {
        string here = AppContext.BaseDirectory;
        string candidate = Path.GetFullPath(Path.Combine(
            here, "..", "..", "..", "..", "..",
            "managed", "Tf2DemoSalvage.Viewer3D", "bin",
            Path.GetFileName(Path.GetDirectoryName(here.TrimEnd(Path.DirectorySeparatorChar))!) ?? "Debug",
            "net10.0-windows", "tf2demoview.exe"));

        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            $"The viewer was not found at '{candidate}'. Build Tf2DemoSalvage.Viewer3D first.",
            candidate);
    }
}
