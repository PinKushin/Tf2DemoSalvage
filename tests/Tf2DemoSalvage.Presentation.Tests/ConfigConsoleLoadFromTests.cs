using System;
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
