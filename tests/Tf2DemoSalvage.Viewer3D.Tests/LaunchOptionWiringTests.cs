using System.IO;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Launch options reaching a real window, rather than merely being parsed.
/// </summary>
/// <remarks>
/// **This level exists because both cheaper ones are structurally blind to the bug it catches.**
/// `LaunchOptionsTests` proves the command line is READ; it cannot see whether anything obeys it.
/// `DemoSystemsTests` proves autoplay starts the clock — and cannot reach that path at all, because
/// <c>DemoTimeline</c>'s constructor is private, so every test there passes <c>timeline: null</c>
/// and returns before the clock is built.
///
/// So the only instrument that can fail is a real <see cref="MainForm"/> loading a real demo, which
/// is <c>docs/memory/three-test-levels-and-the-third-is-missing.md</c> stated for launch options.
///
/// **The bug it was written for.** `Apply` called <c>DemoSystems.Open</c>, which starts playback,
/// and then <c>_transport.SetDemoLength</c>, whose last act is <c>Playing = false</c>. That setter
/// deliberately does not raise <c>PlayPauseToggled</c> — it would re-enter the presenter — so
/// nothing logged it and nothing failed. The viewer wrote *"playback started at load"* and then sat
/// paused for ever, which is exactly what the owner reported.
///
/// It is the THIRD time autoplay's ordering has broken. The other two are written up in
/// <c>DemoSystems.Open</c>, whose remarks say the shape "removes rather than documents" the hazard.
/// It removed half of it: the order inside `Open` was safe and the order around it was not.
///
/// **No window is shown and no device is created.** `LoadDemo` runs the whole load synchronously,
/// and the map read inside it is allowed to fail — `Apply` carries on and says "(map not found)" —
/// so this passes on a machine with no Team Fortress 2 installed, which is what CI is.
///
/// **A corpus demo rather than a synthetic one, and that is a deliberate cost.** Autoplay needs a
/// CLOCK, the clock needs <c>IntervalPerTick</c>, and that comes from a decoded timeline — which
/// needs a <c>dem_datatables</c> carrying a real schema. `SyntheticDemo` can write one and is
/// `internal` to `Core.Tests`; reaching it from here would mean a test project referencing another
/// test project, and copying it would duplicate `SyntheticSchema` as well. Skipping when the demo
/// is absent is the price, and <c>Assert.Ignore</c> says so rather than passing quietly.
/// </remarks>
/// <remarks>Serial, because this constructs a Windows Form — see B178.</remarks>
[NonParallelizable]
public sealed class LaunchOptionWiringTests
{
    /// <summary>The committed era specimen the UI suite also opens.</summary>
    private static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", "tf2-2013-build1729296-pov-cp_badlands.dem"));

    [Test]
    public void LoadDemo_WithAutoplay_LeavesTheTransportPlaying()
    {
        RequireTheDemo();

        using MainForm form = new("--autoplay", DemoPath);

        form.LoadDemo(DemoPath);

        // **The transport, not a log line.** The viewer already LOGGED that playback started; the
        // defect was that something switched it off immediately afterwards, so the only faithful
        // measurement is the flag `Advance` actually reads.
        form.Transport.Playing.ShouldBeTrue(
            "--autoplay must survive the rest of the load: DemoSystems.Open starts playback and "
            + "anything resetting the transport after it silently stops the demo for ever");

        // **The half that a careless fix would break, asserted on the SAME load rather than in its
        // own test.** `SetDemoLength` does two things — it sizes and enables the scrub bar, and it
        // clears the playing flag — so a fix that merely deleted the call would leave a demo that
        // plays and cannot be scrubbed, which is worse than the bug and would satisfy the line
        // above. One load, two measurements: a separate test would pay another map read for
        // nothing, and this fixture already costs about eighteen seconds a case.
        form.Transport.LastTick.ShouldBeGreaterThan(
            0, "the scrub bar is sized by the same call that was clearing the playing flag");
    }

    [Test]
    public void LoadDemo_WithoutAutoplay_LeavesTheTransportPaused()
    {
        // **The control, and without it the test above cannot fail for the right reason.** A viewer
        // that played every demo it opened would satisfy the assertion above while ignoring the
        // option entirely — which is the "wrong condition" case from the testing standards: correct
        // and broken predict the same observation.
        RequireTheDemo();

        using MainForm form = new(DemoPath);

        form.LoadDemo(DemoPath);

        form.Transport.Playing.ShouldBeFalse("nothing asked for playback");
    }

    private static void RequireTheDemo()
    {
        if (!File.Exists(DemoPath))
        {
            Assert.Ignore(
                $"The corpus demo is not present at {DemoPath}, and autoplay needs a decoded "
                + "timeline to build a clock over.");
        }
    }
}
