using System;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The live particles of one system, in the attributes Valve's own header names (B373).
/// </summary>
/// <remarks>
/// **The attribute set is READ FROM SOURCE, not invented**
/// (<c>src/public/particles/particles.h:62</c>). Only the ones the operators this project
/// implements actually touch are stored; the header declares thirty-two, and carrying all of them
/// would be inventing state nothing writes.
///
/// <code>
/// DEFPARTICLE_ATTRIBUTE( XYZ, 0 );            // required
/// DEFPARTICLE_ATTRIBUTE( LIFE_DURATION, 1 );  // particle lifetime (duration) of particle
/// DEFPARTICLE_ATTRIBUTE( PREV_XYZ, 2 );       // prev coordinates for verlet integration
/// DEFPARTICLE_ATTRIBUTE( RADIUS, 3 );
/// DEFPARTICLE_ATTRIBUTE( ROTATION, 4 );
/// DEFPARTICLE_ATTRIBUTE( TINT_RGB, 6 );
/// DEFPARTICLE_ATTRIBUTE( ALPHA, 7 );
/// DEFPARTICLE_ATTRIBUTE( CREATION_TIME, 8 );  // relative to particle system creation
/// </code>
///
/// **`PREV_XYZ` existing at all is what fixes the integrator**: the header says it is there "for
/// verlet integration", so a particle carries where it WAS rather than how fast it is going, and an
/// Euler integrator would be a different simulation wearing the same parameters.
///
/// **Parallel arrays rather than a particle struct**, which is the engine's own shape — it keeps
/// attributes in SIMD-aligned streams and an operator declares which it reads and writes
/// (<c>GetReadAttributes</c>, <c>GetWrittenAttributes</c>). An operator here touches one attribute
/// across every particle, for the same reason.
///
/// **The streams are internal rather than public**, so an operator in this assembly writes them
/// directly and nothing outside can resize one out from under the others — eight parallel arrays
/// have one invariant and it is that they are the same length.
/// </remarks>
public sealed class ParticleStore
{
    /// <summary>Where each particle is — <c>XYZ</c>.</summary>
    internal Vector3[] Position = new Vector3[Initial];

    /// <summary>Where each was last step — <c>PREV_XYZ</c>, for Verlet integration.</summary>
    internal Vector3[] Previous = new Vector3[Initial];

    /// <summary>How long each lives, in seconds — <c>LIFE_DURATION</c>.</summary>
    internal float[] Lifetime = new float[Initial];

    /// <summary>When each was born, relative to the system — <c>CREATION_TIME</c>.</summary>
    internal float[] Born = new float[Initial];

    /// <summary>Each particle's radius — <c>RADIUS</c>.</summary>
    internal float[] Radius = new float[Initial];

    /// <summary>Each particle's radius AT SPAWN, which is a separate attribute the engine reads.</summary>
    /// <remarks>
    /// **`GetReadInitialAttributes` is why this exists** — `particles.h:602`, *"Used when an
    /// operator needs to read the attributes of a particle at spawn time"*. An operator that scales
    /// a radius across a life must read the spawn value and WRITE the current one; reading the
    /// current one and writing it back compounds every step.
    ///
    /// **Measured, and it is why this field was added rather than assumed**: running the real
    /// `rockettrail` definition for twenty steps took a particle's radius to **265** because
    /// `Radius Scale` multiplied its own output. A one-step unit test cannot see that, and the
    /// end-to-end run over Valve's own file did
    /// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
    /// </remarks>
    internal float[] RadiusAtBirth = new float[Initial];

    /// <summary>Each particle's tint, 0..255 per channel — <c>TINT_RGB</c>.</summary>
    internal Vector3[] Tint = new Vector3[Initial];

    /// <summary>Each particle's tint at spawn, for the same reason as <see cref="RadiusAtBirth"/>.</summary>
    internal Vector3[] TintAtBirth = new Vector3[Initial];

    /// <summary>Each particle's alpha, 0..1 — <c>ALPHA</c>.</summary>
    internal float[] Alpha = new float[Initial];

    /// <summary>Each particle's rotation, in radians — <c>ROTATION</c>.</summary>
    internal float[] Rotation = new float[Initial];

    /// <summary>How many particles the streams start with.</summary>
    private const int Initial = 64;

    /// <summary>How many particles are alive.</summary>
    public int Count { get; private set; }

    /// <summary>How long the system has been running, in seconds.</summary>
    public float Age { get; private set; }

    /// <summary>Where one particle is.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>Its position.</returns>
    public Vector3 PositionOf(int index) => Position[index];

    /// <summary>How large one particle is.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>Its radius.</returns>
    public float RadiusOf(int index) => Radius[index];

    /// <summary>One particle's tint, 0..255 per channel.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>Its colour.</returns>
    public Vector3 TintOf(int index) => Tint[index];

    /// <summary>How opaque one particle is.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>Its alpha, 0..1.</returns>
    public float AlphaOf(int index) => Alpha[index];

    /// <summary>How long one particle lives.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>Its duration in seconds.</returns>
    public float LifetimeOf(int index) => Lifetime[index];

    /// <summary>Sets one particle's opacity.</summary>
    /// <param name="index">Which particle.</param>
    /// <param name="alpha">Its alpha, 0..1.</param>
    /// <remarks>
    /// **A writer, because the streams are internal and a caller outside this assembly cannot
    /// reach them.** An operator writes `Alpha` directly; anything else — a test placing a state,
    /// a renderer fading a system out — goes through here, so the streams keep their one invariant
    /// of being the same length.
    /// </remarks>
    public void Fade(int index, float alpha) => Alpha[index] = alpha;

    /// <summary>Adds one particle, with defaults an initializer then overwrites.</summary>
    /// <param name="at">Where it is born.</param>
    /// <param name="lives">How long it lives, in seconds.</param>
    /// <returns>Its index.</returns>
    /// <remarks>
    /// **Born with <c>Previous</c> equal to <c>Position</c>, which is zero velocity under Verlet.**
    /// An initializer that gives a particle speed does it by moving <c>Previous</c> backwards,
    /// which is the only way to express a velocity in a scheme that stores none.
    /// </remarks>
    public int Add(Vector3 at, float lives)
    {
        if (Count == Position.Length)
        {
            Grow();
        }

        Position[Count] = at;
        Previous[Count] = at;
        Lifetime[Count] = lives;
        Born[Count] = Age;
        Radius[Count] = 1f;
        RadiusAtBirth[Count] = 1f;
        Tint[Count] = new Vector3(255f, 255f, 255f);
        TintAtBirth[Count] = Tint[Count];
        Alpha[Count] = 1f;
        Rotation[Count] = 0f;

        return Count++;
    }

    /// <summary>How far through its life a particle is, 0 at birth and 1 at death.</summary>
    /// <param name="index">Which particle.</param>
    /// <returns>The fraction, which may exceed 1 for one due to be removed.</returns>
    /// <remarks>
    /// **Every fade operator is written against this fraction rather than against seconds**, which
    /// is why a fade parameter is a 0..1 number rather than a duration.
    /// </remarks>
    public float Through(int index) =>
        index < 0 || index >= Count || Lifetime[index] <= 0f
            ? 1f
            : (Age - Born[index]) / Lifetime[index];

    /// <summary>Advances the clock without moving anything.</summary>
    /// <param name="seconds">How long a step is.</param>
    public void Tick(float seconds) => Age += seconds;

    /// <summary>Removes every particle that has outlived its duration.</summary>
    /// <returns>How many were removed.</returns>
    /// <remarks>
    /// **Swap-and-drop, because nothing depends on the order of the streams**: an operator walks
    /// all of them and a renderer sorts by depth regardless.
    /// </remarks>
    public int Reap()
    {
        int removed = 0;

        for (int index = Count - 1; index >= 0; index--)
        {
            if (Age - Born[index] < Lifetime[index])
            {
                continue;
            }

            int last = --Count;

            Position[index] = Position[last];
            Previous[index] = Previous[last];
            Lifetime[index] = Lifetime[last];
            Born[index] = Born[last];
            Radius[index] = Radius[last];
            RadiusAtBirth[index] = RadiusAtBirth[last];
            Tint[index] = Tint[last];
            TintAtBirth[index] = TintAtBirth[last];
            Alpha[index] = Alpha[last];
            Rotation[index] = Rotation[last];

            removed++;
        }

        return removed;
    }

    /// <summary>Doubles every stream, keeping them the same length.</summary>
    private void Grow()
    {
        int size = Position.Length * 2;

        Array.Resize(ref Position, size);
        Array.Resize(ref Previous, size);
        Array.Resize(ref Lifetime, size);
        Array.Resize(ref Born, size);
        Array.Resize(ref Radius, size);
        Array.Resize(ref RadiusAtBirth, size);
        Array.Resize(ref Tint, size);
        Array.Resize(ref TintAtBirth, size);
        Array.Resize(ref Alpha, size);
        Array.Resize(ref Rotation, size);
    }
}
