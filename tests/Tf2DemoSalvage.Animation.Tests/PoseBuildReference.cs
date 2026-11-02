using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <c>SkeletonPose.Build</c> exactly as it stood at <c>55484e1f</c>, after B622, B624, B625, B626, B633 and B634 and before
/// the iklock-compose work (docs/measurements/2026-11-02-iklock-compose-cost.md): the <c>foreach</c> loops over the
/// layer, sample, controller and chain interfaces, and <c>IkLocks.Solve</c>'s diagnostic re-walk after each successful
/// lock with its mid-solve <c>Reset</c>.
/// </summary>
/// <remarks>
/// **Copied, not shared**, so a fault in the change cannot appear on both sides — and that includes the two leaves the
/// change edits: <c>IkLocks</c> and <c>BoneChain</c> are frozen here too, as <see cref="FrozenIkLocks"/> and
/// <see cref="FrozenBoneChain"/>. Shared are <c>StudioBones</c>' quaternion and matrix arithmetic,
/// <c>StudioPoseBlend.AsDense</c>, <c>StudioBoneController.Value</c>, <see cref="BoneAccessor"/> and <c>IkContext</c>.
/// **<c>IkContext.Solve</c> IS touched** (its rule loop was indexed in the same commit), so a fault there would appear on
/// both sides; what holds it is <c>IkContext</c>'s own tests: starting that loop at 1 reddened five of them (measured
/// 2026-11-02), and the differentials too.
///
/// **Omitted, and held off by the caller**: the procedural bones (<c>JiggleSource</c> null, no quaternion-interpolated
/// bone), the duck-jump shift (0), the three scales (1), the root reflection (null), the diagnostics and the timers.
/// </remarks>
internal sealed class PoseBuildReference
{
    private const int EndLink = 2;

    private readonly StudioBone[] _bones;
    private readonly Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>> _animation;
    private readonly float[][] _local;
    private readonly StudioBonePose[] _layered;
    private readonly StudioBonePose[] _adjusted;
    private readonly int[] _overrideOf;
    private readonly List<FrozenIkLocks> _brackets = [];
    private readonly List<OpenScope> _open = [];
    private readonly List<StudioBonePose[]> _scratch = [];
    private StudioBonePose[] _target = [];
    private int _locals;
    private int[]? _parents;
    private IkContext? _ik;

    public PoseBuildReference(
        IReadOnlyList<StudioBone> bones,
        Func<int, int, float, IReadOnlyList<float>, IReadOnlyList<StudioBonePose>> animation)
    {
        _bones = [.. bones];
        _animation = animation;
        _local = [.. Enumerable.Range(0, bones.Count).Select(_ => new float[12])];
        _layered = new StudioBonePose[bones.Count];
        _adjusted = new StudioBonePose[bones.Count];
        _overrideOf = new int[bones.Count];
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

    public int BaseNested { get; set; }

    public IReadOnlyList<(StudioIkRule Rule, Vector3 Position, Quaternion Rotation, float Weight)> IkErrors { get; set; } = [];

    public int BarrelBone { get; set; } = -1;

    public (float X, float Y, float Z, float W) BarrelRotation { get; set; } = (0f, 0f, 0f, 1f);

    public int SpunBarrels { get; private set; }

    public int SolvedChains => _ik?.Solved ?? 0;

    public int AppliedLocks => _brackets.Sum(bracket => bracket.Applied);

    public (int Moved, float Furthest) LockEffect =>
        (_brackets.Sum(bracket => bracket.Moved), _brackets.Aggregate(0f, (most, bracket) => MathF.Max(most, bracket.FurthestMove)));

    public void Build(int boneMask, BoneAccessor into, BoneBitList alreadyWritten)
    {
        IReadOnlyList<StudioBonePose> animated = _animation(Sequence, Frame, FrameFraction, PoseValues);

        animated = Accumulate(animated);
        animated = Adjust(animated);
        animated = SpinBarrel(animated);

        ReadOnlySpan<StudioBonePose> dense = DenseOf(animated);

        if (dense.IsEmpty)
        {
            Array.Fill(_overrideOf, -1);

            for (int entry = 0; entry < animated.Count; entry++)
            {
                int bone = animated[entry].Bone;

                if (bone >= 0 && bone < _bones.Length)
                {
                    _overrideOf[bone] = entry;
                }
            }
        }

        for (int bone = 0; bone < _bones.Length; bone++)
        {
            if (alreadyWritten.IsMarked(bone))
            {
                continue;
            }

            ref readonly StudioBone rest = ref _bones[bone];

            if ((rest.Flags & boneMask) == 0)
            {
                continue;
            }

            (float X, float Y, float Z, float W) rotation = rest.Rotation;
            (float X, float Y, float Z) position = rest.Position;

            if (!dense.IsEmpty || _overrideOf[bone] >= 0)
            {
                StudioBonePose moved = dense.IsEmpty ? animated[_overrideOf[bone]] : dense[bone];

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

        ReachWithIk(boneMask, into, alreadyWritten);
    }

    private void ReachWithIk(int boneMask, BoneAccessor into, BoneBitList alreadyWritten)
    {
        if (IkChains.Count == 0 || IkErrors.Count == 0)
        {
            return;
        }

        _ik ??= new IkContext();
        _parents ??= [.. _bones.Select(bone => bone.Parent)];

        _ik.Solve(IkChains, IkErrors, into, _parents, _local);

        if (_ik.Solved == 0)
        {
            return;
        }

        int from = int.MaxValue;

        foreach (StudioIkChain chain in IkChains)
        {
            foreach (StudioIkLink link in chain.Links)
            {
                if (link.Bone >= 0 && link.Bone < from)
                {
                    from = link.Bone;
                }
            }
        }

        for (int bone = from + 1; bone < _bones.Length; bone++)
        {
            int parent = _bones[bone].Parent;

            if (parent < 0 ||
                parent >= bone ||
                !alreadyWritten.IsMarked(bone) ||
                !alreadyWritten.IsMarked(parent) ||
                (_bones[bone].Flags & boneMask) == 0 ||
                IsChainBone(bone))
            {
                continue;
            }

            StudioBones.Concatenate(into.Bone(parent), _local[bone], into.BoneForWrite(bone));
        }
    }

    private bool IsChainBone(int bone)
    {
        for (int chain = 0; chain < IkChains.Count; chain++)
        {
            IReadOnlyList<StudioIkLink> links = IkChains[chain].Links;

            for (int link = 0; link < links.Count; link++)
            {
                if (links[link].Bone == bone)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private FrozenIkLocks Bracket(int depth)
    {
        _parents ??= [.. _bones.Select(bone => bone.Parent)];

        while (_brackets.Count <= depth)
        {
            _brackets.Add(new FrozenIkLocks(_parents, _bones.Length));
        }

        return _brackets[depth];
    }

    /// <summary>A scope; <paramref name="Nested"/> marks a layer's lock bracket that nests layers or sits inside another bracket.</summary>
    private readonly record struct OpenScope(int Close, StudioBonePose[] On, IReadOnlyList<StudioIkLock>? Locks, OwnPose? Own, bool Nested = false);

    /// <summary>Test instrument, not the frozen path: nested layer brackets whose solve applied two or more locks at once.</summary>
    public int NestedBracketsWithBothLocks { get; private set; }

    private readonly record struct OwnPose(StudioBonePose[] Into, int Layer, float Weight);

    private IReadOnlyList<StudioBonePose> Accumulate(IReadOnlyList<StudioBonePose> basePose)
    {
        if (Layers.Count == 0 && Locks.Count == 0)
        {
            return basePose;
        }

        StudioBonePose[] result = _layered;
        bool locked = Locks.Count > 0 && IkChains.Count > 0;

        ReadOnlySpan<StudioBonePose> dense = StudioPoseBlend.AsDense(basePose, result.Length);

        if (locked || dense.IsEmpty)
        {
            SeedBind(result);
        }

        _open.Clear();
        _target = result;
        _locals = 0;

        if (locked)
        {
            Bracket(0).Capture(Locks, IkChains, result);
        }

        Overlay(basePose, dense, result);

        if (locked)
        {
            _open.Add(new OpenScope(BaseNested - 1, result, Locks, null));
        }

        CloseBrackets(-1);

        int index = -1;
        int skipThrough = -1;

        foreach (PoseLayer layer in Layers)
        {
            index++;

            if (index <= skipThrough)
            {
                CloseBrackets(index);
                continue;
            }

            float weight = MathF.Min(1f, layer.Weight);
            bool holds = layer.Locks is { Count: > 0 } && IkChains.Count > 0;
            StudioBonePose[] target = _target;

            if (holds)
            {
                Bracket(_open.Count).Capture(layer.Locks!, IkChains, target);
                _open.Add(new OpenScope(
                    index + Math.Max(0, layer.Nested), target, layer.Locks, null, Nested: layer.Nested > 0 || _open.Count > 0));
            }

            int local = Math.Clamp(layer.Local, 0, Math.Max(0, layer.Nested));

            if (weight <= 0f)
            {
                skipThrough = index + local;
                CloseBrackets(index);
                continue;
            }

            IReadOnlyList<StudioBonePose> sampled = _animation(layer.Sequence, layer.Frame, layer.FrameFraction, PoseValues);

            if (local > 0)
            {
                StudioBonePose[] own = Scratch(_locals);
                ReadOnlySpan<StudioBonePose> whole = StudioPoseBlend.AsDense(sampled, own.Length);

                if (whole.IsEmpty)
                {
                    if (layer.Delta)
                    {
                        SeedIdentity(own);
                    }
                    else
                    {
                        SeedBind(own);
                    }
                }

                Overlay(sampled, whole, own);

                _open.Add(new OpenScope(index + local, own, null, new OwnPose(target, index, weight)));
                _locals++;
                _target = own;

                CloseBrackets(index);
                continue;
            }

            foreach (StudioBonePose over in sampled)
            {
                ComposeBone(layer, weight, over, target);
            }

            CloseBrackets(index);
        }

        CloseBrackets(int.MaxValue);

        return result;
    }

    private void ComposeBone(in PoseLayer layer, float weight, in StudioBonePose over, StudioBonePose[] result)
    {
        int bone = over.Bone;

        if (bone < 0 || bone >= result.Length ||
            layer.BoneWeights is not { } weights || bone >= weights.Count)
        {
            return;
        }

        float s2 = weight * weights[bone];

        if (s2 <= 0f)
        {
            return;
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

            return;
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

    private void CloseBrackets(int index)
    {
        while (_open.Count > 0 && _open[^1].Close <= index)
        {
            OpenScope scope = _open[^1];
            _open.RemoveAt(_open.Count - 1);

            if (scope.Own is { } own)
            {
                _locals--;
                _target = own.Into;

                PoseLayer layer = Layers[own.Layer];

                for (int bone = 0; bone < scope.On.Length; bone++)
                {
                    ComposeBone(layer, own.Weight, scope.On[bone], own.Into);
                }
            }
            else if (scope.Locks is { } locks)
            {
                int applied = Bracket(_open.Count).Solve(locks, IkChains, scope.On);

                NestedBracketsWithBothLocks += scope.Nested && applied >= 2 ? 1 : 0;
            }
        }
    }

    private void SeedBind(StudioBonePose[] onto)
    {
        for (int bone = 0; bone < _bones.Length; bone++)
        {
            onto[bone] = new StudioBonePose(bone, _bones[bone].Position, _bones[bone].Rotation);
        }
    }

    private static void SeedIdentity(StudioBonePose[] onto)
    {
        for (int bone = 0; bone < onto.Length; bone++)
        {
            onto[bone] = new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }
    }

    private static void Overlay(IReadOnlyList<StudioBonePose> pose, ReadOnlySpan<StudioBonePose> dense, StudioBonePose[] onto)
    {
        if (!dense.IsEmpty)
        {
            dense.CopyTo(onto);
            return;
        }

        for (int entry = 0; entry < pose.Count; entry++)
        {
            StudioBonePose moved = pose[entry];

            if (moved.Bone >= 0 && moved.Bone < onto.Length)
            {
                onto[moved.Bone] = moved;
            }
        }
    }

    private StudioBonePose[] Scratch(int depth)
    {
        while (_scratch.Count <= depth)
        {
            _scratch.Add(new StudioBonePose[_bones.Length]);
        }

        return _scratch[depth];
    }

    private IReadOnlyList<StudioBonePose> SpinBarrel(IReadOnlyList<StudioBonePose> pose)
    {
        if (BarrelBone < 0 || BarrelBone >= _bones.Length)
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
            spinning.Add(new StudioBonePose(BarrelBone, _bones[BarrelBone].Position, spun));
        }

        SpunBarrels++;

        return spinning;
    }

    private IReadOnlyList<StudioBonePose> Adjust(IReadOnlyList<StudioBonePose> pose)
    {
        if (Controllers.Count == 0 || BoneControllers.Count == 0)
        {
            return pose;
        }

        StudioBonePose[] adjusted = _adjusted;
        ReadOnlySpan<StudioBonePose> dense = DenseOf(pose);

        if (!dense.IsEmpty)
        {
            dense.CopyTo(adjusted);
        }
        else
        {
            for (int bone = 0; bone < _bones.Length; bone++)
            {
                adjusted[bone] = new StudioBonePose(bone, _bones[bone].Position, _bones[bone].Rotation);
            }

            foreach (StudioBonePose moved in pose)
            {
                if (moved.Bone >= 0 && moved.Bone < adjusted.Length)
                {
                    adjusted[moved.Bone] = moved;
                }
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
                    rotation = StudioBones.ScaleBefore(1f, StudioAnimation.FromEulerRadians(Radians(value), 0f, 0f), rotation);
                    break;

                case StudioBoneController.RotateY:
                    rotation = StudioBones.ScaleBefore(1f, StudioAnimation.FromEulerRadians(0f, Radians(value), 0f), rotation);
                    break;

                case StudioBoneController.RotateZ:
                    rotation = StudioBones.ScaleBefore(1f, StudioAnimation.FromEulerRadians(0f, 0f, Radians(value)), rotation);
                    break;

                default:
                    continue;
            }

            adjusted[bone] = new StudioBonePose(bone, position, rotation);
            touched = true;
        }

        return touched ? adjusted : pose;
    }

    private ReadOnlySpan<StudioBonePose> DenseOf(IReadOnlyList<StudioBonePose> pose)
    {
        if (ReferenceEquals(pose, _layered) || ReferenceEquals(pose, _adjusted))
        {
            return (StudioBonePose[])pose;
        }

        return StudioPoseBlend.AsDense(pose, _bones.Length);
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180f);

    /// <summary><c>IkLocks</c> at <c>55484e1f</c>: B622's slam on a failed solve, and the diagnostic re-walk after a success.</summary>
    private sealed class FrozenIkLocks(int[] parents, int bones)
    {
        private const float NoticeableMove = 0.01f;

        private readonly FrozenBoneChain _chain = new(parents, bones);
        private (Vector3 Position, Quaternion Rotation, bool Held)[] _held = [];

        public int Applied { get; private set; }

        public int Moved { get; private set; }

        public float FurthestMove { get; private set; }

        public void Capture(IReadOnlyList<StudioIkLock> locks, IReadOnlyList<StudioIkChain> chains, IReadOnlyList<StudioBonePose> pose)
        {
            if (_held.Length < locks.Count)
            {
                _held = new (Vector3, Quaternion, bool)[locks.Count];
            }

            _chain.Reset();

            for (int at = 0; at < locks.Count; at++)
            {
                _held[at] = default;

                if (EffectorOf(locks[at], chains) is not { } bone)
                {
                    continue;
                }

                _chain.Build(bone, pose);

                (Quaternion rotation, Vector3 position) = Decompose(_chain.Matrix(bone));

                _held[at] = (position, rotation, true);
            }
        }

        /// <returns>How many locks this call applied (the test's own count, beside the frozen code).</returns>
        public int Solve(IReadOnlyList<StudioIkLock> locks, IReadOnlyList<StudioIkChain> chains, StudioBonePose[] pose)
        {
            int before = Applied;

            _chain.Reset();

            for (int at = 0; at < locks.Count && at < _held.Length; at++)
            {
                if (!_held[at].Held || EffectorOf(locks[at], chains) is not { } bone)
                {
                    continue;
                }

                StudioIkChain chain = chains[locks[at].Chain];

                _chain.Build(bone, pose);
                _chain.Build(chain.Links[0].Bone, pose);
                _chain.Build(chain.Links[1].Bone, pose);

                Vector3 now = Position(_chain.Matrix(bone));
                float weight = Math.Clamp(locks[at].PositionWeight, 0f, 1f);

                Vector3 wanted = (now * (1f - weight)) + (_held[at].Position * weight);

                bool solved = IkContext.SolveChain(chain, wanted, _chain.Bones);

                Compose(_held[at].Rotation, Position(_chain.Matrix(bone)), _chain.Bones.BoneForWrite(bone));

                (float X, float Y, float Z, float W) sequenced = pose[bone].Rotation;

                Rebuild(bone, pose);

                pose[bone] = pose[bone] with
                {
                    Rotation = StudioBones.Slerp(pose[bone].Rotation, sequenced, Math.Clamp(locks[at].LocalRotationWeight, 0f, 1f)),
                };

                Rebuild(chain.Links[1].Bone, pose);
                Rebuild(chain.Links[0].Bone, pose);

                if (!solved)
                {
                    continue;
                }

                Applied++;

                _chain.Reset();
                _chain.Build(bone, pose);

                float travelled = (Position(_chain.Matrix(bone)) - now).Length();

                if (travelled > NoticeableMove)
                {
                    Moved++;
                }

                FurthestMove = MathF.Max(FurthestMove, travelled);
            }

            return Applied - before;
        }

        private static int? EffectorOf(StudioIkLock held, IReadOnlyList<StudioIkChain> chains)
        {
            if (held.Chain < 0 || held.Chain >= chains.Count || chains[held.Chain].Links.Count <= EndLink)
            {
                return null;
            }

            int bone = chains[held.Chain].Links[EndLink].Bone;

            return bone >= 0 ? bone : null;
        }

        private void Rebuild(int bone, StudioBonePose[] pose)
        {
            if (bone < 0 || bone >= pose.Length || bone >= parents.Length)
            {
                return;
            }

            int parent = parents[bone];

            Span<float> local = stackalloc float[12];

            if (parent < 0 || parent >= parents.Length)
            {
                _chain.Matrix(bone).CopyTo(local);
            }
            else
            {
                Span<float> inverted = stackalloc float[12];

                StudioBones.Invert(_chain.Matrix(parent), inverted);
                StudioBones.Concatenate(inverted, _chain.Matrix(bone), local);
            }

            (float x, float y, float z, float w) = StudioBones.ToQuaternion(local);

            pose[bone] = new StudioBonePose(bone, (local[3], local[7], local[11]), (x, y, z, w));
        }

        private static Vector3 Position(ReadOnlySpan<float> matrix) => new(matrix[3], matrix[7], matrix[11]);

        private static (Quaternion Rotation, Vector3 Position) Decompose(ReadOnlySpan<float> matrix)
        {
            (float x, float y, float z, float w) = StudioBones.ToQuaternion(matrix);

            return (new Quaternion(x, y, z, w), Position(matrix));
        }

        private static void Compose(Quaternion rotation, Vector3 position, Span<float> matrix) =>
            StudioBones.FromQuaternion((rotation.X, rotation.Y, rotation.Z, rotation.W), (position.X, position.Y, position.Z), matrix);
    }

    /// <summary><c>BoneChain</c> at <c>55484e1f</c>: an interface pose, read twice per bone, and a zeroed walk buffer.</summary>
    private sealed class FrozenBoneChain(int[] parents, int bones)
    {
        private const int MaximumDepth = 128;

        private readonly int _count = Math.Max(0, bones);
        private readonly bool[] _computed = new bool[Math.Max(0, bones)];

        public BoneAccessor Bones { get; } = new(Math.Max(0, bones));

        public void Reset() => Array.Clear(_computed);

        public void Build(int bone, IReadOnlyList<StudioBonePose> pose)
        {
            if (bone < 0 || bone >= _count || _computed[bone])
            {
                return;
            }

            int depth = 0;
            Span<int> upwards = stackalloc int[MaximumDepth];

            for (int at = bone; at >= 0 && at < _count && !_computed[at];)
            {
                if (depth == MaximumDepth)
                {
                    break;
                }

                upwards[depth++] = at;
                _computed[at] = true;

                at = parents[at];
            }

            Span<float> local = stackalloc float[12];

            for (int step = depth - 1; step >= 0; step--)
            {
                int at = upwards[step];

                StudioBonePose stated = Stated(pose, at);

                StudioBones.FromQuaternion(stated.Rotation, stated.Position, local);

                int parent = parents[at];

                if (parent < 0 || parent >= _count)
                {
                    local.CopyTo(Bones.BoneForWrite(at));
                }
                else
                {
                    StudioBones.Concatenate(Bones.Bone(parent), local, Bones.BoneForWrite(at));
                }
            }
        }

        public ReadOnlySpan<float> Matrix(int bone) => bone >= 0 && bone < _count ? Bones.Bone(bone) : [];

        private static StudioBonePose Stated(IReadOnlyList<StudioBonePose> pose, int bone)
        {
            if (bone < pose.Count && pose[bone].Bone == bone)
            {
                return pose[bone];
            }

            for (int at = 0; at < pose.Count; at++)
            {
                if (pose[at].Bone == bone)
                {
                    return pose[at];
                }
            }

            return new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }
    }
}
