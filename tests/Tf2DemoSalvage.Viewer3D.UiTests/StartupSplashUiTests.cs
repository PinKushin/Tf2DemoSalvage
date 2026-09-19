namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The window shown the moment the program starts, before the main window exists (D182).</summary>
/// <remarks>
/// The owner: *"I want to see something happening basically immediately after double clicking the program, Like how maui and
/// MPF do just naturally."* The launcher watches the process's top-level windows until the main one appears, and says whether the
/// splash was among them.
/// </remarks>
public sealed class StartupSplashUiTests
{
    [Test]
    public void Launch_BeforeTheMainWindow_ShowsTheStartupSplash() =>
        ViewerSession.App.SplashSeen.ShouldBeTrue("no startup splash appeared before the main window");

    /// <remarks>*"I just want to see how long our boot time is, and be able to notice if it becomes crazy long."*</remarks>
    [Test]
    public void ReportStartup_WhenTheWindowIsShown_LogsTheBootTimeOnce() =>
        ViewerSession.App.Count("startup: window shown").ShouldBe(1);

    [Test]
    public void StartupSplash_OnceTheMainWindowIsUp_IsGone() =>
        ViewerSession.App.Exists(MainForm.StartupSplashId).ShouldBeFalse("the splash is not a child of the main window");
}
