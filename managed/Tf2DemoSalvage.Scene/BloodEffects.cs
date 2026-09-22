using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One particle system a blood event starts: which, and control point 0 — its origin and orientation.</summary>
/// <param name="System">The system's name.</param>
/// <param name="Point">Control point 0; `DispatchParticleEffect` sets control point 1 to the same origin.</param>
public readonly record struct BloodBurst(string System, ParticleControlPoint Point);

/// <summary>`TFBloodSprayCallback` (`tf_fx_blood.cpp`): the impact puff and the spray a hit player throws (B415).</summary>
/// <remarks>
/// <code>
/// DispatchParticleEffect( "blood_impact_red_01", origin, VectorAngles( −normal ), player )
/// distance = | origin − MainViewOrigin() |,   lod = 0.25 · distance / 512
/// if | normal · view forward | > 0.5:       // spraying along the view — turn it aside
///     push = RandomFloat( 0.5, 1.5 ) + lod
///     right when ( distance ≥ 512 and normal · view right > 0 ) or ( distance &lt; 512 and RandomFloat( 0, 1 ) > 0.5 ),
///     otherwise left: normal ± view right · push
/// DispatchParticleEffect( distance &lt; 400 ? "blood_spray_red_01" : "blood_spray_red_01_far", origin,
///                         VectorAngles( normal ), player )
/// </code>
///
/// **Not built:** the underwater variants (the bleeding player's `WL_Eyes`, which a demo does not send for another
/// player), the birthday and Pyrovision systems (a holiday and a local item this viewer does not model), and low
/// violence. `right` and `up` are computed from the normal in the callback and never used — Valve's, not reproduced.
/// </remarks>
public static class BloodEffects
{
    /// <summary>The impact puff.</summary>
    public const string Impact = "blood_impact_red_01";

    /// <summary>The spray within 400 units of the view.</summary>
    public const string Spray = "blood_spray_red_01";

    /// <summary>The spray further away.</summary>
    public const string SprayFar = "blood_spray_red_01_far";

    /// <summary>Every system a blood event can start, for loading with the map.</summary>
    public static readonly string[] Systems = [Impact, Spray, SprayFar];

    /// <summary>The two systems one blood event starts, turned against the view it arrived under.</summary>
    /// <param name="blood">The event.</param>
    /// <param name="eye">`MainViewOrigin()`.</param>
    /// <param name="viewAngles">`MainViewAngles()`, degrees.</param>
    /// <param name="random">`RandomFloat`.</param>
    /// <returns>The impact and the spray.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is null.</exception>
    public static (BloodBurst Impact, BloodBurst Spray) For(
        SceneBlood blood, Vector3 eye, (float Pitch, float Yaw, float Roll) viewAngles, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Vector3 origin = new(blood.Origin.X, blood.Origin.Y, blood.Origin.Z);
        Vector3 normal = new(blood.Normal.X, blood.Normal.Y, blood.Normal.Z);

        BloodBurst impact = new(Impact, Point(origin, -normal));

        float distance = Vector3.Distance(origin, eye);
        float lod = 0.25f * (distance / 512f);
        (float fx, float fy, float fz) = AngleVectors.Forward(viewAngles.Pitch, viewAngles.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(viewAngles.Pitch, viewAngles.Yaw, viewAngles.Roll);
        Vector3 forward = new(fx, fy, fz);
        Vector3 right = new(rx, ry, rz);

        if (MathF.Abs(Vector3.Dot(normal, forward)) > 0.5f)
        {
            float push = random(0.5f, 1.5f) + lod;
            bool toRight = (distance >= 512f && Vector3.Dot(normal, right) > 0f) ||
                           (distance < 512f && random(0f, 1f) > 0.5f);

            normal += toRight ? right * push : right * -push;
        }

        return (impact, new BloodBurst(distance < 400f ? Spray : SprayFar, Point(origin, normal)));
    }

    /// <summary>Control point 0 at an origin, oriented by `AngleVectors( VectorAngles( direction ) )`.</summary>
    private static ParticleControlPoint Point(Vector3 origin, Vector3 direction)
    {
        (float pitch, float yaw, float roll) = AngleVectors.Angles(direction.X, direction.Y, direction.Z);
        (float fx, float fy, float fz) = AngleVectors.Forward(pitch, yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(pitch, yaw, roll);
        (float ux, float uy, float uz) = AngleVectors.Up(pitch, yaw, roll);

        return new ParticleControlPoint(origin, new Vector3(fx, fy, fz), new Vector3(rx, ry, rz), new Vector3(ux, uy, uz));
    }
}
