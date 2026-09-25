using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>One legacy emitter's particles and the rules they move by — `CSimpleEmitter` and its subclasses (B415).</summary>
/// <param name="kind">Which class it is.</param>
public sealed class ImpactEmitter(ImpactEmitterKind kind)
{
    /// <summary>`WIND_ACCEL`'s neighbour: `CDustParticle`'s speed floor, 32 units a second on the PC.</summary>
    private const float DustSpeedFloor = 32f;

    /// <summary>`CDustParticle`'s roll floor on the PC.</summary>
    private const float DustRollFloor = 0.5f;

    /// <summary>Which class it is.</summary>
    public ImpactEmitterKind Kind { get; } = kind;

    /// <summary>The particles.</summary>
    public IList<ImpactParticle> Particles { get; } = new List<ImpactParticle>();

    /// <summary>`m_ParticleCollision`, for flecks and trails; a merged fleck emitter takes the newest burst's (<see cref="FleckMerge"/>).</summary>
    public ImpactParticleCollision Collision { get; internal set; } = new();

    /// <summary>A fleck emitter's bounding box's low corner — the binding's box, grown by every burst merged into it.</summary>
    public Vector3 Mins { get; set; }

    /// <summary>Its high corner.</summary>
    public Vector3 Maxs { get; set; }

    /// <summary>`bitsPARTICLE_TRAIL_COLLIDE`: a trail keeps its planes only with it.</summary>
    public bool Collides { get; set; }

    /// <summary>`bitsPARTICLE_TRAIL_VELOCITY_DAMPEN` with `m_flVelocityDampen`, or null.</summary>
    public float? VelocityDampen { get; set; }

    /// <summary>`CSimpleGlowEmitter`'s `m_flDeathTime` as an age: every particle goes when the emitter is this old.</summary>
    public float? Death { get; set; }

    /// <summary>How long the emitter has run.</summary>
    public float Age { get; private set; }

    /// <summary>Steps every particle — the kind's `SimulateParticles`.</summary>
    /// <param name="seconds">`timeDelta`.</param>
    /// <param name="trace">The world trace, for flecks and trails.</param>
    /// <param name="random">`random->RandomFloat`, for a bounce's damping.</param>
    public void Step(float seconds, Func<Vector3, Vector3, BspTrace> trace, Func<float, float, float> random)
    {
        Age += seconds;

        if (Death is { } death && Age > death)
        {
            Particles.Clear();
            return;
        }

        if (Kind == ImpactEmitterKind.Trail && !Collides)
        {
            Collision.ClearPlanes();
        }

        for (int each = Particles.Count - 1; each >= 0; each--)
        {
            ImpactParticle particle = Particles[each];

            bool alive = Kind switch
            {
                ImpactEmitterKind.Fleck => StepFleck(ref particle, seconds, trace, random),
                ImpactEmitterKind.Trail => StepTrail(ref particle, seconds, trace, random),
                _ => StepSimple(ref particle, seconds),
            };

            if (alive)
            {
                Particles[each] = particle;
            }
            else
            {
                Particles.RemoveAt(each);
            }
        }
    }

    /// <summary>`CSimpleEmitter::SimulateParticles`, with `CDustParticle`'s overrides for dust.</summary>
    private bool StepSimple(ref ImpactParticle particle, float seconds)
    {
        if (Kind == ImpactEmitterKind.Dust)
        {
            // `decay = exp( log( 0.0001 ) * dtime / 0.5 )`, and a speed below 32 is raised to 32 along the old heading.
            Vector3 before = particle.Velocity;
            Vector3 after = before * MathF.Exp(MathF.Log(0.0001f) * seconds / 0.5f);

            particle.Velocity = after.LengthSquared() < DustSpeedFloor * DustSpeedFloor
                ? Normalized(before) * DustSpeedFloor
                : after;
        }

        particle.Position += particle.Velocity * seconds;
        particle.Lifetime += seconds;
        particle.Roll += particle.RollDelta * seconds;

        if (Kind == ImpactEmitterKind.Dust)
        {
            float delta = particle.RollDelta + (particle.RollDelta * (seconds * -8f));

            if (MathF.Abs(delta) < DustRollFloor)
            {
                delta = delta > 0f ? DustRollFloor : -DustRollFloor;
            }

            particle.RollDelta = delta;
        }

        return particle.Lifetime < particle.DieTime;
    }

    /// <summary>`CFleckParticles::SimulateParticles`.</summary>
    private bool StepFleck(
        ref ImpactParticle particle, float seconds, Func<Vector3, Vector3, BspTrace> trace, Func<float, float, float> random)
    {
        particle.Lifetime += seconds;

        if (particle.Lifetime >= particle.DieTime)
        {
            return false;
        }

        particle.Roll += particle.RollDelta * seconds;

        Vector3 position = particle.Position;
        Vector3 velocity = particle.Velocity;
        float rollDelta = particle.RollDelta;

        if (Collision.Move(ref position, ref velocity, ref rollDelta, seconds, trace, random))
        {
            velocity = Vector3.Zero;
            rollDelta = 0f;
        }

        particle.Position = position;
        particle.Velocity = velocity;
        particle.RollDelta = rollDelta;

        return true;
    }

    /// <summary>`CTrailParticles::SimulateParticles`: no roll, so the collision is handed none.</summary>
    private bool StepTrail(
        ref ImpactParticle particle, float seconds, Func<Vector3, Vector3, BspTrace> trace, Func<float, float, float> random)
    {
        Vector3 position = particle.Position;
        Vector3 velocity = particle.Velocity;
        float noRoll = 0f;

        Collision.Move(ref position, ref velocity, ref noRoll, seconds, trace, random);

        if (VelocityDampen is { } dampen)
        {
            velocity *= MathF.Max(0f, 1f - (seconds * dampen));
        }

        particle.Position = position;
        particle.Velocity = velocity;
        particle.Lifetime += seconds;

        return particle.Lifetime < particle.DieTime;
    }

    private static Vector3 Normalized(Vector3 value) =>
        value == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(value);
}

/// <summary>`CFXQuad`: a flat quad on a surface, scaling and fading over its life.</summary>
/// <param name="Material">`m_pMaterial`.</param>
/// <param name="Origin">`m_vecOrigin`.</param>
/// <param name="Normal">`m_vecNormal`.</param>
/// <param name="Scale">`m_flStartScale`, `m_flEndScale`.</param>
/// <param name="Alpha">`m_flStartAlpha`, `m_flEndAlpha`.</param>
/// <param name="DieTime">`m_flDieTime`.</param>
/// <param name="Yaw">`m_flYaw`, in degrees.</param>
public sealed record ImpactQuad(
    string Material, Vector3 Origin, Vector3 Normal, (float Start, float End) Scale, (float Start, float End) Alpha,
    float DieTime, float Yaw)
{
    /// <summary>`m_flLifeTime`.</summary>
    public float Lifetime { get; set; }
}

/// <summary>Everything one bullet impact put into the world as particles: its emitters and its quads (B415).</summary>
public sealed class ImpactEffect
{
    /// <summary>The emitters.</summary>
    public IList<ImpactEmitter> Emitters { get; } = new List<ImpactEmitter>();

    /// <summary>The quads.</summary>
    public IList<ImpactQuad> Quads { get; } = new List<ImpactQuad>();

    /// <summary>Whether nothing is left to draw.</summary>
    public bool Finished
    {
        get
        {
            foreach (ImpactEmitter emitter in Emitters)
            {
                if (emitter.Particles.Count > 0)
                {
                    return false;
                }
            }

            foreach (ImpactQuad quad in Quads)
            {
                if (quad.Lifetime < quad.DieTime)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Steps every emitter and quad by one interval.</summary>
    /// <param name="seconds">The interval.</param>
    /// <param name="trace">The world trace.</param>
    /// <param name="random">The effect's draws.</param>
    public void Step(float seconds, Func<Vector3, Vector3, BspTrace> trace, Func<float, float, float> random)
    {
        foreach (ImpactEmitter emitter in Emitters)
        {
            emitter.Step(seconds, trace, random);
        }

        foreach (ImpactQuad quad in Quads)
        {
            quad.Lifetime += seconds;
        }
    }
}
