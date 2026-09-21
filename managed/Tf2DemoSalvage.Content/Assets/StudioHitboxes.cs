using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One hitbox, <c>mstudiobbox_t</c>, with its bone's contents beside it.</summary>
/// <param name="Bone">The bone it rides.</param>
/// <param name="Group">`group`, the hit group — head, chest, a limb.</param>
/// <param name="Min">`bbmin`, in the bone's space.</param>
/// <param name="Max">`bbmax`.</param>
/// <param name="Contents">That bone's `contents`, which `TraceToStudio` tests against the trace's mask.</param>
public readonly record struct StudioHitbox(int Bone, int Group, Vector3 Min, Vector3 Max, int Contents);

/// <summary>A model's hitbox sets, and a ray against them — <c>TraceToStudio</c> (B415).</summary>
/// <remarks>
/// **What a bullet hits on a player**: `UTIL_PlayerBulletTrace` traces with `MASK_SOLID | CONTENTS_HITBOX`, so the
/// client clips each bullet against the posed hitboxes rather than the player's box. The set is `m_nHitboxSet`'s,
/// which every TF2 player leaves at 0.
///
/// **A ray only.** `TraceToStudio` hands a swept box to `SweepBoxToStudio`, which a bullet never is.
/// **Model scale is not applied** — `TraceToStudio` rescales the bones and the ray when `GetModelScale()` is not 1,
/// which a player's is unless a spell or an attribute has shrunk them.
/// </remarks>
public static class StudioHitboxes
{
    /// <summary>`flProjEpsilon` in `ClipRayToHitbox`.</summary>
    private const float ProjectionEpsilon = 0.01f;

    /// <summary>Reads every hitbox set a model declares.</summary>
    /// <param name="model">The whole <c>.mdl</c>.</param>
    /// <returns>The sets, by index; each is its boxes in file order.</returns>
    /// <remarks>A count or offset that points outside the file ends the read with what was whole, as the other readers do.</remarks>
    public static IReadOnlyList<IReadOnlyList<StudioHitbox>> Read(ReadOnlySpan<byte> model)
    {
        if (model.Length < StudioLayout.HeaderHitboxSetIndexOffset + 4)
        {
            return [];
        }

        int bones = BinaryPrimitives.ReadInt32LittleEndian(model[StudioLayout.HeaderBoneCountOffset..]);
        int boneIndex = BinaryPrimitives.ReadInt32LittleEndian(model[StudioLayout.HeaderBoneIndexOffset..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(model[StudioLayout.HeaderHitboxSetCountOffset..]);
        int index = BinaryPrimitives.ReadInt32LittleEndian(model[StudioLayout.HeaderHitboxSetIndexOffset..]);

        List<IReadOnlyList<StudioHitbox>> sets = [];

        for (int set = 0; set < count; set++)
        {
            int at = index + (set * StudioLayout.HitboxSetStride);

            if (at < 0 || at + StudioLayout.HitboxSetStride > model.Length)
            {
                break;
            }

            int boxes = BinaryPrimitives.ReadInt32LittleEndian(model[(at + StudioLayout.HitboxSetCountOffset)..]);
            int first = at + BinaryPrimitives.ReadInt32LittleEndian(model[(at + StudioLayout.HitboxSetIndexOffset)..]);

            List<StudioHitbox> read = [];

            for (int box = 0; box < boxes; box++)
            {
                int from = first + (box * StudioLayout.HitboxStride);

                if (from < 0 || from + StudioLayout.HitboxStride > model.Length)
                {
                    break;
                }

                int bone = BinaryPrimitives.ReadInt32LittleEndian(model[from..]);
                int contentsAt = boneIndex + (bone * StudioLayout.BoneStride) + StudioLayout.BoneContentsOffset;
                int contents = bone >= 0 && bone < bones && contentsAt >= 0 && contentsAt + 4 <= model.Length
                    ? BinaryPrimitives.ReadInt32LittleEndian(model[contentsAt..])
                    : 0;

                read.Add(new StudioHitbox(
                    bone,
                    BinaryPrimitives.ReadInt32LittleEndian(model[(from + 4)..]),
                    Vector(model, from + StudioLayout.HitboxMinOffset),
                    Vector(model, from + StudioLayout.HitboxMinOffset + 12),
                    contents));
            }

            sets.Add(read);
        }

        return sets;
    }

    /// <summary>How far along a ray it gets before a hitbox stops it — <c>TraceToStudio</c>'s ray branch.</summary>
    /// <param name="set">The boxes.</param>
    /// <param name="bone">Each bone's world matrix, 3×4 in Valve's layout (translation in column 3).</param>
    /// <param name="start">`ray.m_Start`.</param>
    /// <param name="delta">`ray.m_Delta`.</param>
    /// <param name="mask">The trace's contents mask.</param>
    /// <returns>The fraction of the ray, or null when no box is hit.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static float? Trace(
        IReadOnlyList<StudioHitbox> set, Func<int, IReadOnlyList<float>> bone, Vector3 start, Vector3 delta, int mask)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(bone);

        float fraction = 1f;
        bool hit = false;

        foreach (StudioHitbox box in set)
        {
            if ((box.Contents & mask) == 0)
            {
                continue;
            }

            hit |= Clip(start, delta, box, bone(box.Bone), ref fraction);
        }

        return hit ? fraction : null;
    }

    /// <summary>`ClipRayToHitbox` (`bone_setup.cpp:5153`): true when the ray, already shortened to <paramref name="fraction"/>, meets the box.</summary>
    private static bool Clip(Vector3 start, Vector3 delta, StudioHitbox box, IReadOnlyList<float> m, ref float fraction)
    {
        // "scale by current t so hits shorten the ray and increase the likelihood of early outs"
        Vector3 half = delta * (0.5f * fraction);

        Vector3 centreLocal = (box.Min + box.Max) * 0.5f;
        Vector3 centre = Transform(centreLocal, m);
        Vector3 extents = box.Max - centreLocal;

        Vector3 segment = start + half - centre;
        Vector3 extent = default;
        Vector3 absolute = default;

        for (int j = 0; j < 3; j++)
        {
            float along = (half.X * m[j]) + (half.Y * m[4 + j]) + (half.Z * m[8 + j]);
            float coord = MathF.Abs((segment.X * m[j]) + (segment.Y * m[4 + j]) + (segment.Z * m[8 + j]));

            Set(ref extent, j, along);
            Set(ref absolute, j, MathF.Abs(along));

            if (coord > Get(extents, j) + MathF.Abs(along))
            {
                return false;
            }
        }

        Vector3 cross = Vector3.Cross(half, segment);

        if (Separated(cross, m, 0, (extents.Y * absolute.Z) + (extents.Z * absolute.Y)) ||
            Separated(cross, m, 1, (extents.X * absolute.Z) + (extents.Z * absolute.X)) ||
            Separated(cross, m, 2, (extents.X * absolute.Y) + (extents.Y * absolute.X)))
        {
            return false;
        }

        // Hit: the ray start into bone space, and the (pre-scaled) delta back to its full shortened length.
        Vector3 local = InverseTransform(start, m);

        if (!IntersectRayWithBox(local, extent * 2f, box.Min, box.Max, out float t))
        {
            return false;
        }

        fraction *= t;

        return true;
    }

    /// <summary>One cross-axis test: `cextent > MAX( tmp, flProjEpsilon )`.</summary>
    private static bool Separated(Vector3 cross, IReadOnlyList<float> m, int column, float bound) =>
        MathF.Abs((cross.X * m[column]) + (cross.Y * m[4 + column]) + (cross.Z * m[8 + column])) >
        MathF.Max(bound, ProjectionEpsilon);

    /// <summary>
    /// `IntersectRayWithBox` (`collisionutils.cpp:1130`) with a tolerance of zero, as `ClipRayToHitbox` calls it, and the
    /// `CBaseTrace` overload's reading of the result (`:1208`): the entry fraction, or zero from inside.
    /// </summary>
    private static bool IntersectRayWithBox(Vector3 start, Vector3 delta, Vector3 mins, Vector3 maxs, out float fraction)
    {
        float t1 = -1f;
        float t2 = 1f;
        bool startSolid = true;

        fraction = 1f;

        for (int i = 0; i < 6; i++)
        {
            float d1;
            float d2;

            if (i >= 3)
            {
                d1 = Get(start, i - 3) - Get(maxs, i - 3);
                d2 = d1 + Get(delta, i - 3);
            }
            else
            {
                d1 = -Get(start, i) + Get(mins, i);
                d2 = d1 - Get(delta, i);
            }

            if (d1 > 0 && d2 > 0)
            {
                return false;
            }

            if (d1 <= 0 && d2 <= 0)
            {
                continue;
            }

            if (d1 > 0)
            {
                startSolid = false;
            }

            if (d1 > d2)
            {
                float f = MathF.Max(d1, 0f) / (d1 - d2);

                t1 = MathF.Max(t1, f);
            }
            else
            {
                t2 = MathF.Min(t2, d1 / (d1 - d2));
            }
        }

        if (!startSolid && !(t1 < t2 && t1 >= 0f))
        {
            return false;
        }

        if (t1 < t2 && t1 >= 0f)
        {
            fraction = t1;
            return true;
        }

        // Starting inside: `pTrace->fraction = 0`.
        fraction = 0f;
        return true;
    }

    /// <summary>`VectorTransform`: bone space to world.</summary>
    private static Vector3 Transform(Vector3 v, IReadOnlyList<float> m) =>
        new(
            (v.X * m[0]) + (v.Y * m[1]) + (v.Z * m[2]) + m[3],
            (v.X * m[4]) + (v.Y * m[5]) + (v.Z * m[6]) + m[7],
            (v.X * m[8]) + (v.Y * m[9]) + (v.Z * m[10]) + m[11]);

    /// <summary>`VectorITransform`: world to bone space, the transpose of the rotation.</summary>
    private static Vector3 InverseTransform(Vector3 v, IReadOnlyList<float> m)
    {
        Vector3 d = v - new Vector3(m[3], m[7], m[11]);

        return new Vector3(
            (d.X * m[0]) + (d.Y * m[4]) + (d.Z * m[8]),
            (d.X * m[1]) + (d.Y * m[5]) + (d.Z * m[9]),
            (d.X * m[2]) + (d.Y * m[6]) + (d.Z * m[10]));
    }

    private static float Get(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static void Set(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }

    private static Vector3 Vector(ReadOnlySpan<byte> bytes, int at) =>
        new(
            BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[(at + 8)..]));
}
