namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>The window shown the moment the program starts, before the main window exists (D182).</summary>
/// <remarks>
/// The owner: *"I want to see something happening basically immediately after double clicking the program, Like how maui and
/// MPF do just naturally."* The launcher watches the process's top-level windows until the main one appears, and says whether the
/// splash was among them.
/// </remarks>
public sealed class StartupSplashUiTests
{
    /// <remarks>
    /// **Asserted through the splash's own <c>Shown</c> rather than by watching for its window** (B412, D185). The owner:
    /// *"really the test should just make sure it shows for a millisecond because hypothetically a pc can be fast enough its
    /// never seen by the user, my pc isnt wuite that fast, but it is close its up for less than half a second"*. Under half a
    /// second is shorter than a first walk of the desktop's top-level windows can take, so the poll this used to read reported
    /// "no startup splash appeared" about a window that had been and gone — it failed on `main` for that reason, not because
    /// the splash was missing.
    /// </remarks>
    [Test]
    public void Launch_BeforeTheMainWindow_ShowsTheStartupSplash() =>
        ViewerSession.App.Count("startup: splash shown").ShouldBe(1);

    /// <remarks>*"I just want to see how long our boot time is, and be able to notice if it becomes crazy long."*</remarks>
    [Test]
    public void ReportStartup_WhenTheWindowIsShown_LogsTheBootTimeOnce() =>
        ViewerSession.App.Count("startup: window shown").ShouldBe(1);

    [Test]
    public void StartupSplash_OnceTheMainWindowIsUp_IsGone() =>
        ViewerSession.App.Exists(MainForm.StartupSplashId).ShouldBeFalse("the splash is not a child of the main window");
}
