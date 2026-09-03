using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the command line asked for, and whatever it did not recognise.</summary>
/// <param name="Settings">The config, with any <c>+command value</c> applied over it.</param>
/// <param name="Paths">Everything that was not an option: the demos to open.</param>
/// <param name="ShotPath">Where an automatic capture goes, or null when none was asked for.</param>
/// <param name="ShotTick">Which tick to show before capturing.</param>
/// <param name="FirstPerson">Whether the capture is taken through a player's eyes.</param>
/// <param name="ThirdPerson">
/// Whether the capture is taken over a player's shoulder — the chase camera, `OBS_MODE_CHASE`.
/// The owner asked for it while checking that a reload plays on a running player's arms, which is
/// a claim about a BODY and cannot be judged from first person or from the overhead free camera
/// (D134).
/// </param>
/// <param name="LookAt">Where the overhead camera is centred, or null to frame the map.</param>
/// <param name="Zoom">The overhead camera's zoom.</param>
/// <param name="SurfaceColours">Whether the capture uses the surface-category view.</param>
/// <param name="Spectate">Which entity to follow, or null to choose automatically.</param>
/// <param name="AutoPlay">Whether playback starts as soon as a demo is loaded.</param>
/// <param name="MeasureSeconds">
/// How many seconds of PLAYBACK to run before printing the frame-cost summary and exiting, or null
/// for an ordinary interactive run.
/// </param>
/// <param name="ShowHelp">Whether to print the options and exit without opening anything.</param>
public readonly record struct LaunchOptions(
    ViewerSettings Settings,
    IReadOnlyList<string> Paths,
    string? ShotPath = null,
    int ShotTick = 0,
    bool FirstPerson = false,
    (float X, float Y)? LookAt = null,
    float Zoom = 1f,
    bool SurfaceColours = false,
    int? Spectate = null,
    bool AutoPlay = false,
    double? MeasureSeconds = null,
    bool ShowHelp = false,
    bool ThirdPerson = false);

/// <summary>Reads the viewer's launch options.</summary>
/// <remarks>
/// **This was <c>MainForm.ReadCaptureOptions</c>, writing into eight fields as it went** (B188,
/// D90). Parsing a command line is not window work, and nothing about it could be tested where it
/// was: reaching it meant constructing a form, so every option was covered only by whichever UI
/// test happened to pass one.
///
/// **`+command value` is Valve's own mechanism, not a second spelling of it.** Source sets a cvar at
/// startup exactly this way, and this viewer already speaks Source's vocabulary in its config
/// (D69, D70) — so the command line hands the string to the SAME parser reading the SAME command
/// names. Every setting the config understands is settable from the command line for free, and an
/// unknown command is ignored here exactly as it is in a config.
///
/// **It overrides the config rather than merging into it.** A value passed for one launch must not
/// become the value for every later launch — which is also what makes it usable from the UI suite,
/// which redirects its captures without editing, and therefore without clobbering, the settings the
/// owner actually uses.
///
/// **Every malformed value is reported rather than ignored.** A mistyped tick that quietly captures
/// tick zero is a picture of the wrong moment, which is worse than no picture.
/// </remarks>
public static class LaunchOptionsReader
{
    /// <summary>Reads the arguments over a starting configuration.</summary>
    /// <param name="arguments">The command line, less the executable.</param>
    /// <param name="settings">The config as loaded, which options override.</param>
    /// <param name="log">Where a malformed or unrecognised value is reported.</param>
    /// <returns>What was asked for.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static LaunchOptions Read(
        IReadOnlyList<string> arguments, ViewerSettings settings, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(log);

        List<string> paths = [];
        Queue<string> pending = new(arguments);

        LaunchOptions read = new(settings, paths);

        // A queue rather than an indexed loop: an option consumes the value after it, and moving a
        // loop counter from inside the body is the shape analyzers rightly object to.
        while (pending.Count > 0)
        {
            string argument = pending.Dequeue();

            if (argument == "--shot" && pending.Count > 0)
            {
                read = read with { ShotPath = pending.Dequeue() };
                continue;
            }

            if (argument == "--look" && pending.Count > 1)
            {
                string x = pending.Dequeue();
                string y = pending.Dequeue();

                if (Number(x) is { } worldX && Number(y) is { } worldY)
                {
                    read = read with { LookAt = (worldX, worldY) };
                    continue;
                }

                log.LogWarning("{Message}", $"--look {x} {y} is not a position; ignoring it");
                continue;
            }

            if (argument == "--colours")
            {
                read = read with { SurfaceColours = true };
                continue;
            }

            // **An option because it was an environment variable, and that is the whole reason it
            // had no coverage.** `TF2VIEW_AUTOPLAY` had exactly one reference in the repository —
            // its own declaration — so nothing set it, nothing asserted it, and its ordering broke
            // three separate times without a single test going red.
            //
            // A process-wide variable also cannot be exercised without setting it for every test in
            // the run, which is why the one place that read it had to be the window that owns the
            // process. As an option it is per-launch, and a test can simply pass it.
            //
            // The variable still works, because a shell somewhere may already export it; see
            // `MainForm.AutoPlayVariable`.
            if (argument == "--autoplay")
            {
                read = read with { AutoPlay = true };
                continue;
            }

            if (argument.StartsWith('+') && argument.Length > 1 && pending.Count > 0)
            {
                string command = argument[1..];
                string value = pending.Dequeue();

                read = read with
                {
                    Settings = ViewerSettings.Parse(
                        string.Create(CultureInfo.InvariantCulture, $"{command} \"{value}\""),
                        onto: read.Settings),
                };

                log.LogInformation("{Message}", $"{command} {value} (from the command line)");
                continue;
            }

            // **The capture a person actually wants to look at is the first-person one**, and until
            // this flag existed the only route to it was the UI suite pressing V — which meant it
            // could only be taken on whichever demo that suite happens to open, at whichever tick it
            // could reach. See docs/findings/29 for what that produced: a picture of a wall at the
            // last tick of a solo recording.
            if (argument == "--first-person")
            {
                read = read with { FirstPerson = true };
                continue;
            }

            // **The capture that can answer a question about a BODY** (D134). A gesture layer plays
            // on a player's arms while their legs keep running, and neither of the other two
            // cameras can show it: first person draws the viewmodel, and the free camera opens
            // overhead where a person is a few pixels. The mode already existed — `SwitchCameraMode`
            // is the second stop of its cycle, on Space, which is where TF2 puts it — and only the
            // launch route was missing, so a headless check had to be driven through a keystroke or
            // not at all.
            //
            // **This comment and the summary above both said "the C key", and nothing has ever been
            // bound to C.** The owner read it in `--help` and corrected it: *"it shouldnt be C, its
            // space like it is in the source engine"* — which is what the code already did, so the
            // defect was entirely in the three places that described it. A wrong key in help text
            // is worse than none, because the reader presses it, nothing happens, and the feature
            // reads as broken.
            if (argument == "--third-person")
            {
                read = read with { ThirdPerson = true };
                continue;
            }

            // **Seconds of PLAYBACK, not of wall clock** — the distinction that made every
            // hand-driven measurement wrong. A run timed from process start spends its first twenty
            // seconds reading archives and building the map, so a "forty second" measurement was
            // about two seconds of frames. Only the viewer knows when playback began.
            if (argument == "--measure" && pending.Count > 0)
            {
                string seconds = pending.Dequeue();

                if (double.TryParse(
                        seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double run)
                    && run > 0)
                {
                    read = read with { MeasureSeconds = run };
                }
                else
                {
                    // Refused rather than absorbed, because a measurement that silently became an
                    // ordinary run is one somebody waits on and then reads nothing from.
                    log.LogWarning(
                        "{Message}", $"--measure wants a number of seconds; '{seconds}' is not one");
                }

                continue;
            }

            if (argument is "--help" or "-h" or "-?" or "/?")
            {
                read = read with { ShowHelp = true };
                continue;
            }

            if (argument == "--zoom" && pending.Count > 0)
            {
                string value = pending.Dequeue();

                if (Number(value) is { } zoom)
                {
                    read = read with { Zoom = zoom };
                    continue;
                }

                log.LogWarning("{Message}", $"--zoom {value} is not a number; ignoring it");
                continue;
            }

            // **Which player to watch, because otherwise there is no choosing.** The viewer
            // spectates whoever `SpectatorTarget.Choose` picks — the lowest entity index on a
            // playing team — and a match has eighteen players. Anything that happens to anybody else
            // is on screen for nobody, which made the off hand unviewable: z1800 carries six spies
            // with a watch drawn, and not one of them is ever the chosen target.
            if (argument == "--spectate" && pending.Count > 0)
            {
                string value = pending.Dequeue();

                if (Whole(value) is { } entity)
                {
                    read = read with { Spectate = entity };
                    continue;
                }

                log.LogWarning("{Message}", $"--spectate {value} is not a number; ignoring it");
                continue;
            }

            if (argument == "--tick" && pending.Count > 0)
            {
                string value = pending.Dequeue();

                if (Whole(value) is { } tick)
                {
                    read = read with { ShotTick = tick };
                    continue;
                }

                // Not silent: a mistyped tick that quietly captures tick zero is a picture of the
                // wrong moment, which is worse than no picture.
                log.LogWarning("{Message}", $"--tick {value} is not a number; capturing tick 0");
                continue;
            }

            paths.Add(argument);
        }

        return read;
    }

    /// <summary>A number as the command line spells it, or null.</summary>
    /// <remarks>Invariant culture: a launch option is not localised, and "1,5" is not 1.5 here.</remarks>
    private static float? Number(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float read)
            ? read
            : null;

    /// <summary>A whole number as the command line spells it, or null.</summary>
    private static int? Whole(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int read)
            ? read
            : null;
}
