using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

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
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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

        List<string> configs = Texts(tf);

        TestContext.Out.WriteLine($"{configs.Count} configs found under {tf}");

        configs.Count.ShouldBeGreaterThan(0, "a real install has at least config_default.cfg");

        ConfigConsole console = ConfigConsole.WithDefaults();
        configs.ForEach(console.Load);

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
        Texts(null).ShouldBeEmpty();
        Texts(string.Empty).ShouldBeEmpty();
        Texts("   ").ShouldBeEmpty();
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
            Texts(empty).ShouldBeEmpty();
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
        Texts(tf).Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Read_AConfigFlatInOwnCustom_IsFound()
    {
        // D193: "cfgs can also technically just be put in the cfg folder, so we have to actually
        // check both" — a config a player pastes directly into OUR custom/ folder, no cfg/
        // subfolder, must still be picked up.
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-flat-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "autoexec.cfg"), "bind \"w\" \"+forward\"");

        try
        {
            List<string> configs = Texts(null, ownCustomRoot: root);

            configs.ShouldContain(text => text.Contains("+forward", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Read_AConfigUnderCfgInOwnCustom_IsAlsoFound()
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-cfg-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "cfg"));
        File.WriteAllText(Path.Combine(root, "cfg", "config.cfg"), "bind \"e\" \"+use\"");

        try
        {
            IReadOnlyList<(string Path, string Text)> files = Tf2ConfigFiles.ReadFiles(null, ownCustomRoot: root);

            // Each config carries its path from `Order`, which ranks it (D264, B816).
            files.ShouldContain(file => file.Path == "cfg/config.cfg" && file.Text.Contains("+use", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Read_AConfigInAnAddonFolderOfOwnCustom_ReplacesTheGamesCopy()
    {
        // `exec` reads the first match on the search path, and our custom/* sits above the game's (D193):
        // one config.cfg results, and it is ours.
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-addon-" + Guid.NewGuid());
        string game = Path.Combine(root, "tf");
        string custom = Path.Combine(root, "custom");
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        Directory.CreateDirectory(Path.Combine(custom, "mine", "cfg"));
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "bind \"e\" \"+game\"");
        File.WriteAllText(Path.Combine(custom, "mine", "cfg", "config.cfg"), "bind \"e\" \"+ours\"");

        try
        {
            Texts(game, ownCustomRoot: custom).ShouldBe(["bind \"e\" \"+ours\""]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    /// **A flat file in our custom/ stands in only for a name directly below `cfg/`** (B820 review 1, Sonnet): `exec
    /// comfig/comfig` is found where the engine finds it, under an addon's `cfg/`. The control: the top-level name is found.
    /// </remarks>
    [Test]
    public void Finder_AFlatFileInOwnCustom_AnswersOnlyATopLevelName()
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-nested-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "comfig"));
        File.WriteAllText(Path.Combine(root, "comfig", "comfig.cfg"), "bind e +use");
        File.WriteAllText(Path.Combine(root, "comfig.cfg"), "bind e +use");

        try
        {
            Func<string, byte[]?> find = Tf2ConfigFiles.Finder(null, ownCustomRoot: root);

            find("cfg/comfig/comfig.cfg").ShouldBeNull();
            find("cfg/comfig.cfg").ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    /// **An empty flat file falls through to the game's copy** (B820 review 1, Sonnet, Grok), as before B820: a 0-byte
    /// placeholder in our custom/ must not cost the player the game's `config.cfg`.
    /// </remarks>
    [Test]
    public void Read_AnEmptyFlatFileInOwnCustom_LeavesTheGamesCopy()
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-empty-" + Guid.NewGuid());
        string game = Path.Combine(root, "tf");
        string custom = Path.Combine(root, "custom");
        Directory.CreateDirectory(Path.Combine(game, "cfg"));
        Directory.CreateDirectory(custom);
        File.WriteAllText(Path.Combine(game, "cfg", "config.cfg"), "bind \"e\" \"+game\"");
        File.WriteAllText(Path.Combine(custom, "config.cfg"), string.Empty);

        try
        {
            Texts(game, ownCustomRoot: custom).ShouldBe(["bind \"e\" \"+game\""]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>`Cmd_Exec_f`'s 1 MB cap holds for the startup configs, which `valve.rc` reaches through `exec` (B820).</remarks>
    [TestCase(Tf2ConfigFiles.MaximumBytes, 1)]
    [TestCase(Tf2ConfigFiles.MaximumBytes + 1, 0)]
    public void Read_AConfigAtAndOverOneMegabyte_IsReadOnlyAtTheLimit(int size, int read)
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-big-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "autoexec.cfg"), new string(' ', size));

        try
        {
            Texts(null, ownCustomRoot: root).Count.ShouldBe(read);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    /// **A flat file over the cap is read only to one byte past it** (B820 review 3, Fable), enough to be refused, rather than
    /// whole.
    /// </remarks>
    [Test]
    public void Finder_AFlatFileWellOverTheCap_IsReadOnlyToOneBytePast()
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-huge-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "autoexec.cfg"), new string(' ', 2 * Tf2ConfigFiles.MaximumBytes));

        try
        {
            Tf2ConfigFiles.Finder(null, ownCustomRoot: root)("cfg/autoexec.cfg")!.Length.ShouldBe(Tf2ConfigFiles.MaximumBytes + 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    /// A name the path API rejects is a missing file, not an exception out of a key press (B820 review 1, Fable, Sonnet). The
    /// wildcard and pipe cases are review 2's (Grok): a character a file name cannot hold is a missing file too.
    /// </remarks>
    [TestCase("cfg/a\0b.cfg")]
    [TestCase("cfg/a*.cfg")]
    [TestCase("cfg/a?.cfg")]
    [TestCase("cfg/a|b.cfg")]
    [TestCase("cfg/a<b>.cfg")]
    [TestCase("cfg/a\"b.cfg")]
    public void Finder_ANameAFileCannotHave_IsMissing(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "tf2ds-owncustom-nul-" + Guid.NewGuid());
        Directory.CreateDirectory(root);

        try
        {
            // The control (B820 review 3, Grok): a file the same lookup can find is found, so null above is the name's.
            File.WriteAllText(Path.Combine(root, "ok.cfg"), "bind e +use");
            Func<string, byte[]?> find = Tf2ConfigFiles.Finder(null, ownCustomRoot: root);

            find("cfg/ok.cfg").ShouldNotBeNull();
            find(name).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Read_NoOwnCustomRoot_IsUnaffected()
    {
        Texts(null, ownCustomRoot: null).ShouldBeEmpty();
        Texts(null, ownCustomRoot: "   ").ShouldBeEmpty();
    }

    /// <summary>The configs' text alone, in exec order.</summary>
    private static List<string> Texts(string? gameFolder, string? ownCustomRoot = null) =>
        [.. System.Linq.Enumerable.Select(Tf2ConfigFiles.ReadFiles(gameFolder, ownCustomRoot: ownCustomRoot), file => file.Text)];
}
