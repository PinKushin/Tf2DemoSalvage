using System;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// Reading the viewer's launch options.
/// </summary>
/// <remarks>
/// **This was <c>MainForm.ReadCaptureOptions</c> and had no test of its own** (B188, D90). Reaching
/// it meant constructing a form, so every option was covered only by whichever UI test happened to
/// pass one — which is to say the ones the UI suite uses were exercised and the rest were not.
///
/// **Every malformed case is a PAIR with its well-formed twin**, because a parser that ignored an
/// option entirely passes any test that only checks the bad input is refused.
/// </remarks>
public sealed class LaunchOptionsTests
{
    [Test]
    public void Read_WithNoOptions_TreatsEverythingAsAPath()
    {
        // The ordinary launch: a demo, or several, and nothing else.
        LaunchOptions read = Read("a.dem", "b.dem");

        read.Paths.ShouldBe(["a.dem", "b.dem"]);
        read.ShotPath.ShouldBeNull();
    }

    [Test]
    public void Read_AnOptionAndItsValue_ConsumesBothRatherThanLeavingOneAsAPath()
    {
        // **The failure this shape produces is a phantom demo.** An option that consumed only its
        // own token would leave the VALUE in the path list, and the viewer would try to open a file
        // called "42".
        LaunchOptions read = Read("--tick", "42", "a.dem");

        read.ShotTick.ShouldBe(42);
        read.Paths.ShouldBe(["a.dem"]);
    }

    [Test]
    public void Read_AMalformedTick_KeepsTickZeroAndSaysSo()
    {
        // Not silent: a mistyped tick that quietly captures tick zero is a picture of the wrong
        // moment, which is worse than no picture.
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(["--tick", "soon"], ViewerSettings.Load(), log);

        read.ShotTick.ShouldBe(0);
        log.Count("--tick soon is not a number").ShouldBe(1);
    }

    [Test]
    public void Read_ALookPosition_IsTakenAsAPair()
    {
        // Two values, and both are consumed or neither is — a half-read pair leaves a number in the
        // path list.
        LaunchOptions read = Read("--look", "512", "-256", "a.dem");

        read.LookAt.ShouldBe((512f, -256f));
        read.Paths.ShouldBe(["a.dem"]);
    }

    [Test]
    public void Read_AMalformedLook_IsIgnoredAndSaysSo()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(
            ["--look", "middle", "ish"], ViewerSettings.Load(), log);

        read.LookAt.ShouldBeNull();
        log.Count("is not a position").ShouldBe(1);
    }

    /// <remarks>
    /// **An unrecognised option used to become a DEMO PATH, silently** (B357). The viewer then
    /// failed to open it, loaded no map, and said nothing — which is what
    /// <c>--surface-colours</c> did, a name this program's own <c>--help</c> advertised and this
    /// parser never accepted. Half an hour of a session went into photographing an empty screen.
    /// </remarks>
    [Test]
    public void Read_AnOptionItDoesNotKnow_IsReportedRatherThanTakenAsADemo()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(
            ["--surface-colours", "a.dem"], ViewerSettings.Load(), log);

        read.Paths.ShouldBe(["a.dem"], "an option is not a demo, however unrecognised");
        log.Count("is not an option this viewer knows").ShouldBe(1);
    }

    /// <remarks>
    /// The control, and the reason the test above uses a leading dash rather than a known list: a
    /// bare word IS a demo path, and a rule that reported everything it did not recognise would
    /// refuse every demo.
    /// </remarks>
    [Test]
    public void Read_ABareWord_IsStillTakenAsADemoPath()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(
            ["surface-colours.dem"], ViewerSettings.Load(), log);

        read.Paths.ShouldBe(["surface-colours.dem"]);
        log.Count("is not an option this viewer knows").ShouldBe(0);
    }

    [Test]
    public void Read_APlusCommand_AppliesItOverTheConfig()
    {
        // **Valve's own mechanism rather than a second spelling of it.** Source sets a cvar at
        // startup this way, and the string goes to the SAME parser reading the SAME command names
        // the config file uses (D69, D70) — so every setting is settable here for free.
        ViewerSettings before = ViewerSettings.Load();

        LaunchOptions read = Read("+fov_desired", "75");

        read.Settings.FieldOfView.ShouldBe(75f);
        read.Settings.ShouldNotBe(before, "the command line must override the config it was given");
    }

    [Test]
    public void Read_AnUnknownPlusCommand_IsIgnoredRatherThanRefused()
    {
        // **Ignoring what it does not implement is the primary feature, not an afterthought**
        // (D69). A real config is hundreds of mat_*/cl_* lines this viewer has never heard of, and
        // the command line speaks the same vocabulary.
        LaunchOptions read = Read("+mat_picmip", "-1", "a.dem");

        read.Paths.ShouldBe(["a.dem"], "the value must not be left behind as a demo to open");
    }

    [Test]
    public void Read_TheFlags_AreOffUnlessGiven()
    {
        // The control for the two flag cases below: a parser that set them unconditionally would
        // pass an assertion that only checks the flag is on when passed.
        LaunchOptions read = Read("a.dem");

        read.FirstPerson.ShouldBeFalse();
        read.SurfaceColours.ShouldBeFalse();
        read.Spectate.ShouldBeNull();
        read.Zoom.ShouldBe(1f);
        read.AutoPlay.ShouldBeFalse();
    }

    [Test]
    public void Read_Autoplay_IsAsked()
    {
        // **An option because it was an environment variable, and that is why it had no test.**
        // `TF2VIEW_AUTOPLAY` had exactly one reference in the repository — its own declaration —
        // so nothing set it, nothing asserted it, and the ordering it depends on broke three
        // times unnoticed. A process-wide variable also cannot be exercised without setting it
        // for every test in the run, which is the reason the one place that read it was a window.
        Read("--autoplay", "a.dem").AutoPlay.ShouldBeTrue();
    }

    [Test]
    public void Read_TheFlags_AreOnWhenGiven()
    {
        LaunchOptions read = Read("--first-person", "--colours", "a.dem");

        read.FirstPerson.ShouldBeTrue();
        read.SurfaceColours.ShouldBeTrue();
    }

    [Test]
    public void Read_ASpectateTarget_IsKept()
    {
        // Which player to watch, because otherwise there is no choosing: a match has eighteen
        // players and the viewer follows whichever one `SpectatorTarget.Choose` picks.
        Read("--spectate", "11").Spectate.ShouldBe(11);
    }

    /// <remarks>
    /// **This asserted the fall-through until B357, and the intent it recorded is why it changed.**
    /// The old comment read *"visible as a nonsense demo rather than silently doing nothing"* — so
    /// the goal was always that the user find out. Becoming a path achieved that only by accident,
    /// through a failed file open, and the same fall-through is what made `--surface-colours`
    /// invisible: a name `--help` advertised, silently filed as a demo, no map, nothing said.
    ///
    /// The warning serves the original intent directly. What the test still pins is the part that
    /// mattered most: the end-of-line case must not hang or throw.
    /// </remarks>
    [Test]
    public void Read_AnOptionWithItsValueMissing_IsReportedRatherThanTakenAsAPath()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(["--tick"], ViewerSettings.Load(), log);

        read.Paths.ShouldBeEmpty();
        read.ShotTick.ShouldBe(0);
        log.Count("is not an option this viewer knows").ShouldBe(1);
    }

    [Test]
    public void Read_WithNoArguments_Refuses()
    {
        Should.Throw<ArgumentNullException>(() =>
            LaunchOptionsReader.Read(null!, ViewerSettings.Load(), NullLogger.Instance));
    }

    [Test]
    public void Read_WithNoLogger_Refuses()
    {
        // Every malformed value is reported rather than returned, so a null sink is a caller mistake
        // rather than a quiet mode.
        Should.Throw<ArgumentNullException>(() =>
            LaunchOptionsReader.Read([], ViewerSettings.Load(), log: null!));
    }

    /// <remarks>
    /// **Written after a session spent driving the viewer by hand.** Every measurement was build,
    /// launch under the machine lock, wait for the process, sleep, kill it, grep the log — six calls
    /// each time, and the sleep was wall-clock so a "forty second" run was about two seconds of
    /// playback once loading had taken its share. `--measure` moves the clock to the only place that
    /// knows when playback actually started.
    /// </remarks>
    [Test]
    public void Read_WithMeasure_TakesTheSecondsToRunFor()
    {
        LaunchOptions read = Read("a.dem", "--measure", "45");

        read.MeasureSeconds.ShouldBe(45d);
        read.Paths.ShouldBe(["a.dem"]);
    }

    /// <remarks>
    /// The malformed twin, per this file's own rule: a parser that ignored the option entirely would
    /// pass the test above only if the bad case is checked too.
    /// </remarks>
    [Test]
    public void Read_WithMeasureAndNoNumber_LeavesItOffAndKeepsThePath()
    {
        LaunchOptions read = Read("a.dem", "--measure", "soon");

        read.MeasureSeconds.ShouldBeNull();
    }

    /// <remarks>
    /// **`--help` answers and stops.** The flag it was written for is `--first-person`, which
    /// existed and was reported in an audit as not existing, because the grep looking for it ran
    /// over the wrong project. A viewer that lists its own options answers that in one call.
    /// </remarks>
    [Test]
    public void Read_WithHelp_AsksForTheListAndOpensNoDemo()
    {
        LaunchOptions read = Read("a.dem", "--help");

        read.ShowHelp.ShouldBeTrue();
    }

    [Test]
    public void Read_WithoutHelp_DoesNotAskForTheList()
    {
        Read("a.dem").ShowHelp.ShouldBeFalse();
    }

    private static LaunchOptions Read(params string[] arguments) =>
        LaunchOptionsReader.Read(arguments, ViewerSettings.Load(), NullLogger.Instance);
}
