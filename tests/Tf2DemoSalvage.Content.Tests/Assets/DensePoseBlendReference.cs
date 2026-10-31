using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>StudioPoseBlend.BlendInto</c> exactly as it stood at <c>44ca01f7</c>, before the dense-pose change
/// (docs/measurements/2026-10-31-setup-bones-read.md §5): both inputs seeded over the whole skeleton and overlaid, then
/// blended. Every function on that path is copied verbatim (<c>BlendSpans</c>, <c>SeedInto</c>, <c>SeedRest</c>,
/// <c>SeedIdentity</c>, <c>Overlay</c>, <c>BlendExpanded</c>, <c>Align</c>, <c>Normalize</c>).
/// **Kept only here, never in production**: a reference that called production would be broken the same way.
/// </summary>
internal static class DensePoseBlendReference
{
    /// <summary>The old <c>BlendInto</c>, into a fresh array: seed, overlay, seed, overlay, blend.</summary>
    public static StudioBonePose[] Blend(
        IReadOnlyList<StudioBone> bones,
        IReadOnlyList<StudioBonePose> first,
        IReadOnlyList<StudioBonePose> second,
        float s,
        bool additive)
    {
        StudioBonePose[] left = new StudioBonePose[bones.Count];
        StudioBonePose[] right = new StudioBonePose[bones.Count];

        SeedInto(bones, additive, left);
        Overlay(first, left);
        SeedInto(bones, additive, right);
        Overlay(second, right);

        BlendExpanded(bones, s, left, right);

        return left;
    }

    private static void SeedInto(IReadOnlyList<StudioBone> bones, bool additive, Span<StudioBonePose> full)
    {
        if (additive)
        {
            SeedIdentity(full);
        }
        else
        {
            SeedRest(bones, full);
        }
    }

    private static void BlendExpanded(
        IReadOnlyList<StudioBone> bones, float s, Span<StudioBonePose> left, ReadOnlySpan<StudioBonePose> right)
    {
        float weightFirst = 1f - s;

        for (int bone = 0; bone < left.Length; bone++)
        {
            (float X, float Y, float Z, float W) p = left[bone].Rotation;

            (float X, float Y, float Z, float W) q =
                (bones[bone].Flags & StudioBoneFlags.FixedAlignment) != 0
                    ? right[bone].Rotation
                    : Align(p, right[bone].Rotation);

            (float X, float Y, float Z) a = left[bone].Position;
            (float X, float Y, float Z) b = right[bone].Position;

            left[bone] = new StudioBonePose(
                bone,
                ((a.X * weightFirst) + (b.X * s),
                 (a.Y * weightFirst) + (b.Y * s),
                 (a.Z * weightFirst) + (b.Z * s)),
                Normalize((
                    (p.X * weightFirst) + (q.X * s),
                    (p.Y * weightFirst) + (q.Y * s),
                    (p.Z * weightFirst) + (q.Z * s),
                    (p.W * weightFirst) + (q.W * s))));
        }
    }

    private static void SeedIdentity(Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            full[bone] = new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }
    }

    private static void SeedRest(IReadOnlyList<StudioBone> bones, Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            StudioBone rest = bones[bone];

            full[bone] = new StudioBonePose(bone, rest.Position, rest.Rotation);
        }
    }

    private static void Overlay(IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int entry = 0; entry < pose.Count; entry++)
        {
            StudioBonePose moved = pose[entry];

            if (moved.Bone >= 0 && moved.Bone < full.Length)
            {
                full[moved.Bone] = moved;
            }
        }
    }

    private static (float X, float Y, float Z, float W) Align(
        (float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        float apart =
            ((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)) +
            ((p.Z - q.Z) * (p.Z - q.Z)) + ((p.W - q.W) * (p.W - q.W));

        float together =
            ((p.X + q.X) * (p.X + q.X)) + ((p.Y + q.Y) * (p.Y + q.Y)) +
            ((p.Z + q.Z) * (p.Z + q.Z)) + ((p.W + q.W) * (p.W + q.W));

        return apart > together ? (-q.X, -q.Y, -q.Z, -q.W) : q;
    }

    private static (float X, float Y, float Z, float W) Normalize((float X, float Y, float Z, float W) q)
    {
        float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));

        return length > 0f ? (q.X / length, q.Y / length, q.Z / length, q.W / length) : q;
    }
}
