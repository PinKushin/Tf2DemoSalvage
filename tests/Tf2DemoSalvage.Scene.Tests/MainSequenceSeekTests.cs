using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// D233, the owner's seek decision (*"Recover real start (Recommended)"*): a seek that lands mid-activity shows the
/// SAME variant as playing through to that moment did.
/// </summary>
/// <remarks>
/// The engine rolls once when the activity changes and holds the roll (`multiplayer_animstate.cpp:1170-1177`); the
/// roll is seeded on (entity, activity, the tick the activity began). Playing through knows that tick; a seek has to
/// recover it by scanning the timeline back, through <see cref="EntityModelSet.PlayerPropAt"/>. Each test plays a
/// synthetic timeline through at a frame cadence, then lands on the played moments by seeking and compares every
/// player's pick. The cadences are fractional on purpose: a frame lands between ticks, and at 60 fps on a 66-tick
/// server it skips whole ticks.
/// </remarks>
[TestFixture]
public sealed class MainSequenceSeekTests
{
    private const string Stand = "ACT_MP_STAND_PRIMARY";
    private const string Run = "ACT_MP_RUN_PRIMARY";
    private const float Standing = 0f;
    private const float Running = 250f;
    private const string Soldier = "models/player/soldier.mdl";

    /// <summary>
    /// Four players: one standing from before the timeline's first tick, two changing activity at fractional and whole
    /// ticks, one who dies (absent) and respawns standing.
    /// </summary>
    private static readonly Schedule[] Players =
    [
        new(1, [(0d, Standing)]),
        new(2, [(0d, Running), (40.5d, Standing), (97.2d, Running), (160d, Standing), (251.7d, Running), (330d, Standing)]),
        new(3, [(12d, Standing), (55d, Running), (57.4d, Standing), (140.6d, Running), (260d, Standing)]),
        new(4, [(0d, Standing), (100d, null), (303d, Standing), (340d, Running)]),
    ];

    private const double End = 400d;

    /// <summary>
    /// **The ceiling, written down as a test**: a stretch shorter than the gap between two frames is never seen by
    /// play-through, which holds across it as the engine does at that frame rate, while a seek's whole-tick scan sees
    /// it and keys on the tick after it. Checking every skipped whole tick on the held path would cost the held path
    /// its zero allocations (RISKS B584).
    /// </summary>
    [Test]
    public void UpdateClientSideAnimations_AStretchBetweenTwoFrames_IsHeldThroughByPlayButEndsTheStretchForASeek()
    {
        // Frames at 39.1 and 40.8; the run covers whole tick 40 alone.
        Schedule[] blip = [new(5, [(0d, Standing), (40d, Running), (40.4d, Standing)])];
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(Loaded(blip), 0d, 50d, 1.7, blip);

        At(played, 42.5)[5].ShouldBe(StandKeyedOn(5, 0), "play never drew the run, so the stand began at 0");
        Show(Loaded(blip), 42.5, blip)[5].ShouldBe(StandKeyedOn(5, 41), "the seek's stand began at 41");
        StandKeyedOn(5, 41).ShouldNotBe(StandKeyedOn(5, 0), "the control: the two keys pick different variants");
    }

    [TestCase(1.0)]
    [TestCase(0.6)]
    [TestCase(1.7)]
    [TestCase(5.0)]
    public void UpdateClientSideAnimations_SeekToAPlayedMoment_MatchesPlayThrough(double cadence)
    {
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(Loaded(), 0d, End, cadence);

        foreach ((double tick, Dictionary<int, int> picks) in played)
        {
            Show(Loaded(), tick).ShouldBe(picks, $"a fresh seek to {tick} at cadence {cadence}");
        }
    }

    /// <summary>A seek back on a set that has been playing, then playing on from the landing.</summary>
    [Test]
    public void UpdateClientSideAnimations_SeekBackThenPlayOn_MatchesPlayThrough()
    {
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(Loaded(), 0d, End, 0.6);
        EntityModelSet models = Loaded();
        PlayThrough(models, 0d, End, 0.6);

        foreach (int landing in new[] { 330, 200, 150, 60, 20 })
        {
            foreach ((double tick, Dictionary<int, int> picks) in played.Where(at => at.Tick >= landing && at.Tick < landing + 30))
            {
                Show(models, tick).ShouldBe(picks, $"after seeking back to {landing}, at {tick}");
            }
        }
    }

    /// <summary>
    /// The case filed against the first cut: a seek forward to a moment whose activity matches the one held before the
    /// jump, but which began afresh in between. The held variant belongs to the old stretch, not the landing's.
    /// </summary>
    [Test]
    public void UpdateClientSideAnimations_SeekForwardIntoALaterStretchOfTheSameActivity_MatchesPlayThrough()
    {
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(Loaded(), 0d, End, 1.0);
        EntityModelSet models = Loaded();
        PlayThrough(models, 0d, 30d, 1.0);

        // At 30, player 2 runs (since 0) and player 3 stands (since 12); at 100 both do the same again, in stretches
        // that began at 98 and 58.
        foreach (int landing in new[] { 100, 180, 270, 390 })
        {
            Show(models, landing).ShouldBe(At(played, landing), $"forward to {landing}");
        }
    }

    /// <summary>Landing exactly on, just before and just after the whole tick an activity began.</summary>
    [TestCase(160d)]
    [TestCase(159.99d)]
    [TestCase(160.5d)]
    [TestCase(260d)]
    [TestCase(141d)]
    [TestCase(140.7d)]
    public void UpdateClientSideAnimations_SeekOntoAnActivityBoundary_MatchesPlayThrough(double landing)
    {
        EntityModelSet played = Loaded();
        PlayThrough(played, 0d, landing, 0.25);

        Show(Loaded(), landing).ShouldBe(Show(played, landing));
    }

    /// <summary>
    /// A player who respawns into the activity he died in keeps his variant: `ClearAnimationState` does not reset
    /// the main sequence (`multiplayer_animstate.cpp:136-146`) and a dead player is not animated, so nothing rolls.
    /// </summary>
    [Test]
    public void UpdateClientSideAnimations_RespawnIntoTheActivityHeDiedIn_KeepsTheVariantPlayedOrSought()
    {
        EntityModelSet models = Loaded();
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(models, 0d, 320d, 1.0);
        At(played, 50d)[4].ShouldBe(StandKeyedOn(4, 0));
        At(played, 310d)[4].ShouldBe(StandKeyedOn(4, 0), "played through the death");
        Show(Loaded(), 310d)[4].ShouldBe(StandKeyedOn(4, 0), "sought past it");
        StandKeyedOn(4, 303).ShouldNotBe(StandKeyedOn(4, 0), "the control: a roll at the respawn picks another");
    }

    /// <summary>The control: the stretches are not all one variant, or every comparison above is trivially equal.</summary>
    [Test]
    public void PlayThrough_TheSchedule_ProducesMoreThanOneVariantPerActivity()
    {
        List<(double Tick, Dictionary<int, int> Picks)> played = PlayThrough(Loaded(), 0d, End, 1.0);
        HashSet<int> stands = [.. played.SelectMany(at => at.Picks.Values).Where(sequence => sequence < 8)];
        HashSet<int> runs = [.. played.SelectMany(at => at.Picks.Values).Where(sequence => sequence >= 8)];

        stands.Count.ShouldBeGreaterThan(1);
        runs.Count.ShouldBeGreaterThan(1);
    }

    [Test]
    public void UpdateClientSideAnimations_HeldPathWithASampler_AllocatesNothing()
    {
        EntityModelSet models = Loaded();
        List<SceneProp> drawn = [Player(1, Standing)];
        models.CurrentTick = 10;
        models.UpdateClientSideAnimations(drawn);
        models.CurrentTick = 10.6;
        models.UpdateClientSideAnimations(drawn);

        long before = GC.GetAllocatedBytesForCurrentThread();
        models.CurrentTick = 11.2;
        models.UpdateClientSideAnimations(drawn);
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    /// <summary>
    /// Fast playback (five ticks a frame) through a long stand scans only the ticks each frame skipped, not back to the
    /// stand's start — or every frame would cost a seek.
    /// </summary>
    [Test]
    public void UpdateClientSideAnimations_FivePlayedTicksAFrameThroughALongStand_ScansOnlyTheSkippedTicks()
    {
        Schedule[] stand = [new(6, [(0d, Standing)])];
        EntityModelSet models = Loaded(stand);
        Func<double, int, SceneProp?> timeline = models.PlayerPropAt!;
        int probes = 0;
        models.PlayerPropAt = (tick, entity) =>
        {
            probes++;
            return timeline(tick, entity);
        };

        Show(models, 0d, stand);
        int most = 0;

        for (double tick = 5d; tick <= 1500d; tick += 5d)
        {
            probes = 0;
            Show(models, tick, stand);
            most = Math.Max(most, probes);
        }

        most.ShouldBe(5, "the five whole ticks each frame skipped");
    }

    /// <summary>
    /// A skip's frames (every 99 packets, never drawn, `SimulatesOnly`) scan nothing, and the frame it lands on
    /// recovers every player as a fresh seek would — or a long skip would scan 99 ticks a player a frame.
    /// </summary>
    [Test]
    public void UpdateClientSideAnimations_ASkipThenItsLanding_ScansOnlyOnTheLandingAndMatchesASeek()
    {
        EntityModelSet models = Loaded();
        Func<double, int, SceneProp?> timeline = models.PlayerPropAt!;
        int probes = 0;
        models.PlayerPropAt = (tick, entity) =>
        {
            probes++;
            return timeline(tick, entity);
        };

        PlayThrough(models, 0d, 30d, 1.0);
        probes = 0;
        models.SimulatesOnly = true;

        for (double through = 129d; through < 390d; through += 99d)
        {
            Show(models, through);
        }

        probes.ShouldBe(0, "a skip's frames scan nothing");
        models.SimulatesOnly = false;
        Show(models, 390d).ShouldBe(Show(Loaded(), 390d), "the landing matches a fresh seek");
        probes.ShouldBeGreaterThan(0, "the control: the landing did recover");
    }

    /// <summary>
    /// The seek frame scans back from the tick it SHOWS. `UpdateClientSideAnimations` runs inside `Build`, before
    /// `Pose`, so a clock that only `Pose` sets is the previous frame's when the scan reads it.
    /// </summary>
    [Test]
    public void Build_ASeek_ScansBackFromTheLandingTick()
    {
        EntityModelSet models = new() { Geometry = Variants };
        List<double> asked = [];
        MomentScene scene = new(models, new ViewmodelScene(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
            Appearance = new Soldiers(),
            EarlierPlayer = (tick, entity) =>
            {
                asked.Add(tick);
                return SoldierPlayer(entity);
            },
        };

        scene.Build([SoldierPlayer(3)], [], Info(10d));
        asked.Clear();
        scene.Build([SoldierPlayer(3)], [], Info(500d));

        asked.ShouldNotBeEmpty("the landing is a seek, so the start is recovered");
        asked[0].ShouldBe(500d);
    }

    private static ScenePlayer SoldierPlayer(int entity) =>
        new(EntityIndex: entity, X: 0f, Y: 0f, Z: 0f, Team: SceneTeams.Red, Health: 200, PlayerClass: 3) { Speed = Standing };

    private static MomentInfo Info(double tick) =>
        new(
            Tick: tick,
            CurrentTick: (int)tick,
            FirstPerson: false,
            Followed: null,
            EyeCamera: null,
            IntervalPerTick: 0.015f,
            ViewmodelFieldOfView: 54f);

    private sealed class Soldiers : IPlayerAppearance
    {
        public string? ModelOf(int playerClass) => Soldier;

        public string? WeaponSuffix(string? weaponClass, int? playerClass, int? weaponItem) => null;

        public string? Hands(int playerClass) => null;

        public ItemBodygroups BodygroupsOf(int itemDefinitionIndex, int team) => ItemBodygroups.None;

        public SceneTaunt? TauntForScene(string scene) => null;
    }

    private static List<(double Tick, Dictionary<int, int> Picks)> PlayThrough(
        EntityModelSet models, double from, double to, double cadence, Schedule[]? schedules = null)
    {
        List<(double, Dictionary<int, int>)> played = [];

        // A step count times the cadence, rounded, so a whole tick lands exactly on itself.
        for (long step = 0; from + (step * cadence) <= to; step++)
        {
            double tick = Math.Round(from + (step * cadence), 6);
            played.Add((tick, Show(models, tick, schedules)));
        }

        return played;
    }

    private static int StandKeyedOn(int entity, long began) =>
        Variants(Soldier)!.Skinned!.SelectWeightedSequence(Stand, -1, EntityModelSet.ActivityPickSeed(entity, Stand, began));

    private static Dictionary<int, int> At(List<(double Tick, Dictionary<int, int> Picks)> played, double tick) =>
        played.Single(at => Math.Abs(at.Tick - tick) < 1e-9).Picks;

    private static Dictionary<int, int> Show(EntityModelSet models, double tick, Schedule[]? schedules = null)
    {
        models.CurrentTick = tick;
        List<SceneProp> drawn = [.. (schedules ?? Players).Select(player => player.PropAt(tick)).OfType<SceneProp>()];
        models.UpdateClientSideAnimations(drawn);
        return drawn.ToDictionary(prop => prop.EntityIndex, prop => prop.Pose.Sequence);
    }

    private static EntityModelSet Loaded(Schedule[]? schedules = null)
    {
        Schedule[] timeline = schedules ?? Players;
        EntityModelSet models = new();
        models.Add([Player(1, Standing)], Variants);
        models.PlayerPropAt = (tick, entity) => timeline.Single(player => player.Entity == entity).PropAt(tick);
        return models;
    }

    /// <summary>Eight equal stand variants and four equal run variants, on two class models.</summary>
    private static PropModels.ModelFrames? Variants(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: SyntheticSkinnedModel.WithWeights(
                [.. Enumerable.Repeat((Stand, 1), 8), .. Enumerable.Repeat((Run, 1), 4)]));

    private static SceneProp Player(int entity, float speed) =>
        new(entity, Soldier, SceneModelKind.Studio, new ScenePose { Scale = 1f, Sequence = 99, Speed = speed });

    /// <summary>One player's timeline: from each step's tick on, that speed, or absent (dead) for null.</summary>
    private sealed record Schedule(int Entity, (double From, float? Speed)[] Steps)
    {
        public float? SpeedAt(double tick) =>
            tick < Steps[0].From ? null : Steps.Last(step => step.From <= tick).Speed;

        public SceneProp? PropAt(double tick) => SpeedAt(tick) is { } speed ? Player(Entity, speed) : null;
    }
}
