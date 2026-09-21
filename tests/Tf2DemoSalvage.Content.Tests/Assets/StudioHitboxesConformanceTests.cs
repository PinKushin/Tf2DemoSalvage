using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>A model's hitboxes, and a bullet's ray against them — `TraceToStudio` (B415).</summary>
/// <remarks>
/// `TraceToStudio` (`bone_setup.cpp:5320`) walks every hitbox of the set, skips one whose BONE's contents miss the
/// trace's mask, and clips the ray against the rest with `ClipRayToHitbox` (`:5153`): a separating-axis rejection in
/// world space, then `IntersectRayWithBox` (`collisionutils.cpp:1130`) in the bone's own space. Each hit shortens the
/// ray, so the answer is the nearest box.
/// </remarks>
public sealed class StudioHitboxesConformanceTests
{
    /// <summary>`MASK_SOLID | CONTENTS_HITBOX`, what a bullet traces with.</summary>
    private const int BulletMask = 0x200400B | 0x40000000;

    /// <summary>An identity bone at the origin, in Valve's row-major 3×4 layout.</summary>
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    [Test]
    public void Read_AModelWithOneSet_ReadsEveryBoxAndItsBonesContents()
    {
        IReadOnlyList<IReadOnlyList<StudioHitbox>> sets = StudioHitboxes.Read(Model());

        sets.Count.ShouldBe(1);
        sets[0].Count.ShouldBe(2);
        sets[0][1].ShouldBe(new StudioHitbox(1, 3, new Vector3(-2f, -3f, -4f), new Vector3(5f, 6f, 7f), 0x1));
    }

    [Test]
    public void Read_AModelWithNoSets_IsEmpty()
    {
        byte[] model = Model();

        BinaryPrimitives.WriteInt32LittleEndian(model.AsSpan(StudioLayout.HeaderHitboxSetCountOffset), 0);

        StudioHitboxes.Read(model).Count.ShouldBe(0);
    }

    /// <remarks>A ray down +X from x = −100 meets a box spanning x ∈ [−10, 10] at x = −10: 90 of 200 units.</remarks>
    [Test]
    public void Trace_ARayThroughABox_StopsAtItsNearFace()
    {
        StudioHitbox box = new(0, 0, new Vector3(-10f), new Vector3(10f), 0x1);

        StudioHitboxes.Trace([box], static _ => Identity, new Vector3(-100f, 0f, 0f), new Vector3(200f, 0f, 0f), BulletMask)
            .ShouldNotBeNull().ShouldBe(0.45f, 0.0001f);
    }

    [Test]
    public void Trace_ARayPastABox_Misses()
    {
        StudioHitbox box = new(0, 0, new Vector3(-10f), new Vector3(10f), 0x1);

        StudioHitboxes.Trace([box], static _ => Identity, new Vector3(-100f, 50f, 0f), new Vector3(200f, 0f, 0f), BulletMask)
            .ShouldBeNull();
    }

    /// <remarks>
    /// **The box is in the BONE's frame.** A bone turned 90° about Z maps the box's local +X onto world +Y, so a
    /// box 40 long in local X and 2 wide in Y stands across a ray down world Y and is struck 20 units early.
    /// </remarks>
    [Test]
    public void Trace_ABoxOnATurnedBone_IsHitInTheBonesFrame()
    {
        // Columns are the bone's axes in world space: local X → world +Y, local Y → world −X.
        float[] turned = [0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f];
        StudioHitbox box = new(0, 0, new Vector3(-20f, -1f, -1f), new Vector3(20f, 1f, 1f), 0x1);

        StudioHitboxes.Trace([box], _ => turned, new Vector3(0f, -100f, 0f), new Vector3(0f, 200f, 0f), BulletMask)
            .ShouldNotBeNull().ShouldBe(0.4f, 0.0001f);
    }

    [Test]
    public void Trace_TwoBoxesOnTheRay_AnswersTheNearer()
    {
        StudioHitbox far = new(0, 0, new Vector3(40f, -5f, -5f), new Vector3(50f, 5f, 5f), 0x1);
        StudioHitbox near = new(0, 0, new Vector3(-50f, -5f, -5f), new Vector3(-40f, 5f, 5f), 0x1);

        StudioHitboxes.Trace([far, near], static _ => Identity, new Vector3(-100f, 0f, 0f), new Vector3(200f, 0f, 0f), BulletMask)
            .ShouldNotBeNull().ShouldBe(0.25f, 0.0001f);
    }

    /// <remarks>`if ( ( fBoneContents &amp; fContentsMask ) == 0 ) continue;` — a bone with no contents is not hit.</remarks>
    [Test]
    public void Trace_ABoxWhoseBoneHasNoContents_IsSkipped()
    {
        StudioHitbox box = new(0, 0, new Vector3(-10f), new Vector3(10f), 0);

        StudioHitboxes.Trace([box], static _ => Identity, new Vector3(-100f, 0f, 0f), new Vector3(200f, 0f, 0f), BulletMask)
            .ShouldBeNull();
    }

    /// <summary>A header, two bones and one set of two boxes, at the offsets `studio.h` gives.</summary>
    private static byte[] Model()
    {
        const int Bones = 400;
        const int Sets = 900;
        const int Boxes = 1000;

        byte[] model = new byte[1200];
        Span<byte> bytes = model;

        BinaryPrimitives.WriteInt32LittleEndian(bytes[StudioLayout.HeaderBoneCountOffset..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[StudioLayout.HeaderBoneIndexOffset..], Bones);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(Bones + StudioLayout.BoneContentsOffset)..], 0x1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(Bones + StudioLayout.BoneStride + StudioLayout.BoneContentsOffset)..], 0x1);

        BinaryPrimitives.WriteInt32LittleEndian(bytes[StudioLayout.HeaderHitboxSetCountOffset..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[StudioLayout.HeaderHitboxSetIndexOffset..], Sets);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(Sets + StudioLayout.HitboxSetCountOffset)..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(Sets + StudioLayout.HitboxSetIndexOffset)..], Boxes - Sets);

        int second = Boxes + StudioLayout.HitboxStride;

        BinaryPrimitives.WriteInt32LittleEndian(bytes[second..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[(second + 4)..], 3);

        float[] corners = [-2f, -3f, -4f, 5f, 6f, 7f];

        for (int axis = 0; axis < corners.Length; axis++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes[(second + StudioLayout.HitboxMinOffset + (axis * 4))..], corners[axis]);
        }

        return model;
    }
}
