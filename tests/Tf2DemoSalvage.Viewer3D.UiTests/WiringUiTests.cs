using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using FlaUI.Core.Tools;

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
    public void TheFrameReporter_AfterASecondOfFrames_WroteItsAccount()
    {
        // **The per-second line is the instrument two defects were found with**, so it silently
        // failing to appear is the worst outcome available here: B191 was found by reading which
        // column stayed fat as the others were measured away, and B163 — *"everything freezes for a
        // half a second to maybe a second"* — by the collection counts beside it.
        //
        // **Nothing else can catch it.** `FrameReporter` moved out of the window on 2026-08-26 and
        // its own tests drive it directly with a fake clock; they hold identically whether or not the
        // idle loop ever calls it. That is the B193 shape exactly — a component with green tests and
        // no caller.
        //
        // **Proved sensitive by manipulation, and the interesting part is what the manipulation had
        // to be.** Deleting the `Drew` call outright does not compile: `MessageName` and
        // `_idleEndedBy` exist only to feed it, so the analyzers report both as orphaned (S1144,
        // S4487). That is a structural guard rather than the luck WiringUiTests' table records for
        // `EnsureWeaponRoles`, and it is worth knowing — but it covers only the crudest regression.
        //
        // What does compile, and what this test is actually for: `LogDebug` changed to `LogTrace`.
        // The viewer runs at `+developer 1`, so the line simply stops being written, while
        // `FrameReporterTests` stays green — `RecordingLogger` records every level and the assertions
        // count messages, not levels. Measured 2026-08-26: unit suite 8/8 green, this test the only
        // failure of twenty.
        //
        // Waits on the CONDITION rather than a duration: the line needs a real second of real frames
        // and this suite has no business asserting how long that takes.
        Retry.WhileFalse(
            () => Viewer.Count("frames a second") > 0,
            TimeSpan.FromSeconds(15),
            throwOnTimeout: true,
            timeoutMessage:
                "No per-second frame line was written, so either the idle loop stopped reporting or "
                + "the reporter's clock is never restarted.");

        Viewer.Count("frames a second").ShouldBeGreaterThan(0);
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

    [Test]
    public void TheOpaqueModels_BeforeBeingDrawn_WereSpreadAcrossMoreThanOneSizeBucket()
    {
        // **The B193 shape again, and measured rather than suspected.** With
        // `OpaqueBuckets.InDrawOrder` deleted from the draw loop, all 566 rendering tests stayed
        // green — the sort has no picture to change, only a frame rate, so neither a unit test nor a
        // screenshot can see it.
        //
        // **The line's VALUE is the assertion, not its presence**, because the failure worth
        // catching is not the sort going missing. It is `ModelInstance.Bounds` arriving unset: a
        // zero box buckets as the smallest, so every model lands in bucket 3, the sort returns its
        // input unchanged, and the log still says `opaque draw order`. That reads as `0/0/0/N` — a
        // spread of exactly one — which is why this counts populated buckets rather than lines.
        //
        // Needs the game: without TF2 the models have no `.mdl` to take bounds from, and this would
        // then report a missing install as broken wiring. Same reasoning as the appearance check
        // above.
        ViewerSession.RequireTheGame();

        Retry.WhileFalse(
            () => Viewer.LastLine("opaque draw order:") is not null,
            TimeSpan.FromSeconds(15),
            throwOnTimeout: true,
            timeoutMessage:
                "The draw loop never reported its bucket spread, so either no opaque model reached "
                + "the device or the sort is no longer being applied to them.");

        string line = Viewer.LastLine("opaque draw order:")
            .ShouldNotBeNull("the retry above should have waited for this");

        // `... buckets 3/14/62/8` — the counts are the tail of the line, after the last space.
        string counts = line[(line.LastIndexOf(' ') + 1)..];

        int populated = counts
            .Split('/')
            .Count(bucket => int.TryParse(bucket, out int models) && models > 0);

        populated.ShouldBeGreaterThan(
            1,
            $"every opaque model fell in one bucket ({counts}), which is what an unset "
            + "ModelInstance.Bounds looks like");
    }

    [Test]
    public void TheOpaqueModels_BeforeBeingDrawn_HadTheOnesOutsideTheViewRemoved()
    {
        // **The same B193 shape as its neighbour above, for the cull rather than the sort.** A
        // frustum that is never built culls nothing and reports no error: the picture is identical,
        // every test stays green, and the only evidence is that the two counts on this line agree.
        //
        // **Measured, not assumed: 45 of 49 on this demo at this moment.** Four of the map's models
        // are behind the camera or past its edges. The assertion is the inequality rather than the
        // numbers, because the scene may legitimately gain or lose models — but if it ever reaches
        // equality, either the cull stopped running or a camera change put the whole map on screen,
        // and both are worth a red test.
        //
        // Needs the game for the same reason as the bucket check: with no TF2 there are no models.
        ViewerSession.RequireTheGame();

        Retry.WhileFalse(
            () => Viewer.LastLine("opaque draw order:") is not null,
            TimeSpan.FromSeconds(15),
            throwOnTimeout: true,
            timeoutMessage: "The draw loop never reported what it kept.");

        string line = Viewer.LastLine("opaque draw order:")
            .ShouldNotBeNull("the retry above should have waited for this");

        // `opaque draw order: 45 of 49 models kept, buckets 1/6/0/38`
        Match counted = Regex.Match(
            line,
            @"opaque draw order: (\d+) of (\d+) models kept",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        counted.Success.ShouldBeTrue($"the census line should carry both counts: {line}");

        int kept = int.Parse(counted.Groups[1].Value, CultureInfo.InvariantCulture);
        int offered = int.Parse(counted.Groups[2].Value, CultureInfo.InvariantCulture);

        kept.ShouldBeLessThan(
            offered,
            $"nothing was culled ({line}), which is what an unbuilt view frustum looks like");

        kept.ShouldBeGreaterThan(
            0, "everything was culled, so the frustum is pointing away from the map");
    }
}
