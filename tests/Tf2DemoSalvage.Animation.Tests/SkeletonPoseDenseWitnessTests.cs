using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A witness that <c>SkeletonPose.Accumulate</c> and <c>Adjust</c> actually skip the rest seed for a dense pose.
/// </summary>
/// <remarks>
/// **The differentials prove the short way is never WRONG; only this proves it is ever TAKEN.** A dense test that always
/// answered "no" would pass every bit-identity test, so the skip has to be seen by something that differs between the
/// two routes, and that is the seed itself, counted where it runs (<c>SkeletonPose.RestSeeds</c>). **It counted the
/// skeleton's list reads until B587**, when the rest seed was the one step still reading the bones through their list
/// interface; the bones are an array now and an array read leaves nothing to count.
///
/// Not witnessed here, and not witnessable by bone reads:
/// <list type="bullet">
/// <item>the third consumer, the <c>_overrideOf</c> index in <c>Build</c>: skipping it reads no bone and the index is
/// private. The differential covers both routes for correctness.</item>
/// <item><c>DenseOf</c>'s shortcut for <c>_layered</c> and <c>_adjusted</c>: without it the same arrays are scanned by
/// <c>AsDense</c>, which reads no bone either and answers the same storage. It is a cost branch no output can tell
/// apart, and its <c>||</c>-to-<c>&amp;&amp;</c> mutant is suppressed for that reason (measured equivalent: the whole
/// Animation suite stays green under it). The layer-plus-controller builds in
/// <c>DensePoseAccumulateDifferentialTests</c> exercise the <c>_layered</c> branch through <c>Adjust</c>.</item>
/// </list>
/// </remarks>
public sealed class SkeletonPoseDenseWitnessTests
{
    private const int Bones = 8;

    /// <remarks>
    /// One layer at weight zero, so `Accumulate` runs (it returns early with no layers) and the layer itself reads
    /// nothing: the only difference between the two bases is whether the seed ran.
    /// </remarks>
    [Test]
    public void Build_ADenseBaseUnderALayer_SkipsTheRestSeedThatASparseOneNeeds()
    {
        PoseLayer[] layers = [new(1, 0, 0f, 0f, [.. Enumerable.Repeat(1f, Bones)])];

        int dense = SeedsOfOneBuild(Dense(), pose => pose.Layers = layers);
        int sparse = SeedsOfOneBuild(Sparse(), pose => pose.Layers = layers);

        dense.ShouldBe(0, "the dense base is copied, not seeded");
        sparse.ShouldBe(1, "the sparse base is seeded from rest once");
    }

    /// <remarks>
    /// No layers, so `Accumulate` hands the base straight to `Adjust`, and one controller, so `Adjust` runs. Both bases
    /// read the controller's bone flags once; only the sparse one is seeded.
    /// </remarks>
    [Test]
    public void Build_ADensePoseUnderAController_SkipsTheRestSeedThatASparseOneNeeds()
    {
        void Controlled(SkeletonPose pose)
        {
            pose.Controllers = [new StudioBoneController(3, StudioBoneController.TranslateX, 0f, 4f, 0)];
            pose.BoneControllers = [0.5f];
        }

        int dense = SeedsOfOneBuild(Dense(), Controlled);
        int sparse = SeedsOfOneBuild(Sparse(), Controlled);

        dense.ShouldBe(0, "the dense pose is copied, not seeded");
        sparse.ShouldBe(1, "the sparse pose is seeded from rest once");
    }

    private static int SeedsOfOneBuild(List<StudioBonePose> answer, System.Action<SkeletonPose> configure)
    {
        SkeletonPose pose = new(Skeleton(), (_, _, _, _) => answer);

        configure(pose);
        int before = pose.RestSeeds;

        pose.Build(~0, 0d, new BoneAccessor(Bones), new BoneBitList(Bones));

        return pose.RestSeeds - before;
    }

    private static List<StudioBonePose> Dense() =>
    [
        .. Enumerable.Range(0, Bones).Select(bone =>
            new StudioBonePose(bone, (bone, 1f, -1f), (0.1f * bone, 0.2f, 0.3f, 0.9f))),
    ];

    private static List<StudioBonePose> Sparse() => [.. Dense().Where(moved => moved.Bone % 3 == 0)];

    private static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, Bones).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default,
            Flags: StudioBoneFlags.UsedByAnything)),
    ];
}
