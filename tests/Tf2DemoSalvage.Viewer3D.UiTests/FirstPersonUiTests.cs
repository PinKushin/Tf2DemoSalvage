using System;
using System.IO;
using System.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

using Tf2DemoSalvage.Presentation;

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

    /// <summary>Presses whatever key is bound to switching the camera mode.</summary>
    /// <remarks>
    /// **Pinned rather than hardcoded, and the pinning is the point.** This action was on `V` until
    /// bindings arrived (D68) and is now on `Space`, matching what TF2 binds — its spectator HUD
    /// prints `[%jump%]` beside "Switch Camera Mode".
    ///
    /// A test pressing a literal key fails the *wrong way* when a binding moves: it presses a key
    /// that does nothing, waits for a state change that cannot happen, and reports a timeout. Three
    /// of these did exactly that, and the visible symptom was Windows dinging on every unhandled
    /// press while the retry loop spun — the owner diagnosed it by ear before the log said anything.
    ///
    /// Asserting the binding first turns that into one clear failure naming the cause.
    /// </remarks>
    /// <remarks>
    /// **Moved to <see cref="ViewerSession"/> once a second fixture needed it.** A copy would be a
    /// second place for the binding guard to go stale, and a stale guard is worse than none — it
    /// asserts confidently about a binding that has moved.
    /// </remarks>
    private static void PressSwitchCameraMode() => ViewerSession.PressSwitchCameraMode();

    /// <summary>What the viewer logs when the recorded camera is being followed.</summary>
    private const string FollowingRecorded = "first person on, following the recording's own camera";

    /// <summary>What it logs on the way back out.</summary>
    private const string BackToMap = "first person off, back to the free camera";

    [TearDown]
    public void ReturnToTheMapView()
    {
        // **The mode leaks otherwise**, and the next test in this assembly would run against a
        // camera it did not choose. The bound key is how the viewer itself leaves, so this uses the
        // same route rather than reaching past the UI.
        if (Viewer.Count(FollowingRecorded) > Viewer.Count(BackToMap))
        {
            PressSwitchCameraMode();

            Retry.WhileFalse(
                () => Viewer.Count(BackToMap) >= Viewer.Count(FollowingRecorded),
                TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public void FirstPerson_PressingSwitchCameraMode_FollowsTheRecordingsOwnCamera()
    {
        // **The demo decides which mechanism is used, and the viewer says which.** A message that
        // named neither would leave "it is following the recorder" and "it is spectating an
        // arbitrary player" indistinguishable in the log — and those look identical on screen
        // until the recorder dies.
        int before = Viewer.Count(FollowingRecorded);

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                "Switching camera mode did not follow the recording's own camera.");

        // Stated as an assertion as well as a wait: the retry establishes WHEN to look and this
        // establishes WHAT was found, and only the second appears in a failure report.
        Viewer.Count(FollowingRecorded).ShouldBeGreaterThan(before);
    }

    [Test]
    public void FirstPerson_SwitchCameraModeWhileThePlaylistHasFocus_DoesNothing()
    {
        // **The behaviour the owner asked for, at the level where wiring fails** (B216). A list
        // selects by typed characters, so the playlist uses `SPACE` and every letter exactly as the
        // search box does. Leaving that out was an inconsistency they named: having just accepted
        // *"if someone has selected the search bar they probably dont want the cam to move"*, they
        // asked of the type-ahead paragraph, *"whaT IS THIs if not that?"*
        //
        // **The control for this test is the whole rest of the file**, which presses the same key
        // with the viewport focused and requires it TO work. Without that, "nothing happened" would
        // also be satisfied by a viewer that had stopped switching cameras altogether.
        //
        // **Restored in a `finally`, and the first version was not.** The fixture is shared: a
        // failure that left the playlist focused took four other tests down with it, every one
        // reporting a subject that was perfectly fine
        // (`docs/memory/cancelling-sabotages-mean-coupled-tests.md`).
        try
        {
            Viewer.Find(MainForm.PlaylistId).Focus();

            int before = Viewer.Count(FollowingRecorded);

            PressSwitchCameraMode();

            Retry.WhileFalse(
                () => Viewer.Count(FollowingRecorded) > before,
                TimeSpan.FromSeconds(3)).Result
                .ShouldBeFalse(
                    "the playlist keeps Space for type-ahead, so the camera must not switch");
        }
        finally
        {
            Viewer.Find("Viewport").Focus();
        }
    }

    [Test]
    public void FirstPerson_PressingSwitchCameraModeTwice_ReturnsToTheOverheadView()
    {
        // **A mode, not a one-way door.** The map view is what works on every demo, so leaving has
        // to be as easy as entering — and a toggle that only ever entered would strand somebody on
        // a camera that cannot see the match.
        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Switching camera mode did not enter the first-person view.");

        int before = Viewer.Count(BackToMap);

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(BackToMap) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "A second switch did not return the viewer to the overhead camera.");

        Viewer.Count(BackToMap).ShouldBeGreaterThan(before);
    }

    [Test]
    public void FirstPerson_Capture_WritesAPictureForSomebodyToLookAt()
    {
        // **A viewmodel is a game asset, so with no game there is nothing to wait for.** Without
        // this the test waits fifteen seconds for `models/weapons/v_*.mdl` to be drawn, times out,
        // and reports "the viewmodel never reached the screen" — a true sentence about a missing
        // install, presented as a rendering defect. That is what has kept CI red.
        ViewerSession.RequireTheGame();

        // **This asserts almost nothing, and that is a gap rather than a principle.** The comment
        // here used to say whether the view looks RIGHT is "not answerable by an assertion". That
        // is too strong, and the owner corrected it: "we can use golden image comparison, or we can
        // check pixels colors and or contrast, although that can be flakey".
        //
        // Three separate claims were being run together:
        //
        //   - A specific visual property IS assertable now, with no reference image. "The wall must
        //     not show through an opaque prop" caught B154; "each pass draws something, and not the
        //     same something" caught a mis-wired r_drawworld. Neither needed a person.
        //   - Open-ended "does it look right" needs a person exactly ONCE, to bless a reference.
        //     After that it is a golden comparison and every later change is assertable.
        //   - Flake is a property of the SETUP, not of the technique. Fixed viewport, fixed tick,
        //     fixed device and the render is deterministic — which is how the offscreen tests in
        //     Viewer3D.Tests already work at 64x64 from a fixed matrix.
        //
        // **What this test's weakness actually cost.** The viewmodel pass draws nothing at all
        // (B160) — c_* models go to the world pass and appear at the eye — and this test rendered
        // that picture, wrote it out, and passed, because the only thing it checks is that a file
        // appeared. One mechanical assertion would have caught it: the viewmodel pass draws more
        // than zero instances when the first-person view is on. No judgement, no reference image.
        //
        // It does check ONE thing, which is why it is an ordinary test rather than explicit: that
        // the capture path works at all. A screenshot that silently fails to write is the same
        // silent fallback this project bans everywhere else, and it cost a capture run that
        // reported success and produced nothing.
        //
        // Through the harness rather than SendKeys, because a synthesized keystroke goes to
        // whatever window has focus — which on a shared desktop is somebody else's work.
        //
        // **This used to jump to the END, and that is where the wall came from.** The justification
        // was that TF2 no longer ships the `v_` viewmodels a 2013 recording names, so only the demo's
        // last stretch — where a `c_` model appears — could draw anything. That claim is false: `v_`
        // models ship inside the VPKs and this project renders them, which every off-hand watch in
        // `z1800` demonstrates. The end tick was therefore chosen to satisfy a constraint that did
        // not exist, with no regard for what was in front of the camera.
        //
        // The session now opens at a measured tick (`ViewerSession.OpeningTick`) where the recorder
        // is out on the map holding a rocket launcher, so there is nothing to jump to.
        Retry.WhileFalse(
            () => Viewer.Count("viewmodel models/weapons/") > 0,
            TimeSpan.FromSeconds(10));

        // **F5, which is TF2's screenshot key** (B214). This was F12 until the default moved —
        // TF2 gives F12 to `replay_togglereplaytips` and Steam's overlay takes it too, while our F5
        // was a debug view, so the two were swapped against the game.
        Viewer.PressKey(VirtualKeyShort.F5);

        Retry.WhileFalse(
            () => Viewer.Count("wrote ") > 0, TimeSpan.FromSeconds(10));

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Switching camera mode did not enter the first-person view, so there is nothing to capture.");

        // **Wait for the viewmodel to be DRAWN, not merely resolved.** This waited on the lookup
        // instead, excused by the claim that a 2013 recording names `v_` models the current install
        // no longer ships — so "a capture with empty hands is still the right capture". The claim is
        // false: `v_` models ship in the VPKs and render here. Waiting on the weaker condition is
        // what let this test photograph a frame with nothing in the hands and call it a success.
        Retry.WhileFalse(
            () => Viewer.Count("viewmodel pass: drawing") > 0,
            TimeSpan.FromSeconds(15),
            throwOnTimeout: true,
            timeoutMessage: "The viewmodel never reached the screen, so the capture would show none.");

        // **F5, which is TF2's screenshot key** (B214). This was F12 until the default moved —
        // TF2 gives F12 to `replay_togglereplaytips` and Steam's overlay takes it too, while our F5
        // was a debug view, so the two were swapped against the game.
        Viewer.PressKey(VirtualKeyShort.F5);

        // The only assertion: that a capture was actually taken. Without it a failure to press the
        // key would look like a successful run that produced no evidence.
        Retry.WhileFalse(
            () => Viewer.Count("wrote ") > 1,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: "No screenshot was written for the first-person view.");

        Viewer.Count("wrote ").ShouldBeGreaterThan(1);

        // **The viewmodel is the subject, so its absence is a failure rather than a shrug.** This
        // test is named for capturing the first-person view and the owner's point was blunt: the
        // frame being checked had no viewmodel in it. The pass reports what it drew, so ask.
        Viewer.Count("viewmodel pass: drawing").ShouldBeGreaterThan(
            0, "the viewmodel never reached the screen, so the capture shows the wrong thing");

        // **And that the frame is a view rather than a surface.** Measured before it was asserted:
        // the wall this used to photograph holds 18 distinct colours and the map view holds 146, so
        // the threshold sits between them with room on both sides. Brightness could not separate
        // them — 93 per cent of the map capture's pixels are lit, and planks are lit too.
        ReportStructure().ShouldBeGreaterThan(
            FlatFrameColours,
            "the capture is nearly one colour, which is what a wall in front of the camera looks like");
    }

    /// <summary>Below this, a frame is one surface rather than a view.</summary>
    /// <remarks>
    /// Measured, not chosen: 18 for the wall this suite used to capture, 146 for the map view. Forty
    /// is clear of both, and the gap is wide enough that it does not need to be exact.
    /// </remarks>
    private const int FlatFrameColours = 40;

    /// <summary>Says how much variety the newest capture holds, for choosing a threshold.</summary>
    /// <remarks>
    /// **Only <see cref="IOException"/> is caught, and for a named reason.** The viewer prunes its
    /// captures to the twenty most recent after writing each one, so the file listed a moment ago
    /// can be gone by the time it is opened — the same race that already broke the wait in
    /// <c>ViewportPictureUiTests</c>. Anything else must propagate: this is a diagnostic, and a
    /// diagnostic that hides its own failure is worse than none.
    /// </remarks>
    /// <returns>How many distinct colours the newest capture holds.</returns>
    private static int ReportStructure()
    {
        // The suite's own capture folder, not the viewer's — test captures are kept away from
        // hand-taken screenshots, which they used to evict.
        string folder = ViewerApplication.CaptureFolder;

        string? newest = Directory.EnumerateFiles(folder, "shot-*.png")
            .OrderBy(name => name, StringComparer.Ordinal)
            .LastOrDefault();

        newest.ShouldNotBeNull("no capture was written, so there is nothing to measure");

        try
        {
            using System.Drawing.Bitmap picture = new(newest);

            int colours = FrameStructure.Colours(picture);

            TestContext.Out.WriteLine($"STRUCTURE {Path.GetFileName(newest)}: {colours} distinct colours");

            return colours;
        }
        catch (IOException pruned)
        {
            // **Rethrown as a failure rather than reported.** The viewer prunes its captures to the
            // twenty most recent, so this file can vanish between listing and opening — but a
            // measurement that cannot be taken must not read as a measurement that passed.
            throw new AssertionException(
                $"{Path.GetFileName(newest)} could not be read: {pruned.Message}", pruned);
        }
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

        PressSwitchCameraMode();

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

    [Test]
    public void Click_TheCycleTargetButton_ReachesTheSpectatorCode()
    {
        // **The assertion B145 existed for.** The cycling actions were declared, bound to the mouse,
        // given Source command names and covered by three tests — and no production code read them,
        // so clicking did nothing. **Nothing about a binding table can tell you whether anything
        // consults it**, and no unit test of the search can either: the search was fine, it was
        // simply never called.
        //
        // So this clicks the real button in the real window and asks whether the spectator code ran.
        // It is the only test in the repository that can fail if the wiring is removed.
        //
        // **What it deliberately does not assert is which player is followed.** The UI session opens
        // an era specimen, which is one of the owner's own solo recordings — one player, so a cycle
        // lands back on them (`Next_TheOnlyPlayer_StaysOnThem`). Asserting a *change* of target here
        // would be asserting something the demo cannot show. That claim is made against z1800 in
        // `CorpusSpectatorCyclingTests`, on a nine-versus-nine match.
        //
        // **It counts EITHER outcome, and counting only the successful one was a real defect.**
        // `CycleTarget` logs "following entity N" when the search finds a target and "nobody else to
        // follow at this tick" when it does not. Both prove the click reached the handler, which is
        // the only claim this test makes. Requiring the first made the test depend on whether a
        // solo recording's single player was observable at whatever tick playback happened to be
        // sitting on — a fact about the demo, not about the wiring.
        //
        // It failed for exactly that reason once B171 required a target to be alive and drawn: the
        // viewer was behaving correctly and the owner watched it do so. The owner's verdict on the
        // old form was that it "doesnt actually check anything", and on a one-player demo the part
        // it added beyond the wiring check really did not.
        ViewerSession.RequireTheGame();

        EnsureFirstPerson();

        int before = Viewer.Count(Spectated);

        Viewer.Click(MainForm.ViewportId, MouseButton.Left);

        Retry.WhileFalse(
            () => Viewer.Count(Spectated) > before,
            TimeSpan.FromSeconds(5));

        Viewer.Count(Spectated).ShouldBeGreaterThan(
            before,
            "clicking the button bound to +attack should have reached the spectator code, " +
            "whether or not it found anyone to follow");
    }

    [Test]
    public void Click_TheCycleTargetButton_InTheFreeCamera_DoesNotCycle()
    {
        // **The control, and it is the game's own rule rather than ours.** `spec_next` does nothing
        // unless `GetObserverMode() > OBS_MODE_FIXED`, and in the free camera the left button is
        // already the look-around drag — cycling on it would fight the gesture.
        //
        // Without this, "clicking cycles" and "clicking always cycles" are the same observation.
        ViewerSession.RequireTheGame();

        EnsureFreeCamera();

        int before = Viewer.Count(Spectated);

        Viewer.Click(MainForm.ViewportId, MouseButton.Left);

        // No wait-for-change to do here: the claim is that nothing happens, so the only honest
        // instrument is to give it the same window the positive test gets and then look.
        Retry.WhileFalse(
            () => Viewer.Count(Spectated) > before,
            TimeSpan.FromSeconds(2));

        Viewer.Count(Spectated).ShouldBe(before, "the free camera does not spectate anybody");
    }

    /// <summary>What the viewer logs when a cycle RUNS, whichever way it turns out.</summary>
    /// <remarks>
    /// **The area tag rather than either message**, because both of `CycleTarget`'s outcomes prove
    /// the same thing and it is the only thing these tests claim: the click reached the handler.
    /// "following entity N" additionally requires the search to have found somebody, which on a
    /// one-player POV recording depends on whether that player is observable at the current tick —
    /// a property of the demo that B171 legitimately changed.
    ///
    /// It also sharpens the negative control. In the free camera `CycleTarget` returns before
    /// logging anything at all, so counting the area proves the handler did not RUN, where counting
    /// the success message could not distinguish that from it running and finding nobody.
    /// </remarks>
    private const string Spectated = "[spectate]";

    /// <summary>Enters the first-person view, failing here rather than in the caller.</summary>
    private static void EnsureFirstPerson()
    {
        int before = Viewer.Count(FollowingRecorded);

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FollowingRecorded) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "The viewer did not enter the first-person view.");
    }

    /// <summary>Confirms the free camera is current rather than assuming the teardown left it.</summary>
    /// <remarks>
    /// **Checked rather than assumed, because the alternative fails in the wrong direction.** If a
    /// previous test leaked the first-person mode, the negative test below would be run in the mode
    /// where cycling is *supposed* to work — and would pass only if the feature were broken.
    /// </remarks>
    private static void EnsureFreeCamera() =>
        Viewer.Count(FollowingRecorded).ShouldBe(
            Viewer.Count(BackToMap),
            "this test needs the free camera, and the teardown should have left it there");
}
