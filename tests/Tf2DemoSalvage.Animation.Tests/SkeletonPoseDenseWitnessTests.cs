using System.Collections;
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
/// two routes. The rest seed is the one step that reads the skeleton through its list interface twice per bone
/// (<c>_bones[bone].Position</c> and <c>.Rotation</c>); the per-bone loop reads the array by reference instead. So
/// a skeleton that counts its own index reads separates the routes by exactly two reads a bone.
///
/// Not witnessed here: the third consumer, the <c>_overrideOf</c> index in <c>Build</c>. Skipping it reads no bone, and
/// the index is private, so its short way is equivalent from outside by construction; the differential covers both
/// routes for correctness.
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

        int dense = ReadsOfOneBuild(Dense(), pose => pose.Layers = layers);
        int sparse = ReadsOfOneBuild(Sparse(), pose => pose.Layers = layers);

        (sparse - dense).ShouldBe(2 * Bones, "the sparse base is seeded from rest, two reads a bone; the dense one is not");
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

        int dense = ReadsOfOneBuild(Dense(), Controlled);
        int sparse = ReadsOfOneBuild(Sparse(), Controlled);

        (sparse - dense).ShouldBe(2 * Bones, "the sparse pose is seeded from rest, two reads a bone; the dense one is not");
    }

    private static int ReadsOfOneBuild(List<StudioBonePose> answer, System.Action<SkeletonPose> configure)
    {
        CountingBones bones = new(Skeleton());
        SkeletonPose pose = new(bones, (_, _, _, _) => answer);

        configure(pose);
        bones.Reads = 0;

        pose.Build(~0, 0d, new BoneAccessor(Bones), new BoneBitList(Bones));

        return bones.Reads;
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

    /// <summary>A skeleton that counts how often it is indexed.</summary>
    private sealed class CountingBones(List<StudioBone> bones) : IReadOnlyList<StudioBone>
    {
        public int Reads { get; set; }

        public int Count => bones.Count;

        public StudioBone this[int index]
        {
            get
            {
                Reads++;

                return bones[index];
            }
        }

        public IEnumerator<StudioBone> GetEnumerator() => bones.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
