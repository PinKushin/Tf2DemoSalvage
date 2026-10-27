using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The bone counters read across a fork (finding 105 A3.1, A3.2): every call from every thread counted once.</summary>
/// <remarks>
/// **Many threads, many calls each, and an exact total** — the shape that loses updates to a plain `+=` on almost every
/// run, so a counter that is not atomic fails here rather than reading a few builds short on f12. The control is the same
/// work run serially, which must give the same numbers.
/// </remarks>
public sealed class BoneSetupMeterTests
{
    private const int Threads = 8;
    private const int CallsEach = 50_000;

    [Test]
    public void Record_FromEightThreadsAtOnce_CountsEveryCall()
    {
        BoneSetupMeter meter = new();

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, _ =>
        {
            for (int call = 0; call < CallsEach; call++)
            {
                meter.Record(root: call % 2 == 0, eligible: call % 4 == 0, built: true, ticks: 3);
            }
        });

        const int Calls = Threads * CallsEach;

        (meter.RootBuilds, meter.EligibleBuilds, meter.ChildBuilds).ShouldBe((Calls / 2, Calls / 4, Calls / 2));
        (meter.RootTicks, meter.EligibleTicks, meter.ChildTicks).ShouldBe((3L * Calls / 2, 3L * Calls / 4, 3L * Calls / 2));
    }

    /// <remarks>
    /// **`SkeletonPose.PoseBuilds` and `AnimationCalls` are static and read as deltas** (`EntityModels.Instances.cs`), so a
    /// worker's build must land in them. Each thread builds its own skeleton, as each root does on the pool; serially and
    /// forked the deltas are the same exact count.
    /// </remarks>
    [TestCase(1, TestName = "Build_OnOneThread_CountsEveryBuildAndAnimationCall")]
    [TestCase(Threads, TestName = "Build_OnEightThreadsAtOnce_CountsEveryBuildAndAnimationCall")]
    public void Build_AcrossThreads_CountsEveryBuildAndAnimationCall(int degree)
    {
        const int BuildsEach = 20_000;
        SkeletonPose[] poses = [.. Enumerable.Range(0, Threads).Select(_ => new SkeletonPose([Bone()], (_, _, _, _) => []))];
        (int builds, int calls) = (SkeletonPose.PoseBuilds, SkeletonPose.AnimationCalls);

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = degree }, thread =>
        {
            BoneAccessor bones = new(1);
            BoneBitList written = new(1);

            for (int build = 0; build < BuildsEach; build++)
            {
                bones.WritableBones = ~0;
                bones.ReadableBones = ~0;
                written.Clear();
                poses[thread].Build(~0, 0d, bones, written);
            }
        });

        (SkeletonPose.PoseBuilds - builds, SkeletonPose.AnimationCalls - calls).ShouldBe((Threads * BuildsEach, Threads * BuildsEach));
    }

    private static StudioBone Bone() =>
        new(
            Name: "root",
            Parent: -1,
            Position: (0f, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
            Flags: ~0);
}
