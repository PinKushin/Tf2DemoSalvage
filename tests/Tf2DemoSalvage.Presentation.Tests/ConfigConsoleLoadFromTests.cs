using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Reading the user's own TF2 configs off disk.</summary>
/// <remarks>
/// **This was `MainForm.LoadUserConfig`** (B188, D90). It is the disk half of D69 — a real config
/// must work wholesale — and the parsing half is covered by <c>SourceConfigTests</c>.
/// </remarks>
public sealed class ConfigConsoleLoadFromTests
{
    [Test]
    public void LoadFrom_WithNoInstallAnywhere_KeepsTheCallersBindingsAndSaysWhy()
    {
        // **Null is the contract, and it is the whole reason this returns a nullable.** `MainForm`
        // assigned its field only on the success path; a version that handed back `Bindings()` here
        // would look equivalent and would silently replace the caller's bindings with this
        // console's defaults after an unreadable config.
        RecordingLogger log = new();

        ConfigConsole.WithDefaults()
            .LoadFrom(installedGameFolder: NowhereFolder(), NullLoggerFactory.Instance, log)
            .ShouldBeNull();

        log.Count("no configs under").ShouldBe(1);
    }

    /// <remarks>
    /// **No folder is no install, not this machine's** (B811 review 3, Fable): the session passes `SteamInstall.Machine`'s
    /// answer already, so a second fallback only let a test that says "no install" read the real TF2 configs of whoever
    /// runs it. Red only on a machine with TF2 installed, which is where it mattered.
    /// </remarks>
    [Test]
    public void LoadFrom_WithNoFolderGiven_ReadsNoMachineInstall()
    {
        RecordingLogger log = new();

        ConfigConsole.WithDefaults()
            .LoadFrom(installedGameFolder: null, NullLoggerFactory.Instance, log, ownCustomRoot: NowhereFolder())
            .ShouldBeNull();

        log.Count("no TF2 install found").ShouldBe(1);
    }

    /// <remarks>
    /// **A config's refused cheat is printed by the load when nobody else listens** (B718; B811 review 6, Fable): at startup
    /// the configs load before the settings attach, so this list is the only place TF2's line comes from. The control: with
    /// a listener, the load leaves the printing to it.
    /// </remarks>
    [Test]
    public void LoadFrom_AConfigCheatWithCheatsOff_PrintsTheRefusalOnlyWhenNobodyListens()
    {
        const string Refusal = "Can't use cheat cvar mat_wireframe in multiplayer";
        string folder = TempFolder();
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", "autoexec.cfg"), "mat_wireframe 1\n");

        // No custom/ of this machine's or the test binary's (review 7, Fable): only the folder above is read.
        RecordingLogger alone = new();
        ConfigConsole.WithDefaults().LoadFrom(folder, NullLoggerFactory.Instance, alone, NowhereFolder());

        RecordingLogger heard = new();
        ConfigConsole listened = ConfigConsole.WithDefaults();
        int told = 0;
        listened.CheatRefused += (_, _) => told++;
        listened.LoadFrom(folder, NullLoggerFactory.Instance, heard, NowhereFolder());

        alone.Count(Refusal).ShouldBe(1);
        told.ShouldBe(1, "the control: the listener is told, so it can print it");
        heard.Count(Refusal).ShouldBe(0);
    }

    /// <remarks>
    /// **TF2's saved configs keep their ConVars below the attached settings** (D264; B816 review 2, Fable, Sonnet): a line
    /// in `config.cfg` or `config_default.cfg` is kept in the console, where a reader of an untyped ConVar still finds it,
    /// and is not announced to the settings. The control: the same line in `autoexec.cfg` is announced.
    /// </remarks>
    /// <param name="file">The config the line is in.</param>
    /// <param name="announced">Whether the settings are told.</param>
    [TestCase("config.cfg", 0)]
    [TestCase("config_default.cfg", 0)]
    [TestCase("autoexec.cfg", 1)]
    public void LoadFrom_AConVarWithTheSettingsAttached_IsAnnouncedOnlyFromAutoexec(string file, int announced)
    {
        string folder = TempFolder();
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", file), "cl_showfps 1\n");
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.ReadSettingsFrom(_ => null);
        int told = 0;
        console.ConVarSet += (_, _) => told++;

        console.LoadFrom(folder, NullLoggerFactory.Instance, new RecordingLogger(), NowhereFolder());

        told.ShouldBe(announced);

        if (announced == 0)
        {
            console.ConVar("cl_showfps").ShouldBe("1", "kept in the console, below the settings");
        }
    }

    /// <remarks>
    /// **A saved config's waited ConVar stays below the settings when it runs later** (B832 review 1, Fable): the command a
    /// `wait` puts off runs at a later `Execute` as the saved layer that added it, so it is still not announced. The
    /// control: the same line in `autoexec.cfg` is announced once it runs.
    /// </remarks>
    /// <param name="file">The config the line is in.</param>
    /// <param name="announced">Whether the settings are told.</param>
    [TestCase("config.cfg", 0)]
    [TestCase("autoexec.cfg", 1)]
    public void LoadFrom_AWaitedConVarWithTheSettingsAttached_IsAnnouncedOnlyFromAutoexec(string file, int announced)
    {
        string folder = TempFolder();
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", file), "wait; cl_showfps 1\n");
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.ReadSettingsFrom(_ => null);
        int told = 0;
        console.ConVarSet += (_, _) => told++;

        console.LoadFrom(folder, NullLoggerFactory.Instance, new RecordingLogger(), NowhereFolder());
        told.ShouldBe(0, "the control: the set waits");
        console.Execute(waitAllowed: true);
        console.Execute(waitAllowed: true);

        told.ShouldBe(announced);
        console.ConVar("cl_showfps").ShouldBe(announced == 0 ? "1" : null, "the control: the waited set ran");
    }

    /// <remarks>
    /// **A saved config's `sv_cheats 1` then `0` reverts only its own lines, not the settings' cheats** (D264; B816 review
    /// 2, Sonnet): the settings hold `mat_fullbright 1`. The control: the same lines in `autoexec.cfg` revert it.
    /// </remarks>
    /// <param name="file">The config the lines are in.</param>
    /// <param name="reverted">Whether the settings are told to put `mat_fullbright` back.</param>
    [TestCase("config.cfg", false)]
    [TestCase("autoexec.cfg", true)]
    public void LoadFrom_SvCheatsOnThenOffWithASavedCheat_RevertsItOnlyFromAutoexec(string file, bool reverted)
    {
        string folder = TempFolder();
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", file), "sv_cheats 1\nsv_cheats 0\n");
        ConfigConsole console = ConfigConsole.WithDefaults();
        console.ReadSettingsFrom(name => name == "mat_fullbright" ? "1" : null);
        List<string> told = [];
        console.ConVarSet += (_, set) => told.Add($"{set.Name} {set.Value}");

        console.LoadFrom(folder, NullLoggerFactory.Instance, new RecordingLogger(), NowhereFolder());

        told.Contains("mat_fullbright 0").ShouldBe(reverted);
    }

    [Test]
    public void LoadFrom_WithAConfigThatBinds_TakesTheBindingAndReportsIt()
    {
        // **A control on the test above.** Without a case that actually loads, "returns null" would
        // be satisfied by a method that never reads anything at all.
        string folder = TempFolder();
        string cfg = Path.Combine(folder, "cfg");
        Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "autoexec.cfg"), "bind \"h\" \"+forward\"\n");

        RecordingLogger log = new();

        KeyBindings? bindings = ConfigConsole.WithDefaults()
            .LoadFrom(folder, NullLoggerFactory.Instance, log);

        bindings.ShouldNotBeNull();
        log.Count("binds applied").ShouldBe(1);
    }

    [Test]
    public void LoadFrom_WithAConfigThatBinds_LogsEveryBindingBackToTheUser()
    {
        // **Verbose on purpose, and it stays.** The promise of D69 is that a pasted config works,
        // and the only way a user can check which of their binds this viewer understood is to read
        // them back. A quieter log would make the feature unverifiable from the user's side.
        string folder = TempFolder();
        string cfg = Path.Combine(folder, "cfg");
        Directory.CreateDirectory(cfg);
        File.WriteAllText(Path.Combine(cfg, "autoexec.cfg"), "bind \"h\" \"+forward\"\n");

        RecordingLogger log = new();

        ConfigConsole.WithDefaults().LoadFrom(folder, NullLoggerFactory.Instance, log);

        log.Lines.Count.ShouldBeGreaterThan(
            1, "the summary line alone tells a user nothing about WHICH binds took");
    }

    [Test]
    public void LoadFrom_WithNoInstallButAConfigInOwnCustom_StillLoadsIt()
    {
        // D193: the program's own custom/ is checked even when TF2 itself is not installed at all -
        // a config a player pasted there does not depend on the game being found.
        string ownCustom = TempFolder();
        File.WriteAllText(Path.Combine(ownCustom, "autoexec.cfg"), "bind \"h\" \"+forward\"\n");

        RecordingLogger log = new();

        KeyBindings? bindings = ConfigConsole.WithDefaults()
            .LoadFrom(NowhereFolder(), NullLoggerFactory.Instance, log, ownCustom);

        bindings.ShouldNotBeNull();
    }

    /// <remarks>
    /// **An autoexec's `exec` reads the same search path the autoexec came from** (B820): `//MOD/cfg/&lt;name&gt;` in the engine,
    /// so a file in the game's `cfg/` runs, and a copy in an addon folder of the program's own `custom/` replaces it (D193).
    /// The first load is the control for the second: the game's copy alone binds `h` forward.
    /// </remarks>
    [Test]
    public void LoadFrom_AnAutoexecThatExecsANestedFile_RunsTheFirstCopyOnTheSearchPath()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg", "comfig"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "exec comfig/comfig\n");
        File.WriteAllText(Path.Combine(game, "cfg", "comfig", "comfig.cfg"), "bind h +forward\n");
        string ownCustom = TempFolder();
        Directory.CreateDirectory(Path.Combine(ownCustom, "mine", "cfg", "comfig"));
        File.WriteAllText(Path.Combine(ownCustom, "mine", "cfg", "comfig", "comfig.cfg"), "bind h +back\n");

        ConfigConsole gameOnly = new();
        gameOnly.LoadFrom(game, NullLoggerFactory.Instance, new RecordingLogger(), NowhereFolder());
        gameOnly.KeyDown("h");

        ConfigConsole withOwn = new();
        withOwn.LoadFrom(game, NullLoggerFactory.Instance, new RecordingLogger(), ownCustom);
        withOwn.KeyDown("h");

        gameOnly.IsHeld(ViewerAction.FlyForward).ShouldBeTrue("the game's copy runs");
        withOwn.IsHeld(ViewerAction.FlyBack).ShouldBeTrue("the program's own copy is found first");
        withOwn.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void LoadFrom_WithoutALogger_Refuses()
    {
        Should.Throw<ArgumentNullException>(
            () => ConfigConsole.WithDefaults().LoadFrom("x", NullLoggerFactory.Instance, config: null!));
    }

    /// <remarks>
    /// **A read again replays only a file an earlier read ran, unchanged** (B841 reviews 4 and 5, Sonnet, Fable): the same
    /// `autoexec.cfg` is no second launch (B829), so its waited screenshot is taken once; edited, it is a new file and is
    /// run as one. *The viewer's own rule*: TF2 has no reread.
    /// </remarks>
    [TestCase(false, 1)]
    [TestCase(true, 2)]
    public void LoadFrom_ReadAgain_ReplaysOnlyAFileAnEarlierReadRanUnchanged(bool edited, int raised)
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        triggered().ShouldBe(1, "the control: the first read takes it");

        if (edited)
        {
            File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "wait; screenshot\nbind h +forward\n");
        }

        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(raised);
    }

    /// <remarks>
    /// **The record is kept by path and text**: `config.cfg` and `autoexec.cfg` holding the same waited screenshot are two
    /// files, and each takes one, as TF2 runs both.
    /// </remarks>
    [Test]
    public void LoadFrom_TwoFilesWithTheSameText_EachRunsItsOwn()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "wait; screenshot\n");
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();

        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(2);
    }

    /// <remarks>
    /// **A different file with an earlier file's text is new** (B841 review 5, Sonnet): `autoexec.cfg` first appearing at
    /// the second read with `config.cfg`'s text is run, and `config.cfg` read again is not.
    /// </remarks>
    [Test]
    public void LoadFrom_ANewFileWithAnEarlierFilesText_RunsAsNew()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        triggered().ShouldBe(1, "the control: the first read takes config.cfg's");

        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "wait; screenshot\n");
        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(2);
    }

    /// <remarks>
    /// **A file `exec`'d twice in one read runs twice, as TF2's startup runs it twice; a later read replays it** (B841
    /// review 5, Fable): only an EARLIER read makes a file a replay.
    /// </remarks>
    [Test]
    public void LoadFrom_AFileExecdTwiceInOneRead_RunsTwiceAndIsAReplayAfter()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "exec shots\n");
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "exec shots\n");
        File.WriteAllText(Path.Combine(game, "cfg", "shots.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        triggered().ShouldBe(2, "the control: one read runs it twice");

        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(2);
    }

    /// <remarks>
    /// **A file `exec`'d from a replayed config is judged by itself** (B841 review 5, Fable). With no TF2 found, the
    /// program's own `autoexec.cfg` runs `exec gameshots` and finds nothing; the chosen folder holds it, so the reread
    /// replays `autoexec.cfg` and reads `gameshots.cfg` as new, and its screenshot is taken.
    /// </remarks>
    [Test]
    public void LoadFrom_AReplayedConfigExecingAFileFirstFoundNow_RunsItAsNew()
    {
        string own = TempFolder();
        File.WriteAllText(Path.Combine(own, "autoexec.cfg"), "exec gameshots\n");
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "gameshots.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, null, own);
        triggered().ShouldBe(0, "the control: nothing found to exec yet");

        LoadAndRun(console, game, own);

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **A file an earlier read ran is a replay, whichever file reaches it now** (B841 review 5, Fable): the chosen folder's
    /// new `autoexec.cfg` runs `exec shots`, which the program's own `config.cfg` ran at startup, so it is not taken again.
    /// </remarks>
    [Test]
    public void LoadFrom_ANewConfigExecingAFileAnEarlierReadRan_ReplaysIt()
    {
        string own = TempFolder();
        File.WriteAllText(Path.Combine(own, "config.cfg"), "exec shots\n");
        File.WriteAllText(Path.Combine(own, "shots.cfg"), "wait; screenshot\n");
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "exec shots\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, null, own);
        triggered().ShouldBe(1, "the control: the first read takes it");

        LoadAndRun(console, game, own);

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **A waited `exec` runs after the read, as its queued line** (B841 review 6, Sonnet): it is no longer the read, so it
    /// takes the replay flag the line was queued with, and a reread's copy fires nothing. The first read's fires once.
    /// </remarks>
    [Test]
    public void LoadFrom_AWaitedExecReadAgain_FiresOnlyTheFirstReads()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "wait; exec shots\n");
        File.WriteAllText(Path.Combine(game, "cfg", "shots.cfg"), "screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        triggered().ShouldBe(1, "the control: the first read's waited exec takes it");

        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **The line after an `exec` is its own file's again** (B841 review 6, Sonnet): a replayed `autoexec.cfg` execs a
    /// file the chosen folder first holds, which is new, and its next line's waited screenshot is still the replay's and
    /// is not taken again.
    /// </remarks>
    [Test]
    public void LoadFrom_AReplayedConfigsLineAfterAnExecOfANewFile_IsStillAReplay()
    {
        string own = TempFolder();
        File.WriteAllText(Path.Combine(own, "autoexec.cfg"), "exec gamebinds\nwait; screenshot\n");
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "gamebinds.cfg"), "bind h +forward\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, null, own);
        triggered().ShouldBe(1, "the control: the first read takes it");

        LoadAndRun(console, game, own);

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **A waited `exec` is judged by the read that queued it, whenever it runs** (B841 review 6, Fable, Gemini): the
    /// replayed `autoexec.cfg`'s `wait; exec gameshots` runs after the read, and `gameshots.cfg`, first found in the chosen
    /// folder, is read as new and takes its screenshot; the program's own `config.cfg`'s `wait; exec shots` ran `shots.cfg`
    /// in the first read, so the chosen folder's new `autoexec.cfg` exec'ing it does not take it again.
    /// </remarks>
    [TestCase("autoexec.cfg", "wait; exec gameshots\n", "gameshots.cfg", null, 0, 1)]
    [TestCase("config.cfg", "wait; exec shots\n", "shots.cfg", "exec shots\n", 1, 1)]
    public void LoadFrom_AWaitedExecAcrossAReread_IsJudgedByItsOwnRead(
        string ownName, string ownText, string execd, string? gameAutoexec, int first, int second)
    {
        string own = TempFolder();
        File.WriteAllText(Path.Combine(own, ownName), ownText);
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(gameAutoexec is null ? Path.Combine(game, "cfg") : own, execd), "wait; screenshot\n");

        if (gameAutoexec is not null)
        {
            File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), gameAutoexec);
        }

        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, null, own);
        triggered().ShouldBe(first, "the control: the first read");

        LoadAndRun(console, game, own);

        triggered().ShouldBe(second);
    }

    /// <remarks>
    /// **The record names the file, not its case** (B841 review 6, Fable, Gemini): the search path ignores case on Windows,
    /// so `exec Shots` reaches the `shots.cfg` an earlier read ran.
    /// </remarks>
    [Test]
    public void LoadFrom_AnotherCaseOfAFileAnEarlierReadRan_ReplaysIt()
    {
        const string spelling = "exec Shots\n";
        string own = TempFolder();
        File.WriteAllText(Path.Combine(own, "config.cfg"), "exec shots\n");
        File.WriteAllText(Path.Combine(own, "shots.cfg"), "wait; screenshot\n");
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), spelling);
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, null, own);
        triggered().ShouldBe(1, "the control: the first read takes it");

        LoadAndRun(console, game, own);

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **The record names the file as the finder reads it** (B841 review 7, Fable): `V_RemoveDotSlashes` drops a `./` and
    /// takes either slash, so the game's `exec ./shots` and `exec .\shots` reach the `cfg/shots.cfg` an earlier read ran.
    /// The control: the first read takes it.
    /// </remarks>
    [TestCase("exec ./shots\n")]
    [TestCase("exec .\\shots\n")]
    public void LoadFrom_ADotSlashSpellingOfAFileAnEarlierReadRan_ReplaysIt(string spelling)
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "exec shots\n");
        File.WriteAllText(Path.Combine(game, "cfg", "shots.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        triggered().ShouldBe(1, "the control: the first read takes it");

        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), spelling);
        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **A key's `exec` is no read's, and leaves no mark on the record** (B841 review 7, Fable): a key runs `shots.cfg`
    /// between reads; a later read's new `autoexec.cfg` exec'ing it is the first READ to run it, and takes its screenshot.
    /// </remarks>
    [Test]
    public void LoadFrom_AFileOnlyAKeyRanBefore_RunsAsNew()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "bind k \"exec shots\"\n");
        File.WriteAllText(Path.Combine(game, "cfg", "shots.cfg"), "wait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        LoadAndRun(console, game, NowhereFolder());
        console.KeyDown("k");

        for (int run = 0; run < 2; run++)
        {
            console.Execute(waitAllowed: true);
        }

        triggered().ShouldBe(1, "the control: the key's took it");

        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "exec shots\n");
        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(2);
    }

    /// <remarks>
    /// **A file whose run threw partway is not on the record** (B841 review 6, Fable, Gemini): its lines after the throw
    /// never ran, so the next read runs it as new and its later waited screenshot is taken.
    /// </remarks>
    [Test]
    public void LoadFrom_AFileWhoseRunThrew_IsReadAsNewNextTime()
    {
        string game = TempFolder();
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "autoexec.cfg"), "mat_wireframe 1\nwait; screenshot\n");
        (ConfigConsole console, Func<int> triggered) = Counting();
        bool thrown = false;
        console.CheatRefused += (_, _) =>
        {
            if (!thrown)
            {
                thrown = true;
                throw new ArgumentException("a handler's failure");
            }
        };
        LoadAndRun(console, game, NowhereFolder());
        (thrown, triggered()).ShouldBe((true, 0), "the control: the run threw before the screenshot's line");

        LoadAndRun(console, game, NowhereFolder());

        triggered().ShouldBe(1);
    }

    /// <remarks>
    /// **The launch line's replay judges a file it `exec`s by itself too** (B841 review 6, Fable): `+exec shots` found
    /// nothing at launch; the replay finds `shots.cfg` for the first time, and its waited screenshot is taken.
    /// </remarks>
    [Test]
    public void Stuff_AReplayExecingAFileFirstFoundNow_RunsItAsNew()
    {
        byte[]? shots = null;
        ConfigConsole console = new() { FindConfig = path => path == "cfg/shots.cfg" ? shots : null };
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;
        console.Stuff("exec shots", oneShots: true);

        shots = "wait; screenshot\n"u8.ToArray();
        console.Stuff("exec shots", oneShots: false);

        for (int run = 0; run < 3; run++)
        {
            console.Execute(waitAllowed: true);
        }

        triggered.ShouldBe(1);
    }

    /// <summary>A console counting the one-shots it raises.</summary>
    private static (ConfigConsole Console, Func<int> Triggered) Counting()
    {
        ConfigConsole console = new();
        int triggered = 0;
        console.Triggered += (_, _) => triggered++;

        return (console, () => triggered);
    }

    /// <summary>Reads the configs, then runs three frames' `Cbuf_Execute`, enough for two `wait`s of one tick in turn.</summary>
    private static void LoadAndRun(ConfigConsole console, string? game, string own)
    {
        console.LoadFrom(game, NullLoggerFactory.Instance, new RecordingLogger(), own);

        for (int run = 0; run < 3; run++)
        {
            console.Execute(waitAllowed: true);
        }
    }

    /// <summary>A folder that exists but holds no configs.</summary>
    private static string NowhereFolder() => TempFolder();

    private static string TempFolder()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            "tf2ds-cfg-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(folder);

        return folder;
    }
}
