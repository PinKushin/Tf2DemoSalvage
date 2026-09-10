using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// `--shot` loads, seeks, draws, writes a PNG and exits — and until now nothing checked it (B387).
/// </summary>
/// <remarks>
/// **The whole option was covered by nobody**, which `MainForm` has said in a comment since B196:
/// *"Nothing failed. No test passes `--shot`, so the whole option was covered by nobody."* That was
/// written after the line applying the option went missing for a day. B386 is the second time the
/// path broke silently, and it is the worse kind: the process died on an unhandled exception before
/// writing its PNG, so a headless capture run produced no file and no log entry — the viewer installs
/// no unhandled-exception handler, so the buffered log simply stops mid-frame.
///
/// **An instrument that fails part of the time halves the evidence gathered with it**, and nothing
/// downstream can tell a crashed run from one nobody launched.
///
/// **Why this cannot reuse the shared viewer.** Every other fixture here drives one long-lived
/// application, which is deliberate — see the remarks on <see cref="ViewerSession"/>. `--shot` is a
/// process that closes itself the moment the frame is taken, so there is nothing to attach to and
/// nothing to keep. The overlap with the screenshot key is only <c>CaptureViewport</c>; the part that
/// broke — the opening sequence, the seek, the capture and the exit — is reachable no other way.
///
/// **The subject is an ERA specimen, deliberately, and that is what makes this affordable.** Measured
/// warm, one capture costs 12 seconds on `cp_granary` and 12 on `koth_viaduct`, against **100 seconds**
/// on `z1800` — the demo every other fixture here uses. The whole difference is `koth_harvest_final`
/// and twelve players with cosmetics, and the first version of this test paid all of it for nothing:
/// 2 minutes 6 seconds against a 53-second suite, which is worse than the trade the owner refused for
/// the load tests (*"20 fucking seconds, no"*).
///
/// `CLAUDE.md` warns that era specimens cannot answer a rendering or roster question, and that is
/// right — **this test asks neither.** It asks whether the capture path ran to completion and wrote a
/// PNG, which needs no other players in the map. Being an old protocol is a second benefit: `--shot`
/// is proved on a 2008 recording rather than only on a modern one.
///
/// **What it does NOT catch, measured rather than assumed.** It does not reproduce B386. With the
/// fixed-stride addressing restored, this test passed on `z1800` at tick 20,000 — the pose-parameter
/// width change never bit there. Reaching B386 through `--shot` needs
/// `tools/corpus/local/20130518_0313_cp_granary_blu_blu` at tick 27,692, which is lcor, so CI would
/// skip it and a local run would pay about two minutes. That repro is written down in B386 instead of
/// being bought here. What this test does catch is the B196 class: the option silently doing nothing.
/// </remarks>
public sealed class CaptureUiTests
{
    /// <summary>The cheapest committed specimen that still exercises a real map and a real capture.</summary>
    /// <remarks>
    /// **Not <see cref="ViewerSession.DemoPath"/>**, which is `z1800` and costs eight times as much to
    /// load — see the measurements in the remarks above. Committed (gcor), so CI has it.
    /// </remarks>
    private const string DemoName = "tf2-2008-build3420-stv-cp_granary.dem";

    /// <summary>A tick inside the recording, well past the header.</summary>
    /// <remarks>
    /// **Not zero**, because demo ticks do not start there and a hardcoded zero reports plausibly
    /// while showing the moment before anything happened. The clock clamps, so this cannot land
    /// outside the recording; it only has to be somewhere a frame can be drawn.
    /// </remarks>
    private const int ShotTick = 1000;

    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", DemoName));
    /// <summary>How long a cold load, a seek and a capture may take before the run is called hung.</summary>
    /// <remarks>
    /// **Generous on purpose, and it is a HANG bound rather than an expected duration.** The map,
    /// its textures and every entity model load before the first frame; how long that takes is a
    /// property of the machine. `ViewerSession` waits 120 seconds on the same work, so this is that
    /// plus room for the capture and the shutdown.
    /// </remarks>
    private static readonly TimeSpan LongEnoughToBeAHang = TimeSpan.FromSeconds(180);

    [Test]
    public void Capture_AtATickOnACorpusDemo_ExitsCleanlyAndWritesThePng()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
            return;
        }

        string folder = Path.Combine(
            Path.GetTempPath(), "tf2ds-shot", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        string png = Path.Combine(folder, "capture.png");

        try
        {
            (int Exit, string Errors) run = Capture(png);

            // **The exit code first, and the standard error carried into the message.** This is the
            // whole point of the test: a capture that dies takes its stack with it, so the one place
            // that sees the stack has to be the thing that reports the failure. Asserting the file
            // alone would say "no PNG" and throw the reason away.
            run.Exit.ShouldBe(
                0,
                $"the viewer did not exit cleanly. Its standard error follows:{Environment.NewLine}{run.Errors}");

            File.Exists(png).ShouldBeTrue($"no capture was written to {png}.");

            // **A PNG, not merely a file.** A zero-byte or truncated write is the failure mode a
            // bare existence check cannot see, and the signature is eight bytes at a known offset.
            byte[] written = File.ReadAllBytes(png);

            written.Length.ShouldBeGreaterThan(
                PngSignature.Length, $"the capture at {png} holds no image data.");

            written[..PngSignature.Length].ShouldBe(
                PngSignature, $"the capture at {png} is not a PNG.");
        }
        finally
        {
            Clean(folder);
        }
    }

    /// <summary>The eight bytes every PNG starts with.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Runs one headless capture to completion.</summary>
    /// <param name="png">Where the capture is asked to go.</param>
    /// <returns>The process's exit code and everything it wrote to standard error.</returns>
    private static (int Exit, string Errors) Capture(string png)
    {
        ProcessStartInfo start = new(ViewerApplication.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (string argument in (string[])
        [
            DemoPath,
            "--tick", ShotTick.ToString(CultureInfo.InvariantCulture),
            "--shot", png,
        ])
        {
            start.ArgumentList.Add(argument);
        }

        using Process viewer = Process.Start(start)
            ?? throw new InvalidOperationException(
                $"the viewer at {ViewerApplication.ExecutablePath} did not start.");

        // **Read on their own handlers rather than after the wait.** A process whose pipe fills up
        // blocks writing to it, so draining stderr only after `WaitForExit` deadlocks exactly the
        // run this test exists to observe — the one that dies printing a long stack.
        StringBuilder errors = new();

        viewer.ErrorDataReceived += (_, line) => errors.AppendLine(line.Data);
        viewer.OutputDataReceived += (_, _) => { };

        viewer.BeginErrorReadLine();
        viewer.BeginOutputReadLine();

        // **The return value is checked**, because `WaitForExit(TimeSpan)` reporting false means the
        // process is still running and `ExitCode` would throw rather than answer.
        if (!viewer.WaitForExit(LongEnoughToBeAHang))
        {
            viewer.Kill(entireProcessTree: true);

            Assert.Fail(
                $"the capture did not finish within {LongEnoughToBeAHang.TotalSeconds:0} seconds. " +
                $"Standard error so far:{Environment.NewLine}{errors}");
        }

        return (viewer.ExitCode, errors.ToString());
    }

    /// <summary>Removes this test's capture folder, reporting rather than throwing if it cannot.</summary>
    /// <remarks>
    /// **Its own folder per run**, for the reason `ViewerApplication` gives: this suite once deleted
    /// every hand-taken screenshot the project had by writing into the folder the owner uses.
    /// </remarks>
    private static void Clean(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"could not remove {folder}: {failure.Message}");
        }
    }
}
