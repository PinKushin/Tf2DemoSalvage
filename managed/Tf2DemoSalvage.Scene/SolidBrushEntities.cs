using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A brush entity a trace meets: its model's head node, where it stands, and whether it has moved.</summary>
/// <param name="HeadNode">`dmodel_t::headnode`, the root of the entity's own subtree.</param>
/// <param name="Origin">`m_vecOrigin` at the tick: the model's geometry is compiled about it.</param>
public readonly record struct SolidBrush(int HeadNode, Vector3 Origin);

/// <summary>The brush entities a bullet can stop on, where each stands at a tick.</summary>
/// <remarks>
/// **Only `CBaseDoor`**, of the brush classes a TF2 demo networks: `C_FuncRespawnRoomVisualizer::ShouldCollide` refuses every
/// group but player movement (`c_func_respawnroom.cpp:69`, `func_respawnroom.cpp:494`), and `func_dust` is not solid. A door is
/// `SOLID_BSP`, so `CTraceFilterSimple` passes it and `CM_TransformedBoxTrace` walks its model from its own head node, the ray
/// moved into the model's frame by its origin (`cp_process`'s `*125` spans ±4 × ±80 × ±72 about its origin). *Not carried:*
/// a rotated brush entity's angles — a `func_door` slides and does not turn.
/// </remarks>
public sealed class SolidBrushEntities
{
    private const string DoorClass = "CBaseDoor";

    private readonly (ScenePropTrack Track, int HeadNode)[] _brushes;

    private SolidBrushEntities((ScenePropTrack, int)[] brushes) => _brushes = brushes;

    /// <summary>None, for a demo with no timeline.</summary>
    public static SolidBrushEntities None { get; } = new([]);

    /// <summary>How many brush entities a trace can meet over the demo.</summary>
    public int Count => _brushes.Length;

    /// <summary>The demo's doors, with their models' head nodes.</summary>
    /// <param name="props">Every prop track the timeline holds.</param>
    /// <param name="models">The map's models, by index.</param>
    /// <returns>The brush entities.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SolidBrushEntities From(IReadOnlyList<ScenePropTrack> props, IReadOnlyList<BspModel> models)
    {
        ArgumentNullException.ThrowIfNull(props);
        ArgumentNullException.ThrowIfNull(models);

        List<(ScenePropTrack, int)> brushes = [];

        foreach (ScenePropTrack track in props)
        {
            if (string.Equals(track.ClassName, DoorClass, StringComparison.Ordinal) &&
                track.ModelPath.Length > 1 && track.ModelPath[0] == '*' &&
                int.TryParse(track.ModelPath.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int model) &&
                model > 0 && model < models.Count)
            {
                brushes.Add((track, models[model].HeadNode));
            }
        }

        return new SolidBrushEntities([.. brushes]);
    }

    /// <summary>Where each brush entity stands at a tick, as the demo last stated it.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The ones alive then.</returns>
    public IReadOnlyList<SolidBrush> At(int tick)
    {
        List<SolidBrush> standing = [];

        foreach ((ScenePropTrack track, int headNode) in _brushes)
        {
            if (track.Alive(tick) && track.AtKeyframe(tick) is { } pose)
            {
                standing.Add(new SolidBrush(headNode, new Vector3(pose.X, pose.Y, pose.Z)));
            }
        }

        return standing;
    }
}
