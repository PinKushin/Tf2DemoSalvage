using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Which of the client's legacy emitter classes a particle belongs to — each simulates and draws differently.</summary>
public enum ImpactEmitterKind
{
    /// <summary>`CSimpleEmitter`, and `AddSimpleParticle`'s shared one: straight flight, linear alpha and size.</summary>
    Simple,

    /// <summary>`CDustParticle`: velocity and roll that decay to a floor, and an alpha that ignores its start and end.</summary>
    Dust,

    /// <summary>`CFleckParticles`: falling and bouncing off the world, fading linearly, a fixed size.</summary>
    Fleck,

    /// <summary>`CTrailParticles`: a streak along its velocity, gravity and lateral damping, drawn as a tracer.</summary>
    Trail,
}

/// <summary>One legacy particle — `SimpleParticle`, `FleckParticle` and `TrailParticle` in one.</summary>
/// <param name="Material">What it draws with.</param>
/// <param name="Position">`m_Pos`.</param>
/// <param name="Velocity">`m_vecVelocity`.</param>
/// <param name="DieTime">`m_flDieTime`.</param>
public record struct ImpactParticle(string Material, Vector3 Position, Vector3 Velocity, float DieTime)
{
    /// <summary>`m_flLifetime`.</summary>
    public float Lifetime { get; set; }

    /// <summary>`m_flRoll`, in radians as `SinCos` takes it.</summary>
    public float Roll { get; set; }

    /// <summary>`m_flRollDelta`.</summary>
    public float RollDelta { get; set; }

    /// <summary>`m_uchColor`, 0 to 255.</summary>
    public (byte R, byte G, byte B) Colour { get; set; }

    /// <summary>`m_uchStartAlpha`.</summary>
    public byte StartAlpha { get; set; }

    /// <summary>`m_uchEndAlpha`.</summary>
    public byte EndAlpha { get; set; }

    /// <summary>`m_uchStartSize`.</summary>
    public byte StartSize { get; set; }

    /// <summary>`m_uchEndSize`.</summary>
    public byte EndSize { get; set; }

    /// <summary>A fleck's `m_uchSize`.</summary>
    public byte Size { get; set; }

    /// <summary>A trail's `m_flWidth`.</summary>
    public float Width { get; set; }

    /// <summary>A trail's `m_flLength`, in seconds of velocity.</summary>
    public float Length { get; set; }
}

/// <summary>`CParticleCollision` (`particle_collision.cpp`): the few planes a burst of particles can bounce off (B415).</summary>
/// <remarks>
/// **Planes found once, at setup, by simulating an average particle**: eight steps over two seconds along the burst's
/// direction and its right and left, the first surface on each becoming a plane. A particle moving through one of those
/// planes is then retraced against the real world (`__DEBUG_PARTICLE_COLLISION_RETEST` is on for the PC), and bounces
/// or settles by what that trace hit. A particle crossing no plane never traces at all, which is the point of it.
/// </remarks>
public sealed class ImpactParticleCollision
{
    /// <summary>`COLLISION_EPSILON`.</summary>
    private const float Epsilon = 0.01f;

    /// <summary>`NUM_DISCREET_STEPS`.</summary>
    private const int Steps = 8;

    /// <summary>`NUM_SIMULATION_SECONDS`.</summary>
    private const float Seconds = 2f;

    /// <summary>`MAX_COLLISION_PLANES`.</summary>
    private const int MaximumPlanes = 6;

    private readonly List<(Vector3 Normal, float Distance)> _planes = [];

    /// <summary>`m_flGravity`, 800 until set.</summary>
    public float Gravity { get; set; } = 800f;

    /// <summary>`m_flCollisionDampen`, 0.5 until set.</summary>
    public float Dampen { get; set; } = 0.5f;

    /// <summary>How many planes are active.</summary>
    public int PlaneCount => _planes.Count;

    /// <summary>`ClearActivePlanes`.</summary>
    public void ClearPlanes() => _planes.Clear();

    /// <summary>`CParticleCollision::Setup`: take the gravity and damping, and find the planes.</summary>
    /// <param name="origin">Where the burst starts.</param>
    /// <param name="direction">Its direction, or null for a point burst tested along all six axes.</param>
    /// <param name="minimumSpeed">The slowest particle.</param>
    /// <param name="maximumSpeed">The fastest.</param>
    /// <param name="gravity">`m_flGravity`.</param>
    /// <param name="dampen">`m_flCollisionDampen`.</param>
    /// <param name="trace">The world trace, brushes only.</param>
    /// <exception cref="ArgumentNullException"><paramref name="trace"/> is null.</exception>
    public void Setup(
        Vector3 origin, Vector3? direction, float minimumSpeed, float maximumSpeed, float gravity, float dampen,
        Func<Vector3, Vector3, BspTrace> trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Gravity = gravity;
        Dampen = dampen;
        _planes.Clear();

        float speed = (minimumSpeed + maximumSpeed) * 0.5f;

        if (direction is not { } dir)
        {
            foreach (Vector3 axis in (ReadOnlySpan<Vector3>)[Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ])
            {
                TestForPlane(origin, axis, speed, gravity, trace);
            }

            return;
        }

        // `VectorAngles( dir )` then `AngleVectors( … &vRight … )`: the right of a direction, whatever its roll.
        (float pitch, float yaw, _) = AngleVectors.Angles(dir.X, dir.Y, dir.Z);
        (float rx, float ry, float rz) = AngleVectors.Right(pitch, yaw, 0f);
        Vector3 right = new(rx, ry, rz);

        TestForPlane(origin, dir, speed, gravity, trace);
        TestForPlane(origin, right, speed, gravity, trace);
        TestForPlane(origin, -right, speed, gravity, trace);
    }

    /// <summary>`CParticleCollision::MoveParticle`: gravity, a move, and a bounce or a settle where a plane is crossed.</summary>
    /// <param name="position">`origin`, moved.</param>
    /// <param name="velocity">`velocity`, changed.</param>
    /// <param name="rollDelta">`rollDelta`, when the particle has one.</param>
    /// <param name="seconds">`timeDelta`.</param>
    /// <param name="trace">The world trace.</param>
    /// <param name="random">`random->RandomFloat`.</param>
    /// <returns>`trace.allsolid` of the retrace, when there was one.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public bool Move(
        ref Vector3 position,
        ref Vector3 velocity,
        ref float rollDelta,
        float seconds,
        Func<Vector3, Vector3, BspTrace> trace,
        Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(random);

        if (velocity == Vector3.Zero)
        {
            return false;
        }

        velocity.Z -= Gravity * seconds;

        Vector3 test = position + (velocity * seconds);

        if (_planes.Count > 0 && Crosses(position, test))
        {
            BspTrace hit = trace(position, test);

            if (hit.Fraction < 1f)
            {
                Vector3 normal = new(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);

                position += velocity * ((hit.Fraction - Epsilon) * seconds);

                if (normal.Z >= 0.5f && MathF.Abs(velocity.Z) <= 48f)
                {
                    // Settled: left at the collision point, stopped.
                    velocity = Vector3.Zero;
                    rollDelta = 0f;
                }
                else
                {
                    velocity += normal * (-Vector3.Dot(velocity, normal) * 2f);
                    velocity *= random(Dampen - 0.1f, Dampen + 0.1f);
                    rollDelta *= -0.25f;
                }

                return hit.AllSolid;
            }
        }

        position = test;

        return false;
    }

    /// <summary>`CBaseSimpleCollision::TraceLine`'s coarse half: whether the move crosses any active plane from its front.</summary>
    private bool Crosses(Vector3 start, Vector3 end)
    {
        foreach ((Vector3 normal, float distance) in _planes)
        {
            float before = Vector3.Dot(normal, start) - distance;
            float after = Vector3.Dot(normal, end) - distance;

            // Not from the plane's back, and across it. The coarse fraction, `t − 0.01`, is then never 1, so any
            // crossing sends the move to the real trace.
            if (before >= -Epsilon && (before > Epsilon) != (after > Epsilon))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>`TestForPlane`: step an average particle under gravity and take the first surface it meets.</summary>
    private void TestForPlane(Vector3 start, Vector3 direction, float speed, float gravity, Func<Vector3, Vector3, BspTrace> trace)
    {
        const float step = Seconds / Steps;

        Vector3 increment = direction * (speed * step);
        float fall = gravity * step;

        for (int each = 1; each <= Steps; each++)
        {
            Vector3 end = start + increment;

            end.Z -= fall * (0.5f * (step * each) * (step * each));

            BspTrace hit = trace(start, end);

            if (hit.Fraction < 1f)
            {
                ConsiderPlane(new Vector3(hit.Normal.X, hit.Normal.Y, hit.Normal.Z), hit.Distance);

                return;
            }

            start = end;
        }
    }

    /// <summary>`ConsiderPlane`: a new plane unless the same one is already active, up to six.</summary>
    private void ConsiderPlane(Vector3 normal, float distance)
    {
        // Exactly the same normal and distance, as `ConsiderPlane` compares.
        if (_planes.Contains((normal, distance)))
        {
            return;
        }

        if (_planes.Count < MaximumPlanes)
        {
            _planes.Add((normal, distance));
        }
    }
}
