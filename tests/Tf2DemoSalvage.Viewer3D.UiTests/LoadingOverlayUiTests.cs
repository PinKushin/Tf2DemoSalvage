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
    [Test]
    public void LoadingOverlay_WhenTheWindowFirstAppears_SaysTheDemoIsLoading()
    {
        ViewerSession.LoadingStageAtLaunch.ShouldNotBeNull("the overlay was not on screen while the demo loaded");
        ViewerSession.LoadingStageAtLaunch.ShouldStartWith("Decoding the demo");
    }

    [Test]
    public void LoadingOverlay_OnceTheWorldIsBuilt_IsGone()
    {
        // Synchronised on the condition: the world can be built a moment before the load's last step hides the overlay.
        Retry.WhileTrue(() => ViewerSession.App.Exists(MainForm.LoadingOverlayId), TimeSpan.FromSeconds(10));

        ViewerSession.App.Exists(MainForm.LoadingOverlayId).ShouldBeFalse("the overlay outlived the load it reports on");
    }
}
