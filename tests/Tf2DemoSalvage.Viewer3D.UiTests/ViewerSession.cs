using System;
using System.Diagnostics;
using System.IO;

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
internal sealed class ViewerSession
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

    /// <summary>What the viewer logs when it projects the world through the camera.</summary>
    public const string WorldBuildLine = "building the world";

    /// <summary>What it logs when it decodes and uploads a map's textures.</summary>
    public const string TextureUploadLine = "uploading textures";

    [OneTimeSetUp]
    public void LaunchTheViewer()
    {
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

        _viewer = ViewerApplication.Launch(DemoPath);

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
