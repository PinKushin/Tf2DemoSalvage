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
