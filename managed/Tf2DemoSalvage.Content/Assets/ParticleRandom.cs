using System;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The draws a particle initializer makes — <c>CParticleCollection::RandomInt</c> (B373).
/// </summary>
/// <remarks>
/// **A TABLE indexed by the particle, not a stream of numbers, and that is the whole point.** The
/// engine holds 4,096 precomputed floats and indexes them
/// (<c>src/public/particles/particles.h:49</c>):
///
/// <code>
/// #define MAX_RANDOM_FLOATS 4096
/// #define RANDOM_FLOAT_MASK ( MAX_RANDOM_FLOATS - 1 )
///
/// float flRand = s_pRandomFloats[ ( m_nRandomSeed + nRandomSampleId ) &amp; RANDOM_FLOAT_MASK ];
/// flRand *= ( nMax + 1 - nMin );
/// int nRand = (int)flRand + nMin;                                  // particles.h:1779
/// </code>
///
/// **The sample id is the PARTICLE's id plus a per-operator offset**, which the four-wide overload
/// spells out — <c>s_pRandomFloats[ ( nOfs + ParticleID.m_nValue[0] ) &amp; RANDOM_FLOAT_MASK ]</c>
/// with <c>nOfs = m_nRandomSeed + nRandomSampleOffset</c> (`particles.h:1797-1804`). So a given
/// particle's draw is a pure function of its id: it does not depend on how many particles were born
/// before it, on frame rate, or on the order operators ran in.
///
/// **Which is why this project needs no seed argument to stay reproducible.** `docs/DECISIONS.md`
/// D136 asks that a replay of the same demo produce the same picture, and the reason the existing
/// code took a MIDPOINT instead of a draw was to honour that. It never had to: the engine's own
/// scheme is deterministic, so drawing Valve's way and replaying identically are the same thing. The
/// midpoint was a divergence bought for nothing — `rockettrail` declares `lifetime_min 0.8` and
/// `lifetime_max 1.2`, so every particle in a trail was living exactly 1.0 seconds where the engine
/// spreads them across a 0.4-second band.
///
/// **The one thing NOT established, flagged because it reads like a measurement otherwise:** the
/// CONTENTS of `s_pRandomFloats` are filled in code that ships only as a binary, so the table below
/// is this project's own — uniform on [0,1) and stable across runs, which is every property the
/// arithmetic above depends on, but not float-for-float Valve's. What that can change is which
/// particular puff got which lifetime, never the distribution or the reproducibility. Falsifiable by
/// disassembling `particles.lib`, which ships in the SDK
/// (`docs/memory/absent-from-the-sdk-is-not-unreadable.md`).
/// </remarks>
public static class ParticleRandom
{
    /// <summary>How many floats the table holds — <c>MAX_RANDOM_FLOATS</c>.</summary>
    public const int Floats = 4096;

    /// <summary>What an index is masked with — <c>RANDOM_FLOAT_MASK</c>.</summary>
    private const int Mask = Floats - 1;

    /// <summary>The table, built once.</summary>
    private static readonly float[] Table = Build();

    /// <summary>One float in [0,1) for a particle and a purpose.</summary>
    /// <param name="particle">The particle's own id — <c>PARTICLE_ID</c>.</param>
    /// <param name="offset">Which draw this is, so two initializers do not agree.</param>
    /// <returns>The float.</returns>
    /// <remarks>
    /// **The offset is what keeps two initializers independent.** Without it, `Lifetime Random` and
    /// `Sequence Random` would read the same table entry for one particle, and the longest-lived
    /// puffs would all play the same sequence — a correlation nobody would look for and everybody
    /// would see.
    /// </remarks>
    public static float Sample(int particle, int offset) =>
        Table[(offset + particle) & Mask];

    /// <summary>A float between two bounds — <c>CParticleCollection::RandomFloat</c>.</summary>
    /// <param name="particle">The particle's id.</param>
    /// <param name="offset">Which draw this is.</param>
    /// <param name="least">The lower bound.</param>
    /// <param name="most">The upper bound.</param>
    /// <returns>The value.</returns>
    public static float Between(int particle, int offset, float least, float most) =>
        (Sample(particle, offset) * (most - least)) + least;

    /// <summary>A whole number between two bounds — <c>CParticleCollection::RandomInt</c>.</summary>
    /// <param name="particle">The particle's id.</param>
    /// <param name="offset">Which draw this is.</param>
    /// <param name="least">The lower bound, included.</param>
    /// <param name="most">The upper bound, INCLUDED.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// **`nMax + 1 - nMin`, so the upper bound is inclusive** — `particles.h:1783`. Reading it as
    /// exclusive would mean `sequence_max = 3` never selected the fourth sequence, and a quarter of
    /// a sheet would go unused while everything still drew.
    /// </remarks>
    public static int Whole(int particle, int offset, int least, int most)
    {
        if (most < least)
        {
            return least;
        }

        int drawn = (int)(Sample(particle, offset) * (most + 1 - least)) + least;

        // The table is [0,1) so this cannot exceed `most`, but a table that ever contained 1.0
        // would silently hand out an index one past the end of a sequence array.
        return Math.Clamp(drawn, least, most);
    }

    /// <summary>A point inside the unit sphere, uniformly — <c>RandomVectorInUnitSphere</c>.</summary>
    /// <param name="particle">The particle's id.</param>
    /// <param name="offset">Which draw this is; the next two entries are taken as well.</param>
    /// <returns>The point, and its distance from the centre.</returns>
    /// <remarks>
    /// **Read from published source**, `src/mathlib/mathlib_base.cpp:4203`, with Valve's own
    /// citation — *Graphics Gems III*, "Nonuniform random point sets via warping":
    ///
    /// <code>
    /// float flPhi    = acos( 1 - 2 * u );
    /// float flTheta  = 2 * M_PI * v;
    /// float flRadius = powf( w, 1.0f / 3.0f );
    /// pVector->x = flRadius * flSinPhi * flCosTheta;   // y uses SinTheta, z is flCosPhi
    /// return flRadius;
    /// </code>
    ///
    /// **The cube root is what makes it uniform in VOLUME**, and it is the part a hand-rolled
    /// version gets wrong: three uniform components scaled to a radius bunch every particle toward
    /// the centre, so a puff spawns as a dense core with a thin halo instead of an even ball.
    ///
    /// **Three CONSECUTIVE table entries**, which is the collection's own convention for a vector:
    /// `RandomVector` reads `nBaseId`, `nBaseId + 1` and `nBaseId + 2` (`particles.h:1819`).
    /// </remarks>
    public static (Vector3 Point, float Radius) InUnitSphere(int particle, int offset)
    {
        float u = Sample(particle, offset);
        float v = Sample(particle, offset + 1);
        float w = Sample(particle, offset + 2);

        float phi = MathF.Acos(1f - (2f * u));
        float theta = 2f * MathF.PI * v;
        float radius = MathF.Cbrt(w);

        (float sinPhi, float cosPhi) = MathF.SinCos(phi);
        (float sinTheta, float cosTheta) = MathF.SinCos(theta);

        return (
            new Vector3(radius * sinPhi * cosTheta, radius * sinPhi * sinTheta, radius * cosPhi),
            radius);
    }

    /// <summary>Fills the table with numbers that are uniform, unpatterned and always the same.</summary>
    /// <remarks>
    /// **A named, fixed constant rather than <c>Random</c> with a seed**, because the values have to
    /// be identical on every machine and every framework version, and `Random`'s algorithm is not
    /// contractual. This is SplitMix64, which is stated in full where it is used and needs nothing
    /// from the platform.
    /// </remarks>
    private static float[] Build()
    {
        float[] table = new float[Floats];

        ulong state = 0x9E3779B97F4A7C15UL;

        for (int index = 0; index < table.Length; index++)
        {
            state += 0x9E3779B97F4A7C15UL;

            ulong mixed = state;

            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;

            // 24 bits, divided by 2^24, is exactly representable and strictly below one.
            table[index] = (mixed >> 40) / (float)(1 << 24);
        }

        return table;
    }
}
