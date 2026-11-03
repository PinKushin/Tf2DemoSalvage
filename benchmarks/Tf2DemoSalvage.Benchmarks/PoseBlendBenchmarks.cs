using System;
using System.Collections.Generic;
using System.Linq;

using BenchmarkDotNet.Attributes;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Benchmarks;

/// <summary>
/// Finding 101's experiment: how much of a bone blend is managed glue (interface dispatch, struct copies, allocation)
/// and how much is the arithmetic the engine's <c>BlendBones</c> also does.
/// </summary>
/// <remarks>
/// **Real inputs, not invented ones**: the skeleton and two animation frames come from the shipped
/// <c>soldier_animations.mdl</c> through the production readers (<see cref="GameArchives"/>,
/// <see cref="StudioBones.Read"/>, <see cref="StudioAnimation.Pose"/>). The cost of a blend depends on the bone count and
/// on how sparse the two poses are, not on the tick, so the file is the input.
///
/// Four variants, each a rung that removes one thing:
/// <list type="bullet">
/// <item><c>Production</c>: the public <see cref="StudioPoseBlend.Blend"/> (allocates its two arrays; production's
/// <c>BlendInto</c> is internal and the same body into caller storage).</item>
/// <item><c>CopyInto</c>: a verbatim copy of that body into caller arrays, still through <c>IReadOnlyList</c>: the
/// production <c>BlendInto</c> shape. Production minus this is the allocation.</item>
/// <item><c>Flat</c>: the same arithmetic over persistent per-model arrays (rest pose and the fixed-alignment flag
/// extracted once), spans in, spans out, no interface call. CopyInto minus this is the dispatch and struct-copy glue.</item>
/// <item><c>Arithmetic</c>: the blend loop alone over pre-expanded SoA float arrays: what a data-oriented scene that
/// keeps poses dense would pay. The floor for C#.</item>
/// </list>
/// Every variant's output is asserted bitwise-equal to production in setup, or the run throws.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20)]
public class PoseBlendBenchmarks
{
    private const string Folder = @"F:\SteamLibrary\steamapps\common\Team Fortress 2\tf";

    private IReadOnlyList<StudioBone> _bones = [];
    private IReadOnlyList<StudioBonePose> _first = [];
    private IReadOnlyList<StudioBonePose> _second = [];
    private StudioBonePose[] _firstArray = [];
    private StudioBonePose[] _secondArray = [];
    private StudioBonePose[] _into = [];
    private StudioBonePose[] _scratch = [];
    private float[] _restPosition = [];
    private float[] _restRotation = [];
    private bool[] _fixed = [];
    private float[] _leftP = [], _leftQ = [], _rightP = [], _rightQ = [];

    /// <summary>The blend fraction; a mid value so neither side is trivially dropped.</summary>
    private const float S = 0.37f;

    /// <summary>
    /// Production's two paths: an ordinary blend seeds absent bones from the rest pose (reading the skeleton per bone),
    /// a delta blend (every player's aim matrix) seeds identity and never reads it.
    /// </summary>
    [Params(false, true)]
    public bool Additive { get; set; }

    /// <summary>Which animation name fragment picks the pair; chosen for shape, not as a pair one tick blends.</summary>
    private string Pick => Additive ? "aimmatrix" : "run_";

    [GlobalSetup]
    public void Load()
    {
        GameArchives archives = GameArchives.Open(Folder);
        byte[] mdl = archives.Read("models/player/soldier_animations.mdl")
            ?? throw new InvalidOperationException("soldier_animations.mdl not found: the archive reader or the install is missing.");

        _bones = StudioBones.Read(mdl);
        ReadOnlyMemory<byte> file = mdl;
        int[] picked = Enumerable.Range(0, StudioAnimation.Count(file))
            .Where(a => StudioAnimation.Name(file, a).Contains(Pick, StringComparison.OrdinalIgnoreCase) && StudioAnimation.IsDelta(file, a) == Additive)
            .Select(a => (a, n: StudioAnimation.Pose(file, _bones, a, 0).Count))
            .Where(p => p.n > 0)
            .Take(2)
            .Select(p => p.a)
            .ToArray();

        if (picked.Length < 2)
        {
            throw new InvalidOperationException($"fewer than two '{Pick}' animations with poses: the control failed.");
        }

        _first = StudioAnimation.Pose(file, _bones, picked[0], 0);
        _second = StudioAnimation.Pose(file, _bones, picked[1], 0);
        _firstArray = [.. _first];
        _secondArray = [.. _second];

        int n = _bones.Count;
        _into = new StudioBonePose[n];
        _scratch = new StudioBonePose[n];
        _restPosition = new float[n * 3];
        _restRotation = new float[n * 4];
        _fixed = new bool[n];
        _leftP = new float[n * 3]; _rightP = new float[n * 3];
        _leftQ = new float[n * 4]; _rightQ = new float[n * 4];

        for (int b = 0; b < n; b++)
        {
            StudioBone bone = _bones[b];

            // The seed: the rest pose for an ordinary blend, identity for a delta (production's ExpandIdentityInto).
            (float X, float Y, float Z) position = Additive ? (0f, 0f, 0f) : bone.Position;
            (float X, float Y, float Z, float W) rotation = Additive ? (0f, 0f, 0f, 1f) : bone.Rotation;
            (_restPosition[b * 3], _restPosition[(b * 3) + 1], _restPosition[(b * 3) + 2]) = position;
            (_restRotation[b * 4], _restRotation[(b * 4) + 1], _restRotation[(b * 4) + 2], _restRotation[(b * 4) + 3]) = rotation;
            _fixed[b] = (bone.Flags & StudioBoneFlags.FixedAlignment) != 0;
        }

        Console.WriteLine(
            $"// pose-blend inputs: {n} bones, '{StudioAnimation.Name(file, picked[0])}' ({_first.Count} posed) and " +
            $"'{StudioAnimation.Name(file, picked[1])}' ({_second.Count} posed), fixed-alignment bones {_fixed.Count(f => f)}");

        Verify();
    }

    private void Verify()
    {
        IReadOnlyList<StudioBonePose> expected = Production();

        Check("CopyInto", expected, CopyInto());
        Check("Flat", expected, Flat());

        Arithmetic();
        for (int b = 0; b < _bones.Count; b++)
        {
            StudioBonePose e = expected[b];
            if (e.Position != (_leftP[b * 3], _leftP[(b * 3) + 1], _leftP[(b * 3) + 2]) ||
                e.Rotation != (_leftQ[b * 4], _leftQ[(b * 4) + 1], _leftQ[(b * 4) + 2], _leftQ[(b * 4) + 3]))
            {
                throw new InvalidOperationException($"Arithmetic differs from production at bone {b}.");
            }
        }

        // Control: the equality check must be able to fail. A blend at another fraction differs somewhere.
        IReadOnlyList<StudioBonePose> other = StudioPoseBlend.Blend(_bones, _first, _second, S + 0.1f, Additive);
        if (Enumerable.Range(0, _bones.Count).All(b => other[b] == expected[b]))
        {
            throw new InvalidOperationException("Control failed: a different fraction compared equal, so the check sees nothing.");
        }
    }

    private static void Check(string name, IReadOnlyList<StudioBonePose> expected, StudioBonePose[] actual)
    {
        for (int b = 0; b < expected.Count; b++)
        {
            if (expected[b] != actual[b])
            {
                throw new InvalidOperationException($"{name} differs from production at bone {b}: {actual[b]} vs {expected[b]}.");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public IReadOnlyList<StudioBonePose> Production() =>
        StudioPoseBlend.Blend(_bones, _first, _second, S, Additive);

    /// <summary>
    /// A copy of production's <c>BlendSpans</c> into caller storage (the <c>BlendInto</c> shape), loop for loop:
    /// two <c>ExpandInto</c> (or <c>ExpandIdentityInto</c>) passes, then the blend.
    /// </summary>
    [Benchmark]
    public StudioBonePose[] CopyInto()
    {
        IReadOnlyList<StudioBone> bones = _bones;
        Span<StudioBonePose> left = _into;
        Span<StudioBonePose> right = _scratch;

        if (Additive)
        {
            ExpandIdentityInto(_first, left);
            ExpandIdentityInto(_second, right);
        }
        else
        {
            ExpandInto(bones, _first, left);
            ExpandInto(bones, _second, right);
        }

        float weightFirst = 1f - S;

        for (int bone = 0; bone < left.Length; bone++)
        {
            (float X, float Y, float Z, float W) p = left[bone].Rotation;
            (float X, float Y, float Z, float W) q =
                (bones[bone].Flags & StudioBoneFlags.FixedAlignment) != 0 ? right[bone].Rotation : Align(p, right[bone].Rotation);
            (float X, float Y, float Z) a = left[bone].Position;
            (float X, float Y, float Z) b = right[bone].Position;

            left[bone] = new StudioBonePose(
                bone,
                ((a.X * weightFirst) + (b.X * S), (a.Y * weightFirst) + (b.Y * S), (a.Z * weightFirst) + (b.Z * S)),
                Normalize(((p.X * weightFirst) + (q.X * S), (p.Y * weightFirst) + (q.Y * S),
                    (p.Z * weightFirst) + (q.Z * S), (p.W * weightFirst) + (q.W * S))));
        }

        return _into;
    }

    /// <summary>The same blend over persistent flat per-model state: no interface call, no 100-byte bone copy.</summary>
    [Benchmark]
    public StudioBonePose[] Flat()
    {
        Span<StudioBonePose> left = _into;
        Span<StudioBonePose> right = _scratch;
        ReadOnlySpan<float> rp = _restPosition;
        ReadOnlySpan<float> rq = _restRotation;
        ReadOnlySpan<bool> fixedAlign = _fixed;

        for (int bone = 0; bone < left.Length; bone++)
        {
            StudioBonePose rest = new(bone, (rp[bone * 3], rp[(bone * 3) + 1], rp[(bone * 3) + 2]),
                (rq[bone * 4], rq[(bone * 4) + 1], rq[(bone * 4) + 2], rq[(bone * 4) + 3]));
            left[bone] = rest;
            right[bone] = rest;
        }

        OverlaySpan(_firstArray, left);
        OverlaySpan(_secondArray, right);

        float weightFirst = 1f - S;

        for (int bone = 0; bone < left.Length; bone++)
        {
            (float X, float Y, float Z, float W) p = left[bone].Rotation;
            (float X, float Y, float Z, float W) q = fixedAlign[bone] ? right[bone].Rotation : Align(p, right[bone].Rotation);
            (float X, float Y, float Z) a = left[bone].Position;
            (float X, float Y, float Z) b = right[bone].Position;

            left[bone] = new StudioBonePose(
                bone,
                ((a.X * weightFirst) + (b.X * S), (a.Y * weightFirst) + (b.Y * S), (a.Z * weightFirst) + (b.Z * S)),
                Normalize(((p.X * weightFirst) + (q.X * S), (p.Y * weightFirst) + (q.Y * S),
                    (p.Z * weightFirst) + (q.Z * S), (p.W * weightFirst) + (q.W * S))));
        }

        return _into;
    }

    /// <summary>
    /// The arithmetic alone, over dense SoA arrays already holding both poses: the per-frame cost if the scene kept
    /// dense poses (as the engine's <c>BoneVector</c>/<c>BoneQuaternion</c> arrays are) instead of expanding sparse lists.
    /// The expansion (rest copy + overlay) is done here too, as array copies, because the engine also seeds its arrays.
    /// </summary>
    [Benchmark]
    public float[] Arithmetic()
    {
        _restPosition.AsSpan().CopyTo(_leftP);
        _restPosition.AsSpan().CopyTo(_rightP);
        _restRotation.AsSpan().CopyTo(_leftQ);
        _restRotation.AsSpan().CopyTo(_rightQ);
        OverlaySoa(_firstArray, _leftP, _leftQ);
        OverlaySoa(_secondArray, _rightP, _rightQ);

        Span<float> lp = _leftP, lq = _leftQ;
        ReadOnlySpan<float> rp = _rightP, rq = _rightQ;
        ReadOnlySpan<bool> fixedAlign = _fixed;
        float w1 = 1f - S;
        int n = fixedAlign.Length;

        for (int b = 0; b < n; b++)
        {
            int i = b * 4;
            float px = lq[i], py = lq[i + 1], pz = lq[i + 2], pw = lq[i + 3];
            float qx = rq[i], qy = rq[i + 1], qz = rq[i + 2], qw = rq[i + 3];

            if (!fixedAlign[b])
            {
                float apart = ((px - qx) * (px - qx)) + ((py - qy) * (py - qy)) + ((pz - qz) * (pz - qz)) + ((pw - qw) * (pw - qw));
                float together = ((px + qx) * (px + qx)) + ((py + qy) * (py + qy)) + ((pz + qz) * (pz + qz)) + ((pw + qw) * (pw + qw));
                if (apart > together)
                {
                    qx = -qx; qy = -qy; qz = -qz; qw = -qw;
                }
            }

            float x = (px * w1) + (qx * S), y = (py * w1) + (qy * S), z = (pz * w1) + (qz * S), w = (pw * w1) + (qw * S);
            float length = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
            if (length > 0f)
            {
                x /= length; y /= length; z /= length; w /= length;
            }

            lq[i] = x; lq[i + 1] = y; lq[i + 2] = z; lq[i + 3] = w;

            int j = b * 3;
            lp[j] = (lp[j] * w1) + (rp[j] * S);
            lp[j + 1] = (lp[j + 1] * w1) + (rp[j + 1] * S);
            lp[j + 2] = (lp[j + 2] * w1) + (rp[j + 2] * S);
        }

        return _leftQ;
    }

    private static void ExpandIdentityInto(IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            full[bone] = new StudioBonePose(bone, (0f, 0f, 0f), (0f, 0f, 0f, 1f));
        }

        Overlay(pose, full);
    }

    private static void ExpandInto(IReadOnlyList<StudioBone> bones, IReadOnlyList<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        for (int bone = 0; bone < full.Length; bone++)
        {
            StudioBone rest = bones[bone];
            full[bone] = new StudioBonePose(bone, rest.Position, rest.Rotation);
        }

        Overlay(pose, full);
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

    private static void OverlaySpan(ReadOnlySpan<StudioBonePose> pose, Span<StudioBonePose> full)
    {
        foreach (StudioBonePose moved in pose)
        {
            if ((uint)moved.Bone < (uint)full.Length)
            {
                full[moved.Bone] = moved;
            }
        }
    }

    private static void OverlaySoa(ReadOnlySpan<StudioBonePose> pose, Span<float> p, Span<float> q)
    {
        int n = q.Length / 4;
        foreach (StudioBonePose moved in pose)
        {
            if ((uint)moved.Bone < (uint)n)
            {
                (p[moved.Bone * 3], p[(moved.Bone * 3) + 1], p[(moved.Bone * 3) + 2]) = moved.Position;
                (q[moved.Bone * 4], q[(moved.Bone * 4) + 1], q[(moved.Bone * 4) + 2], q[(moved.Bone * 4) + 3]) = moved.Rotation;
            }
        }
    }

    private static (float X, float Y, float Z, float W) Align((float X, float Y, float Z, float W) p, (float X, float Y, float Z, float W) q)
    {
        float apart = ((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)) + ((p.Z - q.Z) * (p.Z - q.Z)) + ((p.W - q.W) * (p.W - q.W));
        float together = ((p.X + q.X) * (p.X + q.X)) + ((p.Y + q.Y) * (p.Y + q.Y)) + ((p.Z + q.Z) * (p.Z + q.Z)) + ((p.W + q.W) * (p.W + q.W));
        return apart > together ? (-q.X, -q.Y, -q.Z, -q.W) : q;
    }

    private static (float X, float Y, float Z, float W) Normalize((float X, float Y, float Z, float W) q)
    {
        float length = MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z) + (q.W * q.W));
        return length > 0f ? (q.X / length, q.Y / length, q.Z / length, q.W / length) : q;
    }
}
