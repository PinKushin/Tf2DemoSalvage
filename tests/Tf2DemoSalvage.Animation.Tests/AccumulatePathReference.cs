using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <c>SkeletonPose.Build</c>'s accumulate path exactly as it stood at <c>44ca01f7</c>, before the dense-pose change
/// (docs/measurements/2026-10-31-setup-bones-read.md §5): <c>Accumulate</c> seeding <c>_layered</c> from rest and
/// overlaying the base, <c>Adjust</c> seeding and overlaying again, <c>SpinBarrel</c>, the <c>_overrideOf</c> index,
/// and the per-bone loop's override-or-rest choice, local transform and concatenate.
/// </summary>
/// <remarks>
/// **Copied, not shared**, so a fault in the change cannot appear on both sides. Shared are only leaves the change does
/// not touch: <c>StudioBones</c>' quaternion and matrix arithmetic, <c>IkLocks</c>, <c>StudioBoneController.Value</c>.
///
/// **Omitted, and held off by the caller**: the procedural bones (<c>JiggleSource</c> null), IK (<c>IkErrors</c>
/// empty), the duck-jump shift (0), the three scales (1), the root reflection (null), the diagnostics and the timers.
/// None of them is edited by the change, and the differential sets each to its inert value and checks it as a control.
/// </remarks>
internal sealed class AccumulatePathReference
{
    private readonly IReadOnlyList<StudioBone> _bones;
    private readonly Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>> _animation;
    private readonly int[] _overrideOf;
    private readonly StudioBonePose[] _layered;
    private readonly StudioBonePose[] _adjusted;
    private readonly float[][] _local;
    private IkLocks? _held;

    public AccumulatePathReference(
        IReadOnlyList<StudioBone> bones,
        Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>> animation)
    {
        _bones = bones;
        _animation = animation;
        _overrideOf = new int[bones.Count];
        _layered = new StudioBonePose[bones.Count];
        _adjusted = new StudioBonePose[bones.Count];
        _local = [.. Enumerable.Range(0, bones.Count).Select(_ => new float[12])];
    }

    public int Sequence { get; set; }

    public int Frame { get; set; }

    public float FrameFraction { get; set; }

    public IReadOnlyList<float> PoseValues { get; set; } = [];

    public IReadOnlyList<float>? EntityTransform { get; set; }

    public IReadOnlyList<PoseLayer> Layers { get; set; } = [];

    public IReadOnlyList<float> BoneControllers { get; set; } = [];

    public IReadOnlyList<StudioBoneController> Controllers { get; set; } = [];

    public IReadOnlyList<StudioIkChain> IkChains { get; set; } = [];

    public IReadOnlyList<StudioIkLock> Locks { get; set; } = [];

    public int BarrelBone { get; set; } = -1;

    public (float X, float Y, float Z, float W) BarrelRotation { get; set; } = (0f, 0f, 0f, 1f);

    public void Build(int boneMask, BoneAccessor into, BoneBitList alreadyWritten)
    {
        IReadOnlyList<StudioBonePose> animated = _animation(Sequence, Frame, FrameFraction, PoseValues);

        animated = Accumulate(animated);
        animated = Adjust(animated);
        animated = SpinBarrel(animated);

        Array.Fill(_overrideOf, -1);

        for (int entry = 0; entry < animated.Count; entry++)
        {
            int bone = animated[entry].Bone;

            if (bone >= 0 && bone < _bones.Count)
            {
                _overrideOf[bone] = entry;
            }
        }

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            if (alreadyWritten.IsMarked(bone))
            {
                continue;
            }

            StudioBone rest = _bones[bone];

            if ((rest.Flags & boneMask) == 0)
            {
                continue;
            }

            (float X, float Y, float Z, float W) rotation = rest.Rotation;
            (float X, float Y, float Z) position = rest.Position;

            if (_overrideOf[bone] is var entry && entry >= 0)
            {
                StudioBonePose moved = animated[entry];

                rotation = moved.Rotation;
                position = moved.Position;
            }

            StudioBones.FromQuaternion(rotation, position, _local[bone]);

            float[] destination = into.BoneForWrite(bone);

            if (rest.Parent >= 0 && rest.Parent < bone)
            {
                StudioBones.Concatenate(into.Bone(rest.Parent), _local[bone], destination);
            }
            else if (EntityTransform is { } placement)
            {
                StudioBones.Concatenate([.. placement], _local[bone], destination);
            }
            else
            {
                _local[bone].CopyTo(destination, 0);
            }

            alreadyWritten.Mark(bone);
        }
    }

    private IkLocks Held() => _held ??= new IkLocks([.. _bones.Select(bone => bone.Parent)], _bones.Count);

    private IReadOnlyList<StudioBonePose> Accumulate(IReadOnlyList<StudioBonePose> basePose)
    {
        if (Layers.Count == 0 && Locks.Count == 0)
        {
            return basePose;
        }

        StudioBonePose[] result = _layered;

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            result[bone] = new StudioBonePose(
                bone, _bones[bone].Position, _bones[bone].Rotation);
        }

        if (Locks.Count > 0 && IkChains.Count > 0)
        {
            Held().Capture(Locks, IkChains, result);
        }

        for (int entry = 0; entry < basePose.Count; entry++)
        {
            StudioBonePose moved = basePose[entry];

            if (moved.Bone >= 0 && moved.Bone < result.Length)
            {
                result[moved.Bone] = moved;
            }
        }

        if (Locks.Count > 0 && IkChains.Count > 0)
        {
            Held().Solve(Locks, IkChains, result);
        }

        foreach (PoseLayer layer in Layers)
        {
            if (layer.Weight <= 0f)
            {
                continue;
            }

            float weight = MathF.Min(1f, layer.Weight);

            bool holds = layer.Locks is { Count: > 0 } && IkChains.Count > 0;

            if (holds)
            {
                Held().Capture(layer.Locks!, IkChains, result);
            }

            IReadOnlyList<StudioBonePose> sampled =
                _animation(layer.Sequence, layer.Frame, layer.FrameFraction, PoseValues);

            foreach (StudioBonePose over in sampled)
            {
                int bone = over.Bone;

                if (bone < 0 || bone >= result.Length ||
                    layer.BoneWeights is not { } weights || bone >= weights.Count)
                {
                    continue;
                }

                float s2 = weight * weights[bone];

                if (s2 <= 0f)
                {
                    continue;
                }

                if (s2 > 1f)
                {
                    s2 = 1f;
                }

                StudioBonePose under = result[bone];

                if (layer.Delta)
                {
                    result[bone] = new StudioBonePose(
                        bone,
                        (under.Position.X + (over.Position.X * s2),
                         under.Position.Y + (over.Position.Y * s2),
                         under.Position.Z + (over.Position.Z * s2)),
                        layer.Post
                            ? StudioBones.ScaleAfter(under.Rotation, s2, over.Rotation)
                            : StudioBones.ScaleBefore(s2, over.Rotation, under.Rotation));

                    continue;
                }

                float s1 = 1f - s2;

                result[bone] = new StudioBonePose(
                    bone,
                    ((under.Position.X * s1) + (over.Position.X * s2),
                     (under.Position.Y * s1) + (over.Position.Y * s2),
                     (under.Position.Z * s1) + (over.Position.Z * s2)),
                    (_bones[bone].Flags & StudioBoneFlags.FixedAlignment) != 0
                        ? StudioBones.SlerpNoAlign(over.Rotation, under.Rotation, s1)
                        : StudioBones.Slerp(under.Rotation, over.Rotation, s2));
            }

            if (holds)
            {
                Held().Solve(layer.Locks!, IkChains, result);
            }
        }

        return result;
    }

    private IReadOnlyList<StudioBonePose> SpinBarrel(IReadOnlyList<StudioBonePose> pose)
    {
        if (BarrelBone < 0 || BarrelBone >= _bones.Count)
        {
            return pose;
        }

        (float X, float Y, float Z, float W) spun = BarrelRotation;

        List<StudioBonePose> spinning = new(pose.Count + 1);
        bool wrote = false;

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone == BarrelBone)
            {
                spinning.Add(new StudioBonePose(moved.Bone, moved.Position, spun));
                wrote = true;
            }
            else
            {
                spinning.Add(moved);
            }
        }

        if (!wrote)
        {
            spinning.Add(
                new StudioBonePose(BarrelBone, _bones[BarrelBone].Position, spun));
        }

        return spinning;
    }

    private IReadOnlyList<StudioBonePose> Adjust(IReadOnlyList<StudioBonePose> pose)
    {
        if (Controllers.Count == 0 || BoneControllers.Count == 0)
        {
            return pose;
        }

        StudioBonePose[] adjusted = _adjusted;

        for (int bone = 0; bone < _bones.Count; bone++)
        {
            adjusted[bone] = new StudioBonePose(
                bone, _bones[bone].Position, _bones[bone].Rotation);
        }

        foreach (StudioBonePose moved in pose)
        {
            if (moved.Bone >= 0 && moved.Bone < adjusted.Length)
            {
                adjusted[moved.Bone] = moved;
            }
        }

        bool touched = false;

        foreach (StudioBoneController controller in Controllers)
        {
            int bone = controller.Bone;

            if (bone < 0 || bone >= adjusted.Length ||
                controller.InputField < 0 || controller.InputField >= BoneControllers.Count)
            {
                continue;
            }

            if ((_bones[bone].Flags & StudioBoneFlags.UsedByAnything) == 0)
            {
                continue;
            }

            float value = controller.Value(BoneControllers[controller.InputField]);

            StudioBonePose current = adjusted[bone];

            (float X, float Y, float Z) position = current.Position;
            (float X, float Y, float Z, float W) rotation = current.Rotation;

            switch (controller.Axis)
            {
                case StudioBoneController.TranslateX:
                    position.X += value;
                    break;

                case StudioBoneController.TranslateY:
                    position.Y += value;
                    break;

                case StudioBoneController.TranslateZ:
                    position.Z += value;
                    break;

                case StudioBoneController.RotateX:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(Radians(value), 0f, 0f), rotation);
                    break;

                case StudioBoneController.RotateY:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(0f, Radians(value), 0f), rotation);
                    break;

                case StudioBoneController.RotateZ:
                    rotation = StudioBones.ScaleBefore(
                        1f, StudioAnimation.FromEulerRadians(0f, 0f, Radians(value)), rotation);
                    break;

                default:
                    continue;
            }

            adjusted[bone] = new StudioBonePose(bone, position, rotation);
            touched = true;
        }

        return touched ? adjusted : pose;
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);
}
