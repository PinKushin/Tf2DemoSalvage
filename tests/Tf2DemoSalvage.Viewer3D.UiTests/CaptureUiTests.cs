using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private const string DemoName = "tf2-2008-build3420-spec-stv-cp_granary.dem";

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
            (int Exit, string Errors) run = Capture(DemoPath, png, LongEnoughToBeAHang);

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

    /// <summary>
    /// What a worktree holds in place of a demo when <c>git lfs pull</c> has not run: three lines of text.
    /// </summary>
    private const string LfsPointer =
        "version https://git-lfs.github.com/spec/v1\n" +
        "oid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\n" +
        "size 12345\n";

    /// <summary>
    /// Far below the ~90-second minimum the patience backstop took to give up on a failed load, and far
    /// above a window opening and closing (~3 s).
    /// </summary>
    private static readonly TimeSpan FailsFast = TimeSpan.FromSeconds(45);

    /// <summary>
    /// A file that is not a demo must end a headless capture at once, non-zero, naming the file and why.
    /// </summary>
    /// <remarks>
    /// **Observed on an LFS-pointer worktree**: the load failed in milliseconds and logged why, then
    /// `--shot` waited out `PatienceFrames` drawing an empty viewport, and exited zero having written
    /// nothing — the capture test above reported a 180-second hang instead of a missing demo.
    /// </remarks>
    [Test]
    public void Capture_OnAnLfsPointerInsteadOfADemo_ExitsNonZeroNamingTheFileAndTheHeader()
    {
        string folder = Path.Combine(
            Path.GetTempPath(), "tf2ds-shot", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        string pointer = Path.Combine(folder, "pointer.dem");
        string png = Path.Combine(folder, "capture.png");

        File.WriteAllText(pointer, LfsPointer);

        try
        {
            (int Exit, string Errors) run = Capture(pointer, png, FailsFast);

            run.Exit.ShouldBe(1, $"standard error:{Environment.NewLine}{run.Errors}");
            run.Errors.ShouldContain("pointer.dem");
            run.Errors.ShouldContain("invalid demo header ID");
            File.Exists(png).ShouldBeFalse("a capture of no demo was written.");
        }
        finally
        {
            Clean(folder);
        }
    }

    /// <summary>
    /// A capture handed more than one path must end at once, non-zero, naming them — it has no demo to wait for.
    /// </summary>
    /// <remarks>
    /// **Observed 2026-10-11 through `run-exclusive.ps1`**, whose `Start-Process -ArgumentList` joins its arguments
    /// with spaces and no quotes: a demo under a folder with a space in its name reached the viewer as three paths,
    /// which is "a playlist, none opened on its own" — and `--shot` then drew 30,000 empty frames and exited ZERO
    /// with nothing written. The shot script's `test -f` passed on the previous run's picture.
    /// </remarks>
    [Test]
    public void Capture_OfSeveralPathsInsteadOfOne_ExitsNonZeroNamingThem()
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
            // The halves of a path split at its space, as the viewer received them.
            (int Exit, string Errors) run = Capture(
                Path.Combine(folder, "with"), png, FailsFast, ShotTick, Path.Combine("space", "a.dem"));

            run.Exit.ShouldBe(1, $"standard error:{Environment.NewLine}{run.Errors}");
            run.Errors.ShouldContain("2 paths");
            run.Errors.ShouldContain("a.dem");
            File.Exists(png).ShouldBeFalse("a capture of no demo was written.");
        }
        finally
        {
            Clean(folder);
        }
    }

    /// <summary>
    /// <c>--spectate</c> with a name nobody in the demo has must end a capture at once, non-zero, naming the name.
    /// </summary>
    /// <remarks>
    /// It logged a warning and "chose normally" — the free camera on an STV demo — so a picture asked for of one
    /// player was a picture of the map, exit zero. An interactive window keeps the warning and the free camera.
    /// </remarks>
    [Test]
    public void Capture_SpectatingANameNobodyHas_ExitsNonZeroNamingIt()
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
            (int Exit, string Errors) run = Capture(
                DemoPath, png, LongEnoughToBeAHang, ShotTick, "--spectate", "nobody-by-this-name");

            run.Exit.ShouldBe(1, $"standard error:{Environment.NewLine}{run.Errors}");
            run.Errors.ShouldContain("--spectate nobody-by-this-name");
            File.Exists(png).ShouldBeFalse("a capture of the wrong subject was written.");
        }
        finally
        {
            Clean(folder);
        }
    }

    /// <summary><c>mat_hdr_level 1</c> must bloom a real frame that <c>mat_hdr_level 0</c> does not (B537).</summary>
    /// <remarks>
    /// `GetBloomAmount` (`viewpostprocess.cpp:1429-1463`) blooms at level 1 on an HDR map, at 0.3 of the amount, and not at
    /// 0. Bloom is ADDED to the frame, so level 1's picture is the brighter. Measured on this capture 2026-10-14: the
    /// summed channel means rose 6.3 (140.0/103.2/72.1 to 143.1/105.3/73.1); before B537 the two were the same picture.
    /// Not a pixel-wise "no darker" — 57 of 542,184 pixels were, a capture being not perfectly repeatable.
    /// </remarks>
    [Test]
    public void Capture_AtHdrLevel1AgainstLevel0_IsBrighterByTheBloom()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
            return;
        }

        string folder = Path.Combine(Path.GetTempPath(), "tf2ds-shot", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            double ldr = MeanAt(folder, 0);
            double bloomed = MeanAt(folder, 1);

            TestContext.Out.WriteLine($"HDRLEVEL summed channel means: level 0 {ldr:0.00}, level 1 {bloomed:0.00}");

            (bloomed - ldr).ShouldBeGreaterThan(3.0, "level 1 is LDR with bloom, so it is brighter than level 0");
        }
        finally
        {
            Clean(folder);
        }
    }

    /// <summary>One capture at a <c>mat_hdr_level</c>, read back as the sum of its three channel means.</summary>
    private static double MeanAt(string folder, int level)
    {
        string png = Path.Combine(folder, $"level{level}.png");
        (int Exit, string Errors) run = Capture(
            DemoPath, png, LongEnoughToBeAHang, ShotTick, "+mat_hdr_level", level.ToString(CultureInfo.InvariantCulture));

        run.Exit.ShouldBe(0, $"standard error:{Environment.NewLine}{run.Errors}");

        using System.Drawing.Bitmap picture = new(png);
        long sum = 0;

        for (int y = 0; y < picture.Height; y++)
        {
            for (int x = 0; x < picture.Width; x++)
            {
                System.Drawing.Color pixel = picture.GetPixel(x, y);
                sum += pixel.R + pixel.G + pixel.B;
            }
        }

        return (double)sum / (picture.Width * picture.Height);
    }

    /// <summary>The eight bytes every PNG starts with.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Runs one headless capture to completion.</summary>
    /// <param name="demo">The file named on the command line.</param>
    /// <param name="png">Where the capture is asked to go.</param>
    /// <param name="hang">How long before the run is called hung and killed.</param>
    /// <param name="tick">The tick to capture.</param>
    /// <param name="extra">More arguments — console commands such as <c>+cl_drawmonitors 0</c>.</param>
    /// <returns>The process's exit code and everything it wrote to standard error.</returns>
    internal static (int Exit, string Errors) Capture(
        string demo, string png, TimeSpan hang, int tick = ShotTick, params string[] extra) =>
        Capture(null, demo, png, hang, tick, extra);

    /// <summary>Runs one headless capture to completion, with variables set for the viewer only.</summary>
    /// <param name="environment">Such as <c>TF2VIEW_CAMERA</c>; null for none.</param>
    /// <param name="demo">The file named on the command line.</param>
    /// <param name="png">Where the capture is asked to go.</param>
    /// <param name="hang">How long before the run is called hung and killed.</param>
    /// <param name="tick">The tick to capture.</param>
    /// <param name="extra">More arguments.</param>
    /// <returns>The process's exit code and everything it wrote to standard error.</returns>
    internal static (int Exit, string Errors) Capture(
        System.Collections.Generic.IReadOnlyDictionary<string, string>? environment,
        string demo, string png, TimeSpan hang, int tick, params string[] extra)
    {
        ProcessStartInfo start = new(ViewerApplication.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        // **Nothing of the test runner's own `TF2VIEW_*` reaches the capture** (D219): a `TF2VIEW_CAMERA` left in a
        // developer's shell would move every golden's frame. **Except the window's geometry on CI**, which `test.yml`
        // sets job-wide to fit its small screen; a golden passes its own view size anyway.
        bool onCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));

        foreach (string inherited in start.Environment.Keys
            .Where(name => name.StartsWith("TF2VIEW_", StringComparison.OrdinalIgnoreCase)
                && !(onCi && name is (MainForm.WindowSizeVariable or MainForm.WindowPositionVariable)))
            .ToList())
        {
            start.Environment.Remove(inherited);
        }

        foreach ((string name, string value) in environment ?? new System.Collections.Generic.Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }

        foreach (string argument in (string[])
        [
            .. ViewerApplication.Silent,
            demo,
            "--tick", tick.ToString(CultureInfo.InvariantCulture),
            "--shot", png,
            .. extra,
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
        if (!viewer.WaitForExit(hang))
        {
            viewer.Kill(entireProcessTree: true);

            Assert.Fail(
                $"the capture did not finish within {hang.TotalSeconds:0} seconds. " +
                $"Standard error so far:{Environment.NewLine}{errors}");
        }

        return (viewer.ExitCode, errors.ToString());
    }

    /// <summary>Removes this test's capture folder, reporting rather than throwing if it cannot.</summary>
    /// <remarks>
    /// **Its own folder per run**, for the reason `ViewerApplication` gives: this suite once deleted
    /// every hand-taken screenshot the project had by writing into the folder the owner uses.
    /// </remarks>
    internal static void Clean(string folder)
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
