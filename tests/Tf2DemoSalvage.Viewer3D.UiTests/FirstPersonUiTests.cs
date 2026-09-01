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

    /// <summary>How many camera-mode changes the viewer has announced, across all three modes.</summary>
    private static int Transitions() =>
        Viewer.Count(FirstPersonOn) + Viewer.Count("third person on,") + Viewer.Count(BackToMap);

    /// <summary>The shared prefix of both first-person entry lines.</summary>
    /// <remarks>
    /// **The prefix wherever the claim is only "first person was entered".** Most of this suite
    /// asserts mode changes, not mechanisms, and pinning those waits to one mechanism's full
    /// sentence is what silently broke five of them when the session demo changed from a POV
    /// recording to a SourceTV one — the viewer switched modes on screen while every wait watched
    /// for a sentence this demo never logs. <see cref="ViewerSession.FirstPersonOn"/> existed for
    /// exactly this and this file did not use it.
    /// </remarks>
    private const string FirstPersonOn = ViewerSession.FirstPersonOn;

    /// <summary>What the viewer logs when first person spectates a player.</summary>
    /// <remarks>
    /// **The full sentence, used only where the MECHANISM is the claim.** The session demo is
    /// SourceTV (`z1800`), which carries no recorded camera, so the correct mechanism is spectating
    /// a player — the engine does the same, `spec_mode` walking the roster because there is no
    /// recorder to follow. A POV recording logs the other sentence, "first person on, following
    /// the recording's own camera".
    /// </remarks>
    private const string SpectatingAPlayer =
        "first person on, spectating a player (this demo has no recorded camera)";

    /// <summary>What it logs on the way back out, from either player mode.</summary>
    /// <remarks>
    /// **The shared tail, not the whole sentence.** Leaving names the mode being left — "first
    /// person off" or "third person off" — because the first is untrue when the camera was chasing.
    /// Waiting on the common part asks the question this suite actually has, which is whether the
    /// camera came back, rather than which mode it came back from.
    /// </remarks>
    private const string BackToMap = "back to the free camera";

    [TearDown]
    public void ReturnToTheMapView()
    {
        // **The mode leaks otherwise**, and the next test in this assembly would run against a
        // camera it did not choose. The bound key is how the viewer itself leaves, so this uses the
        // same route rather than reaching past the UI.
        //
        // **Cycles until free rather than pressing once, because the key now has three stops.**
        // `SwitchCameraMode` runs Free -> first person -> third person, matching the spectator mode
        // cycle in `shareddefs.h`. One press used to be the whole way out and is now a step to third
        // person, which left the following test starting in a mode it did not choose — a shared
        // fixture turning one behaviour change into several unrelated-looking failures.
        // **Press until the viewer SAYS it reached free, at most a full lap.** Comparing counts of
        // two different messages to guess the current mode is what broke when a third mode arrived:
        // entering third person logs neither of them, so the guess was stale the moment the cycle
        // grew. Watching for the one line that only appears on arriving at Free needs no guess, and
        // terminates from any starting mode — three presses from Free, two from first person, one
        // from third.
        // **Each press waits for ANY mode change, never for a particular one.** Every press logs
        // exactly one of three lines, so their sum always rises and the wait ends in milliseconds.
        // Waiting for the free-camera line specifically means two presses in three wait for
        // something that will not happen and burn the whole timeout — a negative retry is a sleep
        // (`docs/memory/a-negative-retry-is-a-sleep.md`), and with one of these per test it cost
        // this suite most of its runtime.
        // **Against a baseline, not against zero.** These counts accumulate for the life of the
        // shared viewer, so `== 0` is true only until the first teardown ever runs and false
        // afterwards — the loop would stop pressing and every later test would inherit whatever
        // mode the previous one left.
        int free = Viewer.Count(BackToMap);

        for (int press = 0; press < 3 && Viewer.Count(BackToMap) == free; press++)
        {
            int before = Transitions();

            PressSwitchCameraMode();

            Retry.WhileFalse(
                () => Transitions() > before,
                TimeSpan.FromSeconds(5),
                throwOnTimeout: false);
        }
    }

    [Test]
    public void FirstPerson_PressingSwitchCameraMode_SpectatesAPlayer()
    {
        // **The demo decides which mechanism is used, and the viewer says which.** A message that
        // named neither would leave "it is following the recorder" and "it is spectating an
        // arbitrary player" indistinguishable in the log — and those look identical on screen
        // until the recorder dies. The session demo is SourceTV, so the right mechanism here is
        // spectating; a viewer that logged "following the recording's own camera" against `z1800`
        // would be claiming a camera the file does not carry.
        int before = Viewer.Count(SpectatingAPlayer);

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(SpectatingAPlayer) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage:
                "Switching camera mode did not spectate a player, which is the only first-person "
                + "mechanism a SourceTV demo has.");

        // Stated as an assertion as well as a wait: the retry establishes WHEN to look and this
        // establishes WHAT was found, and only the second appears in a failure report.
        Viewer.Count(SpectatingAPlayer).ShouldBeGreaterThan(before);
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

            int before = Viewer.Count(FirstPersonOn);
            int frames = Viewer.Count("viewmodel pass skipped");

            PressSwitchCameraMode();

            // **Synchronised on a frame rather than on a clock**, which is the rule and also three
            // seconds cheaper. This waited out a fixed three-second window for something that must
            // not happen — a wait that pays its full cost on every green run, and which "the viewer
            // has frozen" satisfies just as well as "the key was correctly ignored".
            //
            // `viewmodel pass skipped` is written once per frame while the camera is NOT
            // first-person, so waiting for it to advance proves the app processed the press and is
            // still drawing the free camera. Then the negative means something.
            Retry.WhileFalse(
                () => Viewer.Count("viewmodel pass skipped") > frames,
                TimeSpan.FromSeconds(10),
                throwOnTimeout: true,
                timeoutMessage:
                    "No free-camera frame was drawn after the press, so this test cannot tell a "
                    + "correctly ignored key from a viewer that stopped rendering.");

            Viewer.Count(FirstPersonOn).ShouldBe(
                before, "the playlist keeps Space for type-ahead, so the camera must not switch");
        }
        finally
        {
            Viewer.Find("Viewport").Focus();
        }
    }

    [Test]
    public void FirstPerson_CyclingAllTheWayRound_ReturnsToTheOverheadView()
    {
        // **A mode, not a one-way door.** The map view is what works on every demo, so leaving has
        // to be as easy as entering — and a cycle that never came back would strand somebody on a
        // camera that cannot see the match.
        //
        // **Three presses rather than two, and the change is deliberate.** The key now runs
        // Free -> first person -> third person -> Free, matching the spectator mode cycle in
        // `shareddefs.h`; it used to be a two-state toggle. Renamed with it, because a test called
        // `...Twice...` that presses three times is a lie that survives review.
        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FirstPersonOn) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Switching camera mode did not enter the first-person view.");

        int before = Viewer.Count(BackToMap);

        // Through third person, then out.
        PressSwitchCameraMode();
        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(BackToMap) > before,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Cycling right round did not return the viewer to the overhead camera.");

        Viewer.Count(BackToMap).ShouldBeGreaterThan(before);
    }

    [Test]
    public void FirstPerson_TheViewmodel_IsNotDrawnInTheFreeCamera()
    {
        // **The negative nobody was asserting**, and it exists because a dead wait was found where
        // this belongs. `FirstPerson_Capture` waited ten seconds for a viewmodel to appear *before*
        // switching to first person — a condition that can never be true there — on a log line the
        // viewer does not write. It cost half the suite's runtime and checked nothing.
        //
        // The owner named both the symptom and the fix: *"the stalls are not when the app in in
        // first person"*, and *"that test either needs to be changed to actually check something
        // that isnt being checked anywher else, or ripped out and we can replace it with a test
        // that actually tests something novel"*. Every other viewmodel test in this file asserts it
        // DOES draw; none asserted it stops.
        //
        // **The control is `skipped`, and without it this test is worthless.** "No new viewmodel
        // draws" is also satisfied by a viewer that has stopped rendering, has crashed, or never got
        // to the pass at all. `viewmodel pass skipped: ... camera False` is written once per frame
        // by the pass itself, so waiting for it to advance proves three things at once: frames are
        // flowing, the pass ran, and the camera is genuinely not first-person.
        //
        // **Synchronised on that rather than on a clock**, so it costs a frame instead of a timeout.
        const string Skipped = "viewmodel pass skipped";
        const string Drawing = "viewmodel pass: drawing";

        int drawnBefore = Viewer.Count(Drawing);
        int skippedBefore = Viewer.Count(Skipped);

        Retry.WhileFalse(
            () => Viewer.Count(Skipped) > skippedBefore,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage:
                "The viewmodel pass never reported skipping a frame, so either nothing is being "
                + "drawn or the viewer is already in first person — and this test cannot measure "
                + "either way.");

        Viewer.Count(Drawing).ShouldBe(
            drawnBefore,
            "the viewmodel must not be drawn while the free camera is looking at the map");
    }

    [Test]
    public void FirstPerson_TheViewmodel_IsDrawnAfterSwitchingToFirstPerson()
    {
        // **The positive half of the pair**, extracted from the old capture test (2026-08-26). It was
        // the one genuinely novel assertion buried in a test that otherwise duplicated
        // `ViewportPictureUiTests` and took two screenshots nobody looked at.
        //
        // It needs no screenshot at all: the pass reports what it drew, so ask it.
        // `FirstPerson_TheViewmodel_IsNotDrawnInTheFreeCamera` is the other half, and together they
        // say the viewmodel appears when the view calls for it and not otherwise — which neither
        // said alone.
        //
        // **The guard came back after CI went red** (2026-08-26). Extracting this from
        // `FirstPerson_Capture` left `RequireTheGame` behind in the original, so on the runner —
        // which has no TF2 — it waited fifteen seconds for a game asset that cannot exist and failed
        // the whole workflow. Its siblings all skip there, which is the tell: three `Skipped` lines
        // and one `Failed` in the same log.
        //
        // This is not gating a failure away. A viewmodel IS a game asset, so with no install there
        // is nothing to draw and nothing to assert — the distinction
        // `docs/memory/ci-is-the-machine-without-tf2.md` exists to keep.
        ViewerSession.RequireTheGame();

        int before = Viewer.Count("viewmodel pass: drawing");

        PressSwitchCameraMode();

        try
        {
            Retry.WhileFalse(
                () => Viewer.Count(FirstPersonOn) > 0,
                TimeSpan.FromSeconds(5),
                throwOnTimeout: true,
                timeoutMessage: "Switching camera mode did not enter the first-person view.");

            // **Drawn, not merely resolved.** This used to wait on the model LOOKUP, excused by a
            // claim that a 2013 recording names `v_` models the current install no longer ships. The
            // claim is false — `v_` models ship in the VPKs and render here — and waiting on the
            // weaker condition is what let the old test pass with nothing in the hands.
            Retry.WhileFalse(
                () => Viewer.Count("viewmodel pass: drawing") > before,
                TimeSpan.FromSeconds(15),
                throwOnTimeout: true,
                timeoutMessage: "The viewmodel never reached the screen in first person.");

            Viewer.Count("viewmodel pass: drawing").ShouldBeGreaterThan(
                before, "the weapon in hand must be drawn once the view is through the player's eyes");
        }
        finally
        {
            // Back to the free camera: the fixture is shared, and the negative test above measures
            // exactly this state.
            PressSwitchCameraMode();
        }
    }

    [Test]
    public void FirstPerson_TheCapturedFrame_IsAViewRatherThanASurface()
    {
        // **A viewmodel is a game asset, so with no game there is nothing to wait for.** Without
        // this the test waits fifteen seconds for `models/weapons/v_*.mdl` to be drawn, times out,
        // and reports "the viewmodel never reached the screen" — a true sentence about a missing
        // install, presented as a rendering defect. That is what has kept CI red.
        ViewerSession.RequireTheGame();

        // **One subject: is the captured frame a VIEW or a flat surface.** Everything else this test
        // used to carry has moved out, and the reason is the owner's, 2026-08-26:
        //
        // > *"tests cant have good names if they are testing more than one thing, wtf kind of test
        // > setup is that"*
        //
        // It was `FirstPerson_Capture_WritesAPictureForSomebodyToLookAt` and did three unrelated
        // things: took a free-camera screenshot, took a first-person screenshot, and checked a file
        // appeared, a viewmodel drew, and the frame had structure. The name described none of them,
        // and that is precisely how a ten-second wait on a log line the viewer never writes survived
        // in the middle of it — nobody reading the name had a reason to look.
        //
        // What went where:
        //
        //   - "a screenshot gets written" was already `ViewportPictureUiTests`. Deleted, not moved.
        //   - "the viewmodel draws in first person" is now its own test above, and needs no capture.
        //   - "the viewmodel does NOT draw in the free camera" is new — the negative nobody asserted,
        //     which is what the dead wait had been groping at.
        //   - the colour-count check stayed here, and it is the only one that needs a picture.
        //
        // **The measurement, kept because it was measured rather than chosen.** The wall this suite
        // used to photograph holds 18 distinct colours and the map view holds 146, so the threshold
        // sits between them with room either side. Brightness could not separate them — 93 per cent
        // of the map capture's pixels are lit, and planks are lit too.
        //
        // **The session opens at a measured tick** (`ViewerSession.OpeningTick`) where the recorder
        // is out on the map holding a rocket launcher. It used to jump to the END instead, on the
        // false premise that TF2 no longer ships the `v_` viewmodels a 2013 recording names — they
        // ship inside the VPKs and render here — and that end tick is where the photographed WALL
        // came from, which is the defect this colour count now catches.
        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FirstPersonOn) > 0,
            TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Switching camera mode did not enter the first-person view, so there is nothing to capture.");

        // (No viewmodel wait. Whether the weapon is drawn is
        // `FirstPerson_TheViewmodel_IsDrawnAfterSwitchingToFirstPerson`'s subject, and this test
        // does not care — a frame is a view or a wall regardless of what is in the hands.)

        // **One capture, taken because `ReportStructure` below needs a frame to count colours in.**
        // F5 is TF2's screenshot key (B214) — it was F12 until the default moved, which TF2 gives to
        // replay tips and Steam's overlay claims as well.
        Viewer.PressKey(VirtualKeyShort.F5);

        // Waited on so the file exists before it is read. **Not asserted here**: that a screenshot
        // gets written is `ViewportPictureUiTests`'s subject, and duplicating it would mean two
        // tests reddening for one defect while this one's real subject went unreported.
        Retry.WhileFalse(
            () => Viewer.Count("wrote ") > 0,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: "No screenshot was written, so there is no frame to measure.");

        // **And that the frame is a view rather than a surface.** Measured before it was asserted:
        // the wall this used to photograph holds 18 distinct colours and the map view holds 146, so
        // the threshold sits between them with room on both sides. Brightness could not separate
        // them — 93 per cent of the map capture's pixels are lit, and planks are lit too.
        ReportStructure().ShouldBeGreaterThan(
            FlatFrameColours,
            "the capture is nearly one colour, which is what a wall in front of the camera looks like");

        // Back to the free camera, because the fixture is shared and two other tests measure that
        // state directly.
        PressSwitchCameraMode();
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
        int frames = Viewer.Count("viewmodel pass skipped");

        Viewer.Click(MainForm.ViewportId, MouseButton.Left);

        // **Synchronised on a frame, not on a clock** (2026-08-26). This gave the viewer a flat
        // two-second window and then looked, under a comment claiming there was nothing to wait for:
        // *"the claim is that nothing happens, so the only honest instrument is to give it the same
        // window the positive test gets"*. There IS something to wait for — evidence that the app
        // processed the click and carried on — and a fixed window is both slower and weaker, since
        // "the viewer froze" satisfies it exactly as well as "the click was correctly ignored".
        //
        // The owner noticed the cost from outside, watching the suite: *"it sits on the free cam and
        // i dont see antyhing happen for a little while"*. Three negative tests were each paying a
        // full fixed window on every green run.
        //
        // `viewmodel pass skipped` is written once per frame while the camera is not first-person,
        // so its advancing proves the viewer is alive, still in the free camera, and past the click.
        Retry.WhileFalse(
            () => Viewer.Count("viewmodel pass skipped") > frames,
            TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage:
                "No free-camera frame was drawn after the click, so this test cannot tell a "
                + "correctly ignored click from a viewer that stopped rendering.");

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
        int before = Viewer.Count(FirstPersonOn);

        PressSwitchCameraMode();

        Retry.WhileFalse(
            () => Viewer.Count(FirstPersonOn) > before,
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
        Viewer.Count(FirstPersonOn).ShouldBe(
            Viewer.Count(BackToMap),
            "this test needs the free camera, and the teardown should have left it there");
}
