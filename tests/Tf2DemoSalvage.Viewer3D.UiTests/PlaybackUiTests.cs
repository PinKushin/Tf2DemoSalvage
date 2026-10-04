using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Plays a real demo at speed and requires the viewer to finish on its own (B408) — formerly gate phase 3,
/// <c>build/playback-check.ps1</c>, now a counted test.
/// </summary>
/// <remarks>
/// **Every other fixture here holds one tick and never plays**, so a hang in the physics loop passed the whole UI suite for
/// days (B408). This is the one test that advances the clock.
///
/// **Its own viewer, never <see cref="ViewerSession"/>'s.** Playing moves the demo off
/// <see cref="ViewerSession.OpeningTick"/>, which every other test assumes, and seeking cannot put it back: Source cannot
/// seek to an exact tick. So it launches a separate process, as <see cref="CaptureUiTests"/> does, on the same committed
/// demo — <c>z1800</c>, gcor, so CI runs it too, where the script's lcor default made it a local-only check.
///
/// **At 8x, because fast-forward is what finds lifecycle bugs** (D189), passed as Valve's own <c>+demo_timescale</c>.
/// Twenty seconds of playback at 8x is 160 s of match, about 10,600 ticks past 20,000, well inside z1800's 57,551.
/// </remarks>
public sealed class PlaybackUiTests
{
    /// <summary>Seconds of PLAYBACK to measure — <c>--measure</c> counts playback, not wall clock.</summary>
    private const int PlaybackSeconds = 20;

    private const string Speed = "8";

    /// <summary>A hang bound, not an expected duration: z1800 alone loads in about 100 s, plus 20 s of play.</summary>
    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(420);

    /// <summary>The heading <c>MainForm.MeasuredPlaybackHeading</c> prints to standard output.</summary>
    private static readonly Regex Heading = new(
        @"measured (?<played>[\d.]+) seconds of playback, (?<samples>\d+) samples \((?<rebuilds>\d+) rebuild reports\), ending at tick (?<endTick>\d+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Test]
    public void Play_AtEightTimesSpeedFromTheOpeningTick_ReachesALaterTickInContinuousRateReportsAndExits()
    {
        if (!System.IO.File.Exists(ViewerSession.DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {ViewerSession.DemoPath}.");
            return;
        }

        string output = Play();

        Match measured = Heading.Match(output);

        measured.Success.ShouldBeTrue($"the viewer exited without its measurement:{Environment.NewLine}{output}");

        double played = double.Parse(measured.Groups["played"].Value, CultureInfo.InvariantCulture);
        int samples = int.Parse(measured.Groups["samples"].Value, CultureInfo.InvariantCulture);
        int endTick = int.Parse(measured.Groups["endTick"].Value, CultureInfo.InvariantCulture);

        played.ShouldBeGreaterThanOrEqualTo(PlaybackSeconds, output);

        // **Half the ideal, derived rather than guessed:** a rate report needs at least one second (FrameRateLog's interval)
        // of on-screen frame time and overshoots by the frame that crossed it, so the ideal 20 s run is 19-20 reports. A mean
        // interval under twice nominal is still continuous sampling.
        samples.ShouldBeGreaterThanOrEqualTo((PlaybackSeconds + 1) / 2, output);

        // **The samples must be of a demo, not of the loading screen** (D182): the tick the frame drew when measuring
        // stopped, past the one it started from. A rebuild count cannot prove it — that line prints once per hundred.
        endTick.ShouldBeGreaterThan(ViewerSession.OpeningTick, output);
    }

    /// <summary>Runs the viewer to its own exit and returns everything it wrote.</summary>
    private static string Play()
    {
        ProcessStartInfo start = new(ViewerApplication.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (string argument in (string[])
        [
            ViewerSession.DemoPath,
            "--tick", ViewerSession.OpeningTick.ToString(CultureInfo.InvariantCulture),
            "--autoplay",
            "--measure", PlaybackSeconds.ToString(CultureInfo.InvariantCulture),
            "+demo_timescale", Speed,
        ])
        {
            start.ArgumentList.Add(argument);
        }

        using Process viewer = Process.Start(start)
            ?? throw new InvalidOperationException($"the viewer at {ViewerApplication.ExecutablePath} did not start.");

        // Drained on handlers, not after the wait: a full pipe blocks the writer, deadlocking exactly the run being observed.
        StringBuilder output = new();
        object gate = new();

        viewer.OutputDataReceived += (_, line) => { lock (gate) { output.AppendLine(line.Data); } };
        viewer.ErrorDataReceived += (_, line) => { lock (gate) { output.AppendLine(line.Data); } };

        viewer.BeginOutputReadLine();
        viewer.BeginErrorReadLine();

        if (!viewer.WaitForExit(LongEnoughToBeAHang))
        {
            // Its OWN process id, never the image name, which would take the shared session's viewer with it.
            viewer.Kill(entireProcessTree: true);

            Assert.Fail(
                $"the viewer was still running after {LongEnoughToBeAHang.TotalSeconds:0} s; playback hung. " +
                $"Output so far:{Environment.NewLine}{output}");
        }

        // The parameterless wait returns only once the redirected streams have hit end of file.
        viewer.WaitForExit();

        lock (gate)
        {
            return output.ToString();
        }
    }
}
