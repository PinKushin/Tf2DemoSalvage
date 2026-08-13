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

    /// <summary>Where the log is written.</summary>
    public static string Path => _path ??= System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tf2DemoSalvage",
        "viewer.log");

    /// <summary>Where the previous run's log is kept.</summary>
    public static string Previous => System.IO.Path.ChangeExtension(Path, ".previous.log");

    /// <summary>Starts a new log for this run.</summary>
    /// <param name="version">What is running, for the header.</param>
    /// <remarks>
    /// **One run per file, and the previous run kept beside it.** A log covering one session
    /// answers "what happened just now", which is the question anyone actually asks; a growing file
    /// answers it worse.
    ///
    /// But truncating outright destroys evidence, and it did: an owner stress-tested full screen,
    /// the viewer was relaunched to look at something else, and the measurements were gone. The
    /// interesting run is very often the one BEFORE the current one, because noticing something
    /// worth investigating is what prompts the relaunch.
    ///
    /// One generation is enough. Two files answer "what happened just now" and "what happened the
    /// time before", and nothing beyond that has ever been wanted here.
    /// </remarks>
    public static void Begin(string version)
    {
        lock (Gate)
        {
            _failed = false;

            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

                if (File.Exists(Path))
                {
                    // Overwrites the older generation, which is the one nobody has asked for.
                    File.Copy(Path, Previous, overwrite: true);
                }

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
