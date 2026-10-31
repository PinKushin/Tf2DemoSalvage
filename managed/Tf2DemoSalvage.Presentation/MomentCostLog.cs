using System;
using System.Diagnostics;
using System.Globalization;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// The mean cost of a scene rebuild, written every hundred rebuilds.
/// </summary>
/// <remarks>
/// **This is the breakdown of `advance`, and `advance` is the frame.** Measured on
/// `tf2-2026-pub-pov-clean` at 96 fps, an 11 ms frame is `advance 7.7, draw 1.7, sound 0.4` — so
/// seventy per cent of the time is the scene rebuild and the GPU is very nearly idle. That number
/// came from <see cref="FrameRateLog"/>; this says which part of the rebuild it is.
///
/// **<see cref="StallReport.Moment"/> already names those parts and cannot report this**, because
/// it fires only past 30 ms. At 11 ms it never fires, so the columns exist, are measured every
/// rebuild, and are discarded — the same shape as the frame phases before `FrameRateLog`.
///
/// **Counted in rebuilds rather than timed**, because this runs inside the rebuild and has no clock
/// to consult. At the rate above a hundred rebuilds is about a second. The line states how many it
/// averaged, so a reader never has to assume the count.
///
/// **A mean, never the last rebuild.** The owner, on the first version of the frame log: *"a probe
/// that only polls per second is way too slow so that better be a fucking average"* — the same rule
/// and the same trap, since one rebuild in a hundred is a sample dressed as a measurement
/// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#log-the-event-not-a-sample-of-it`).
/// </remarks>
public sealed class MomentCostLog
{
    /// <summary>How many rebuilds each line averages.</summary>
    public const int DefaultEvery = 100;

    private readonly int _every;

    private int _rebuilds;
    private long _sample;
    private long _total;
    private long _drawList;

    // The parts of `drawlist` (B578), carried from `MomentScene.Build`.
    private long _drawPrepare;
    private long _drawPlayers;
    private long _drawPlace;
    private long _drawFilters;
    private long _drawFirstPerson;
    private long _models;
    private long _pose;
    private long _weapons;
    private long _viewmodel;

    // The parts of `pose`, which is the largest column of the largest column. Measured every
    // rebuild by EntityModelSet and discarded by the same threshold as everything else here.
    private long _lighting;
    private long _simulate;
    private long _wornLight;
    private long _setup;

    // Finding 105 A6: setup split root against child, and the slowest root (mean of each rebuild's, and the window's).
    private long _setupRoot;

    /// <summary>The parts of <see cref="_setupRoot"/>, summed as each rebuild carried them (the 2026-10-31 setup-parts split).</summary>
    private RootSetupParts _rootParts;
    private long _setupEligible;
    private long _eligibleBuilds;
    private long _setupChild;
    private long _prePose;
    private long _prePosed;
    private long _prePoseBlend;
    private long _rootBuilds;
    private long _childBuilds;
    private long _maxRootSum;
    private long _maxRootPeak;
    private long _skin;
    private long _animation;
    private long _corpses;
    private long _corpseSteps;
    private int _corpsesHeld;
    private long _collate;
    private long _baked;
    private long _place;
    private long _advance;
    private long _undrawn;
    private UndrawnCensus _census;
    private int _corpseRebuilds;
    private long _buildsSimulating;
    private long _buildsDrawing;
    private long _buildsCorpses;
    private long _buildsPlacing;
    private long _placeCompose;
    private long _placeFade;
    private long _placeBuild;
    private long _placeBinds;
    private long _placeLookups;
    private long _placeMatrix;
    private long _placeSwap;
    private long _placeFlex;

    /// <summary>The parts of <see cref="_placeFlex"/>, summed as each rebuild carried them.</summary>
    private FlexCounters _flex;
    private long _corpseBuilding;

    /// <summary><see cref="MomentPhases.PoseRest"/>, summed as each rebuild computed it — carried, not re-derived here.</summary>
    private long _rest;

    /// <summary>Skeletons actually rebuilt, against the entities posed.</summary>
    private long _poseBuilds;
    private long _report;
    private long _drawn;
    private long _selected;
    private long _hidden;
    private long _unjudged;

    private long _fixed;

    private long _byIdentity;
    private long _playersSampled;

    /// <summary>Starts a log.</summary>
    /// <param name="every">
    /// Rebuilds per line; <see cref="DefaultEvery"/> when not given. Taken as a parameter so a test
    /// can use a count it can write out by hand rather than driving a hundred rebuilds to see one
    /// line.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="every"/> is not positive.</exception>
    public MomentCostLog(int every = DefaultEvery)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(every);

        _every = every;
    }

    /// <summary>Offers one rebuild, and answers with a line when enough have accumulated.</summary>
    /// <param name="phases">What <c>MomentScene.Build</c> measured.</param>
    /// <param name="playerTicks">What PlayersAt cost, of that total (B258).</param>
    /// <param name="sampleTicks">
    /// Reading the tick's players and props off the timeline, which is measured outside
    /// <paramref name="phases"/> and is its own column — 2 to 6.5 ms in the breakdowns seen so far,
    /// so dropping it would hide a real cost.
    /// </param>
    /// <returns>The line to log, or <c>null</c> when fewer than <see cref="DefaultEvery"/> have.</returns>
    public string? Report(in MomentPhases phases, long sampleTicks, long playerTicks = 0)
    {
        _sample += sampleTicks;
        _playersSampled += playerTicks;
        _total += phases.Total;
        _drawList += phases.DrawList;
        _drawPrepare += phases.DrawListParts.Prepare;
        _drawPlayers += phases.DrawListParts.Players;
        _drawPlace += phases.DrawListParts.Place;
        _drawFilters += phases.DrawListParts.Filters;
        _drawFirstPerson += phases.DrawListParts.FirstPerson;
        _models += phases.Models;
        _pose += phases.Pose;
        _weapons += phases.Weapons;
        _viewmodel += phases.Viewmodel;

        _lighting += phases.Counters.Lighting;
        _simulate += phases.Counters.Simulate;
        _wornLight += phases.Counters.WornLight;
        _setup += phases.Counters.Setup;
        _setupRoot += phases.Counters.SetupRoot;
        _rootParts = _rootParts.Add(phases.Counters.RootParts);
        _setupEligible += phases.Counters.SetupEligible;
        _eligibleBuilds += phases.Counters.EligibleBuilds;
        _setupChild += phases.Counters.SetupChild;
        _prePose += phases.Counters.PrePose;
        _prePosed += phases.Counters.PrePosed;
        _prePoseBlend += phases.Counters.PrePoseBlend;
        _rootBuilds += phases.Counters.RootBuilds;
        _childBuilds += phases.Counters.ChildBuilds;
        _maxRootSum += phases.Counters.MaxRoot;
        _maxRootPeak = Math.Max(_maxRootPeak, phases.Counters.MaxRoot);
        _skin += phases.Counters.Skin;
        _animation += phases.Counters.Animation;
        _corpses += phases.Counters.Corpses;
        _corpseSteps += phases.Counters.CorpseSteps;
        _collate += phases.Counters.Collate;
        _baked += phases.Counters.Baked;
        _place += phases.Counters.Place;
        _advance += phases.Counters.Advance;
        _undrawn += phases.Undrawn;
        UndrawnCensus c = phases.UndrawnCensus;
        _census = new(
            _census.SunWorld + c.SunWorld, _census.SunUndrawn + c.SunUndrawn, _census.Props + c.Props,
            _census.Instances + c.Instances);
        _corpseRebuilds += phases.Counters.CorpseRebuilds;
        _buildsSimulating += phases.Counters.BuildsSimulating;
        _buildsDrawing += phases.Counters.BuildsDrawing;
        _buildsCorpses += phases.Counters.BuildsCorpses;
        _buildsPlacing += phases.Counters.BuildsPlacing;
        _placeCompose += phases.Counters.PlaceCompose;
        _placeFade += phases.Counters.PlaceFade;
        _placeBuild += phases.Counters.PlaceBuild;
        _placeBinds += phases.Counters.PlaceBinds;
        _placeLookups += phases.Counters.PlaceLookups;
        _placeMatrix += phases.Counters.PlaceMatrix;
        _placeSwap += phases.Counters.PlaceSwap;
        _placeFlex += phases.Counters.PlaceFlex;
        _flex = _flex.Add(phases.Counters.Flex);
        _corpseBuilding += phases.Counters.CorpseBuilding;
        _rest += phases.PoseRest;

        // **The MOST it held over the window, not the mean** (B413). This is a level rather than a cost, and it exists to
        // answer one question — does the environment plateau, or keep growing — which an average across a window that
        // includes quiet moments would flatten out of sight.
        _corpsesHeld = Math.Max(_corpsesHeld, phases.Counters.CorpsesHeld);
        _poseBuilds += phases.Counters.PoseBuilds;
        _report += phases.Counters.Report;

        // **How many of the posed props survive to be drawn.** The engine never poses the others:
        // `CClientLeafSystem::CollateRenderablesInLeaf` gathers renderables out of VISIBLE leaves,
        // so `SetupBones` is reached only for what the PVS and frustum already kept. This project
        // poses every prop the tick carries, so the ratio is the size of that divergence.
        _drawn += phases.Drawn;
        _selected += phases.Selected;
        _hidden += phases.Hidden;
        _unjudged += phases.Unjudgeable;
        _fixed += phases.Counters.CollateFixed;
        _byIdentity += phases.Counters.CollateByIdentity;

        if (++_rebuilds < _every)
        {
            return null;
        }

        int over = _rebuilds;

        // Stryker disable all : the String mutator wraps the interpolated literal in a ternary that
        // cannot bind to string.Create's interpolated-string handler (CS1620), and Safe Mode then
        // drops every mutation in this method — B410.
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"moment cost, mean over {over} rebuilds: {Mean(_total + _sample, over):0.#} ms"
            + $" = sample {Mean(_sample, over):0.#} (players {Mean(_playersSampled, over):0.##})"
            + $", drawlist {Mean(_drawList, over):0.#}"
            + $" (prepare {Mean(_drawPrepare, over):0.##}, players {Mean(_drawPlayers, over):0.##}"
            + $", place {Mean(_drawPlace, over):0.##}, filters {Mean(_drawFilters, over):0.##}"
            + $" (first person {Mean(_drawFirstPerson, over):0.##}))"
            + $", models {Mean(_models, over):0.#}"
            + $", pose {Mean(_pose, over):0.#}"
            + $", weapons {Mean(_weapons, over):0.#}"
            + $", viewmodel {Mean(_viewmodel, over):0.#}"

            // **`rest` is a residual and every other pose column is measured**, which is what makes
            // it the one worth reading: all of them small with this large means the cost is in
            // something no timer covers yet.
            + $"; pose = lighting {Mean(_lighting, over):0.#}"
            + $", simulate {Mean(_simulate, over):0.#}"
            + $", wornlight {Mean(_wornLight, over):0.#}"
            + $", setup {Mean(_setup, over):0.#}"
            + $" (roots {Mean(_setupRoot, over):0.###} over {_rootBuilds / (double)over:0.#} built"
            + $", eligible {Mean(_setupEligible, over):0.###} over {_eligibleBuilds / (double)over:0.#}"
            + $", slowest {Mean(_maxRootSum, over):0.###} peak {Mean(_maxRootPeak, 1):0.###}"
            + $"; children {Mean(_setupChild, over):0.###} over {_childBuilds / (double)over:0.#} built"
            + $"; prepose {Mean(_prePose, over):0.###} over {_prePosed / (double)over:0.#}, blend {Mean(_prePoseBlend, over):0.###})"

            // The parts of `roots`, each carried from where it ran; `unsplit` is the residual no part covers (guards, mask
            // bookkeeping, a ragdoll, the timers), and layer sampling is inside accumulate, so it is not subtracted twice.
            + $", roots split (blend {Mean(_rootParts.Blend, over):0.###}, base {Mean(_rootParts.Base, over):0.###}"
            + $", accumulate {Mean(_rootParts.Accumulate, over):0.###} of which layer sampling {Mean(_rootParts.Sampling, over):0.###}"
            + $" over {_rootParts.Samples / (double)over:0.#}"

            // Accumulate's own parts (2026-10-31 accumulate-parts): `other` is its residual beside sampling, and the
            // enumerators are a count. None of these words is one the older readers anchor on.
            + $" (seed {Mean(_rootParts.Seed, over):0.###}, capture {Mean(_rootParts.Capture, over):0.###}"
            + $", solve {Mean(_rootParts.Solve, over):0.###}"
            + $", compose {Mean(_rootParts.Compose, over):0.###}, other {Mean(_rootParts.AccumulateOther, over):0.###}"
            + $"; enumerators {_rootParts.Enumerators / (double)over:0.#})"
            + $", adjust {Mean(_rootParts.Adjust, over):0.###}, loop {Mean(_rootParts.Loop, over):0.###}"
            + $", ik {Mean(_rootParts.Ik, over):0.###}, finish {Mean(_rootParts.Finish, over):0.###}"
            + $"; unsplit {Mean(_setupRoot - _rootParts.Sum, over):0.###})"
            + $", skin {Mean(_skin, over):0.#}"
            + $", anim {Mean(_animation, over):0.#}"
            + $", reports {Mean(_report, over):0.#}"
            + $", collate {Mean(_collate, over):0.#}"
            + $", baked {Mean(_baked, over):0.#}"
            + $", place {Mean(_place, over):0.#}"
            + $" (compose {Mean(_placeCompose, over):0.##}, fade {Mean(_placeFade, over):0.##}, build {Mean(_placeBuild, over):0.##} of which binds {Mean(_placeBinds, over):0.##}, lookups {Mean(_placeLookups, over):0.##} = matrix {Mean(_placeMatrix, over):0.##}, swap {Mean(_placeSwap, over):0.##}, flex {Mean(_placeFlex, over):0.##})"

            // The parts of `flex` at a finer width than the line's: about a tenth of a millisecond a face over ten parts.
            + $", flex parts (driving {Mean(_flex.Driving, over):0.###} = scenes {Mean(_flex.Scenes, over):0.###}"
            + $", voices {Mean(_flex.Voices, over):0.###}, step {Mean(_flex.Step, over):0.###}"
            + $"; local {Mean(_flex.Local, over):0.###}, rules {Mean(_flex.Rules, over):0.###}"
            + $", delayed {Mean(_flex.Delayed, over):0.###}, probe {Mean(_flex.Probe, over):0.###}"
            + $", corners {Mean(_flex.Corners, over):0.###}, clear {Mean(_flex.Clear, over):0.###}"
            + $", deltas {Mean(_flex.Deltas, over):0.###}, slots {Mean(_flex.Slots, over):0.###}"
            + $", resting {Mean(_flex.Resting, over):0.###}"
            + $"; calls {_flex.Calls / (double)over:0.#}, faces {_flex.Faces / (double)over:0.#}"
            + $" = own {_flex.Own / (double)over:0.#}, worn {_flex.Worn / (double)over:0.#}"
            + $", corpse {_flex.Corpse / (double)over:0.#}, resting {_flex.RestingFaces / (double)over:0.#}"
            + $"; hits {_flex.Hits / (double)over:0.#}, misses {_flex.Misses / (double)over:0.#}"
            + $", vertices {_flex.Vertices / (double)over:0.#})"
            + $", undrawn {Mean(_undrawn, over):0.###} ({_census.Props / (double)over:0.#} props"
            + $", {_census.Instances / (double)over:0.#} instances, sun {_census.SunUndrawn / (double)over:0.##}"
            + $" vs world {_census.SunWorld / (double)over:0.##})"
            + $", advance {Mean(_advance, over):0.#}"
            + $" (corpses {Mean(_corpses, over):0.#} over {_corpseSteps / (double)over:0.#} steps, {_corpsesHeld} held"
            + $", {_corpseRebuilds} rebuilt in {Mean(_corpseBuilding, over):0.#})"
            + $", rest {Mean(_rest, over):0.#}"
            // **Builds against posed is the number that says WHICH fps problem this is.** Roughly
            // equal means the readable-bone cache works and the cost is bone math; builds far above
            // posed means mask thrash, where a wearable asking for bones its owner has not built
            // forces the owner to rebuild, repeatedly, inside one frame.
            + $"; built {_poseBuilds / (double)over:0.#}"
            + $" (simulate {_buildsSimulating / (double)over:0.#}, corpses {_buildsCorpses / (double)over:0.#}"
            + $", draw {_buildsDrawing / (double)over:0.#}, place {_buildsPlacing / (double)over:0.#})"
            + $"; posed {_drawn / (double)over:0.#} of {_selected / (double)over:0.#} selected"
            + $", {_hidden / (double)over:0.#} hidden by pvs"
            + $", {_unjudged / (double)over:0.#} unjudgeable"

            // Finding 82: of the unparented props collated, how many were filed by identity, untouched.
            + $"; collated unparented {_fixed / (double)over:0.#}, by identity {_byIdentity / (double)over:0.#}");

        // Stryker restore all

        _rebuilds = 0;
        _sample = 0;
        _total = 0;
        _drawList = 0;
        _drawPrepare = 0;
        _drawPlayers = 0;
        _drawPlace = 0;
        _drawFilters = 0;
        _drawFirstPerson = 0;
        _models = 0;
        _pose = 0;
        _weapons = 0;
        _viewmodel = 0;
        _lighting = 0;
        _simulate = 0;
        _wornLight = 0;
        _setup = 0;
        _setupRoot = 0;
        _rootParts = default;
        _setupEligible = 0;
        _eligibleBuilds = 0;
        _setupChild = 0;
        _prePose = 0;
        _prePosed = 0;
        _prePoseBlend = 0;
        _rootBuilds = 0;
        _childBuilds = 0;
        _maxRootSum = 0;
        _maxRootPeak = 0;
        _skin = 0;
        _animation = 0;
        _corpses = 0;
        _corpseSteps = 0;
        _corpsesHeld = 0;
        _collate = 0;
        _baked = 0;
        _place = 0;
        _advance = 0;
        _undrawn = 0;
        _census = default;
        _corpseRebuilds = 0;
        _buildsSimulating = 0;
        _buildsDrawing = 0;
        _buildsCorpses = 0;
        _buildsPlacing = 0;
        _placeCompose = 0;
        _placeFade = 0;
        _placeBuild = 0;
        _placeBinds = 0;
        _placeLookups = 0;
        _placeMatrix = 0;
        _placeSwap = 0;
        _placeFlex = 0;
        _flex = default;
        _corpseBuilding = 0;
        _rest = 0;
        _poseBuilds = 0;
        _report = 0;
        _drawn = 0;
        _selected = 0;
        _hidden = 0;
        _unjudged = 0;
        _fixed = 0;
        _byIdentity = 0;
        _playersSampled = 0;

        return line;
    }

    /// <summary>Mean milliseconds per rebuild.</summary>
    private static double Mean(long ticks, int rebuilds) =>
        ticks / (double)Stopwatch.Frequency * 1000d / rebuilds;
}
