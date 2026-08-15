using System;
using System.IO;
using System.Runtime.InteropServices;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Tools;
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

    /// <summary>Where the viewer keeps its logs and screenshots.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage");

    private readonly UIA3Automation _automation;
    private readonly Application _application;
    private readonly DateTime _launched;

    private ViewerApplication(Application application, UIA3Automation automation, Window window)
    {
        _application = application;
        _automation = automation;
        Window = window;
        _launched = DateTime.Now;
    }

    /// <summary>The viewer's main window.</summary>
    public Window Window { get; }

    /// <summary>
    /// The log file THIS launch is writing, or null if it has not appeared yet.
    /// </summary>
    /// <remarks>
    /// **The viewer stamps each run's log with the time it started**, keeping the last fifty, so
    /// there is no fixed <c>viewer.log</c> to read. Two UI fixtures went on reading that name after
    /// the change and counted a file that does not exist: <see cref="Count"/> answers zero for a
    /// missing file, so every wait for "the map loaded" ran its full sixty seconds and then failed
    /// saying the map never loaded — while the viewer sat in front of the tester with the map
    /// plainly on screen. Three windows opened and nothing was ever done with them.
    ///
    /// That is the failure mode this project keeps meeting: **the instrument reported confidently
    /// about a quantity it was not measuring.** A log reader that cannot find its log must not be
    /// able to look like a log with nothing in it, so this returns null and the callers say so.
    ///
    /// Newest wins, but only among files written since this instance launched — the folder is full
    /// of previous runs, and the newest of those would answer questions about a viewer that exited
    /// minutes ago. The one-second slack covers the log being created a moment before the
    /// constructor runs, since the window has to exist before this object does.
    /// </remarks>
    public string? LogPath
    {
        get
        {
            if (!Directory.Exists(Folder))
            {
                return null;
            }

            string? newest = null;
            DateTime newestAt = _launched.AddSeconds(-30);

            foreach (string candidate in Directory.GetFiles(Folder, "viewer-*.log"))
            {
                DateTime written = File.GetLastWriteTime(candidate);

                if (written >= newestAt)
                {
                    newest = candidate;
                    newestAt = written;
                }
            }

            return newest;
        }
    }

    /// <summary>How many times this run's log contains a line.</summary>
    /// <param name="line">The text to count, as the viewer writes it.</param>
    /// <returns>The number of matching lines, or −1 when no log exists yet.</returns>
    /// <remarks>
    /// **−1 rather than 0 for "no log", because those are different answers.** Zero is a real
    /// measurement meaning the viewer has not done the thing yet; no log at all means nothing was
    /// measured. Collapsing them is what let a wrong path masquerade as a viewer that never loaded
    /// a map. Callers waiting for a count to rise are unaffected — −1 is below every threshold —
    /// but a caller that wants to report the difference now can.
    ///
    /// Opened shared, because the viewer holds the file open and appends to it while this reads. A
    /// plain <c>File.ReadAllText</c> throws an IOException here, intermittently, which reads as
    /// flake and is not.
    /// </remarks>
    public int Count(string line)
    {
        if (LogPath is not { } path)
        {
            return -1;
        }

        try
        {
            using FileStream file = new(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(file);

            int seen = 0;

            while (reader.ReadLine() is { } entry)
            {
                if (entry.Contains(line, StringComparison.Ordinal))
                {
                    seen++;
                }
            }

            return seen;
        }
        catch (IOException)
        {
            // The viewer was mid-write. Reporting nothing lets the retry loop ask again, which is
            // the only correct answer available at this instant.
            return -1;
        }
    }

    /// <summary>Launches the viewer, optionally opening paths at startup.</summary>
    /// <param name="arguments">Files or folders, as a file association would pass them.</param>
    /// <returns>The running application.</returns>
    /// <exception cref="FileNotFoundException">The viewer has not been built.</exception>
    public static ViewerApplication Launch(params string[] arguments)
    {
        string executable = LocateExecutable();

        // **Geometry is inherited, never forced.** The viewer honours TF2VIEW_WINDOW_SIZE and
        // TF2VIEW_WINDOW_POS if they are set, and these tests simply do not set them - so a local
        // run uses the ordinary window, which is what a user actually sees. Pinning every run to
        // CI's small window would hide anything that only breaks at a normal size.
        //
        // To reproduce a CI-only layout failure, set both before running:
        //   $env:TF2VIEW_WINDOW_SIZE = "754x512"; $env:TF2VIEW_WINDOW_POS = "85,78"
        //
        // Both, not just the size. With only a size the window sits at (0,0), where screen
        // coordinates and window-relative coordinates are the same number, so any confusion
        // between the two is invisible - PokemonBattleJournal lost real time to exactly that.
        Log($"window geometry: size={Environment.GetEnvironmentVariable("TF2VIEW_WINDOW_SIZE") ?? "default"}, " +
            $"pos={Environment.GetEnvironmentVariable("TF2VIEW_WINDOW_POS") ?? "default"}");

        Application application = arguments.Length == 0
            ? Application.Launch(executable)
            : Application.Launch(executable, string.Join(' ', arguments));

        UIA3Automation automation = new();

        // GetMainWindow polls until the window exists rather than assuming it is up, which
        // matters most on the first run after a build when the process starts cold.
        Window window = application.GetMainWindow(automation, FindTimeout)
            ?? throw new InvalidOperationException(
                $"The viewer's main window did not appear within {FindTimeout}.");

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
                $"No element with automation id '{automationId}' appeared within {FindTimeout}. " +
                $"The live tree was:{Environment.NewLine}{DescribeTree()}");
    }

    /// <summary>Brings the viewer to the foreground so keystrokes reach it.</summary>
    /// <remarks>
    /// Synthesized keyboard input goes to whatever window has focus, not to the process that was
    /// launched - so a test that presses a key without doing this types into whatever happens to
    /// be in front. That is the same hazard that makes UI tests take the machine-wide lock.
    ///
    /// **SetForegroundWindow is not enough, and fails silently.** Windows refuses a foreground
    /// steal from a process that does not already own the foreground or the input queue, and
    /// returns without error - measured here as `hasFocus=False` while every key press vanished.
    /// A real mouse click is granted focus legitimately, because that is a user action, so the
    /// fallback clicks the window and re-checks rather than trusting the first attempt.
    /// </remarks>
    public void Focus()
    {
        Window.SetForeground();

        if (HasFocus())
        {
            return;
        }

        Log("SetForeground did not take; clicking the title bar to acquire focus");

        // The title bar rather than the client area: a click inside the viewport would land on
        // whatever control is being tested and could press it.
        Window.Click();

        Retry.WhileFalse(() => HasFocus(), TimeSpan.FromSeconds(5));
        Log($"focus acquired: {HasFocus()}");
    }

    /// <summary>Whether keyboard input will reach the viewer.</summary>
    /// <remarks>
    /// **The question is where a keystroke lands, not which element reports focus.** This used to
    /// read <c>Window.Properties.HasKeyboardFocus</c>, which is the TOP-LEVEL window's own flag -
    /// and that is false whenever focus legitimately sits on a child, which on this form it always
    /// does: the playlist takes it the moment the window opens. So the check failed on a window
    /// that was foreground and typable, the caller clicked the title bar, then waited out a
    /// five-second retry for a flag that could never become true.
    ///
    /// Nothing about that was flake and nothing about it was the application. It cost five seconds
    /// of every test that focuses the window, and it made a failure elsewhere read as "the viewer
    /// would not take focus".
    ///
    /// Asking the automation system which element has focus, and whether that element belongs to
    /// our process, answers the question actually being asked.
    /// </remarks>
    public bool HasFocus()
    {
        try
        {
            AutomationElement? focused = _automation.FocusedElement();

            return focused is not null &&
                focused.Properties.ProcessId.ValueOrDefault == _application.ProcessId;
        }
        catch (Exception failure) when (
            failure is COMException or TimeoutException or PropertyNotSupportedException)
        {
            // A window closing under the query, or an element that has gone away between the two
            // calls. Reported rather than swallowed: a focus check that quietly says "no" is how a
            // test spends its retry budget on a question nobody answered.
            Log($"focus check failed: {failure.Message}");

            return false;
        }
    }

    /// <summary>Writes a diagnostic line, flushed so a CI log keeps it on a crash.</summary>
    /// <param name="message">What happened.</param>
    /// <remarks>
    /// Prefixed and flushed, mirroring PokemonBattleJournal's UI tests - diagnosing a UI failure
    /// from a CI log without this is guesswork, and an unflushed line is exactly the one lost
    /// when a test host dies.
    /// </remarks>
    public static void Log(string message)
    {
        Console.WriteLine($"[viewer-ui] {message}");
        Console.Out.Flush();
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

    /// <summary>Describes the live automation tree, for when a find fails.</summary>
    /// <returns>One line per element: control type, automation id and name.</returns>
    /// <remarks>
    /// **Worth logging on any failure.** "No element with automation id X" says nothing about what
    /// WAS there - whether the id is wrong, the control never got a handle, or the window found
    /// was a dialog rather than the shell. The tree answers all three at once.
    /// </remarks>
    public string DescribeTree()
    {
        System.Text.StringBuilder description = new();
        Describe(Window, 0, description);
        return description.ToString();
    }

    private static void Describe(AutomationElement element, int depth, System.Text.StringBuilder into)
    {
        into.Append(' ', depth * 2)
            .Append(element.ControlType)
            .Append(" id='").Append(element.AutomationId)
            .Append("' name='").Append(element.Name)
            .AppendLine("'");

        // Depth-capped: a WinForms tree is shallow, and an unbounded walk on a failure path is a
        // second failure waiting to happen.
        if (depth >= 6)
        {
            return;
        }

        foreach (AutomationElement child in element.FindAllChildren())
        {
            Describe(child, depth + 1, into);
        }
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
