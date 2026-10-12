using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One posed hitbox of the entity a control point follows — <c>ModelHitBoxInfo_t</c> (`particles.h:173`).</summary>
/// <param name="Min">`m_vecBoxMins`, in the bone's space.</param>
/// <param name="Max">`m_vecBoxMaxes`.</param>
/// <param name="Row0">The first row of `m_Transform`, the bone's 3×4 world matrix; its fourth entry is the translation.</param>
/// <param name="Row1">The second row.</param>
/// <param name="Row2">The third row.</param>
public readonly record struct ModelHitbox(Vector3 Min, Vector3 Max, Vector4 Row0, Vector4 Row1, Vector4 Row2)
{
    /// <summary>A box whose bone is unturned, at a world position.</summary>
    /// <param name="min">The box's least corner.</param>
    /// <param name="max">Its greatest.</param>
    /// <param name="origin">Where the bone is.</param>
    /// <returns>The box.</returns>
    public static ModelHitbox At(Vector3 min, Vector3 max, Vector3 origin) =>
        new(min, max, new Vector4(1f, 0f, 0f, origin.X), new Vector4(0f, 1f, 0f, origin.Y), new Vector4(0f, 0f, 1f, origin.Z));

    /// <summary>A box on a posed bone.</summary>
    /// <param name="min">The box's least corner.</param>
    /// <param name="max">Its greatest.</param>
    /// <param name="bone">The bone's world matrix, twelve floats in Valve's layout (translation in column 3).</param>
    /// <returns>The box.</returns>
    public static ModelHitbox On(Vector3 min, Vector3 max, ReadOnlySpan<float> bone) =>
        new(
            min,
            max,
            new Vector4(bone[0], bone[1], bone[2], bone[3]),
            new Vector4(bone[4], bone[5], bone[6], bone[7]),
            new Vector4(bone[8], bone[9], bone[10], bone[11]));

    /// <summary>`VectorTransform`: a point in the bone's space, in the world.</summary>
    /// <param name="local">The point.</param>
    /// <returns>Where it is.</returns>
    public Vector3 ToWorld(Vector3 local)
    {
        Vector4 point = new(local, 1f);

        return new Vector3(Vector4.Dot(Row0, point), Vector4.Dot(Row1, point), Vector4.Dot(Row2, point));
    }

    /// <summary>The same box with its bone somewhere else, unturned.</summary>
    /// <param name="by">How far it moved.</param>
    /// <returns>The moved box.</returns>
    public ModelHitbox Moved(Vector3 by) =>
        this with { Row0 = Row0 + new Vector4(0f, 0f, 0f, by.X), Row1 = Row1 + new Vector4(0f, 0f, 0f, by.Y), Row2 = Row2 + new Vector4(0f, 0f, 0f, by.Z) };

    /// <summary>The point a relative coordinate names: each axis lerped from <see cref="Min"/> to <see cref="Max"/>, then posed.</summary>
    /// <param name="relative">`HITBOX_RELATIVE_XYZ`.</param>
    /// <returns>The world position.</returns>
    public Vector3 Within(Vector3 relative) => ToWorld(Min + ((Max - Min) * relative));
}

/// <summary>
/// <c>Position on Model Random</c> — <c>C_INIT_CreateOnModel</c>: a particle born at a random point in one of the
/// hitboxes of the entity a control point follows (B538).
/// </summary>
/// <remarks>
/// **Read in `builtin_initializers.obj` and in published source**, both quoted in `ModelHitboxConformanceTests`: the
/// initializer hands the collection to `CParticleSystemQuery::GetRandomPointsOnControllingObjectHitBox`
/// (`particlesystemquery.cpp:187`) and writes what comes back to `XYZ`, `PREV_XYZ`, `HITBOX_RELATIVE_XYZ` and
/// `HITBOX_INDEX`.
///
/// **`hitbox scale` is unpacked as an INT into a member the call passes as a float** (the unpack table at
/// `0xfd420`: type 2 at +0x40, default "1.0"), so a declared 1 reaches the query as 1.4e-45: the range
/// <c>( 1 − scale, scale )</c> is ( 1, ~0 ) and each coordinate is one minus its draw. Reproduced, not corrected.
///
/// *Not built:* "force to be inside model", which tests each try with `ClipRayToEntity` (no effect TF2's overheal or
/// its beams run declares it); the model's scale on the box (`GetModelScale()`, 1 for a player no spell has changed);
/// a brush model's render bounds.
/// </remarks>
public static class PositionOnModel
{
    /// <summary>The <c>functionName</c> a <c>.pcf</c> uses.</summary>
    public const string Named = "Position on Model Random";

    /// <summary>`RandomFloat( 0, 72 )`: the noise a try's goodness starts from.</summary>
    private const float GoodnessNoise = 72f;

    /// <summary>`IsZero( 0.0001 )`.</summary>
    private const float NoBias = 0.0001f;

    /// <summary>`MAX( nNumIters, 5 )` when a bias is declared.</summary>
    private const int BiasedTries = 5;

    /// <summary>Places one new particle.</summary>
    /// <param name="one">The declared initializer.</param>
    /// <param name="into">The collection.</param>
    /// <param name="index">The particle.</param>
    /// <param name="point">The control point it names, where it is NOW (`GetControlPointAtTime( cp, m_flCurTime )`).</param>
    /// <param name="boxes">That point's entity's posed hitboxes; empty when it follows none.</param>
    internal static void Place(
        ParticleFunction one, ParticleStore into, int index, ParticleControlPoint point, IReadOnlyList<ModelHitbox> boxes)
    {
        if (boxes.Count == 0)
        {
            // "don't have a model or am in editor or something - fill return with control point"
            into.Position[index] = point.At;
            into.Previous[index] = point.At;
            into.HitboxIndex[index] = 0;
            into.HitboxRelative[index] = Vector3.Zero;

            return;
        }

        // The int's bits, read as the float the query is handed.
        float most = BitConverter.Int32BitsToSingle((int)one.Number("hitbox scale", 1d));
        float least = 1f - most;
        Vector4 declared = one.Vector("direction bias", default);
        Vector3 bias = new(declared.X, declared.Y, declared.Z);
        int tries = (int)one.Number("force to be inside model", 0d);

        if (MathF.Abs(bias.X) > NoBias || MathF.Abs(bias.Y) > NoBias || MathF.Abs(bias.Z) > NoBias)
        {
            tries = Math.Max(tries, BiasedTries);
        }

        float best = -1.0e20f;
        int seed = into.Seed;

        // `do { } while ( nTryCnt-- )`: one more try than the count.
        for (int attempt = 0; attempt <= tries; attempt++)
        {
            int box = ParticleRandom.Whole(seed, into.Query(), 0, 0, boxes.Count - 1);
            Vector3 relative = new(
                ParticleRandom.Between(seed, into.Query(), 0, least, most),
                ParticleRandom.Between(seed, into.Query(), 0, least, most),
                ParticleRandom.Between(seed, into.Query(), 0, least, most));
            Vector3 world = boxes[box].Within(relative);
            float goodness = ParticleRandom.Between(seed, into.Query(), 0, 0f, GoodnessNoise) + Vector3.Dot(world - point.At, bias);

            if (goodness > best)
            {
                best = goodness;
                into.Position[index] = world;
                into.Previous[index] = world;
                into.HitboxIndex[index] = box;
                into.HitboxRelative[index] = relative;
            }
        }
    }
}

/// <summary>
/// <c>Movement Lock to Bone</c> — <c>C_OP_LockToBone</c>: each particle moves as the point of its hitbox it was born on
/// moves, fading out of the lock by its age in seconds (B538).
/// </summary>
/// <remarks>
/// Read in `builtin_particle_ops.obj` (`0x115d90`), quoted in `ModelHitboxConformanceTests`. **The fade is in SECONDS of
/// age, not fractions of a life**: TF2's overheal pluses live two seconds, ride the body for the first and are let go
/// across the second, which is the trail a running patient leaves.
/// </remarks>
public static class LockToBone
{
    /// <summary>The <c>functionName</c> a <c>.pcf</c> uses.</summary>
    public const string Named = "Movement Lock to Bone";

    /// <summary>Carries every particle with its hitbox.</summary>
    /// <param name="particles">The live collection.</param>
    /// <param name="parameters">The declared operator.</param>
    /// <param name="now">The control point's entity's hitboxes this step — `m_pHitBoxes`.</param>
    /// <param name="before">The same on the step before — `m_pPrevBoxes`.</param>
    /// <param name="strength">The operator's strength, `flStrength`.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Operate(
        ParticleStore particles,
        ParticleFunction parameters,
        IReadOnlyList<ModelHitbox> now,
        IReadOnlyList<ModelHitbox> before,
        float strength = 1f)
    {
        ArgumentNullException.ThrowIfNull(particles);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(before);

        // `CurAndPrevValid`: boxes, and as many as last time.
        if (now.Count == 0 || before.Count != now.Count)
        {
            return;
        }

        float start = (float)parameters.Number("lifetime fade start", 0d);
        float declaredEnd = (float)parameters.Number("lifetime fade end", 0d);
        float end = declaredEnd > 0f ? declaredEnd : float.MaxValue;
        float rate = start < declaredEnd ? 1f / (declaredEnd - start) : 0f;

        for (int index = 0; index < particles.Count; index++)
        {
            float age = particles.Age - particles.Born[index];
            int box = particles.HitboxIndex[index];

            if (age >= end || box < 0 || box >= now.Count)
            {
                continue;
            }

            Vector3 relative = particles.HitboxRelative[index];
            Vector3 delta = now[box].Within(relative) - before[box].Within(relative);

            if (start < age)
            {
                delta *= (1f - ((age - start) * rate)) * strength;
            }

            particles.Position[index] += delta;
            particles.Previous[index] += delta;
        }
    }
}
