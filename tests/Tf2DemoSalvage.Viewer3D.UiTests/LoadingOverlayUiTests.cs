using System;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The loading screen the owner asked for: up at once, gone once the demo is on screen.</summary>
/// <remarks>
/// *"is there a way to add like a splash screen that pops up immediately on boot and gives the user a progress bar?"* — the window
/// used to stay off screen for the whole decode and map read. What it said at launch is read by <see cref="ViewerSession"/>,
/// because by the time a test runs the load is over.
/// </remarks>
public sealed class LoadingOverlayUiTests
{
    /// <summary>The overlay was on screen during the load, and the stage it was first told was the decode.</summary>
    /// <remarks>
    /// **Two sources, because the label cannot answer the second** (B707). UI Automation reads it whenever it arrives: on run
    /// 41669140049 that was after the 52.88 s decode, so it said "Loading the map…", correctly. The log carries what was told,
    /// from the presenter, at the moment it was told; `ViewerPresenterTests` pins the order of the stages.
    /// </remarks>
    [Test]
    public void LoadingOverlay_WhenTheWindowFirstAppears_SaysTheDemoIsLoading()
    {
        ViewerSession.LoadingStageAtLaunch.ShouldNotBeNull("the overlay was not on screen while the demo loaded");
        ViewerSession.App.Count("loading overlay: Decoding the demo").ShouldBeGreaterThan(0, "the overlay was never told the decode");
    }

    [Test]
    public void LoadingOverlay_OnceTheWorldIsBuilt_IsGone()
    {
        // Synchronised on the condition: the world can be built a moment before the load's last step hides the overlay.
        Retry.WhileTrue(() => ViewerSession.App.Exists(MainForm.LoadingOverlayId), TimeSpan.FromSeconds(10));

        ViewerSession.App.Exists(MainForm.LoadingOverlayId).ShouldBeFalse("the overlay outlived the load it reports on");
    }
}
