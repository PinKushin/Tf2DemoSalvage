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

        LaunchOptions read = Read("+demo_fov_override", "75");

        read.Settings.DemoFovOverride.ShouldBe(75f);
        read.Settings.ShouldNotBe(before, "the command line must override the config it was given");
    }

    [Test]
    public void Read_DemoFovOverrideZero_TurnsTheOverrideOff() =>
        Read("+demo_fov_override", "0").Settings.DemoFovOverride.ShouldBe(0f);

    [Test]
    public void Read_FovDesired_ChangesNothing()
    {
        // Userinfo the server turns into `m_iDefaultFOV` (tf_gamerules.cpp:10275); playback has no server (D194).
        Read("+fov_desired", "75").Settings.DemoFovOverride.ShouldBe(ViewerSettings.Load().DemoFovOverride);
    }

    [Test]
    public void Read_AnUnknownPlusCommand_IsIgnoredRatherThanRefused()
    {
        // **Ignoring what it does not implement is the primary feature, not an afterthought**
        // (D69). A real config is hundreds of mat_*/cl_* lines this viewer has never heard of, and
        // the command line speaks the same vocabulary. It is handed to the console, which ignores it.
        LaunchOptions read = Read("+mat_picmip", "2", "a.dem");

        read.Paths.ShouldBe(["a.dem"], "the value must not be left behind as a demo to open");
        read.AfterConfigs.ShouldBe(["mat_picmip 2"]);
    }

    /// <remarks>
    /// **`stuffcmds` runs every `+command`, with the one argument after it as its value, unquoted** (B824; `engine.dll` x64
    /// build 1007 FUN_1801683d0: `Q_snprintf("%s %s\n", name, ParmValueByIndex(i))`, then the value's index is skipped). So
    /// `+exec x` runs `exec x`, and `+bind F8 screenshot` runs `bind F8` alone: the value is one argument, and `screenshot`
    /// is a bare argument of its own. Before, only a declared ConVar was handed on, quoted, and `+exec` ran nothing.
    /// </remarks>
    [Test]
    public void Read_APlusCommandWithAValue_IsHandedOnWithTheOneArgumentAfterIt()
    {
        Read("+exec", "x", "a.dem").AfterConfigs.ShouldBe(["exec x"]);

        LaunchOptions bind = Read("+bind", "F8", "screenshot");
        bind.AfterConfigs.ShouldBe(["bind F8"]);
        bind.Paths.ShouldBe(["screenshot"], "the second word is an argument of its own, as the engine's loop leaves it");

        Read("+cl_showfps", "1;bind q quit").AfterConfigs
            .ShouldBe(["cl_showfps 1;bind q quit"], "pasted as the engine pastes it; the console splits it as TF2's does");
    }

    /// <remarks>
    /// **A `+command` takes no value when the next argument is missing or starts with `+` or `-`** (B824; tier0
    /// `CCommandLine::ParmValueByIndex`, x64 0x180003230, returns its default there): `+quit` at the end runs `quit`, and
    /// `+mat_picmip -1` runs `mat_picmip` alone, as in TF2, with `-1` read as an option of its own. The control: a plain
    /// value is taken.
    /// </remarks>
    [Test]
    public void Read_APlusCommandBeforeAnOptionOrAtTheEnd_RunsWithNoValue()
    {
        Read("+cl_showfps", "1").AfterConfigs.ShouldBe(["cl_showfps 1"], "the control: a plain value is taken");

        Read("a.dem", "+quit").AfterConfigs.ShouldBe(["quit"]);
        Read("+toggleconsole", "+cl_showfps", "1").AfterConfigs.ShouldBe(["toggleconsole", "cl_showfps 1"]);

        LaunchOptions minus = Read("+mat_picmip", "-1", "a.dem");
        minus.AfterConfigs.ShouldBe(["mat_picmip"]);
        minus.Paths.ShouldBe(["a.dem"]);

        LaunchOptions option = Read("+jpeg", "--tick", "5");
        option.AfterConfigs.ShouldBe(["jpeg"]);
        option.ShotTick.ShouldBe(5, "the option after it is read as an option");
    }

    /// <remarks>
    /// **An empty argument is a value** (B824): `ParmValueByIndex` tests the next argument's first character, so an empty
    /// one is returned and pasted as `name ` with nothing after the space, as the engine's `"%s %s\n"` does.
    /// </remarks>
    [Test]
    public void Read_ALonePlus_PastesAnEmptyNameAndItsValue()
    {
        // B824 review 1, Sonnet: FUN_1801683d0 tests only the first character, so `+` alone is a command with an empty
        // name; `" %s"` puts its value first on the line, where the console runs it as the command. Alone, it pastes a
        // bare line, which runs nothing. Before, `+` fell through and was opened as a demo.
        LaunchOptions quit = Read("+", "quit");
        quit.AfterConfigs.ShouldBe([" quit"]);
        quit.Paths.ShouldBeEmpty();

        LaunchOptions alone = Read("+", "a.dem");
        alone.AfterConfigs.ShouldBe([" a.dem"], "the next argument is its value, as for any `+`");

        Read("a.dem", "+").Paths.ShouldBe(["a.dem"], "a lone `+` at the end is no demo");
    }

    [Test]
    public void Read_APlusCommandWithAnEmptyArgument_TakesItAsTheValueAndStoresNothing()
    {
        // The console line `fps_max ` has one token, so it prints and sets nothing (B824 review 1, Fable); the early store
        // must not set what the console will not. Whether tier0's own tokenizer ever yields an empty argument is
        // *interpolated* (tier0's parser is not read); `ParmValueByIndex` returns one if it is there. The control: a
        // plain value is stored.
        Read("+fps_max", "60").Settings.ConVarText("fps_max").ShouldBe("60", "the control");

        LaunchOptions empty = Read("+fps_max", string.Empty, "a.dem");
        empty.AfterConfigs.ShouldBe(["fps_max "]);
        empty.Settings.ConVarText("fps_max").ShouldBe(Read("a.dem").Settings.ConVarText("fps_max"));
    }

    /// <remarks>
    /// **A value of several statements is handed on whole and stored early only when it is one** (B824 review 1, Fable):
    /// the console splits `fps_max 60;quit` at the `;` and sets `60`, so storing `60;quit` for early readers would be a
    /// value the console never holds; and a refused set's line still runs its other statements, as TF2's would, so even
    /// a replicated cheat's line is handed on. The control: one statement is stored.
    /// </remarks>
    [Test]
    public void Read_AValueOfSeveralStatements_IsHandedOnWholeAndNotStoredEarly()
    {
        LaunchOptions several = Read("+fps_max", "60;quit", "+cl_forwardspeed", "500;bind q quit", "a.dem");

        several.AfterConfigs.ShouldBe(["fps_max 60;quit", "cl_forwardspeed 500;bind q quit"]);
        several.Settings.ConVarText("fps_max").ShouldBe(Read("a.dem").Settings.ConVarText("fps_max"));
    }

    /// <remarks>
    /// **An empty `+sv_cheats` changes nothing the line's cheat gate reads** (B824 review 1, Fable): the console prints it
    /// and keeps 1, so a replicated cheat after it is not refused. The control: `+sv_cheats 0` refuses it.
    /// </remarks>
    [Test]
    public void Read_AnEmptySvCheats_KeepsTheLinesCheatsOn()
    {
        const string Refusal = "Can't use cheat cvar cl_forwardspeed in multiplayer";
        RecordingLogger off = new();
        RecordingLogger empty = new();

        LaunchOptionsReader.Read(["+sv_cheats", "1", "+sv_cheats", "0", "+cl_forwardspeed", "5"], ViewerSettings.Load(), off);
        LaunchOptionsReader.Read(["+sv_cheats", "1", "+sv_cheats", string.Empty, "+cl_forwardspeed", "5"], ViewerSettings.Load(), empty);

        off.Count(Refusal).ShouldBe(1, "the control");
        empty.Count(Refusal).ShouldBe(0);
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
    public void Read_WithDemoTimescale_TakesThePlaybackSpeedAndLeavesTheConfigAlone()
    {
        // Valve's runtime speed command, which gate phase 3 plays at 8x through (D189); not a saved setting.
        LaunchOptions read = Read("a.dem", "+demo_timescale", "8");

        read.PlaybackSpeed.ShouldBe(8d);
        read.Settings.ShouldBe(ViewerSettings.Load());
        read.Paths.ShouldBe(["a.dem"]);
    }

    [Test]
    public void Read_WithANonPositiveDemoTimescale_RefusesIt()
    {
        Read("a.dem", "+demo_timescale", "0").PlaybackSpeed.ShouldBeNull();
        Read("a.dem", "+demo_timescale", "fast").PlaybackSpeed.ShouldBeNull();
    }

    [Test]
    public void Read_WithThenSeek_TakesTheTickToSeekBackTo()
    {
        // B420's scenario: play, then pause, seek back and measure the paused frame.
        LaunchOptions read = Read("a.dem", "--measure", "20", "--then-seek", "20142");

        read.ThenSeek.ShouldBe(20142);
        read.Paths.ShouldBe(["a.dem"]);
    }

    [Test]
    public void Read_WithThenSeekAndNoTick_LeavesItOff()
    {
        Read("a.dem", "--then-seek", "soon").ThenSeek.ShouldBeNull();
    }

    /// <remarks>**`--windows` turns the window cache on, and it is off without it** (D217 step 4).</remarks>
    [Test]
    public void Read_WithAndWithoutWindows_TurnsTheWindowCacheOnOnlyWhenAsked()
    {
        (Read("a.dem", "--windows").Windows, Read("a.dem").Windows, Read("a.dem", "--windows").Paths.Single()).ShouldBe((true, false, "a.dem"));
    }

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
    public void Read_WithHud_ChoosesThatHudAndOpensTheDemo()
    {
        LaunchOptions read = Read("a.dem", "--hud", "C:/huds/myhud");

        (read.Hud, read.Paths.Count).ShouldBe(("C:/huds/myhud", 1));
    }

    [Test]
    public void Read_WithoutHud_ChoosesNone() => Read("a.dem").Hud.ShouldBeNull("the stock HUD (D193)");

    [Test]
    public void Read_WithoutHelp_DoesNotAskForTheList()
    {
        Read("a.dem").ShowHelp.ShouldBeFalse();
    }

    /// <remarks>
    /// **Every cheat ConVar and every `+sv_cheats` on the line is handed to the console after the configs, in the line's order,
    /// and none is stored** (B718, B811; TF2's `cfg/valve.rc` runs `stuffcmds` after `exec autoexec.cfg`), so the console's
    /// gate decides each one, admitted or refused, where the session's tests measure it. Stored at read time, a cheat the
    /// line admitted lost to an earlier deferred set of the same ConVar (B811 review 2).
    /// </remarks>
    [Test]
    public void Read_CheatsAndSvCheatsOnTheLine_AreHandedToTheConsoleInOrderAndNotStored()
    {
        LaunchOptions mixed = Read("+mat_fullbright", "1", "+sv_cheats", "1", "+mat_fullbright", "0", "+r_screenfademinsize", "12", "a.dem");

        mixed.AfterConfigs.ShouldBe(["mat_fullbright 1", "sv_cheats 1", "mat_fullbright 0", "r_screenfademinsize 12"]);
        mixed.Settings.ConVarText("mat_fullbright").ShouldBe(Read("a.dem").Settings.ConVarText("mat_fullbright"), "not stored");
        mixed.Settings.ScreenFadeMinSize.ShouldBe(0f, "not stored");
        Read("a.dem").AfterConfigs.ShouldBeNull("the control: no console option, nothing handed");

        // B816: a non-cheat is stored for early readers and handed on too, so it runs after the configs as well.
        LaunchOptions plain = Read("+cl_showfps", "1", "a.dem");
        plain.AfterConfigs.ShouldBe(["cl_showfps 1"]);
        plain.Settings.ConVarText("cl_showfps").ShouldBe("1");

        // B824: any command is handed on, as `stuffcmds` runs it; the sidecar's keys go through `--set` instead.
        Read("+alias", "a", "a.dem").AfterConfigs.ShouldBe(["alias a"]);
    }

    /// <remarks>
    /// **A replicated cheat is the server's, so the line's own `sv_cheats` gates it and the console never sees it**
    /// (B811 review 1, Sonnet; reviews 2 and 3): `+cl_forwardspeed 500` is refused with TF2's line and not handed on; after
    /// `+sv_cheats 1` it is not refused, and still not handed on, only the `sv_cheats` is.
    /// </remarks>
    [Test]
    public void Read_AReplicatedCheat_IsGatedOnTheLineAndNeverHandedOn()
    {
        const string Refusal = "Can't use cheat cvar cl_forwardspeed in multiplayer";
        RecordingLogger refusedLog = new();
        RecordingLogger admittedLog = new();

        // B824 review 1: the line is handed on either way, as `stuffcmds` pastes it; the console's client sets never take a
        // replicated ConVar, and a line of several statements still runs the rest.
        LaunchOptionsReader.Read(["+cl_forwardspeed", "500", "a.dem"], ViewerSettings.Load(), refusedLog)
            .AfterConfigs.ShouldBe(["cl_forwardspeed 500"]);
        LaunchOptionsReader.Read(["+sv_cheats", "1", "+cl_forwardspeed", "500", "a.dem"], ViewerSettings.Load(), admittedLog)
            .AfterConfigs.ShouldBe(["sv_cheats 1", "cl_forwardspeed 500"]);

        refusedLog.Count(Refusal).ShouldBe(1);
        admittedLog.Count(Refusal).ShouldBe(0, "the control: the line's own sv_cheats admits it");
    }

    /// <remarks>
    /// **`--set` is the sidecar's route: one declared ConVar, its value whole and quoted** (B824; above Valve). A `--like`
    /// sidecar is a file the viewer reads, so its keys and values are untrusted text, and `+` now pastes a value unquoted
    /// and drops one starting with `-`. `--set` keeps B811/B816's guard: a declared ConVar only, and a value that would
    /// close its quotes or start another line is refused (B811 reviews 10, 11). The control: a plain value, negative
    /// included, is handed on quoted.
    /// </remarks>
    /// <param name="value">The value given.</param>
    [TestCase("1\"; bind q quit; \"")]
    [TestCase("1\nbind q quit")]
    [TestCase("1\rbind q quit")]
    [TestCase("1\0bind q quit")]
    public void Read_ASetValueThatBreaksItsQuotes_IsRefused(string value)
    {
        Read("--set", "sv_cheats", "1", "--set", "mat_wireframe", "1", "--set", "fps_max", "-1", "a.dem").AfterConfigs
            .ShouldBe(["sv_cheats \"1\"", "mat_wireframe \"1\"", "fps_max \"-1\""], "the control: plain values");

        Read("--set", "sv_cheats", "1", "--set", "mat_wireframe", value, "a.dem").AfterConfigs.ShouldBe(["sv_cheats \"1\""]);
        Read("--set", "sv_cheats", value, "a.dem").AfterConfigs.ShouldBeNull();
        Read("--set", "cl_showfps", value, "a.dem").Settings.ConVarText("cl_showfps").ShouldNotBe(value);
    }

    /// <remarks>
    /// **`--set` takes a declared ConVar only** (B816 review 5, Fable, kept for the sidecar route): an undeclared key is a
    /// console line of the sidecar's own making. Its value and both arguments are consumed either way, so nothing is left
    /// as a demo. The control: a declared non-cheat is stored for early readers and handed on.
    /// </remarks>
    [Test]
    public void Read_ASetOfAnUndeclaredName_IsRefused()
    {
        LaunchOptions plain = Read("--set", "cl_showfps", "1", "a.dem");
        plain.AfterConfigs.ShouldBe(["cl_showfps \"1\""], "the control");
        plain.Settings.ConVarText("cl_showfps").ShouldBe("1");

        LaunchOptions undeclared = Read("--set", "x\nbind q quit", "1", "--set", "alias", "a", "a.dem");
        undeclared.AfterConfigs.ShouldBeNull();
        undeclared.Paths.ShouldBe(["a.dem"]);
    }

    /// <remarks>
    /// **`--set` with its value missing is reported, and its name is no demo** (B824 review 1, Fable, Grok). The control:
    /// a whole `--set` is handed on.
    /// </remarks>
    [Test]
    public void Read_ASetWithItsValueMissing_IsReportedRatherThanTakenAsAPath()
    {
        Read("--set", "cl_showfps", "1").AfterConfigs.ShouldBe(["cl_showfps \"1\""], "the control");

        RecordingLogger log = new();
        LaunchOptions read = LaunchOptionsReader.Read(["a.dem", "--set", "cl_showfps"], ViewerSettings.Load(), log);

        read.Paths.ShouldBe(["a.dem"]);
        read.AfterConfigs.ShouldBeNull();
        log.Count("--set wants a ConVar and a value").ShouldBe(1);
    }

    /// <remarks>
    /// **The launch line finds a ConVar in any case, as the console does** (B791 review 9, Sonnet; the console's lookup is the
    /// closed `vstdlib`'s, *interpolated*): `+MAT_WIREFRAME 1` is handed on under its own name.
    /// </remarks>
    [Test]
    public void Read_ACheatConVarInUpperCase_IsHandedOnUnderItsOwnName()
    {
        Read("+SV_CHEATS", "1", "+MAT_WIREFRAME", "1", "a.dem").AfterConfigs.ShouldBe(["sv_cheats 1", "mat_wireframe 1"]);
    }

    /// <remarks>
    /// **`+DEMO_TIMESCALE 8` is the playback speed as `+demo_timescale 8` is** (B791 review 11, Fable): the console finds a
    /// command in any case; before, the upper-case one fell to the generic branch and set no speed.
    /// </remarks>
    [Test]
    public void Read_DemoTimescaleInUpperCase_SetsThePlaybackSpeed() =>
        Read("+DEMO_TIMESCALE", "8", "a.dem").PlaybackSpeed.ShouldBe(8d);

    /// <remarks>
    /// **A settings line in another case is kept under the ConVar's own name** (B791 review 10, Sonnet): the parser finds a
    /// typed name as the console does, so `TF_HUD_TARGET_ID_ALPHA 50` sets `tf_hud_target_id_alpha`.
    /// </remarks>
    [Test]
    public void Parse_AClientConVarInUpperCase_IsKept() =>
        ViewerSettings.Parse("TF_HUD_TARGET_ID_ALPHA 50", null).ConVarText("tf_hud_target_id_alpha").ShouldBe("50");

    private static LaunchOptions Read(params string[] arguments) =>
        LaunchOptionsReader.Read(arguments, ViewerSettings.Load(), NullLogger.Instance);
}
