using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// vphysics' `vcollisionevent_t` for one impact, as its collision listener fills it — the pre-collision half at
/// <c>FUN_180016b30</c> and the post-collision half at <c>FUN_180016aa0</c> (B172).
/// </summary>
/// <param name="FirstObject">`pObjects[0]`, the contact's first object.</param>
/// <param name="SecondObject">`pObjects[1]`.</param>
/// <param name="FirstMaterial">The contact's first material — what `surfaceProps[0]` is looked up from.</param>
/// <param name="SecondMaterial">`surfaceProps[1]`'s material.</param>
/// <param name="DeltaCollisionTime">
/// `deltaCollisionTime`: seconds since this pair last collided, from IVP's `d_time_since_last_collision`; a gap over 999
/// reads as 1 (`listener+0x6c`).
/// </param>
/// <param name="CollisionSpeed">
/// `collisionSpeed`, in inches a second: `|normal · relative velocity| × 39.370079` (`DAT_18011f004`) over the contact
/// record as IVP leaves it after the impact.
/// </param>
public readonly record struct IvpCollisionEvent(
    IvpCollisionObject? FirstObject,
    IvpCollisionObject? SecondObject,
    IIvpMaterial? FirstMaterial,
    IIvpMaterial? SecondMaterial,
    float DeltaCollisionTime,
    float CollisionSpeed)
{
    /// <summary>`DAT_18011f004`: 39.370079, inches to the metre.</summary>
    private const float InchesPerMetre = 39.370079f;

    /// <summary>`FUN_180016b30`'s ceiling on a pair's gap, past which it reads as one second.</summary>
    private const float LongestGap = 999f;

    /// <summary>The event for one impact's record.</summary>
    /// <param name="record">The contact record after the impact, its relative velocity restored.</param>
    /// <param name="sinceLast">IVP's `d_time_since_last_collision`.</param>
    /// <returns>The event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static IvpCollisionEvent From(IvpContactRecord record, float sinceLast)
    {
        ArgumentNullException.ThrowIfNull(record);

        (float X, float Y, float Z) normal = record.Normal;
        (float X, float Y, float Z) velocity = record.RelativeVelocity;

        // `ABS( n.y·v.y + n.x·v.x + n.z·v.z ) * DAT_18011f004`, in the decompiler's order.
        float speed = MathF.Abs((normal.Y * velocity.Y) + (normal.X * velocity.X) + (normal.Z * velocity.Z)) * InchesPerMetre;

        return new IvpCollisionEvent(
            record.FirstObject,
            record.SecondObject,
            record.FirstMaterial,
            record.SecondMaterial,
            sinceLast > LongestGap ? 1f : sinceLast,
            speed);
    }
}
