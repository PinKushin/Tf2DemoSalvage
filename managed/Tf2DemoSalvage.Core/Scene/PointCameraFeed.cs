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

/// <summary>Which camera <c>CViewRender::DrawMonitors</c> leaves in <c>_rt_Camera</c>, tick by tick (B511).</summary>
/// <remarks>
/// **Every active, non-dormant camera renders into the ONE target** (`viewrender.cpp:3240-3287`): the loop walks
/// <c>GetPointCameraList()</c>, skips <c>!IsActive() || IsDormant()</c>, and <c>DrawOneMonitor</c> clears colour and
/// depth and draws the scene into the same <c>GetCameraTexture()</c> for each. So the camera on screen is the LAST one
/// the walk reaches. The list is a <c>C_EntityClassList</c>, whose <c>Insert</c> PREPENDS (`cliententitylist.h:54-58`)
/// in the constructor (`c_point_camera.cpp:38-46`), so the walk runs newest-constructed first and the
/// earliest-constructed active camera is drawn last and wins.
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

    private readonly List<(int Tick, ScenePointCamera? Camera)> _samples = [];

    /// <summary>Every change of the monitor's camera, null when none draws, in tick order.</summary>
    public IReadOnlyList<(int Tick, ScenePointCamera? Camera)> Samples => _samples;

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

    /// <summary>Records the monitor's camera after a packet, on change.</summary>
    /// <param name="tick">The packet's tick.</param>
    /// <param name="entity">The client's entity at an index, or null.</param>
    /// <param name="dormant">Whether an entity is dormant at this tick.</param>
    public void Sample(int tick, Func<int, EntityState?> entity, Func<int, bool> dormant)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(dormant);

        ScenePointCamera? chosen = Choose(_constructed, entity, dormant);

        if (_samples.Count == 0 ? chosen is not null : _samples[^1].Camera != chosen)
        {
            _samples.Add((tick, chosen));
        }
    }

    /// <summary>The camera <c>DrawMonitors</c> leaves in the target.</summary>
    /// <param name="constructed">The cameras the client holds, oldest-constructed first.</param>
    /// <param name="entity">The client's entity at an index, or null.</param>
    /// <param name="dormant">Whether an entity is dormant.</param>
    /// <returns>The oldest active, non-dormant camera, or null.</returns>
    public static ScenePointCamera? Choose(
        IReadOnlyList<int> constructed, Func<int, EntityState?> entity, Func<int, bool> dormant)
    {
        ArgumentNullException.ThrowIfNull(constructed);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(dormant);

        foreach (int index in constructed)
        {
            if (!dormant(index) && entity(index)?.PointCamera() is { } camera)
            {
                return camera;
            }
        }

        return null;
    }

    /// <summary>The monitor's camera at a tick, or null.</summary>
    /// <param name="tick">The tick being drawn.</param>
    /// <returns>The last sample at or before the tick.</returns>
    public ScenePointCamera? At(int tick)
    {
        ScenePointCamera? found = null;

        foreach ((int at, ScenePointCamera? camera) in _samples)
        {
            if (at > tick)
            {
                break;
            }

            found = camera;
        }

        return found;
    }
}
