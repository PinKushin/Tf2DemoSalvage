using System;
using System.Collections.Generic;
using System.Threading;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>The lock counters can be read while a build opens a deeper bracket (the B624 review).</summary>
/// <remarks>
/// **`AppliedLocks` and `LockEffect` sum one `IkLocks` per nesting depth, and a build grows that set the first time a
/// depth opens** (B625). Read through a `List` enumerator, a counter read racing that growth threw
/// <see cref="InvalidOperationException"/>; the single `_held?.Applied` it replaced could not. Today the only reader runs
/// after `Instances` returns, but nothing stops a status line reading it mid-build.
///
/// **A race, so the test makes it likely rather than certain**: every build is on a FRESH pose, so every build grows the
/// set from nothing to two depths, while a second thread reads the counters of whichever pose is current in a tight loop.
/// The count of collisions is not asserted; any exception is. Measured on the List-based reads (`202fb56e`): red in
/// every run, half as "Collection was modified" and half as a null bracket read mid-growth.
/// </remarks>
public sealed class LockCounterReadTests
{
    private const int Everything = ~0;

    [Test]
    public void AppliedLocks_ReadWhileABuildOpensANewDepth_NeverThrows()
    {
        SkeletonPose? current = null;
        Exception? caught = null;
        bool done = false;

        Thread reader = new(() =>
        {
            try
            {
                while (!Volatile.Read(ref done))
                {
                    if (Volatile.Read(ref current) is { } pose)
                    {
                        _ = pose.AppliedLocks;
                        _ = pose.LockEffect;
                    }
                }
            }
            // Both ways the List-based read failed, measured on it: "Collection was modified", and a NULL bracket read
            // from a slot the growing list had counted but not yet filled — the tear, which crashed the test host.
            catch (Exception thrown) when (thrown is InvalidOperationException or NullReferenceException)
            {
                caught = thrown;
            }
        });

        reader.Start();

        try
        {
            // 300,000 builds is about a second; on the List-based reads 20,000 reddened 6 runs in 10, so the count is set
            // where a miss would need the race to lose fifteen times in a row.
            for (int build = 0; build < 300_000 && Volatile.Read(ref caught) is null; build++)
            {
                SkeletonPose pose = Pose();
                Volatile.Write(ref current, pose);

                BoneAccessor bones = new(4) { WritableBones = Everything, ReadableBones = Everything };
                pose.Build(Everything, 0d, bones, new BoneBitList(4));
            }
        }
        finally
        {
            Volatile.Write(ref done, true);
            reader.Join();
        }

        caught.ShouldBeNull("a counter read must not tear while a build adds a bracket depth");
    }

    /// <summary>The leg of <see cref="NestedLockBracketConformanceTests"/> with two nested locking layers: depths 0 and 1.</summary>
    private static SkeletonPose Pose() =>
        new(Bones(), (sequence, _, _, _) =>
            sequence == 0 ? Rest() : [new StudioBonePose(0, (0f, sequence, 0f), (0f, 0f, 0f, 1f))])
        {
            Sequence = 0,
            IkChains = Chains,
            Layers =
            [
                new PoseLayer(1, 0, 0f, 1f, [1f, 0f, 0f, 0f], Locks: [Lock], Nested: 1),
                new PoseLayer(2, 0, 0f, 1f, [1f, 0f, 0f, 0f], Locks: [Lock]),
            ],
        };

    private static readonly StudioIkLock Lock = new(Chain: 0, PositionWeight: 1f, LocalRotationWeight: 0f, Flags: 0);

    private static readonly IReadOnlyList<StudioIkChain> Chains =
    [
        new StudioIkChain(
            "leg",
            0,
            [new StudioIkLink(1, (0f, 0f, 0f)), new StudioIkLink(2, (0f, 0f, 0f)), new StudioIkLink(3, (0f, 0f, 0f))]),
    ];

    private static StudioBonePose[] Rest() =>
    [
        new StudioBonePose(0, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(1, (0f, 0f, 0f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(2, (10f, 0f, 5f), (0f, 0f, 0f, 1f)),
        new StudioBonePose(3, (10f, 0f, -5f), (0f, 0f, 0f, 1f)),
    ];

    private static StudioBone[] Bones()
    {
        StudioBonePose[] rest = Rest();
        int[] parents = [-1, 0, 1, 2];
        StudioBone[] bones = new StudioBone[rest.Length];

        for (int bone = 0; bone < rest.Length; bone++)
        {
            bones[bone] = new StudioBone(
                $"bone{bone}",
                parents[bone],
                rest[bone].Position,
                rest[bone].Rotation,
                new float[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f },
                Flags: Everything);
        }

        return bones;
    }
}
