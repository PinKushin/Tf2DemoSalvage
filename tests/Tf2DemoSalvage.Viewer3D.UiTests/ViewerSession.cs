using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// One viewer, launched once, shared by every test in this assembly.
/// </summary>
/// <remarks>
/// **A SetUpFixture runs once for the whole namespace**, which is what makes this the right home
/// for the application rather than a per-fixture <c>OneTimeSetUp</c>. Three fixtures each launched
/// their own, and because <c>Close</c> only asks a window to shut and returns immediately, they
/// accumulated: three viewers were observed alive together, each holding a Direct3D device, each
/// writing its own log, and each locking the executable so the next build failed with
/// "The file is locked by: tf2demoview (11284), tf2demoview (8040), tf2demoview (29452)".
///
/// Launching is also the expensive part — a runtime, a device against a real adapter, and a map
/// read of a hundred megabytes — so paying it once is worth far more than the isolation it costs.
/// Nothing here mutates state another test reads; the one thing that leaks is full screen, and the
/// fixtures put that back themselves.
///
/// **The demo is opened at launch, for every test.** The fixtures that ran without one existed
/// before there was a map to load and could not see anything a real scene would: with no world and
/// no textures, correct code and broken code produce identical observations.
///
/// Modelled on PokemonBattleJournal's <c>AppiumSetup</c>, which solved the same problem for a MAUI
/// app — single static instance, leftovers killed before launching, killed again at the end. No
/// Appium here: FlaUI drives UIA in-process, so there is no server to start and no port to keep out
/// of another suite's way.
/// </remarks>
[SetUpFixture]
internal sealed partial class ViewerSession
{
    private static ViewerApplication? _viewer;

    /// <summary>The running viewer.</summary>
    /// <exception cref="InvalidOperationException">Called outside a run, so nothing was launched.</exception>
    public static ViewerApplication App => _viewer ?? throw new InvalidOperationException(
        "The viewer is not running; ViewerSession did not complete its setup.");

    /// <summary>A committed demo whose map ships with the game.</summary>
    public static string DemoPath => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", "tf2-2013-build1729296-pov-cp_badlands.dem"));

    /// <summary>The tick the session opens at, chosen so a capture is worth looking at.</summary>
    /// <remarks>
    /// **The suite used to photograph a wall, and the reason it did was a false premise.** The capture
    /// test jumped to the END of this demo, justified like this: TF2 replaced the `v_` viewmodels with
    /// `c_` models around 2011, so a 2013 recording names `v_scattergun_scout.mdl` at tick 0, "which
    /// the current install no longer ships, and the renderer correctly draws nothing". The end tick
    /// was picked because a `c_` model is named there.
    ///
    /// **That claim is wrong, and checking it is what unstuck this.** `v_` models are shipped — inside
    /// the VPKs, which is why looking for loose files says nothing — and this project already renders
    /// them: every off-hand watch in `z1800` is `v_models/v_watch_*.mdl` and they draw. Verified
    /// directly at this tick: `viewmodel pass: drawing 1 at v_rocketlauncher_soldier`.
    ///
    /// So the constraint that forced the end of the demo never existed, and the end of a solo
    /// recording is a player parked against a spawn gate — which is shut for the whole demo whatever
    /// they did, because brush entities draw at their COMPILED position (B71).
    ///
    /// **2500 was measured, not guessed**
    /// (`OffHandProbe.MainHandViewmodel_OnTheUiSuitesDemo_IsReported`). The recorder is at
    /// (−2521, −2072, 478) holding a rocket launcher: out on the map, above the ground, with sky and
    /// buildings in frame. Its capture holds many times the colour variety of the spawn-gate frame it
    /// replaces.
    ///
    /// The two `c_` ranges are both poor choices for the opposite reason — the pyro never leaves
    /// spawn (x −608..−279, z 192 throughout) and the sniper settles at (−157, −4260) by tick 7750 and
    /// does not move again.
    /// </remarks>
    public const int OpeningTick = 2500;

    /// <summary>What the viewer logs when it projects the world through the camera.</summary>
    public const string WorldBuildLine = "building the world";

    /// <summary>What it logs when it decodes and uploads a map's textures.</summary>
    public const string TextureUploadLine = "uploading textures";

    [OneTimeSetUp]
    public void LaunchTheViewer()
    {
        RefuseIfAGameHasTheDesktop();

        if (!File.Exists(DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {DemoPath}.");
            return;
        }

        // **Leftovers first.** A run that crashed, or was stopped mid-test, leaves a viewer holding
        // the executable — and the next build then fails on a locked file rather than on anything
        // to do with the code. Killed rather than closed, because these are already known to be
        // abandoned and nothing is waiting to hear from them.
        KillStrayViewers();

        // Opened at a chosen tick rather than at zero, so the captures show something. Passed on the
        // command line because the scrub bar does not support the RangeValue pattern and so cannot
        // be set through automation — `TransportUiTests` records that, and reads the tick label
        // instead.
        _viewer = ViewerApplication.Launch(
            DemoPath, "--tick", OpeningTick.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Synchronised on the world appearing in the log, not on a delay. Loading a map reads a
        // hundred megabytes and decodes a couple of hundred textures, and how long that takes is a
        // property of the machine, not of the program.
        Retry.WhileFalse(
            () => App.Count(WorldBuildLine) > 0 && App.Count(TextureUploadLine) > 0,
            TimeSpan.FromSeconds(120),
            throwOnTimeout: true,
            timeoutMessage:
                $"The viewer never reported building a world. Log: {_viewer.LogPath ?? "NONE FOUND"} " +
                $"in {ViewerApplication.Folder} (−1 below means no log was read); " +
                $"worlds {_viewer.Count(WorldBuildLine)}, textures {_viewer.Count(TextureUploadLine)}.");
    }

    [OneTimeTearDown]
    public void CloseTheViewer()
    {
        _viewer?.Dispose();
        _viewer = null;

        // Asked politely above, and confirmed here. A viewer that survived its own Dispose would
        // otherwise be inherited by the next run as a "stray" — which works, but hides the fact
        // that shutting down did not.
        KillStrayViewers();
    }

    /// <summary>Games that capture input, and must not be in front when this suite runs.</summary>
    /// <remarks>
    /// **Deliberately a short list rather than "anything that is not us".** The owner having an
    /// editor or a browser focused while tests run is normal and harmless — these tests drive their
    /// own window and a stolen click lands somewhere recoverable. A GAME is different: it captures
    /// the mouse, so synthesized input goes into it as movement and fire commands, and the owner's
    /// session is what pays.
    ///
    /// A broader check would be safer and would also refuse most of the time, which is how a guard
    /// gets deleted.
    /// </remarks>
    private static readonly string[] InputCapturingGames =
    [
        "tf_win64", "tf", "hl2", "csgo", "cs2", "portal2", "left4dead2",
    ];

    /// <summary>Refuses to run while a game holds the foreground.</summary>
    /// <remarks>
    /// **One P/Invoke pair, microseconds, once per assembly** — the cost had to be nil or this would
    /// not be worth having, and the owner said so directly.
    ///
    /// Why it exists: <c>run-exclusive.ps1</c> serialises this machine against other AGENTS and
    /// knows nothing about the owner's own running game. A UI phase firing while TF2 is focused does
    /// not fail; it delivers clicks and keystrokes into TF2. That hazard is written down in the
    /// global rules for two agents sharing a desktop and had never been written down for the human
    /// case — it was found on 2026-08-16, after a whole session of UI runs with nothing checking it.
    ///
    /// Ignores rather than fails, because the owner being mid-game is not a defect in this code and
    /// a red suite would train someone to rerun it until it passed.
    /// </remarks>
    private static void RefuseIfAGameHasTheDesktop()
    {
        nint window = GetForegroundWindow();

        if (window == 0)
        {
            return;
        }

        _ = GetWindowThreadProcessId(window, out uint owner);

        string? name = null;

        try
        {
            using Process process = Process.GetProcessById((int)owner);
            name = process.ProcessName;
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            // The window's process ended between the two calls, which is not a reason to stop.
            return;
        }

        if (InputCapturingGames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            Assert.Ignore(
                $"{name} has the foreground. This suite drives the desktop with synthesized input, " +
                "so running now would deliver clicks and keystrokes into the game. Close it or " +
                "alt-tab away and run again.");
        }
    }

    // **Pinned to System32, which is CA5392 and is a real rule rather than ceremony.** An unqualified
    // P/Invoke searches the application directory first, so a user32.dll dropped beside the test
    // binaries would be loaded in preference to Windows' own. That matters more here than in most
    // places: this assembly runs from a build output directory that tooling writes to.
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(
        System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(
        System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    /// <summary>Ends any viewer left behind by an earlier run.</summary>
    private static void KillStrayViewers()
    {
        foreach (Process stray in Process.GetProcessesByName("tf2demoview"))
        {
            try
            {
                stray.Kill(entireProcessTree: true);
                stray.WaitForExit(10_000);

                ViewerApplication.Log($"killed a stray viewer, process {stray.Id}");
            }
            catch (InvalidOperationException)
            {
                // It exited between being listed and being killed, which is the outcome wanted.
            }
            catch (System.ComponentModel.Win32Exception failure)
            {
                // Access denied, or it is already terminating. Reported rather than swallowed: a
                // viewer that cannot be killed will lock the next build, and that failure would
                // otherwise arrive with no explanation attached to it.
                ViewerApplication.Log($"could not kill viewer {stray.Id}: {failure.Message}");
            }
            finally
            {
                stray.Dispose();
            }
        }
    }
}
