using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>`PerformCustomEffects` (`fx_impact.cpp`): the debris, dust and sparks a bullet throws off a surface (B415).</summary>
/// <remarks>
/// **The legacy path, because `cl_new_impact_effects` is `"0"`** (`fx_impact.cpp:212`) and TF2 ships no config setting
/// it. After `Impact` has placed its decal:
///
/// <code>
/// surface flags SKY | NODRAW | HINT | SKIP  → nothing
/// concrete, tile, wood   → FX_DebrisFlecks       dirt, sand → FX_DustImpact
/// metal, vent            → FX_MetalSpark( reflect + random ±0.2 )     computer → Sparks( origin + normal )
/// </code>
///
/// `r_drawflecks` is `"1"` by default, so the flecks are drawn; the particle throttle is 1. **Not built:** the antlion
/// and warp-shield branches (no TF2 surface has either), and `CFleckParticles`' merging of nearby fleck emitters, which
/// re-runs an older emitter's collision setup at the newer impact.
/// </remarks>
public static class ImpactEffects
{
    /// <summary>`g_Mat_Fleck_Cement`.</summary>
    public static readonly string[] FleckCement = ["effects/fleck_cement1", "effects/fleck_cement2"];

    /// <summary>`g_Mat_Fleck_Wood`.</summary>
    public static readonly string[] FleckWood = ["effects/fleck_wood1", "effects/fleck_wood2"];

    /// <summary>`g_Mat_DustPuff`.</summary>
    public static readonly string[] DustPuff = ["particle/particle_smokegrenade", "particle/particle_noisesphere"];

    /// <summary>`g_Mat_BloodPuff[0]`.</summary>
    public const string BloodPuff = "effects/blood";

    /// <summary>`g_Material_Spark`.</summary>
    public const string Spark = "effects/spark";

    /// <summary>The metal spark's glow quad.</summary>
    public const string YellowFlare = "effects/yellowflare";

    /// <summary>The electric spark's glow.</summary>
    public const string YellowFlareNoZ = "effects/yellowflare_noz";

    /// <summary>Every material these effects draw with, for loading with the map.</summary>
    public static readonly string[] Materials =
        [.. FleckCement, .. FleckWood, .. DustPuff, BloodPuff, Spark, YellowFlare, YellowFlareNoZ];

    /// <summary>`PerformCustomEffects( vecOrigin, tr, shotDir, iMaterial, 1 )`.</summary>
    /// <param name="gameMaterial">`iMaterial`, the struck surface's `game.material`.</param>
    /// <param name="flags">The struck surface's flags.</param>
    /// <param name="origin">`vecOrigin`, the trace's end.</param>
    /// <param name="normal">`tr.plane.normal`.</param>
    /// <param name="shotDirection">`vecShotDir`, normalised.</param>
    /// <param name="colour">`GetColorForSurface`, asked only by the effects that tint.</param>
    /// <param name="trace">The world trace, for the collision setup.</param>
    /// <param name="random">`random->RandomFloat`.</param>
    /// <returns>The effect, or null for a surface that throws nothing.</returns>
    /// <exception cref="ArgumentNullException">A delegate is null.</exception>
    public static ImpactEffect? Perform(
        char gameMaterial,
        SurfaceProperties flags,
        Vector3 origin,
        Vector3 normal,
        Vector3 shotDirection,
        Func<Vector3> colour,
        Func<Vector3, Vector3, BspTrace> trace,
        Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(colour);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(random);

        if ((flags & (SurfaceProperties.Sky | SurfaceProperties.NoDraw | SurfaceProperties.Hint | SurfaceProperties.Skip)) != 0)
        {
            return null;
        }

        Draw draw = new(random);

        switch (gameMaterial)
        {
            case 'C' or 'T':
                return DebrisFlecks(origin, normal, FleckCement, colour(), trace, draw);
            case 'W':
                return DebrisFlecks(origin, normal, FleckWood, colour(), trace, draw);
            case 'D' or 'N':
                return DustImpact(origin, normal, colour(), draw);
            case 'M' or 'V':
                Vector3 reflect = shotDirection + (normal * (Vector3.Dot(shotDirection, normal) * -2f));

                reflect += new Vector3(draw.Float(-0.2f, 0.2f), draw.Float(-0.2f, 0.2f), draw.Float(-0.2f, 0.2f));

                return MetalSpark(origin, reflect, normal, draw);
            case 'P':
                return ElectricSpark(origin + normal, 1, 1, null, trace, draw);
            default:
                return null;
        }
    }

    /// <summary>`FX_DebrisFlecks`, the PC version: flecks, a dust trail, grit and a capper.</summary>
    private static ImpactEffect DebrisFlecks(
        Vector3 origin, Vector3 normal, string[] flecks, Vector3 colour, Func<Vector3, Vector3, BspTrace> trace, Draw draw)
    {
        ImpactEffect effect = new();

        effect.Emitters.Add(Flecks(origin, normal, flecks, colour, trace, draw));

        ImpactEmitter dust = new(ImpactEmitterKind.Simple);
        Vector3 offset = origin + (normal * 2f);

        for (int i = 0; i < 2; i++)
        {
            Vector3 dir = normal + draw.Spread(0.8f);
            byte start = (byte)draw.Int(2, 4);
            Vector3 velocity = dir * (draw.Float(2f, 24f) * (i + 1));

            velocity.Z -= draw.Float(8f, 32f) * (i + 1);

            dust.Particles.Add(Puff(DustPuff[0], offset, velocity, 1f, start, (byte)(start * 8), (byte)draw.Int(100, 200), 0,
                draw.Float(0f, 360f), draw.Float(-1f, 1f), colour, draw.Float(0.5f, 1.25f)));
        }

        for (int i = 0; i < 4; i++)
        {
            float die = draw.Float(0.25f, 0.5f);
            Vector3 dir = normal + draw.Spread(0.8f);
            byte start = (byte)draw.Int(1, 4);
            Vector3 velocity = dir * draw.Float(8f, 32f);

            velocity.Z -= draw.Float(8f, 64f);

            dust.Particles.Add(Puff(BloodPuff, offset, velocity, die, start, (byte)(start * 4), 255, 0,
                draw.Float(0f, 360f), draw.Float(-2f, 2f), colour, draw.Float(0.5f, 1.25f)));
        }

        dust.Particles.Add(Capper(offset, normal, colour, draw));
        effect.Emitters.Add(dust);

        return effect;
    }

    /// <summary>`FX_DebrisFlecks`' bullet-hole capper: one slow puff over the hole.</summary>
    private static ImpactParticle Capper(Vector3 offset, Vector3 normal, Vector3 colour, Draw draw)
    {
        float die = draw.Float(1f, 1.5f);
        Vector3 dir = normal + draw.Spread(0.8f);
        byte start = (byte)draw.Int(4, 8);
        Vector3 velocity = dir * draw.Float(2f, 24f);

        velocity.Z = draw.Float(-2f, 2f);

        return Puff(DustPuff[0], offset, velocity, die, start, (byte)(start * 4f), (byte)draw.Int(100, 200), 0,
            draw.Float(0f, 360f), draw.Float(-2f, 2f), colour, draw.Float(0.5f, 1.25f));
    }

    /// <summary>`CreateFleckParticles`: four to sixteen flecks, bouncing, from a point one unit off the surface.</summary>
    private static ImpactEmitter Flecks(
        Vector3 origin, Vector3 normal, string[] flecks, Vector3 colour, Func<Vector3, Vector3, BspTrace> trace, Draw draw)
    {
        const float minimumSpeed = 64f;
        const float maximumSpeed = 128f;
        const float spray = 0.6f - 0.2f;

        // `trace->endpos + trace->plane.normal * 1.0`; the end is the origin.
        Vector3 spawn = origin + normal;
        ImpactEmitter emitter = new(ImpactEmitterKind.Fleck);

        // `FLECK_ANGULAR_SPRAY − iScale · 0.2`, at least 0.2; `FLECK_GRAVITY` 800, `FLECK_DAMPEN` 0.3.
        emitter.Collision.Setup(spawn, normal, minimumSpeed, maximumSpeed, 800f, 0.3f, trace);

        int count = (int)(0.5f + draw.Int(4, 16));

        for (int i = 0; i < count; i++)
        {
            string material = flecks[draw.Int(0, 1)];
            Vector3 dir = normal + draw.Spread(spray);
            byte size = (byte)draw.Int(1, 2);
            ImpactParticle fleck = new(material, spawn, dir * (draw.Float(minimumSpeed, maximumSpeed) * (3 - size)), 3f)
            {
                Size = size,
                Roll = draw.Float(0f, 360f),
                RollDelta = draw.Float(0f, 360f),
            };

            float ramp = draw.Float(0.75f, 1.25f);

            fleck.Colour = Ramp(colour, ramp);
            emitter.Particles.Add(fleck);
        }

        return emitter;
    }

    /// <summary>`FX_DustImpact`, the PC version: three kinds of four, from the origin itself.</summary>
    private static ImpactEffect DustImpact(Vector3 origin, Vector3 normal, Vector3 colour, Draw draw)
    {
        const float spread = 0.2f;

        ReadOnlySpan<int> ids = [3, 1, 2, 0];
        ImpactEmitter dust = new(ImpactEmitterKind.Dust);

        // `(int)( 0.50 + 4 )`, `(int)( 0.83 + 4 )`, `(int)( 0.17 + 4 )`: four of each at full throttle.
        for (int i = 0; i < 4; i++)
        {
            int id = ids[i];
            float die = draw.Float(0.5f, 1f);
            Vector3 velocity = Normalized(draw.Spread(spread) + (normal * draw.Float(1f, 6f))) * (draw.Float(250f, 500f) * id);
            byte start = (byte)(draw.Int(3, 4) * (id + 1));

            dust.Particles.Add(Puff(DustPuff[0], origin, velocity, die, start, (byte)(start * 4), (byte)draw.Int(32, 255), 0,
                draw.Int(0, 360), draw.Float(-8f, 8f), colour, draw.Float(0.75f, 1.25f)));
        }

        for (int i = 0; i < 4; i++)
        {
            int id = ids[i];
            float die = draw.Float(0.25f, 0.75f);
            Vector3 velocity = Normalized(draw.Spread(spread) + (normal * draw.Float(1f, 6f))) * (draw.Float(250f, 500f) * id);
            byte start = (byte)(draw.Int(2, 4) * (id + 1));

            dust.Particles.Add(Puff(BloodPuff, origin, velocity, die, start, (byte)(start * 2), 255, 0,
                draw.Int(0, 360), draw.Float(-2f, 2f), colour, draw.Float(0.75f, 1.25f)));
        }

        for (int i = 0; i < 4; i++)
        {
            // `offset` is computed here and never used: every particle starts at the origin.
            _ = draw.Float(-8f, 8f);
            _ = draw.Float(-8f, 8f);

            float die = draw.Float(0.5f, 1f);
            Vector3 velocity = Normalized(draw.Spread(1f) + normal) * draw.Float(0f, 50f);
            byte start = (byte)draw.Int(1, 4);

            dust.Particles.Add(Puff(DustPuff[0], origin, velocity, die, start, (byte)(start * 4), (byte)draw.Int(32, 64), 0,
                draw.Int(0, 360), draw.Float(-16f, 16f), colour, draw.Float(0.75f, 1.25f)));
        }

        ImpactEffect effect = new();

        effect.Emitters.Add(dust);

        return effect;
    }

    /// <summary>`FX_MetalSpark`: four to eight damped sparks along the reflection, and a glow on the surface.</summary>
    private static ImpactEffect MetalSpark(Vector3 position, Vector3 direction, Vector3 normal, Draw draw)
    {
        const float spread = 0.5f;
        const float length = 0.1f;

        Vector3 offset = position + normal;
        ImpactEmitter sparks = new(ImpactEmitterKind.Trail) { VelocityDampen = 8f };

        sparks.Collision.Gravity = 400f;
        sparks.Collision.Dampen = 0.25f;

        // `RandomInt( 4, 8 ) * ( iScale * 2 )`, then the throttle.
        int count = (int)(0.5f + (draw.Int(4, 8) * 2));

        for (int i = 0; i < count; i++)
        {
            // Every third spark flies far only in a big batch, `iScale > 1`; a bullet's is 1.
            float die = draw.Float(0.05f, 0.1f);
            float spreadOffset = draw.Float(0f, 2f);
            Vector3 dir = Normalized(direction + draw.Spread(spread * spreadOffset));
            float width = draw.Float(1f, 4f);
            float streak = draw.Float(length * 0.25f, length);
            float speed = draw.Float(128f * (2f - spreadOffset), 512f * (2f - spreadOffset));

            sparks.Particles.Add(new ImpactParticle(Spark, offset, dir * speed, die)
            {
                Width = width,
                Length = streak,
                Colour = (255, 255, 255),
            });
        }

        ImpactEffect effect = new();

        effect.Emitters.Add(sparks);

        float yaw = draw.Int(0, 360);
        int scale = draw.Int(24, 28);

        effect.Quads.Add(new ImpactQuad(YellowFlare, offset, normal, (scale, 0f), (1f, 0f), 0.1f, yaw));

        return effect;
    }

    /// <summary>`IEffects::Sparks` from a `CTESparks` — `FX_ElectricSpark( pos, nMagnitude, nTrailLength, pVecDir )`.</summary>
    /// <param name="position">`m_vecOrigin`.</param>
    /// <param name="magnitude">`m_nMagnitude`.</param>
    /// <param name="trailLength">`m_nTrailLength`.</param>
    /// <param name="direction">`m_vecDir`, or null for none.</param>
    /// <param name="trace">The world trace, for the collision setup.</param>
    /// <param name="random">`random->RandomFloat`.</param>
    /// <returns>The effect.</returns>
    public static ImpactEffect Sparks(
        Vector3 position,
        int magnitude,
        int trailLength,
        Vector3? direction,
        Func<Vector3, Vector3, BspTrace> trace,
        Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(random);

        return ElectricSpark(position, magnitude, trailLength, direction, trace, new Draw(random));
    }

    /// <summary>`IEffects::MetalSparks` and `::Ricochet` — `FX_MetalSpark( position, direction, direction )`.</summary>
    /// <param name="position">`m_vecPos`.</param>
    /// <param name="direction">`m_vecDir`, which is also the surface normal the glow faces.</param>
    /// <param name="random">`random->RandomFloat`.</param>
    /// <returns>The effect.</returns>
    public static ImpactEffect MetalSparks(Vector3 position, Vector3 direction, Func<float, float, float> random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return MetalSpark(position, direction, direction, new Draw(random));
    }

    /// <summary>`FX_ElectricSpark( pos, nMagnitude, nTrailLength, vecDir )` (`fx_sparks.cpp:300`).</summary>
    private static ImpactEffect ElectricSpark(
        Vector3 position, int magnitude, int trailLength, Vector3? direction, Func<Vector3, Vector3, BspTrace> trace, Draw draw)
    {
        ImpactEffect effect = new();
        ImpactEmitter big = new(ImpactEmitterKind.Trail) { Collides = true, VelocityDampen = 0f };

        // `Setup( pos, NULL, 0, 64, 300, 800, 0.3, VELOCITY_DAMPEN )` — the collide flag is forced by `Setup`, and
        // `m_flVelocityDampen` stays at its constructor's zero.
        big.Collision.Setup(position, null, 64f, 300f, 800f, 0.3f, trace);

        int count = (int)(magnitude * magnitude * draw.Float(2f, 4f));

        for (int i = 0; i < count; i++)
        {
            float die = magnitude * draw.Float(1f, 2f);
            Vector3 dir = new(draw.Float(-1f, 1f), draw.Float(-1f, 1f), draw.Float(-1f, 1f));

            dir.Z = draw.Float(0.5f, 1f);

            if (direction is { } lean)
            {
                dir = Normalized(dir + (2f * lean));
            }

            float width = draw.Float(2f, 5f);
            float streak = trailLength * draw.Float(0.02f, 0.05f);

            big.Particles.Add(new ImpactParticle(Spark, position, dir * draw.Float(64f, 300f), die)
            {
                Width = width,
                Length = streak,
                Colour = (255, 255, 255),
            });
        }

        effect.Emitters.Add(big);

        ImpactEmitter little = new(ImpactEmitterKind.Trail) { VelocityDampen = 0f };

        little.Collision.Gravity = 400f;

        count = magnitude * draw.Int(16, 32);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = new(draw.Float(-1f, 1f), draw.Float(-1f, 1f), draw.Float(-1f, 1f));

            if (direction is { } lean)
            {
                dir = Normalized(dir + lean);
            }

            float width = draw.Float(2f, 4f);
            float streak = trailLength * draw.Float(0.02f, 0.03f);
            float die = magnitude * draw.Float(0.1f, 0.2f);

            little.Particles.Add(new ImpactParticle(Spark, position, dir * draw.Float(128f, 256f), die)
            {
                Width = width,
                Length = streak,
                Colour = (255, 255, 255),
            });
        }

        effect.Emitters.Add(little);

        // The caps: a `CSimpleGlowEmitter` that dies 0.2 s after it starts. *Not built:* its pixel-visibility test.
        ImpactEmitter caps = new(ImpactEmitterKind.Simple) { Death = 0.2f };

        caps.Particles.Add(new ImpactParticle(YellowFlareNoZ, position, Vector3.Zero, 0.2f)
        {
            Colour = (255, 255, 255),
            StartAlpha = 255,
            EndAlpha = 255,
            StartSize = (byte)(magnitude * draw.Int(4, 8)),
            EndSize = 0,
            Roll = draw.Int(0, 360),
        });

        byte grey = (byte)draw.Int(32, 64);

        caps.Particles.Add(new ImpactParticle(YellowFlareNoZ, position, Vector3.Zero, 0.2f)
        {
            Colour = (grey, grey, grey),
            StartAlpha = grey,
            EndAlpha = 0,
            StartSize = (byte)(magnitude * draw.Int(32, 64)),
            EndSize = 0,
            Roll = draw.Int(0, 360),
            RollDelta = draw.Float(-1f, 1f),
        });

        Vector3 smoke = new(position.X + draw.Float(-4f, 4f), position.Y + draw.Float(-4f, 4f), position.Z);
        Vector3 rise = new(draw.Float(-16f, 16f), draw.Float(-16f, 16f), 16f);
        byte size = (byte)draw.Int(4, 8);

        caps.Particles.Add(new ImpactParticle(DustPuff[1], smoke, rise, 1f)
        {
            Colour = (255, 255, 200),
            StartAlpha = (byte)draw.Int(16, 32),
            EndAlpha = 0,
            StartSize = size,
            EndSize = (byte)(size * 4f),
            Roll = draw.Int(0, 360),
            RollDelta = draw.Float(-2f, 2f),
        });

        effect.Emitters.Add(caps);

        return effect;
    }

    /// <summary>A simple particle, its colour the surface's ramped.</summary>
    private static ImpactParticle Puff(
        string material, Vector3 at, Vector3 velocity, float die, byte startSize, byte endSize, byte startAlpha,
        byte endAlpha, float roll, float rollDelta, Vector3 colour, float ramp) =>
        new(material, at, velocity, die)
        {
            StartSize = startSize,
            EndSize = endSize,
            StartAlpha = startAlpha,
            EndAlpha = endAlpha,
            Roll = roll,
            RollDelta = rollDelta,
            Colour = Ramp(colour, ramp),
        };

    /// <summary>`MIN( 1, colour · ramp ) · 255`, truncated into a byte.</summary>
    private static (byte R, byte G, byte B) Ramp(Vector3 colour, float ramp) =>
        (
            (byte)(MathF.Min(1f, colour.X * ramp) * 255f),
            (byte)(MathF.Min(1f, colour.Y * ramp) * 255f),
            (byte)(MathF.Min(1f, colour.Z * ramp) * 255f));

    private static Vector3 Normalized(Vector3 value) =>
        value == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(value);

    /// <summary>`random->RandomFloat` and `RandomInt` over one draw.</summary>
    private readonly struct Draw(Func<float, float, float> random)
    {
        public float Float(float least, float most) => random(least, most);

        /// <summary>`RandomInt`, inclusive of both ends.</summary>
        public int Int(int least, int most) =>
            Math.Min(most, least + (int)random(0f, most - least + 1));

        /// <summary>A vector each of whose components is `RandomFloat( −spread, spread )`.</summary>
        public Vector3 Spread(float spread) =>
            new(random(-spread, spread), random(-spread, spread), random(-spread, spread));
    }
}
