using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// A track's keyframes, stored as the moving half inline and the rest shared with the keyframe before (D217).
/// </summary>
/// <remarks>
/// **Measured before it was built** (`prop-churn`, f12): of 1,492,310 consecutive keyframe pairs, X and Y change on 71%,
/// Z and yaw on 43%, pitch on 23%, roll on 6% — and nothing else on more than 1.3%. A whole <see cref="ScenePose"/> is
/// 336 bytes, so storing one per keyframe held 856 MB of f12's timeline to repeat values that had not changed.
///
/// **What it hands back is the pose it was given, by value.** The shared half is reused only when every value in it is
/// equal — lists element by element — so a list reference can differ from the caller's while its contents cannot.
/// </remarks>
internal sealed class PoseKeyframes : IReadOnlyList<(int Tick, ScenePose Pose)>
{
    private readonly List<Stored> _stored = [];

    /// <inheritdoc/>
    public int Count => _stored.Count;

    /// <inheritdoc/>
    public (int Tick, ScenePose Pose) this[int index]
    {
        get
        {
            Stored stored = _stored[index];

            return (stored.Tick, stored.Rest.Pose with
            {
                X = stored.X, Y = stored.Y, Z = stored.Z,
                Pitch = stored.Pitch, Yaw = stored.Yaw, Roll = stored.Roll,
            });
        }
    }

    /// <summary>The tick of one keyframe, without rebuilding its pose.</summary>
    /// <param name="index">Which keyframe.</param>
    /// <returns>Its tick.</returns>
    public int TickAt(int index) => _stored[index].Tick;

    /// <summary>Appends a keyframe, sharing the unmoving half with the one before when its values are all equal.</summary>
    /// <param name="tick">The keyframe's tick.</param>
    /// <param name="pose">The pose.</param>
    public void Add(int tick, ScenePose pose)
    {
        ScenePose rest = pose with { X = 0f, Y = 0f, Z = 0f, Pitch = 0f, Yaw = 0f, Roll = 0f };

        Shared shared = _stored.Count > 0 && SameValues(_stored[^1].Rest.Pose, rest)
            ? _stored[^1].Rest
            : new Shared(rest);

        _stored.Add(new Stored(tick, pose.X, pose.Y, pose.Z, pose.Pitch, pose.Yaw, pose.Roll, shared));
    }

    /// <summary>Whether two poses hold the same values, comparing their lists element by element.</summary>
    /// <param name="left">One pose.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when nothing differs but list identity.</returns>
    internal static bool SameValues(ScenePose left, ScenePose right) =>
        left.PoseParameters.SequenceEqual(right.PoseParameters)
        && left.Layers.SequenceEqual(right.Layers)
        && left.BoneControllers.SequenceEqual(right.BoneControllers)
        && (left.Gestures is null ? right.Gestures is null : right.Gestures is not null && left.Gestures.SequenceEqual(right.Gestures))
        && WithoutLists(left) == WithoutLists(right);

    private static ScenePose WithoutLists(ScenePose pose) =>
        pose with { PoseParameters = NoFloats, Layers = NoLayers, BoneControllers = NoFloats, Gestures = null };

    // One instance each, because the record compares lists by reference.
    private static readonly float[] NoFloats = [];

    private static readonly SceneAnimationLayer[] NoLayers = [];

    /// <inheritdoc/>
    public IEnumerator<(int Tick, ScenePose Pose)> GetEnumerator()
    {
        for (int index = 0; index < _stored.Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The half that rarely changes, held once for a run of keyframes.</summary>
    private sealed record Shared(ScenePose Pose);

    private readonly record struct Stored(int Tick, float X, float Y, float Z, float Pitch, float Yaw, float Roll, Shared Rest);
}
