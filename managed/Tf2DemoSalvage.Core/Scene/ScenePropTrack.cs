using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Where a model-bearing entity was, and what it was doing, at one moment.
/// </summary>
/// <remarks>
/// A struct because a match produces a great many of these and none of them is shared. Scale
/// defaults to 1 rather than 0 so a pose built from properties the demo never sent is drawn at its
/// authored size instead of vanishing.
/// </remarks>
public readonly record struct ScenePose
{
    /// <summary>World position, east.</summary>
    public float X { get; init; }

    /// <summary>World position, north.</summary>
    public float Y { get; init; }

    /// <summary>World position, up.</summary>
    public float Z { get; init; }

    /// <summary>Rotation about the side axis, in degrees.</summary>
    public float Pitch { get; init; }

    /// <summary>Rotation about the vertical axis, in degrees.</summary>
    public float Yaw { get; init; }

    /// <summary>Rotation about the forward axis, in degrees.</summary>
    public float Roll { get; init; }

    /// <summary>Size relative to the model as authored.</summary>
    public float Scale { get; init; } = 1f;

    /// <summary>Which animation is playing, or −1 when the entity does not animate.</summary>
    public int Sequence { get; init; } = -1;

    /// <summary>How far through that animation, from 0 to 1.</summary>
    public float Cycle { get; init; }

    /// <summary>Builds a pose at the world origin, unrotated and unanimated.</summary>
    public ScenePose()
    {
    }
}

/// <summary>
/// One entity's pose over the whole demo, stored as the moments it changed.
/// </summary>
/// <remarks>
/// **Keyframes rather than a pose per tick, and the arithmetic decided it.** A 1,600-second demo
/// is about 106,000 frames and a match carries a few hundred model-bearing entities, so a pose per
/// entity per frame is tens of millions of records — for a scene in which most of them never move.
/// A health pack that sits still all match costs one keyframe.
///
/// It also matches what a demo is. The stream sends only what changed, so the moments recorded
/// here are exactly the moments the demo spoke. Nothing is interpolated between them: a door that
/// opened at tick 900 was shut at 899, and inventing a position halfway would be this project
/// making up data it was not given.
/// </remarks>
public sealed class ScenePropTrack
{
    private readonly List<(int Tick, ScenePose Pose)> _keyframes = [];

    private int _endTick = int.MaxValue;

    /// <summary>Starts a track for one entity.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <param name="modelPath">The model this entity draws as.</param>
    public ScenePropTrack(int entityIndex, string modelPath)
    {
        EntityIndex = entityIndex;
        ModelPath = modelPath;
    }

    /// <summary>Slot in the entity table.</summary>
    public int EntityIndex { get; }

    /// <summary>The model this entity draws as.</summary>
    public string ModelPath { get; }

    /// <summary>How many moments the entity actually changed at.</summary>
    public int KeyframeCount => _keyframes.Count;

    /// <summary>The first tick this entity was seen at.</summary>
    public int FirstTick => _keyframes.Count > 0 ? _keyframes[0].Tick : int.MaxValue;

    /// <summary>Records a pose, if it differs from the one before it.</summary>
    /// <param name="tick">When the demo stated it.</param>
    /// <param name="pose">The pose.</param>
    /// <remarks>
    /// **Identical means the whole pose, not just the position.** An entity animating on the spot
    /// changes every frame while standing still, and comparing only position would freeze it.
    /// </remarks>
    public void Add(int tick, ScenePose pose)
    {
        if (_keyframes.Count > 0 && _keyframes[^1].Pose == pose)
        {
            return;
        }

        _keyframes.Add((tick, pose));
    }

    /// <summary>Records that the entity ceased to exist.</summary>
    /// <param name="tick">The first tick it was gone.</param>
    /// <remarks>
    /// Without this a picked-up health pack stays on the floor for the rest of the demo, and a
    /// rocket that hit a wall hangs there — a scene that gradually fills with rubbish, which reads
    /// as clutter rather than as a defect.
    /// </remarks>
    public void End(int tick) => _endTick = tick;

    /// <summary>The pose at a tick.</summary>
    /// <param name="tick">The tick to ask about.</param>
    /// <returns>The pose, or <c>null</c> when the entity did not exist then.</returns>
    /// <remarks>
    /// Binary search rather than a scan: a viewer asks this for every tracked entity on every
    /// frame, so a linear walk would be the whole cost of drawing.
    /// </remarks>
    public ScenePose? At(int tick)
    {
        if (_keyframes.Count == 0 || tick >= _endTick || tick < _keyframes[0].Tick)
        {
            return null;
        }

        int low = 0;
        int high = _keyframes.Count - 1;

        while (low < high)
        {
            // Rounded up, so the search moves towards the later keyframe and cannot stall on low.
            int middle = low + ((high - low + 1) / 2);

            if (_keyframes[middle].Tick <= tick)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _keyframes[low].Pose;
    }
}
