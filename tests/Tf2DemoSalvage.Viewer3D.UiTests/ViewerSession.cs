using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.SdkReference;

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

    /// <summary>Skips the caller unless Team Fortress 2 is installed on this machine.</summary>
    /// <remarks>
    /// **The UI suite never had this gate and CI has been red because of it.** A test that waits
    /// for a viewmodel to be drawn is waiting for `models/weapons/v_*.mdl` out of TF2's VPKs; with
    /// no game installed the model cannot resolve, nothing draws, and the wait times out with
    /// *"The viewmodel never reached the screen"*. That message is true and the conclusion drawn
    /// from it is wrong — it reports a missing environment as a defect in the renderer.
    ///
    /// Observed on run 32516362350 and every run around it: the job stopped there, so the count
    /// checks after it had never executed at all. One un-gated precondition was hiding the state of
    /// the whole gate.
    ///
    /// **This used to be twenty lines of its own, and that was the problem, not the length.** It
    /// carried a fourth private copy of the Steam library roots — and a laxer one: it accepted
    /// `TF2_FOLDER` on the folder merely existing, so a stale or mistyped value made this pass
    /// while every other suite skipped. <see cref="GameInstall"/> requires the VPK in there,
    /// because a Steam library keeps a directory for a game that has been uninstalled.
    ///
    /// **The reason text is kept**, because it is specific in a way the generic one is not: it says
    /// what will be missing on screen, not merely that a folder was not found.
    ///
    /// Only tests that need game ASSETS call this. The shell and transport tests drive the window
    /// and need no models, so gating them would hide real breakage.
    /// </remarks>
    public static void RequireTheGame()
    {
        if (!GameInstall.Available)
        {
            Skip.Because(
                "Team Fortress 2 is not installed, so no model can resolve and nothing can be drawn "
                + "into a viewmodel. Set TF2_FOLDER to run these.");
        }
    }

    /// <summary>A committed demo whose map ships with the game.</summary>
    public static string DemoPath => Corpus("tf2-2013-build1729296-pov-cp_badlands.dem");

    /// <summary>The file name of the demo the session opens with.</summary>
    public const string DemoName = "tf2-2013-build1729296-pov-cp_badlands.dem";

    private static string Corpus(string file) => Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory,
        "..", "..", "..", "..", "..",
        "tools", "corpus", "demos", file));

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
    /// **2500 was measured, and it measured a corpse.** It was chosen for the frame it produced —
    /// the recorder reported at (−2521, −2072, 478), "out on the map, above the ground, with sky and
    /// buildings in frame". He is DEAD there. `m_lifeState` reads 2 from tick 2012 to 3208, and
    /// through that whole span the position is frozen or alternates between two fixed points:
    /// (−355, −3635, 68) is where he fell, and (−2521, −2072, 478) and (−337, −3485, 306) are
    /// observer positions. The frame was rich precisely BECAUSE it was a freezecam looking across
    /// the map, and the number praised above is the camera rather than the player.
    ///
    /// **That is why the first-person suite was anchored on a moment where this viewer and TF2
    /// disagree** (B222). TF2 cannot show a dead player in first person at all — the camera switches
    /// to third — so the viewmodel this test waits for is one the engine would never draw. The test
    /// passed on a divergence.
    ///
    /// **1900 is measured the same way and the recorder is alive**: `m_lifeState` 0, at
    /// (−140, −3435, 242) — elevated, and well clear of the spawn at y ≈ −4500 — holding
    /// `v_models/v_rocketlauncher_soldier.mdl`, the same viewmodel 2500 named, so nothing keyed to
    /// that model changes.
    ///
    /// **No margin is needed before the death at 2008, because the suite never advances the demo.**
    /// It opens at a tick and stays there. The owner, on the earlier worry about running into the
    /// death: *"the demo never runs in the UI suite"*, and *"theres really no scene richness that
    /// wont be there 100 ticks before or something before I die"* — which is the whole argument for
    /// simply stepping back to the nearest live tick rather than hunting for another rich frame.
    ///
    /// The alive spans, for anyone choosing differently: 0–2008, 3208–4944, 6228–7700. The two `c_`
    /// ranges are poor for an unrelated reason — the pyro never leaves spawn (x −608..−279, z 192
    /// throughout) and the sniper settles at (−157, −4260) by tick 7700 and does not move again.
    /// </remarks>
    public const int OpeningTick = 1900;

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
        // **One demo, named on the command line, and it opens because of that.**
        //
        // **This was briefly two, and the owner had it removed.** The idea was to test demo
        // switching through the UI. `MainForm` deliberately does not auto-open when handed several
        // files — they are a playlist to choose from — so the session had to open one itself through
        // the playlist, and `--tick` applies to the demo the *command line* loads. The seek stopped
        // happening, the viewer sat at tick zero where nobody holds anything, and the capture tests
        // failed with "the viewmodel never reached the screen". **Every test in the assembly paid
        // for a fixture that two needed.**
        //
        // > *"adding 2 or 3 tests shouldnt be causing a doubling of test time … the real underlying
        // > problem, is you didnt design the tests well."*
        //
        // The second attempt gave the switching tests their own viewer instead, which was worse
        // again — two copies of the application alive at once, which is exactly what the remarks at
        // the top of this file exist to prevent.
        //
        // > *"just forget the load tests as ui tests, it cant be tested without agrivating the hell
        // > out of me, 20 fucking seconds, no."*
        //
        // **A map switch costs about twenty seconds and no test design makes it cheaper**, because
        // the twenty seconds are the work itself (B146). Loading is covered where it costs nothing:
        // `LoadedDemoTests` drives `LoadDemo` and `LoadDemoAsync` with no window at all.
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

    /// <summary>What the viewer logs when the first-person view is entered, either way.</summary>
    /// <remarks>
    /// **The prefix, because which mechanism follows depends on the demo.** A point-of-view
    /// recording carries its own camera; a SourceTV one does not and spectates a player instead.
    /// Both are "first person on", and a fixture that works on either has to wait for the part
    /// they share.
    /// </remarks>
    public const string FirstPersonOn = "first person on";

    /// <summary>Presses whatever key is bound to switching the camera mode.</summary>
    /// <remarks>
    /// **Pinned rather than hardcoded, and the pinning is the point.** This action was on `V` until
    /// bindings arrived (D68) and is now on `SPACE`, matching what TF2 binds — its spectator HUD
    /// prints `[%jump%]` beside "Switch Camera Mode".
    ///
    /// A test pressing a literal key fails the *wrong way* when a binding moves: it presses a key
    /// that does nothing, waits for a state change that cannot happen, and reports a timeout. Three
    /// of these did exactly that, and the visible symptom was Windows dinging on every unhandled
    /// press while the retry loop spun — the owner diagnosed it by ear before the log said anything.
    ///
    /// Asserting the binding first turns that into one clear failure naming the cause. It has caught
    /// a change twice: `V` to `Space`, then `Space` to `SPACE` when D69 made the names config names.
    ///
    /// **Here rather than in one fixture** because two now need it, and a copy would be a second
    /// place for the guard to go stale.
    /// </remarks>
    public static void PressSwitchCameraMode()
    {
        KeyBindings.Defaults[ViewerAction.SwitchCameraMode].ShouldBe(
            "SPACE", "this presses SPACE below — rebind both together");

        App.PressKey(VirtualKeyShort.SPACE);
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
