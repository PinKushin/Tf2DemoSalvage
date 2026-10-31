namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The parts of a root's <c>SetupBones</c> call, in stopwatch ticks, each carried from the code that did the work (B243).
/// </summary>
/// <remarks>
/// **One chain of timestamps through <see cref="SkeletonPose.Build"/>**, so <see cref="Base"/>, <see cref="Accumulate"/>,
/// <see cref="Adjust"/>, <see cref="Loop"/>, <see cref="Ik"/> and <see cref="Finish"/> are disjoint and together are the
/// whole build; <see cref="Blend"/> is timed around <c>BeforeBuild</c> by the entity. What the root's call spent outside
/// all of them — the guards, the mask bookkeeping, a ragdoll, the timers themselves — is the reader's residual, never a
/// number recorded here. Running totals, read as deltas either side of a call like <see cref="BoneSetupMeter"/>'s.
/// </remarks>
/// <param name="Blend"><c>BeforeBuild</c>: the blend inputs (<c>BuildBlend</c>), on every root call, cache hits included.</param>
/// <param name="Base">The base sequence's sample: the same span <see cref="SkeletonPose.AnimationTicks"/> adds.</param>
/// <param name="Accumulate">The layers over the base: the seeding, the lock brackets, each layer's sample and blend.</param>
/// <param name="Sampling">Of <paramref name="Accumulate"/>: the layers' own samples (<c>SkeletonPose.cs</c> layer call).</param>
/// <param name="Samples">How many layer samples <paramref name="Sampling"/> timed.</param>
/// <param name="Adjust">After the layers and before the per-bone loop: <c>CalcBoneAdj</c>, the barrel spin, the override index.</param>
/// <param name="Loop">The per-bone loop: concatenate, quaternion-interpolated and jiggle bones.</param>
/// <param name="Ik"><c>ReachWithIk</c>: the solve and the re-concatenate pass.</param>
/// <param name="Finish">The duck-jump shift and the three player bone scales.</param>
/// <param name="Seed">Of <paramref name="Accumulate"/>: the rest seed, if any, and the base's copy or overlay into the workspace.</param>
/// <param name="Capture">Of <paramref name="Accumulate"/>: every IK-lock capture (<c>IkLocks.Capture</c>), the base's and each layer's.</param>
/// <param name="Solve">Of <paramref name="Accumulate"/>: every IK-lock solve (<c>IkLocks.Solve</c>), the base's and each layer's.</param>
/// <param name="Compose">
/// Of <paramref name="Accumulate"/>: each layer's per-bone pass after its sample — the enumerator, the bone and weight
/// guards, and the slerp or delta add. Interleaved per bone, so not split further.
/// </param>
/// <param name="Enumerators">
/// A count, not ticks: the interface enumerators <c>Accumulate</c>'s <c>foreach</c> loops took (the setup-bones read's
/// M6). Each non-empty one is a heap object; an empty array's is a shared one.
/// </param>
public readonly record struct RootSetupParts(
    long Blend = 0,
    long Base = 0,
    long Accumulate = 0,
    long Sampling = 0,
    long Samples = 0,
    long Adjust = 0,
    long Loop = 0,
    long Ik = 0,
    long Finish = 0,
    long Seed = 0,
    long Capture = 0,
    long Solve = 0,
    long Compose = 0,
    long Enumerators = 0)
{
    /// <summary>The disjoint parts summed: everything but the sub-parts of <see cref="Accumulate"/>, which are inside it.</summary>
    public long Sum => Blend + Base + Accumulate + Adjust + Loop + Ik + Finish;

    /// <summary>
    /// What <see cref="Accumulate"/> spent outside its sub-parts: the statics <c>Build</c> adds before the call, the guard,
    /// the layer loop's own step and weight test, and the timers. The reader's residual, never a recorded number.
    /// </summary>
    public long AccumulateOther => Accumulate - Sampling - Seed - Capture - Solve - Compose;

    /// <summary>What happened between an earlier snapshot and this one.</summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <returns>The difference, field by field.</returns>
    public RootSetupParts Since(RootSetupParts before) =>
        new(
            Blend - before.Blend,
            Base - before.Base,
            Accumulate - before.Accumulate,
            Sampling - before.Sampling,
            Samples - before.Samples,
            Adjust - before.Adjust,
            Loop - before.Loop,
            Ik - before.Ik,
            Finish - before.Finish,
            Seed - before.Seed,
            Capture - before.Capture,
            Solve - before.Solve,
            Compose - before.Compose,
            Enumerators - before.Enumerators);

    /// <summary>This and another, field by field: a window's total.</summary>
    /// <param name="other">The other.</param>
    /// <returns>The sum.</returns>
    public RootSetupParts Add(RootSetupParts other) =>
        new(
            Blend + other.Blend,
            Base + other.Base,
            Accumulate + other.Accumulate,
            Sampling + other.Sampling,
            Samples + other.Samples,
            Adjust + other.Adjust,
            Loop + other.Loop,
            Ik + other.Ik,
            Finish + other.Finish,
            Seed + other.Seed,
            Capture + other.Capture,
            Solve + other.Solve,
            Compose + other.Compose,
            Enumerators + other.Enumerators);
}
