using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using FlaUI.Core.Tools;

using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The loading screen the owner asked for: up at once, gone once the demo is on screen.</summary>
/// <remarks>
/// *"is there a way to add like a splash screen that pops up immediately on boot and gives the user a progress bar?"* — the window
/// used to stay off screen for the whole decode and map read.
/// </remarks>
public sealed class LoadingOverlayUiTests
{
    /// <summary>The label a person reads says the load is on the map, for as long as the map has not arrived.</summary>
    /// <remarks>
    /// **The stage is held, so no client can be too late for it** (B707). This read the shared session's label in one window at
    /// launch, and on run 41669140049 UI Automation arrived after the decode. Here the mirror keeps the fetch open until the test
    /// has read the words, and a load cannot pass the map while its fetch is open (`ViewerPresenter.LoadDemoAsync`). The ceiling
    /// only ends a hung run. That the decode is the first stage is pinned synthetically:
    /// `LoadDemoAsync_ADemoWithItsMap_ReadsTheMapOffTheWindowsThreadBehindTheBarrier`.
    /// </remarks>
    [Test]
    public void LoadingOverlay_WhileTheMapIsBeingFetched_SaysSoOnScreen()
    {
        if (!File.Exists(CaptureUiTests.DemoPath))
        {
            Assert.Ignore($"The corpus demo is not present at {CaptureUiTests.DemoPath}.");
            return;
        }

        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tf2ds-ui-overlay-" + Guid.NewGuid().ToString("N"))).FullName;
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using LocalMirror mirror = new("cp_granary", [], held.Task);
            using ViewerApplication viewer = ViewerApplication.Launch(
                new Dictionary<string, string?>
                {
                    [SteamInstall.SteamRootVariable] = Directory.CreateDirectory(Path.Combine(root, "emptysteam")).FullName,
                    [SteamInstall.OverrideVariable] = null,
                    [MapDownloader.CacheVariable] = Directory.CreateDirectory(Path.Combine(root, "cache")).FullName,
                    [MapDownloader.MirrorVariable] = mirror.Url,
                },
                askForGameFolder: false,
                CaptureUiTests.DemoPath);

            string? stage = null;

            Retry.WhileFalse(
                () =>
                {
                    stage = viewer.Exists(MainForm.LoadingStageId) ? viewer.Find(MainForm.LoadingStageId).Name : stage;

                    return stage?.StartsWith("Loading the map", StringComparison.Ordinal) == true;
                },
                TimeSpan.FromSeconds(180));

            stage.ShouldNotBeNull("the overlay was not on screen while the map was being fetched");
            stage.ShouldStartWith("Loading the map");
            held.SetResult();
        }
        finally
        {
            held.TrySetResult();
            CaptureUiTests.Clean(root);
        }
    }

    [Test]
    public void LoadingOverlay_OnceTheWorldIsBuilt_IsGone()
    {
        // Synchronised on the condition: the world can be built a moment before the load's last step hides the overlay.
        Retry.WhileTrue(() => ViewerSession.App.Exists(MainForm.LoadingOverlayId), TimeSpan.FromSeconds(10));

        ViewerSession.App.Exists(MainForm.LoadingOverlayId).ShouldBeFalse("the overlay outlived the load it reports on");
    }
}
