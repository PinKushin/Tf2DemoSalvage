namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The viewer, running for real, reports none of the wiring faults it is able to report.
/// </summary>
/// <remarks>
/// **Written because three of them shipped and nothing caught any of them** (B193). Extracting code
/// out of <c>MainForm</c> turns an assignment that used to be implicit — <c>new
/// TimelineViewmodels(timeline)</c> written inline — into a property somebody has to set, and a
/// property nobody sets is null, which is a legal state the guard already handles. The guard was
/// written for "no demo open yet"; it cannot tell that from "nobody wired this".
///
/// | what moved | what broke | caught by |
/// |---|---|---|
/// | <c>EnsureWeaponRoles</c> | every weapon suffix answered null | an analyzer, by luck |
/// | <c>AddViewmodel</c> | the first-person weapon never drew | reading the wiring, two commits later |
/// | the model upload | **no entity geometry reached the GPU at all** | a hand audit |
///
/// The viewer suite reported **620/620 green** through all three, and this assembly wrote a
/// screenshot for a human to look at without asserting that anything was in it.
///
/// **This is the third test level** (<c>docs/memory/three-test-levels-and-the-third-is-missing.md</c>):
/// only the real application, launched and driven, fails when the wiring is absent. A unit test
/// proves a component works when called with the values the test chose, and says nothing about
/// whether production calls it or with what.
///
/// **It asserts on ABSENCE, which needs its own control** — a log reader that cannot find its log
/// answers zero for everything and looks exactly like a clean run. <see cref="ViewerApplication"/>
/// answers null rather than zero for a missing file, and the first case below proves the reader is
/// looking at a real log before the others read anything from it.
/// </remarks>
public sealed class WiringUiTests
{
    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication Viewer => ViewerSession.App;

    [Test]
    public void TheLog_ForARunningViewer_IsBeingRead()
    {
        // **The control for every absence assertion below.** Counting zero occurrences of a warning
        // proves nothing if the reader is pointed at nothing — which has happened here before, and
        // cost three windows opened and never used while every wait ran its full sixty seconds.
        //
        // A line every run writes, so finding it means the reader has the right file open.
        Viewer.Count("device created for a").ShouldBeGreaterThan(
            0, "the log reader is not looking at a real log, so the absence checks below are void");
    }

    [Test]
    public void TheScene_AfterLoadingADemo_WasGivenSomewhereToUploadGeometry()
    {
        // **The worst of the three, and it shipped.** Nothing assigned `MomentScene.Upload`, so the
        // packing ran, the posing ran, every matrix was correct — and the geometry never reached the
        // device. Every model then drew against a vertex buffer the renderer had never received,
        // which is B148's symptom, and B148 took a 37 MB log to find.
        Viewer.Count("no model upload").ShouldBe(0);
    }

    [Test]
    public void TheScene_AfterLoadingADemo_WasGivenAViewmodelSource()
    {
        // Shipped too: `AddViewmodel` returned on its first guard, so the first-person weapon never
        // drew. Silent, because a viewer with no demo open legitimately has no viewmodel source.
        Viewer.Count("no viewmodel source").ShouldBe(0);
    }

    [Test]
    public void TheScene_AfterLoadingADemo_WasGivenThePlayersAppearance()
    {
        // **Needs the game, unlike its siblings above, and CI proved it the hard way.** The
        // appearance is built from the installed archives, so with no TF2 present the viewer logs
        // "no player appearance" for a perfectly good reason — and this assertion then reports a
        // missing environment as broken wiring. Run 32966637966 failed exactly there while passing
        // on a machine with the game.
        //
        // The other absence checks in this file need no assets: a viewmodel SOURCE and a model
        // upload are wiring, present or absent regardless of what is installed. Gating those too
        // would hide real breakage, which is why this call is here and not at the top of the class.
        ViewerSession.RequireTheGame();

        // Caught before shipping, but only because an analyzer noticed `EnsureWeaponRoles` had
        // become unreachable. Without the call every weapon suffix answers null and the animation
        // falls back to the generic primary form — the right weapon, the wrong pose, on every
        // player.
        Viewer.Count("no player appearance").ShouldBe(0);
    }

    [Test]
    public void TheScene_AfterLoadingADemo_ActuallyPackedSomeGeometry()
    {
        // **Absence checks alone would pass against a viewer that drew nothing for a reason nobody
        // has thought of yet**, so this is the positive half: the packed set reached the device and
        // said how much. It is the count that separates "the packing failed" from "the posing did".
        //
        // Needs `+developer 1`, which this assembly passes, because the line is `Debug` — per-frame
        // and per-change diagnostics were moved off `Information` when B191 turned out to be one log
        // line taking a machine-wide lock.
        Viewer.Count("entity models:").ShouldBeGreaterThan(0);
    }
}
