using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="StudioPoseBlend.AsDense"/> on every shape, and a witness that <see cref="StudioPoseBlend.BlendInto"/>
/// actually takes the short way for a dense input.
/// </summary>
/// <remarks>
/// **The differentials prove the short way is never WRONG; only these prove it is ever TAKEN.** A predicate that always
/// answered empty would send every pose down the old expansion and pass every bit-identity test, so the shortcut has to
/// be witnessed by something that differs between the two routes. For <c>BlendInto</c> that is the scratch list: the
/// old route expands the second input into it, and the short way never touches it.
/// </remarks>
public sealed class StudioPoseBlendAsDenseTests
{
    private const int Bones = 5;

    [Test]
    public void AsDense_AnArrayOneEntryPerBoneInOrder_IsTheArraysOwnStorage()
    {
        StudioBonePose[] pose = Dense();

        ReadOnlySpan<StudioBonePose> dense = StudioPoseBlend.AsDense(pose, Bones);

        dense.Length.ShouldBe(Bones);
        SameStorage(dense, pose).ShouldBeTrue("the array itself is read, not a copy of it");
    }

    [Test]
    public void AsDense_AListOneEntryPerBoneInOrder_IsTheListsOwnStorage()
    {
        List<StudioBonePose> pose = [.. Dense()];

        ReadOnlySpan<StudioBonePose> dense = StudioPoseBlend.AsDense(pose, Bones);

        dense.Length.ShouldBe(Bones);
        SameStorage(dense, CollectionsMarshal.AsSpan(pose)).ShouldBeTrue("the list's storage is read, not a copy of it");
    }

    [TestCase("a bone named twice")]
    [TestCase("reordered")]
    [TestCase("one short")]
    [TestCase("one long")]
    [TestCase("the last bone wrong")]
    [TestCase("the first bone wrong")]
    [TestCase("behind another interface")]
    [TestCase("empty")]
    public void AsDense_AnyOtherShape_IsEmpty(string shape)
    {
        StudioBonePose[] dense = Dense();

        IReadOnlyList<StudioBonePose> pose = shape switch
        {
            "a bone named twice" => [dense[0], dense[1], dense[1], dense[3], dense[4]],
            "reordered" => [dense[1], dense[0], dense[2], dense[3], dense[4]],
            "one short" => dense[..^1],
            "one long" => [.. dense, dense[0] with { Bone = Bones }],
            "the last bone wrong" => [.. dense[..^1], dense[^1] with { Bone = -1 }],
            "the first bone wrong" => [dense[0] with { Bone = 7 }, .. dense[1..]],
            "behind another interface" => new ReadOnlyCollection<StudioBonePose>(dense),
            _ => [],
        };

        StudioPoseBlend.AsDense(pose, Bones).IsEmpty.ShouldBeTrue(shape);
    }

    /// <remarks>
    /// The control beside the shape cases: the same dense array asked about a skeleton of another size is refused, so
    /// a predicate that ignored the count is caught as well as one that ignored the order.
    /// </remarks>
    [TestCase(Bones - 1)]
    [TestCase(Bones + 1)]
    [TestCase(0)]
    [TestCase(-1)]
    public void AsDense_ASkeletonOfAnotherSize_IsEmpty(int bones) =>
        StudioPoseBlend.AsDense(Dense(), bones).IsEmpty.ShouldBeTrue();

    [Test]
    public void AsDense_NoBonesAndNoEntries_IsEmpty() =>
        StudioPoseBlend.AsDense(Array.Empty<StudioBonePose>(), 0).IsEmpty.ShouldBeTrue();

    /// <remarks>
    /// **The witness.** Two dense inputs: the old route would have sized the scratch list to the skeleton and expanded the
    /// second input into it; the short way reads both where they lie and leaves it alone. The sparse second input is the
    /// control, and it must fill the scratch list, or the witness is measuring nothing.
    /// </remarks>
    [TestCase(true, 0)]
    [TestCase(false, Bones)]
    public void BlendInto_ADenseOrSparseSecondInput_UsesTheScratchListOnlyWhenSparse(bool dense, int scratchCount)
    {
        List<StudioBone> bones = Skeleton();
        List<StudioBonePose> first = [.. Dense()];
        List<StudioBonePose> second = dense ? [.. Dense()] : [Dense()[2]];
        List<StudioBonePose> into = [];
        List<StudioBonePose> scratch = [];

        StudioPoseBlend.BlendInto(bones, first, second, 0.5f, additive: false, into, scratch);

        scratch.Count.ShouldBe(scratchCount);
        into.Count.ShouldBe(Bones, "the control: the blend ran");
    }

    /// <summary>The bound of the blend's loop is checked: three spans of one length or an exception.</summary>
    [TestCase(Bones - 1, Bones)]
    [TestCase(Bones + 1, Bones)]
    [TestCase(Bones, Bones - 1)]
    [TestCase(Bones, Bones + 1)]
    public void BlendExpanded_SpansOfDifferentLengths_Throws(int left, int right)
    {
        List<StudioBone> bones = [.. Skeleton(), .. Skeleton()];
        StudioBonePose[] into = new StudioBonePose[Bones];

        Should.Throw<ArgumentException>(() => StudioPoseBlend.BlendExpanded(
            bones, 0.5f, new StudioBonePose[left], new StudioBonePose[right], into));
    }

    [Test]
    public void BlendExpanded_SpansOfOneLength_Blends()
    {
        StudioBonePose[] into = new StudioBonePose[Bones];

        StudioPoseBlend.BlendExpanded(Skeleton(), 0.5f, Dense(), Dense(), into);

        into[Bones - 1].Bone.ShouldBe(Bones - 1, "the control: the same call with matching lengths runs to the end");
    }

    private static bool SameStorage(ReadOnlySpan<StudioBonePose> a, ReadOnlySpan<StudioBonePose> b) =>
        a.Length == b.Length && a.Overlaps(b, out int offset) && offset == 0;

    private static StudioBonePose[] Dense() =>
    [
        .. Enumerable.Range(0, Bones).Select(bone =>
            new StudioBonePose(bone, (bone, 2f * bone, -bone), (0.1f * bone, 0.2f, 0.3f, 0.9f))),
    ];

    private static List<StudioBone> Skeleton() =>
    [
        .. Enumerable.Range(0, Bones).Select(bone => new StudioBone(
            Name: $"b{bone}",
            Parent: bone - 1,
            Position: (bone, 0f, 0f),
            Rotation: (0f, 0f, 0f, 1f),
            PoseToBone: default)),
    ];
}
