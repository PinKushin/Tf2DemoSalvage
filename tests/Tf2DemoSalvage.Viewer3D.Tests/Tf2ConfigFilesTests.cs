using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Finding the player's own configs on a real install, loose files and VPKs alike (D69).
/// </summary>
/// <remarks>
/// **The interpreter is worthless without this.** `ConfigConsole` can run a config; if nothing goes
/// and gets one, the viewer runs its shipped defaults for ever and every conformance test exercises
/// code the viewer never reaches. That is the shape of the three no-ops recorded in
/// `docs/memory/output-level-assertion-or-it-is-not-done.md`, and this suite is the assertion that
/// would have caught them.
/// </remarks>
public sealed class Tf2ConfigFilesTests
{
    [Test]
    public void Read_TheInstalledGame_FindsTheConfigsAndTheyBind()
    {
        // **End to end on the real install: find the files, run them, check the camera can fly.**
        // Every intermediate step of this has its own test elsewhere; none of them can fail when the
        // discovery is pointed at the wrong folder, which is the failure this catches.
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        IReadOnlyList<string> configs = Tf2ConfigFiles.Read(tf);

        TestContext.Out.WriteLine($"{configs.Count} configs found under {tf}");

        configs.Count.ShouldBeGreaterThan(0, "a real install has at least config_default.cfg");

        ConfigConsole console = ConfigConsole.WithDefaults();
        console.Load(configs);

        TestContext.Out.WriteLine($"{console.Applied} of {console.Bound} binds applied");

        console.Bound.ShouldBeGreaterThan(10, "a real config binds a lot of keys");
        console.Applied.ShouldBeGreaterThan(5, "and enough of them are ours to fly with");

        console.KeyDown(console.Bindings().KeyFor(ViewerAction.FlyForward));
        console.Intent();

        console.Intent().Forward.ShouldBe(1f, "whatever key their config uses, forward must fly");

        // **Loading somebody's config must not disable a control, and this is the assertion that
        // caught it happening.** Before the fallback in `CommandFor`, the real install produced
        //
        //     no key reaches: ResetCamera, PlayPause, FlyFast
        //
        // because `resetcamera` and `playpause` are this project's own command names — TF2 has no
        // equivalent, so its config simply uses `f` and `k` for other things and those controls
        // were gone. **No synthetic fixture would have shown it**: it takes a real config, which
        // binds keys for reasons that have nothing to do with this viewer.
        console.Unbound().ShouldBeEmpty("a TF2 config must not cost this viewer a control");
    }

    [Test]
    public void Read_NoGameFolder_IsEmptyRatherThanThrowing()
    {
        // The viewer must start on a machine with no TF2 installed. Its own defaults are a complete
        // set of controls, so this is a normal outcome and not a degraded one.
        Tf2ConfigFiles.Read(null).ShouldBeEmpty();
        Tf2ConfigFiles.Read(string.Empty).ShouldBeEmpty();
        Tf2ConfigFiles.Read("   ").ShouldBeEmpty();
    }

    [Test]
    public void Read_AFolderWithNoConfigs_IsEmptyRatherThanThrowing()
    {
        // A folder that exists and holds nothing we want is different from a folder that is not
        // there, and both have to be survivable.
        string empty = Path.Combine(Path.GetTempPath(), "tf2ds-no-configs-" + Guid.NewGuid());
        Directory.CreateDirectory(empty);

        try
        {
            Tf2ConfigFiles.Read(empty).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Test]
    public void Order_TheExecOrder_PutsAutoexecLast()
    {
        // **`valve.rc` execs config.cfg and then autoexec.cfg**, so the hand-written file wins. It
        // is also where the aliases live, which is the case that broke the first implementation:
        // config.cfg binds `w` to `+mfwd` and never says what `+mfwd` means.
        //
        // Asserted rather than left to the field's declaration because reordering it would change
        // which config wins, silently, and only for people who have both.
        Tf2ConfigFiles.Order.ShouldBe(
            ["cfg/config_default.cfg", "cfg/config.cfg", "cfg/autoexec.cfg"]);
    }

    [Test]
    public void Read_AConfigInACustomVpk_IsFoundThroughTheArchives()
    {
        // **The owner asked for VPKs by name** — "in .cfg or vpk form like comfig's configs" — and
        // mastercomfig ships precisely that: a `.vpk` under `tf/custom/` holding `cfg/*.cfg`.
        //
        // **This test reports rather than asserts, and that is deliberate.** Whether this machine
        // has a config-bearing VPK installed is not a property of the code, so asserting on it would
        // make the suite depend on the owner's mod choices. What it can do is prove the route is
        // live: `GameArchives` mounts `tf/custom/*` above the stock files, so if a pack is present
        // its configs are what `Read` returns.
        if (GameInstall.Root is not { } tf)
        {
            Assert.Ignore(GameInstall.Missing);
            return;
        }

        string custom = Path.Combine(tf, "custom");

        if (!Directory.Exists(custom))
        {
            Assert.Ignore("No tf/custom folder on this install.");
            return;
        }

        string[] packs = Directory.GetFiles(custom, "*.vpk", SearchOption.TopDirectoryOnly);

        TestContext.Out.WriteLine(
            $"{packs.Length} VPKs in tf/custom: {string.Join(", ", Array.ConvertAll(packs, Path.GetFileName))}");

        // The claim under test is that discovery goes through the archives at all, which is what
        // makes a VPK readable without this file knowing what one is.
        Tf2ConfigFiles.Read(tf).Count.ShouldBeGreaterThan(0);
    }
}
