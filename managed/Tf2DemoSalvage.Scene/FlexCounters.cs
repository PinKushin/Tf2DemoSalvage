namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The parts of the face-flex evaluation (<c>EntityModelSet.FlexFor</c>), running totals in stopwatch ticks and counts,
/// each added where its work ran (B243). Read either side of a rebuild and differenced, as <see cref="EntityModelSet.PoseCounters"/> is.
/// </summary>
/// <param name="Driving">The global weights a face reads: <c>FaceDriving</c>, whole.</param>
/// <param name="Scenes">Of <paramref name="Driving"/>: the actor's scenes now.</param>
/// <param name="Voices">Of <paramref name="Driving"/>: the actor's mouth sources now.</param>
/// <param name="Step">Of <paramref name="Driving"/>: <c>ActorFace.Step</c>, <c>SetupGlobalWeights</c>.</param>
/// <param name="Local">The model's controller values out of the global weights.</param>
/// <param name="Rules"><c>RunFlexRules</c>.</param>
/// <param name="Delayed"><c>RunFlexDelay</c>, and on a miss the copy of the delayed weights the next probe compares with.</param>
/// <param name="Probe">The last face's lookup and the two weight comparisons.</param>
/// <param name="Corners">The model's flexed slots, both lookups.</param>
/// <param name="Clear">On a miss: the scratch growth, the stream, and clearing the scratch.</param>
/// <param name="Deltas">On a miss: the accumulation over the flexes.</param>
/// <param name="Slots">On a miss: the deltas written into the flexed slots.</param>
/// <param name="Resting">The resting face of a prop no player drives, lookup or build.</param>
/// <param name="Calls">Every call, a model with no face included.</param>
/// <param name="Faces">Calls past the first guard: a model with vertex animation, packed.</param>
/// <param name="Own">Faces driven as a player's own.</param>
/// <param name="Worn">Faces driven as a bone-merged item on a player.</param>
/// <param name="Corpse">Faces driven as a dead player's corpse.</param>
/// <param name="RestingFaces">Faces no player drives.</param>
/// <param name="Hits">Driven faces whose weights and delayed weights matched the last face's.</param>
/// <param name="Misses">Driven faces that were rebuilt.</param>
/// <param name="Vertices">Vertex animations the accumulation applied, on the misses.</param>
public readonly record struct FlexCounters(
    long Driving = 0,
    long Scenes = 0,
    long Voices = 0,
    long Step = 0,
    long Local = 0,
    long Rules = 0,
    long Delayed = 0,
    long Probe = 0,
    long Corners = 0,
    long Clear = 0,
    long Deltas = 0,
    long Slots = 0,
    long Resting = 0,
    int Calls = 0,
    int Faces = 0,
    int Own = 0,
    int Worn = 0,
    int Corpse = 0,
    int RestingFaces = 0,
    int Hits = 0,
    int Misses = 0,
    long Vertices = 0)
{
    /// <summary>What happened between an earlier snapshot and this one.</summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <returns>The difference, field by field.</returns>
    public FlexCounters Since(FlexCounters before) =>
        new(
            Driving - before.Driving,
            Scenes - before.Scenes,
            Voices - before.Voices,
            Step - before.Step,
            Local - before.Local,
            Rules - before.Rules,
            Delayed - before.Delayed,
            Probe - before.Probe,
            Corners - before.Corners,
            Clear - before.Clear,
            Deltas - before.Deltas,
            Slots - before.Slots,
            Resting - before.Resting,
            Calls - before.Calls,
            Faces - before.Faces,
            Own - before.Own,
            Worn - before.Worn,
            Corpse - before.Corpse,
            RestingFaces - before.RestingFaces,
            Hits - before.Hits,
            Misses - before.Misses,
            Vertices - before.Vertices);

    /// <summary>This and another, field by field: a window's total.</summary>
    /// <param name="other">The other.</param>
    /// <returns>The sum.</returns>
    public FlexCounters Add(FlexCounters other) =>
        new(
            Driving + other.Driving,
            Scenes + other.Scenes,
            Voices + other.Voices,
            Step + other.Step,
            Local + other.Local,
            Rules + other.Rules,
            Delayed + other.Delayed,
            Probe + other.Probe,
            Corners + other.Corners,
            Clear + other.Clear,
            Deltas + other.Deltas,
            Slots + other.Slots,
            Resting + other.Resting,
            Calls + other.Calls,
            Faces + other.Faces,
            Own + other.Own,
            Worn + other.Worn,
            Corpse + other.Corpse,
            RestingFaces + other.RestingFaces,
            Hits + other.Hits,
            Misses + other.Misses,
            Vertices + other.Vertices);
}

/// <summary>The running totals <see cref="FlexCounters"/> snapshots, added to where each piece of work runs.</summary>
internal sealed class FlexTally
{
    public long Driving { get; set; }

    public long Scenes { get; set; }

    public long Voices { get; set; }

    public long Step { get; set; }

    public long Local { get; set; }

    public long Rules { get; set; }

    public long Delayed { get; set; }

    public long Probe { get; set; }

    public long Corners { get; set; }

    public long Clear { get; set; }

    public long Deltas { get; set; }

    public long Slots { get; set; }

    public long Resting { get; set; }

    public int Calls { get; set; }

    public int Faces { get; set; }

    public int Own { get; set; }

    public int Worn { get; set; }

    public int Corpse { get; set; }

    public int RestingFaces { get; set; }

    public int Hits { get; set; }

    public int Misses { get; set; }

    public long Vertices { get; set; }

    public FlexCounters Snapshot() => new(
        Driving, Scenes, Voices, Step, Local, Rules, Delayed, Probe, Corners, Clear, Deltas, Slots, Resting,
        Calls, Faces, Own, Worn, Corpse, RestingFaces, Hits, Misses, Vertices);
}
