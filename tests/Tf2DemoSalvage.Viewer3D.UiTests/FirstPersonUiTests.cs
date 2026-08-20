using System;

using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// Entering and leaving the first-person view, driven through a real window.
/// </summary>
/// <remarks>
/// **What this can and cannot establish, stated up front because the difference matters here.**
///
/// It can show that the key reaches the form, that the mode changes, that the viewer says which
/// mechanism it is using, and that the mode is a mode rather than a one-way door. All of those are
/// checkable by looking at what the program did.
///
/// It cannot show that the picture is right. A camera at the correct coordinates pointing the
/// correct way still draws a wrong image if the projection, the basis or the eye height is wrong,
/// and every one of those produces a plausible-looking view rather than an error. That question
/// belongs to a person with the viewer open — <c>docs/memory/</c> records three defects in one
/// session that were found only by somebody looking, against assertions that were passing.
///
/// **The session's demo is a point-of-view recording** (2013 badlands), so the recorded-camera path
/// is the one exercised. The SourceTV path — spectating a player because there is no recorded
/// camera — has unit coverage and no UI coverage, because this assembly launches one viewer with
/// one demo and a second launch is the expensive thing the shared session exists to avoid.
/// </remarks>
public sealed class FirstPersonUiTests
{
    /// <summary>The one viewer this assembly runs, with its demo already open.</summary>
    private static ViewerApplication Viewer => ViewerSession.App;

    /// <summary>What the viewer logs when the recorded camera is being followed.</summary>
    private const string FollowingRecorded = "first person on, following the recording's own camera";

    /// <summary>What it logs on the way back out.</summary>
    private const string BackToMap = "first person off, back to the map view";

    [TearDown]
    public void ReturnToTheMapView()
    {
        // **The mode leaks otherwise**, and the next test in this assembly would run against a
        // camera it did not choose. Pressing V is how the viewer itself leaves, so this uses the
        // same route rather than reaching past the UI.
        if (Viewer.Count(FollowingRecorded) > Viewer.Count(BackToMap))
        {
            Viewer.PressKey(VirtualKeyShort.KEY_V);

            Retry.WhileFalse(
                () => Viewer.Count(BackToMap) >= Viewer.Count(FollowingRecorded),
                TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void FirstPerson_PressingV_FollowsTheRecordingsOwnCamera()
    {
        // **The demo decides which mechanism is used, and the viewer says which.** A message that
        // named neither would leave "it is following the recorder" and "it is spectating an
        // arbitrary player" indistinguishable in the log — and those look identical on screen
        // until the recorder dies.
        int before = Viewer.Count(FollowingRecorded);

        Viewer.PressKey(VirtualKeyShort.KEY_V);

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                "V did not put the viewer into the first-person view following the recording.");

        // Stated as an assertion as well as a wait: the retry establishes WHEN to look and this
        // establishes WHAT was found, and only the second appears in a failure report.
        Viewer.Count(FollowingRecorded).ShouldBeGreaterThan(before);
    }

    [Test]
    public void FirstPerson_PressingVTwice_ReturnsToTheMapView()
    {
        // **A mode, not a one-way door.** The map view is what works on every demo, so leaving has
        // to be as easy as entering — and a toggle that only ever entered would strand somebody on
        // a camera that cannot see the match.
        Viewer.PressKey(VirtualKeyShort.KEY_V);

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "V did not enter the first-person view.");

        int before = Viewer.Count(BackToMap);

        Viewer.PressKey(VirtualKeyShort.KEY_V);

        Retry.WhileFalse(
            () => Viewer.Count(BackToMap) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "A second V did not return the viewer to the map view.");

        Viewer.Count(BackToMap).ShouldBeGreaterThan(before);
    }

    [Test]
    public void FirstPerson_Capture_WritesAPictureForSomebodyToLookAt()
    {
        // **This asserts almost nothing on purpose.** Whether the first-person view looks RIGHT is
        // not answerable by an assertion: a camera at the correct coordinates pointing the correct
        // way still draws a wrong picture if the projection, the basis or the eye height is wrong,
        // and each of those produces a plausible image rather than an error. So this produces the
        // artefact and a person decides.
        //
        // It does check ONE thing, which is why it is an ordinary test rather than explicit: that
        // the capture path works at all. A screenshot that silently fails to write is the same
        // silent fallback this project bans everywhere else, and it cost a capture run that
        // reported success and produced nothing.
        //
        // Through the harness rather than SendKeys, because a synthesized keystroke goes to
        // whatever window has focus — which on a shared desktop is somebody else's work.
        Viewer.PressKey(VirtualKeyShort.F12);

        Retry.WhileFalse(
            () => Viewer.Count("wrote ") > 0, TimeSpan.FromSeconds(10));

        Viewer.PressKey(VirtualKeyShort.KEY_V);

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "V did not enter the first-person view, so there is nothing to capture.");

        // **Wait for the viewmodel to be RESOLVED, not drawn.** Whether it draws depends on the
        // installed game: a demo's precache names the model the recording used, and TF2 replaced
        // the v_models with c_models around 2011 — so a 2013 recording can name
        // v_scattergun_scout.mdl at a tick where the current install has no such file. The
        // renderer reports that honestly as "no-batches" and draws nothing, which is correct
        // behaviour rather than a defect.
        //
        // So the condition is that the lookup happened. A capture with empty hands is still the
        // right capture when the model is not on this machine.
        Retry.WhileFalse(
            () => Viewer.Count("viewmodel models/") > 0,
            TimeSpan.FromSeconds(15));

        Viewer.PressKey(VirtualKeyShort.F12);

        // The only assertion: that a capture was actually taken. Without it a failure to press the
        // key would look like a successful run that produced no evidence.
        Retry.WhileFalse(
            () => Viewer.Count("wrote ") > 1,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: "No screenshot was written for the first-person view.");

        Viewer.Count("wrote ").ShouldBeGreaterThan(1);
    }

    [Test]
    public void FirstPerson_TheViewport_IsRedrawnWhenTheModeChanges()
    {
        // **The camera is uploaded rather than the world rebuilt**, which is the whole reason a
        // second camera could be added without touching the renderer: the geometry is in map
        // coordinates and only the view changes. So the observable is a camera line, and a WORLD
        // rebuild here would be a performance regression wearing a working feature.
        int cameras = Viewer.Count("camera");
        int worlds = Viewer.Count(ViewerSession.WorldBuildLine);

        Viewer.PressKey(VirtualKeyShort.KEY_V);

        Retry.WhileFalse(
            () => Viewer.Count("camera") > cameras,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Changing camera mode did not redraw the viewport.");

        Viewer.Count("camera").ShouldBeGreaterThan(cameras);

        // And the world was NOT rebuilt. A camera change that rebuilt the geometry would satisfy
        // the line above while undoing the design it depends on.
        Viewer.Count(ViewerSession.WorldBuildLine).ShouldBe(worlds);
    }
}
