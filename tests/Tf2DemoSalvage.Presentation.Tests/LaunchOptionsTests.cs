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

    [Test]
    public void Read_AnOptionWithItsValueMissing_IsTreatedAsAPathRatherThanConsumingNothing()
    {
        // **The end-of-line case, and it must not hang or throw.** `--tick` with nothing after it
        // cannot consume a value, so it falls through to the path list — visible as a nonsense
        // "demo" rather than silently doing nothing.
        LaunchOptions read = Read("--tick");

        read.Paths.ShouldBe(["--tick"]);
        read.ShotTick.ShouldBe(0);
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

    private static LaunchOptions Read(params string[] arguments) =>
        LaunchOptionsReader.Read(arguments, ViewerSettings.Load(), NullLogger.Instance);
}
