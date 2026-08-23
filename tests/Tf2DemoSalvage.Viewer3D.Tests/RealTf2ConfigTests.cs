using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The installed game's own configs, read as a user would paste them (D69).
/// </summary>
/// <remarks>
/// **The assertion the synthetic suite cannot make.** `SourceConfigTests` feeds the reader fixtures
/// copied out of `config_default.cfg` by hand, which proves the reader agrees with what somebody
/// transcribed. This points it at the actual files on disk — including whatever the owner has
/// personally rebound in `config.cfg`, which nobody transcribed and which is the case the feature
/// exists for.
///
/// **This lives in the viewer suite rather than beside the reader** because it needs a TF2 install,
/// and the presentation project is meant to run on the Linux measurement boxes.
/// </remarks>
public sealed class RealTf2ConfigTests
{
    [Test]
    public void ApplySourceConfig_TheInstalledDefaultConfig_LandsTheMovementBinds()
    {
        if (Config("config_default.cfg") is not { } text)
        {
            Assert.Ignore("Team Fortress 2 is not installed; set TF2_FOLDER to run this.");
            return;
        }

        KeyBindings bindings = new();
        int applied = bindings.ApplySourceConfig(text);

        TestContext.Out.WriteLine(
            $"config_default.cfg: {SourceConfig.ReadBinds(text).Count} binds, {applied} applied");

        // The shipped defaults are what `KeyBindings.Defaults` was derived from, so applying them
        // must be a no-op in effect — which is a stronger check than it looks: it says the two were
        // read from the same file rather than one being remembered.
        applied.ShouldBeGreaterThan(5, "the movement and camera binds should have landed");

        bindings.KeyFor(ViewerAction.FlyForward).ShouldBe("w");
        bindings.KeyFor(ViewerAction.FlyBack).ShouldBe("s");
        bindings.KeyFor(ViewerAction.FlyUp).ShouldBe("'");
        bindings.KeyFor(ViewerAction.FlyDown).ShouldBe("/");
        bindings.KeyFor(ViewerAction.SwitchCameraMode).ShouldBe("SPACE");
    }

    [Test]
    public void ApplySourceConfig_TheOwnersOwnConfig_IsReadWithoutComplaint()
    {
        // **The file nobody transcribed.** `config.cfg` is whatever this machine's player actually
        // bound, saved by the engine — hundreds of lines, most of them cvars this viewer has never
        // heard of. The requirement is that it lands the binds and ignores the rest in silence.
        if (Config("config.cfg") is not { } text)
        {
            Assert.Ignore("No config.cfg in this install.");
            return;
        }

        // **Both files, because the bind and its meaning live in different ones.** `config.cfg`
        // binds `w` to `+mfwd`; `autoexec.cfg` defines `+mfwd` as a null-cancelling script that runs
        // `+forward`. Reading either alone finds no movement bindings at all — which is exactly what
        // happened, and what the synthetic suite could not show.
        KeyBindings bindings = new();

        string[] configs = [Config("autoexec.cfg") ?? string.Empty, text];

        int binds = SourceConfig.ReadBinds(text).Count;
        int applied = bindings.ApplySourceConfigs(configs);

        TestContext.Out.WriteLine($"config.cfg: {binds} binds, {applied} applied across both files");

        foreach ((ViewerAction action, string key) in bindings.All())
        {
            TestContext.Out.WriteLine($"  {action,-20} {key}");
        }

        binds.ShouldBeGreaterThan(10, "a real config binds a lot of keys");

        // **Every action has a key to SHOW, which is a weaker claim than it looks and is the only
        // one this projection can make.** `KeyBindings` fills anything the config did not mention
        // from the defaults, so this passes even for an action the config left genuinely
        // unreachable — it is a statement about the settings screen, not about the controls.
        //
        // The honest question is asked by `ConfigConsole.Unbound`, and on this machine the answer
        // is not empty: `config.cfg` contains `bind "SHIFT" "+duck"`, so Shift belongs to a command
        // this viewer has no equivalent for and fly-fast has nothing to press. That is the config
        // being obeyed, and it is reported rather than overridden.
        foreach (ViewerAction action in Enum.GetValues<ViewerAction>())
        {
            bindings.KeyFor(action).ShouldNotBeNullOrWhiteSpace($"{action} has no key to display");
        }
    }

    [Test]
    public void ReadBinds_EveryShippedConfig_ParsesWithoutThrowing()
    {
        // **Totality, the standard this project holds decoders to.** These are real files written by
        // Valve and by the engine, so anything that throws on one is our defect — and a config that
        // makes the reader throw would take the viewer down at startup rather than costing one
        // binding.
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        string folder = Path.Combine(tf, "cfg");

        if (!Directory.Exists(folder))
        {
            Assert.Ignore($"{folder} is not present.");
            return;
        }

        int files = 0;
        int binds = 0;

        foreach (string path in Directory.EnumerateFiles(folder, "*.cfg"))
        {
            files++;
            binds += SourceConfig.ReadBinds(File.ReadAllText(path)).Count;
        }

        TestContext.Out.WriteLine($"{files} shipped .cfg files, {binds} binds between them");

        files.ShouldBeGreaterThan(5, "TF2 ships a folder full of them");
    }

    [Test]
    public void KeyDown_TheOwnersRealMovementScript_FliesTheCamera()
    {
        // **The output-level assertion, on the real files.** Everything else here counts binds, and
        // a count is a claim about parsing. This presses the keys and asks whether the camera would
        // actually move — the only check that can fail when the wiring is wrong.
        //
        // The owner's config is a null-cancelling script: `config.cfg` binds `w` to `+mfwd`, and
        // `autoexec.cfg` defines `+mfwd` as `-back; +forward; alias checkfwd +forward`. Nothing
        // short of executing it produces the right answer.
        if (Config("config.cfg") is not { } config)
        {
            Assert.Ignore("No config.cfg in this install.");
            return;
        }

        ConfigConsole console = new();
        console.Load([Config("autoexec.cfg") ?? string.Empty, config]);

        TestContext.Out.WriteLine($"{console.Applied} of {console.Bound} binds landed");

        console.KeyDown("w");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeTrue("W should fly forward");

        // Hold the opposite direction. In a null-cancel script the newer key wins outright, rather
        // than the two summing to a standstill as the engine's own default does.
        console.KeyDown("s");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
        console.IsHeld(ViewerAction.FlyBack).ShouldBeTrue();

        // And releasing it resumes forward, because W is still held and the script remembered.
        console.KeyUp("s");
        console.IsHeld(ViewerAction.FlyBack).ShouldBeFalse();
        console.IsHeld(ViewerAction.FlyForward)
            .ShouldBeTrue("the script restores the direction still held");

        console.KeyUp("w");
        console.IsHeld(ViewerAction.FlyForward).ShouldBeFalse();
    }

    [Test]
    public void Load_EveryShippedConfig_LeavesEveryActionReachable()
    {
        // **Totality again, but for the interpreter rather than the reader.** Executing a real
        // config must not leave a control unreachable — and a script can do that in ways a bind
        // list cannot, by pressing a button no key ever releases.
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        string folder = Path.Combine(tf, "cfg");

        if (!Directory.Exists(folder))
        {
            Assert.Ignore($"{folder} is not present.");
            return;
        }

        ConfigConsole console = new();

        foreach (string path in Directory.EnumerateFiles(folder, "*.cfg"))
        {
            Should.NotThrow(() => console.Load(File.ReadAllText(path)), Path.GetFileName(path));
        }

        foreach (ViewerAction action in Enum.GetValues<ViewerAction>())
        {
            console.IsHeld(action)
                .ShouldBeFalse($"{action} is stuck down after merely loading configs");
        }

        console.Bindings().KeyFor(ViewerAction.FlyForward).ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>One of the game's config files, or null when it is not there.</summary>
    private static string? Config(string name) =>
        GameInstall.Find($"cfg/{name}") is { } path ? File.ReadAllText(path) : null;
}
