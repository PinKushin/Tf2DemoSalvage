using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Where the viewer says what it is doing.
/// </summary>
/// <remarks>
/// **This exists because three separate defects today were invisible rather than hard.** The world
/// shader failed to compile and the exception was caught and dropped, so the map drew as an outline
/// and looked exactly like a renderer nobody had wired up. The textured world was never uploaded
/// because the device did not exist yet, which looked identical. Both cost a build-and-screenshot
/// cycle each to find, and both would have been one line in a log.
///
/// So the rule here is: **anything that silently falls back must say so.** A caught exception that
/// leads to a degraded picture is exactly the event worth recording, because the picture itself
/// cannot report it.
///
/// Written to a file rather than the console: this is a WinForms application with no console
/// attached, and a log that only exists while a debugger is running is not there when it matters.
/// It is also mirrored to <see cref="Debug"/>, so the IDE shows it while developing.
///
/// **Failure to log never fails the caller.** A locked or unwritable log costs its lines, and
/// nothing else — a viewer that refuses to open a demo because it cannot write a log has the
/// priority backwards.
/// </remarks>
internal static class ViewerLog
{
    private static readonly Lock Gate = new();

    private static string? _path;
    private static bool _failed;

    /// <summary>The folder every run's log is written into.</summary>
    public static string Folder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage");

    /// <summary>Where this run's log is written.</summary>
    /// <remarks>
    /// **One file per run, stamped, and none of them overwritten.** This kept exactly two
    /// generations, and that was not enough: relaunching to look at one thing destroys the record
    /// of another, and the runs worth comparing are often several apart — a merge count from four
    /// launches ago against the same count now is what says whether a change did anything.
    ///
    /// Losing that costs a rebuild and a relaunch to recover something that was already measured,
    /// which is the expensive kind of mistake here because each cycle needs the machine's desktop.
    /// </remarks>
    /// <remarks>
    /// **The process id is in the name because more than one viewer can be running.** A UI run
    /// launches one per fixture, and anything reading "the newest viewer log" then reads whichever
    /// instance happened to write last — which during a suite is somebody else's. The stamp alone
    /// cannot separate them, and two viewers started in the same second share it exactly.
    ///
    /// With the id in the name a reader can ask for the log belonging to the process it launched
    /// instead of guessing from timestamps, and that answer stays right however many are alive.
    /// </remarks>
    public static string Path => _path ??= System.IO.Path.Combine(
        Folder,
        string.Create(
            CultureInfo.InvariantCulture,
            $"viewer-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log"));

    /// <summary>How many runs' logs to keep before the oldest are deleted.</summary>
    /// <remarks>
    /// **Pruned by count rather than by age**, because a quiet week should not throw away the last
    /// thing measured. Fifty runs of an ordinary session is a few megabytes.
    /// </remarks>
    private const int RunsKept = 50;

    /// <summary>Starts a new log for this run.</summary>
    /// <param name="version">What is running, for the header.</param>
    /// <remarks>
    /// **One file per run, named for when the run started, and nothing overwritten.** A log
    /// covering one session answers "what happened just now", which is the question anyone
    /// actually asks; a single growing file answers it worse.
    ///
    /// This kept two generations and rotated, which destroyed evidence twice. Once when an owner
    /// stress-tested full screen and a relaunch to look at something else took the measurements
    /// with it. Again while chasing a bone merge, where the useful comparison was against a run
    /// four launches back and the file holding it had long since been rotated out.
    ///
    /// A stamped name also removes a second problem: a reader with the current log open can no
    /// longer collide with the next run, because the next run writes somewhere else.
    /// </remarks>
    public static void Begin(string version)
    {
        lock (Gate)
        {
            _failed = false;

            try
            {
                Directory.CreateDirectory(Folder);
                Prune();

                File.WriteAllText(
                    Path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"TF2 Demo Salvage {version}, started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"),
                    Encoding.UTF8);
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _failed = true;
            }
        }
    }

    /// <summary>Deletes the oldest runs' logs once there are more than are kept.</summary>
    /// <remarks>
    /// **By this program's own naming, not by a wildcard over the folder.** The captures written
    /// by F12 live here too, and a sweep that deleted "old files" would take the screenshots
    /// somebody pressed a key to keep — the same class of mistake as pruning a shared measurement
    /// directory by a name glob and deleting a neighbour's run.
    ///
    /// A failure to prune is not a failure to log: an undeletable old file is a tidiness problem
    /// and losing this run's output is not, so it is swallowed deliberately rather than allowed to
    /// abort <see cref="Begin"/>.
    /// </remarks>
    private static void Prune()
    {
        try
        {
            string[] older = Directory.GetFiles(Folder, "viewer-*.log");

            if (older.Length < RunsKept)
            {
                return;
            }

            Array.Sort(older, StringComparer.Ordinal);

            for (int index = 0; index <= older.Length - RunsKept; index++)
            {
                File.Delete(older[index]);
            }
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Tidiness only. Say nothing, because the log itself is not open yet.
        }
    }

    /// <summary>Records something that happened.</summary>
    /// <param name="area">Which part of the program, such as <c>map</c> or <c>render</c>.</param>
    /// <param name="message">What happened.</param>
    public static void Write(string area, string message) => Append("     ", area, message);

    /// <summary>Records something that went wrong but was survivable.</summary>
    /// <param name="area">Which part of the program.</param>
    /// <param name="message">What went wrong, and what happens instead.</param>
    /// <remarks>
    /// **The important one.** Every degraded fallback in this application goes through here: a
    /// missing texture, a shader that will not compile, a map that cannot be read. The picture
    /// cannot report any of them, so this is the only place they appear.
    /// </remarks>
    public static void Warn(string area, string message) => Append("WARN ", area, message);

    /// <summary>Records an exception that was caught and handled.</summary>
    /// <param name="area">Which part of the program.</param>
    /// <param name="what">What was being attempted.</param>
    /// <param name="failure">The exception.</param>
    public static void Warn(string area, string what, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Append("WARN ", area, $"{what}: {failure.GetType().Name}: {failure.Message}");
    }

    /// <summary>Times an operation and records how long it took.</summary>
    /// <param name="area">Which part of the program.</param>
    /// <param name="what">What is being timed.</param>
    /// <returns>A scope that logs on disposal.</returns>
    /// <remarks>
    /// Timings belong in the log rather than in a comment: "the map took 1.1 seconds" is a claim
    /// that goes stale, and a line saying so on every load is a claim that cannot.
    /// </remarks>
    public static IDisposable Time(string area, string what) => new Timing(area, what);

    private static void Append(string level, string area, string message)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss.fff} {level}[{area}] {message}");

        Debug.WriteLine(line);

        if (_failed)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Stop trying rather than throwing on every line afterwards. See the type remarks:
                // a viewer that cannot write a log still has a demo to show.
                _failed = true;
            }
        }
    }

    private sealed class Timing(string area, string what) : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public void Dispose()
        {
            _clock.Stop();

            Append(
                "     ",
                area,
                string.Create(
                    CultureInfo.InvariantCulture, $"{what} took {_clock.Elapsed.TotalSeconds:F2}s"));
        }
    }
}
