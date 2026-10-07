using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// <c>CCollisionProperty::WorldSpaceCenter</c> (`collisionproperty.h:380-414`): <c>OBBCenter()</c>, the lerp of
/// <c>m_vecMins</c> and <c>m_vecMaxs</c>, through <c>CollisionToWorldSpace</c> (B478).
/// </summary>
public static class WorldSpaceCenter
{
    /// <summary>An entity's world-space centre.</summary>
    /// <param name="placed">Its absolute origin and angles.</param>
    /// <param name="collision">Its collision property, or null when it sends none — the origin then.</param>
    /// <returns>The centre.</returns>
    /// <remarks>
    /// **Added when the box is world-aligned or the angles are zero, transformed otherwise**:
    /// <c>if ( !IsBoundsDefinedInEntitySpace() || ( GetCollisionAngles() == vec3_angle ) ) VectorAdd( … ) else
    /// VectorTransform( … )</c>.
    /// </remarks>
    public static Vector3 Of(ScenePose placed, SceneCollision? collision)
    {
        Vector3 origin = new(placed.X, placed.Y, placed.Z);

        if (collision is not { } box)
        {
            return origin;
        }

        Vector3 centre = Vector3.Lerp(
            new Vector3(box.Mins.X, box.Mins.Y, box.Mins.Z), new Vector3(box.Maxs.X, box.Maxs.Y, box.Maxs.Z), 0.5f);

        // `== vec3_angle`, exact as the engine compares.
        if (!box.BoundsInEntitySpace || placed is { Pitch: 0f, Yaw: 0f, Roll: 0f })
        {
            return origin + centre;
        }

        // `VectorTransform` with `AngleMatrix`'s columns: forward, left (= −right), up.
        (float fx, float fy, float fz) = AngleVectors.Forward(placed.Pitch, placed.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(placed.Pitch, placed.Yaw, placed.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(placed.Pitch, placed.Yaw, placed.Roll);

        return origin +
            (new Vector3(fx, fy, fz) * centre.X) -
            (new Vector3(rx, ry, rz) * centre.Y) +
            (new Vector3(ux, uy, uz) * centre.Z);
    }
}
