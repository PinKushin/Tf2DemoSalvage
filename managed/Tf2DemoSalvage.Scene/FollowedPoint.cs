using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where a control point that rides a player was on an earlier tick (B527).</summary>
/// <remarks>
/// **The engine never asks this**: it reaches a tick by playing to it, and `CParticleProperty::UpdateControlPoint`
/// (particle_property.cpp:560-640) reads each followed point from the entity as it then stood. A viewer that seeks
/// has only today's pose, so a particle effect met part way through is caught up along the path its points took.
///
/// **Interpolated, and stated as that.** The player's recorded origin and yaw are exact; the point is carried rigidly
/// with them. An attachment also moves with the skeleton — the aim pitch, the stride — and that part of its past is
/// not reconstructed: posing a player for every tick of a two-second replay costs a skeleton each. The error is the
/// attachment's swing inside the player, a few units against a path that moves hundreds.
/// </remarks>
public static class FollowedPoint
{
    /// <summary>The point as it was when its player stood somewhere else, facing another way.</summary>
    /// <param name="now">The point as this frame drew it.</param>
    /// <param name="drawn">Where this frame drew the player, which the point's lever arm is measured from.</param>
    /// <param name="recorded">The player's recorded origin on the tick being shown.</param>
    /// <param name="yaw">His recorded yaw on that tick, in degrees.</param>
    /// <param name="recordedThen">His recorded origin on the earlier tick.</param>
    /// <param name="yawThen">His recorded yaw on the earlier tick, in degrees.</param>
    /// <returns>The point carried back with him.</returns>
    public static ParticleControlPoint Then(
        ParticleControlPoint now, Vector3 drawn, Vector3 recorded, float yaw, Vector3 recordedThen, float yawThen)
    {
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (yawThen - yaw) * (MathF.PI / 180f));

        return new ParticleControlPoint(
            drawn + (recordedThen - recorded) + Vector3.Transform(now.At - drawn, turn),
            Vector3.Transform(now.Forward, turn),
            Vector3.Transform(now.Right, turn),
            Vector3.Transform(now.Up, turn));
    }
}
