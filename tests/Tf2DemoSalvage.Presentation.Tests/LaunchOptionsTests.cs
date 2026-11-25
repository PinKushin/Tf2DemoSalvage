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
    /// **An unknown `-flag` takes the next argument as its value, as `stuffcmds` does** (B828): `engine.dll` x64 build 1007
    /// FUN_1801683d0 asks `ParmValueByIndex` for a `-flag`'s value, which tier0's `CCommandLine` gives unless there is no
    /// next argument or it starts with `+` or `-`, and skips it; so `-threads 4` neither runs nor opens `4`. **Above Valve**
    /// (`docs/ABOVE-VALVE.md`, the owner's pick): a `.dem` is never taken as a value, so `-novid a.dem` still opens it, where
    /// TF2 would not play it. Nor is a folder (the next test).
    /// </remarks>
    [TestCase(new[] { "-threads", "4", "a.dem" }, new[] { "a.dem" })]
    [TestCase(new[] { "-threads", "", "a.dem" }, new[] { "a.dem" })]
    [TestCase(new[] { "-novid", "a.dem" }, new[] { "a.dem" })]
    [TestCase(new[] { "-novid", "B.DEM", "c" }, new[] { "B.DEM", "c" })]
    [TestCase(new[] { "-novid", "-threads", "4" }, new string[0])]
    [TestCase(new[] { "-threads", "4" }, new string[0])]
    [TestCase(new[] { "a.dem", "-novid" }, new[] { "a.dem" })]
    public void Read_AnUnknownFlag_TakesItsValueUnlessItIsADemo(string[] arguments, string[] paths)
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(arguments, ViewerSettings.Load(), log);

        read.Paths.ShouldBe(paths);
    }

    /// <remarks>
    /// **An existing folder is never a flag's value either** (B828 review 1, Fable, Sonnet; the owner's pick, above Valve): a
    /// folder on the line opens as a playlist, and `-novid C:\demos` did before. A name that is no folder is still dropped,
    /// and so is a bare word that names one beside the viewer (review 2, Sonnet, Fable, Grok): `-game tf`, run from TF2's
    /// folder, drops `tf` as TF2 does. `./` before the same name keeps it (`/` is a separator on every platform CI runs).
    /// </remarks>
    [Test]
    public void Read_AnUnknownFlagBeforeAFolder_OpensTheFolder()
    {
        RecordingLogger log = new();

        // A name of its own per run (B828 review 4, Fable): Stryker's test hosts share one working directory.
        string bare = $"b828-folder-{Guid.NewGuid():N}";
        string folder = System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(bare)).FullName;
        string missing = System.IO.Path.GetFullPath("no-such-folder-b828");

        try
        {
            LaunchOptions read = LaunchOptionsReader.Read(
                ["-novid", folder, "-novid", missing, "-game", bare, "-novid", $"./{bare}"], ViewerSettings.Load(), log);

            read.Paths.ShouldBe([folder, $"./{bare}"]);
        }
        finally
        {
            System.IO.Directory.Delete(folder);
        }
    }

    /// <remarks>
    /// **A drive-relative folder is a path too** (B828 review 3, Fable): `C:b828-drive` has no separator, but it is rooted
    /// at a drive, so it is kept, where the bare `b828-drive` is a value.
    /// </remarks>
    [Test]
    [Platform("Win")]
    public void Read_AnUnknownFlagBeforeADriveRelativeFolder_OpensTheFolder()
    {
        RecordingLogger log = new();
        string bare = $"b828-drive-{Guid.NewGuid():N}";
        string folder = System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(bare)).FullName;
        string driveRelative = folder[..2] + bare;

        try
        {
            // A working directory on a drive letter (review 4, Fable): a UNC one has no drive-relative spelling.
            Assume.That(folder[1], Is.EqualTo(':'));

            LaunchOptions read = LaunchOptionsReader.Read(
                ["-game", bare, "-novid", driveRelative], ViewerSettings.Load(), log);

            read.Paths.ShouldBe([driveRelative]);
        }
        finally
        {
            System.IO.Directory.Delete(folder);
        }
    }

    /// <remarks>
    /// **A pasted TF2 line with `-h` opens its demo** (B836, the owner's pick, "TF2's meaning"): `-h` is TF2's window
    /// height, so it is an unknown flag here and drops its value as `stuffcmds` does (B828), where it was help and left
    /// `1080` as a demo path.
    /// </remarks>
    [Test]
    public void Read_ATf2WindowSizeLine_OpensItsDemoAndShowsNoHelp()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(["-w", "1920", "-h", "1080", "a.dem"], ViewerSettings.Load(), log);

        read.Paths.ShouldBe(["a.dem"]);
        read.ShowHelp.ShouldBeFalse();
    }

    /// <remarks>
    /// **The viewer's own switches keep the word after them** (B837, the owner's pick, "Keep them as they are"; above
    /// Valve, `docs/ABOVE-VALVE.md`): TF2 skips the word after any `-flag`, but these are the viewer's, so
    /// `--autoplay a.dem` opens `a.dem`.
    /// </remarks>
    [TestCase("--autoplay")]
    [TestCase("--colours")]
    [TestCase("--windows")]
    [TestCase("--first-person")]
    [TestCase("--third-person")]
    public void Read_AViewerSwitchBeforeAWord_KeepsTheWordAsADemo(string option)
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read([option, "a"], ViewerSettings.Load(), log);

        read.Paths.ShouldBe(["a"]);
    }

    /// <remarks>The warning names the value it dropped, and none it kept (B828 review 1, Fable, Gemini, Grok).</remarks>
    [Test]
    public void Read_AnUnknownFlagWithAValue_NamesTheValueInItsWarning()
    {
        RecordingLogger log = new();

        _ = LaunchOptionsReader.Read(["-threads", "4", "-novid", "a.dem"], ViewerSettings.Load(), log);

        log.Count("'-threads' '4' is not an option this viewer knows").ShouldBe(1);
        log.Count("'-novid' is not an option this viewer knows").ShouldBe(1);
    }

    /// <remarks>
    /// The control (B828): a `+command` after an unknown `-flag` is not its value, since `ParmValueByIndex` gives none that
    /// starts with `+`, so it still runs.
    /// </remarks>
    [Test]
    public void Read_APlusCommandAfterAnUnknownFlag_IsNotItsValue()
    {
        RecordingLogger log = new();

        LaunchOptions read = LaunchOptionsReader.Read(["-novid", "+fps_max", "60"], ViewerSettings.Load(), log);

        read.AfterConfigs.ShouldBe(["fps_max 60"]);
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

    /// <remarks>
    /// **What the console will refuse is not stored early either** (B830; B824 review 3, Fable): the launch line is one
    /// `AddText` buffer, which drops a command of 511 bytes or more and every later one (`commandbuffer.cpp:120-126,
    /// 242-243`), so a `+demo_fov_override 75` after a long `+alias` never runs, and storing it for the readers that run
    /// before the console would set what TF2 never sets. A long command's own value is not stored either. The control:
    /// without the long alias, 75 is stored.
    /// </remarks>
    [Test]
    public void Read_AConVarAfterACommandOf511Bytes_IsNotStored()
    {
        Read("+demo_fov_override", "75").Settings.DemoFovOverride.ShouldBe(75f, "the control");

        LaunchOptions read = Read("+alias", "a " + new string('x', 600), "+demo_fov_override", "75");

        read.Settings.DemoFovOverride.ShouldBe(ViewerSettings.Load().DemoFovOverride);
        // A long value that still reads as 75 (`atof` stops at the space), so only the gate keeps it out (B830 review 1, Fable).
        Read("+demo_fov_override", "75 " + new string('x', 600)).Settings.DemoFovOverride.ShouldBe(
            ViewerSettings.Load().DemoFovOverride, "the long command's own value");
    }

    /// <remarks>
    /// **A set first on its line is stored though a later statement on that line is refused** (B830 review 1, Sonnet):
    /// `AddText` queues the commands before the one it refuses (`commandbuffer.cpp:242-243`), so `demo_fov_override 75`
    /// runs in TF2 and the early store must hold it. The control: a first statement that is itself too long, and reads as
    /// 80, is not stored (B830 review 2, Fable, Sonnet: one that reads as 0, the default, could not fail).
    /// </remarks>
    [Test]
    public void Read_ASetBeforeARefusedStatementOnItsLine_IsStored()
    {
        Read("+demo_fov_override", "80 " + new string('x', 600) + ";demo_fov_override 75").Settings.DemoFovOverride
            .ShouldBe(ViewerSettings.Load().DemoFovOverride, "the control: the long statement first");

        Read("+demo_fov_override", "75;alias a " + new string('x', 600)).Settings.DemoFovOverride.ShouldBe(75f);
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
    /// **A value of several statements is handed on whole, and a declared ConVar stores only its first** (B824 reviews 1
    /// and 2, Fable): the console splits `fps_max 60;quit` at the `;` and sets `60`, so `60;quit` would be a value it
    /// never holds; a refused set's line still runs its other statements, as TF2's would, so even a replicated cheat's
    /// line is handed on. The viewer's own setting is stored whole, since the console never reads it and a folder may
    /// hold a `;`. The control: one statement is stored as given.
    /// </remarks>
    [Test]
    public void Read_AValueOfSeveralStatements_IsHandedOnWholeAndStoresWhatTheConsoleSets()
    {
        Read("+fps_max", "60").Settings.ConVarText("fps_max").ShouldBe("60", "the control");

        LaunchOptions several = Read("+fps_max", "45;quit", "+cl_forwardspeed", "500;bind q quit", "a.dem");

        several.AfterConfigs.ShouldBe(["fps_max 45;quit", "cl_forwardspeed 500;bind q quit"]);
        several.Settings.ConVarText("fps_max").ShouldBe("45");

        Read("+" + ViewerSettings.ScreenshotFolderCommand, @"C:\shots;old").Settings.ScreenshotFolder.ShouldBe(@"C:\shots;old");
    }

    /// <remarks>
    /// **The early state is what the console's own parse of the line sets** (B824 review 2, Sonnet): the line goes through
    /// `SourceConfig.Clauses` and `ConVarValue` (B823), so a quoted `--set` value is whole, `;` inside its quotes
    /// included, and `""` sets empty; and a `+` value is cut at `//`, the console's comment. A guess at the console from
    /// the value's text got all three wrong. The controls: a plain `--set` and a plain `+` value are stored as given.
    /// </remarks>
    [Test]
    public void Read_TheEarlyStore_IsWhatTheConsolesParseOfTheLineSets()
    {
        Read("--set", "cl_voice_filter", "ab").Settings.ConVarText("cl_voice_filter").ShouldBe("ab", "the control");
        Read("+cl_voice_filter", "ab").Settings.ConVarText("cl_voice_filter").ShouldBe("ab", "the control");

        Read("--set", "cl_voice_filter", "a;b").Settings.ConVarText("cl_voice_filter").ShouldBe("a;b");
        Read("--set", "cl_voice_filter", "ab", "--set", "cl_voice_filter", string.Empty).Settings.ConVarText("cl_voice_filter")
            .ShouldBe(string.Empty);
        // B824 review 3, Fable: a case the settings parser cannot produce on its own, so only the console's parse gives
        // it: `ArgS` drops a trailing byte of 0x80 or more (B823), so `café` sets `caf`.
        Read("+cl_voice_filter", "café").Settings.ConVarText("cl_voice_filter").ShouldBe("caf");

        // B824 review 4, Sonnet: since the settings parser keeps a quoted `//`, only the console's cut makes this `http:`.
        Read("+cl_voice_filter", "http://x").Settings.ConVarText("cl_voice_filter").ShouldBe("http:");
    }

    /// <remarks>
    /// **The console's set ends at a line feed** (B824 review 3, Fable): it splits a buffer into lines before clauses
    /// (`ConfigConsole.BufferCommands`), so `sv_cheats \n1` prints `sv_cheats` and runs `1`, and the line's cheats stay
    /// off. `SourceConfig.Clauses` does not split there, so the early state reads the first line alone. The control: a
    /// plain `+sv_cheats 1` turns them on.
    /// </remarks>
    [Test]
    public void Read_AValueWithALineFeed_IsReadUpToItAsTheConsoleReadsIt()
    {
        const string Refusal = "Can't use cheat cvar cl_forwardspeed in multiplayer";
        RecordingLogger on = new();
        RecordingLogger split = new();

        LaunchOptionsReader.Read(["+sv_cheats", "0", "+sv_cheats", "1", "+cl_forwardspeed", "5"], ViewerSettings.Load(), on);
        LaunchOptionsReader.Read(["+sv_cheats", "0", "+sv_cheats", "\n1", "+cl_forwardspeed", "5"], ViewerSettings.Load(), split);

        on.Count(Refusal).ShouldBe(0, "the control");
        split.Count(Refusal).ShouldBe(1);
    }

    /// <remarks>
    /// **A carriage return is whitespace to the console, not a line break** (B824 review 4, Fable): `commandbuffer.cpp:196-198`
    /// breaks on `\n` alone, so `sv_cheats \r1` sets 1. The control: `+sv_cheats 0` alone leaves them off.
    /// </remarks>
    [Test]
    public void Read_AValueWithACarriageReturn_IsReadAsWhitespace()
    {
        const string Refusal = "Can't use cheat cvar cl_forwardspeed in multiplayer";
        RecordingLogger off = new();
        RecordingLogger carriage = new();

        LaunchOptionsReader.Read(["+sv_cheats", "0", "+cl_forwardspeed", "5"], ViewerSettings.Load(), off);
        LaunchOptionsReader.Read(["+sv_cheats", "0", "+sv_cheats", "\r1", "+cl_forwardspeed", "5"], ViewerSettings.Load(), carriage);

        off.Count(Refusal).ShouldBe(1, "the control");
        carriage.Count(Refusal).ShouldBe(0);
    }

    /// <remarks>
    /// **A quoted `--set sv_cheats "1;x"` turns the line's cheats on, as the console reads `1`** (B824 review 2, Sonnet).
    /// The control: `--set sv_cheats 0` refuses the replicated cheat after it.
    /// </remarks>
    [Test]
    public void Read_ASetOfSvCheatsWithASemicolonInItsQuotes_TurnsTheLinesCheatsOn()
    {
        const string Refusal = "Can't use cheat cvar cl_forwardspeed in multiplayer";
        RecordingLogger off = new();
        RecordingLogger on = new();

        LaunchOptionsReader.Read(["--set", "sv_cheats", "0", "--set", "cl_forwardspeed", "5"], ViewerSettings.Load(), off);
        LaunchOptionsReader.Read(["--set", "sv_cheats", "1;x", "--set", "cl_forwardspeed", "5"], ViewerSettings.Load(), on);

        off.Count(Refusal).ShouldBe(1, "the control");
        on.Count(Refusal).ShouldBe(0);

        // B824 review 2, Grok: `+sv_cheats 0;echo x` sets 0 at the console, so the line's cheats go off with it.
        RecordingLogger plus = new();
        LaunchOptionsReader.Read(["+sv_cheats", "1", "+sv_cheats", "0;echo x", "+cl_forwardspeed", "5"], ViewerSettings.Load(), plus);
        plus.Count(Refusal).ShouldBe(1);

        // B824 review 2, Fable: `+sv_cheats //x` is a comment at the console, which keeps 1; the raw value reads 0.
        RecordingLogger comment = new();
        LaunchOptionsReader.Read(["+sv_cheats", "1", "+sv_cheats", "//x", "+cl_forwardspeed", "5"], ViewerSettings.Load(), comment);
        comment.Count(Refusal).ShouldBe(0);
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
    /// **A `//` inside quotes is part of the value, not a comment** (B824 review 3, Sonnet): TF2's tokenizer eats a `//`
    /// comment only between tokens, and a quoted string runs to its closing quote (`utlbuffer.cpp:1242-1326`), so
    /// `cl_screenshot_folder "//nas/shots"` is that folder. The settings parser cut at any `//`, so a launch
    /// `+cl_screenshot_folder //nas/shots` stored an empty folder. The control: an unquoted `//` is still a comment.
    /// </remarks>
    [Test]
    public void Parse_ADoubleSlashInsideQuotes_IsPartOfTheValue()
    {
        ViewerSettings.Parse("cl_screenshot_folder shots // where they go", null).ScreenshotFolder.ShouldBe("shots", "the control");

        ViewerSettings.Parse("cl_screenshot_folder \"//nas/shots\"", null).ScreenshotFolder.ShouldBe("//nas/shots");
        ViewerSettings.Parse("cl_screenshot_folder \"shots\" // note", null).ScreenshotFolder
            .ShouldBe("shots", "the closing quote ends the string, so the comment after it is one (review 4, Fable)");
        Read("+" + ViewerSettings.ScreenshotFolderCommand, "//nas/shots").Settings.ScreenshotFolder.ShouldBe("//nas/shots");
    }

    /// <remarks>
    /// **A set stores its value whole, quotes and `//` included** (B831): the console sets `cl_voice_filter` to its
    /// `ArgS`, `x"//y"`, and `Set` re-quoted that into a line and parsed it back, where the second `"` closed the quote and
    /// the `//` cut it to `x`. Config text has no escapes, so no quoting could carry it; the value is stored directly. The
    /// control: a plain value.
    /// </remarks>
    [Test]
    public void Set_AValueHoldingAQuoteAndADoubleSlash_StoresItWhole()
    {
        new ViewerSettings().Set("cl_voice_filter", "ab").ConVarText("cl_voice_filter").ShouldBe("ab", "the control");

        new ViewerSettings().Set("cl_voice_filter", "x\"//y\"").ConVarText("cl_voice_filter").ShouldBe("x\"//y\"");
        Read("+cl_voice_filter", "x\"//y\"").Settings.ConVarText("cl_voice_filter").ShouldBe("x\"//y\"");
    }

    /// <remarks>
    /// **A viewer setting given in quotes on the launch line loses them, as a console set does to its `ArgS`** (B831
    /// review 1, Fable): `stuffcmds` pastes `cl_screenshot_folder "D:\shots"`; `ArgS` keeps the opening quote
    /// (`convar.cpp:432-451`) and the set drops one leading `"` and, then, one trailing `"` (`engine.dll` build 1007
    /// FUN_180178310, B823). Before B831 the re-parse stripped them; storing the argument whole
    /// kept them, an invalid path. The control: an unquoted folder.
    /// </remarks>
    [Test]
    public void Read_AViewerSettingInQuotes_DropsItsQuotesAsArgSDoes()
    {
        Read("+" + ViewerSettings.ScreenshotFolderCommand, @"D:\shots").Settings.ScreenshotFolder.ShouldBe(@"D:\shots", "the control");

        Read("+" + ViewerSettings.ScreenshotFolderCommand, "\"D:\\shots\"").Settings.ScreenshotFolder.ShouldBe(@"D:\shots");

        // B831 review 2, Fable: the leading quote goes alone, and a trailing one stays unless one led.
        Read("+" + ViewerSettings.ScreenshotFolderCommand, "\"D:\\shots").Settings.ScreenshotFolder.ShouldBe(@"D:\shots");
        Read("+" + ViewerSettings.ScreenshotFolderCommand, "D:\\shots\"").Settings.ScreenshotFolder.ShouldBe("D:\\shots\"");
    }

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
