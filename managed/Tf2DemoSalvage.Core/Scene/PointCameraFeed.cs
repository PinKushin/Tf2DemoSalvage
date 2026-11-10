using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The <c>point_camera</c> whose view is in <c>_rt_Camera</c> at a moment (B511).</summary>
/// <param name="EntityIndex">The camera entity.</param>
/// <param name="Origin">Its <c>GetAbsOrigin()</c>, the monitor view's origin.</param>
/// <param name="Angles">Its <c>GetAbsAngles()</c>, the monitor view's angles.</param>
/// <param name="FieldOfView"><c>m_FOV</c>, horizontal, in degrees.</param>
/// <param name="UseScreenAspectRatio"><c>m_bUseScreenAspectRatio</c>: the screen's aspect rather than 1.</param>
/// <param name="Fog">The camera's own fog when <c>m_bFogEnable</c>, or null for the view's (world) fog.</param>
public readonly record struct ScenePointCamera(
    int EntityIndex,
    (float X, float Y, float Z) Origin,
    (float Pitch, float Yaw, float Roll) Angles,
    float FieldOfView,
    bool UseScreenAspectRatio,
    SceneFog? Fog);

/// <summary>The cameras <c>CViewRender::DrawMonitors</c> renders, in its order, tick by tick (B511, B715).</summary>
/// <remarks>
/// **Every active, non-dormant camera renders into the ONE target** (`viewrender.cpp:3240-3287`): the loop walks
/// <c>GetPointCameraList()</c>, skips <c>!IsActive() || IsDormant()</c>, and <c>DrawOneMonitor</c> clears colour and
/// depth and draws the scene into the same <c>GetCameraTexture()</c> for each. So the camera on screen is the LAST one
/// the walk reaches, and every earlier one is still a whole view with its side effects (its under-water overlay, its
/// shadows, its queries). The list is a <c>C_EntityClassList</c>, whose <c>Insert</c> PREPENDS
/// (`cliententitylist.h:54-58`) in the constructor (`c_point_camera.cpp:38-46`), so the walk runs newest-constructed
/// first and the earliest-constructed active camera is drawn last and wins.
///
/// **Construction is the entity's creation on the client, not each PVS entry**: a camera that leaves becomes dormant
/// and is kept (<c>LeavePVS</c>), and only a delete ends it. The server sends a camera only to a player who can see
/// an <c>info_camera_link</c> target (<c>PointCameraSetupVisibility</c>, `info_camera_link.cpp`), so a camera
/// whose monitor is out of sight goes dormant and draws nothing.
/// </remarks>
public sealed class PointCameraFeed
{
    /// <summary>The camera's server class.</summary>
    public const string ClassName = "CPointCamera";

    /// <summary><c>DT_PointCamera</c>, its own table (`point_camera.cpp`, <c>IMPLEMENT_SERVERCLASS_ST</c>).</summary>
    public const string Table = "DT_PointCamera";

    /// <summary>The cameras the client holds, oldest-constructed first.</summary>
    private readonly List<int> _constructed = [];

    private readonly List<(int Tick, ScenePointCamera[] Cameras)> _samples = [];

    /// <summary>Every change of the cameras drawn, in tick order; each is in the walk's order.</summary>
    public IReadOnlyList<(int Tick, ScenePointCamera[] Cameras)> Samples => _samples;

    /// <summary>Notes a snapshot's update to one entity: a camera's creation or deletion.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <param name="className">Its class.</param>
    /// <param name="update">What the snapshot said.</param>
    public void Observe(int entityIndex, string className, EntityUpdateType update)
    {
        if (update == EntityUpdateType.Delete)
        {
            _constructed.Remove(entityIndex);
            return;
        }

        if (update == EntityUpdateType.Enter && className == ClassName && !_constructed.Contains(entityIndex))
        {
            _constructed.Add(entityIndex);
        }
    }

    /// <summary>Records the cameras drawn after a packet, on change.</summary>
    /// <param name="tick">The packet's tick.</param>
    /// <param name="entity">The client's entity at an index, or null.</param>
    /// <param name="dormant">Whether an entity is dormant at this tick.</param>
    public void Sample(int tick, Func<int, EntityState?> entity, Func<int, bool> dormant)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(dormant);

        ScenePointCamera[] walked = Walk(_constructed, entity, dormant);
        ScenePointCamera[] last = _samples.Count == 0 ? [] : _samples[^1].Cameras;

        if (!walked.AsSpan().SequenceEqual(last))
        {
            _samples.Add((tick, walked));
        }
    }

    /// <summary>The cameras <c>DrawMonitors</c> draws, in its order; the last is the one left in the target.</summary>
    /// <param name="constructed">The cameras the client holds, oldest-constructed first.</param>
    /// <param name="entity">The client's entity at an index, or null.</param>
    /// <param name="dormant">Whether an entity is dormant.</param>
    /// <returns>Every active, non-dormant camera, newest-constructed first.</returns>
    public static ScenePointCamera[] Walk(
        IReadOnlyList<int> constructed, Func<int, EntityState?> entity, Func<int, bool> dormant)
    {
        ArgumentNullException.ThrowIfNull(constructed);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(dormant);

        List<ScenePointCamera> walked = [];

        for (int i = constructed.Count - 1; i >= 0; i--)
        {
            if (!dormant(constructed[i]) && entity(constructed[i])?.PointCamera() is { } camera)
            {
                walked.Add(camera);
            }
        }

        return [.. walked];
    }

    /// <summary>The cameras drawn at a tick, in the walk's order.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The last sample at or before the tick; empty before the first.</returns>
    public IReadOnlyList<ScenePointCamera> At(int tick)
    {
        ScenePointCamera[] found = [];

        foreach ((int at, ScenePointCamera[] cameras) in _samples)
        {
            if (at > tick)
            {
                break;
            }

            found = cameras;
        }

        return found;
    }
}
