using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// How a gibbed player comes apart — <c>CreatePlayerGibs</c>'s throw, per piece (B371).
/// </summary>
/// <remarks>
/// **The body is NOT drawn for a gib death, and that half is a divergence on its own.**
/// `CreateTFGibs` spawns the pieces and then removes the ragdoll — `EndFadeOut()`, or
/// `SetRenderMode( kRenderNone )` (`c_tf_player.cpp:1124-1133`). `m_bGib` was decoded here and read
/// by nothing, so a gibbed death drew a whole corpse standing where TF2 shows pieces.
///
/// **The throw, read out of the engine rather than tuned:**
///
/// <code>
/// // CreateTFGibs, c_tf_player.cpp:1106
/// Vector vecVelocity = m_vecForce + m_vecRagdollVelocity;
/// VectorNormalize( vecVelocity );
/// pPlayer->CreatePlayerGibs( origin, vecVelocity, m_vecForce.Length(), m_bBurning );
///
/// // CreatePlayerGibs, :7429
/// AngularImpulse angularImpulse( RandomFloat( 0.0f, 120.0f ), RandomFloat( 0.0f, 120.0f ), 0.0 );
/// Vector vecBreakVelocity = vecVelocity;
/// vecBreakVelocity.z += tf_playergib_forceup.GetFloat();
/// VectorNormalize( vecBreakVelocity );
/// vecBreakVelocity *= tf_playergib_force.GetFloat();
/// if ( flSpeed > tf_playergib_maxspeed.GetFloat() ) … scale to maxspeed …
///
/// // CreateGibsFromList, props_shared.cpp:1464-1479 — per piece
/// float flScale = VectorNormalize( objectVelocity );
/// objectVelocity.x += RandomFloat( -1.f, 1.0f );
/// objectVelocity.y += RandomFloat( -1.0f, 1.0f );
/// objectVelocity.z += RandomFloat( 0.0f, 1.0f );
/// VectorNormalize( objectVelocity );
/// objectVelocity *= flScale;
/// </code>
///
/// **Every piece leaves at the same SPEED and a different direction**, and the scatter is not
/// symmetric: x and y draw from −1..1 and z from 0..1, so a gib is never thrown downward by the
/// scatter itself. The angular impulse is drawn ONCE and shared by every piece, because
/// `CreatePlayerGibs` computes it before the loop.
///
/// **The three cvars are effectively constants in a shipped game.** All are `FCVAR_CHEAT` and
/// `FCVAR_DEVELOPMENTONLY`, which is *"Hidden in released products"* (`iconvar.h:40`) — so a player
/// cannot reach them and the defaults stand. Their registered NAMES carry a Valve typo worth
/// knowing if one is ever exposed: `tf_playersgib_force` and `tf_playersgib_forceup` have an extra
/// `s`, while `tf_playergib_maxspeed` does not (`c_tf_player.cpp:184-186`).
///
/// **The draws are keyed to the corpse rather than taken from a stream, which is D136's rule
/// already applied to the death animation.** `RandomFloat` reads a running stream a demo does not
/// record, so the engine's actual numbers are unreachable — what is reproducible is the
/// DISTRIBUTION, and keying on the corpse buys something the engine cannot do: scrubbing back over
/// a death shows the same gibs it showed going forward. See <see cref="RagdollDeath"/>, where the
/// owner's position on this is recorded.
/// </remarks>
public static class PlayerGibs
{
    /// <summary>Upward velocity added before the throw is normalised — <c>tf_playersgib_forceup</c>.</summary>
    public const float ForceUp = 1.0f;

    /// <summary>How hard a piece is thrown — <c>tf_playersgib_force</c>.</summary>
    public const float Force = 500f;

    /// <summary>The cap on that throw — <c>tf_playergib_maxspeed</c>.</summary>
    public const float MaximumSpeed = 400f;

    /// <summary>The greatest angular impulse a piece is given — <c>RandomFloat( 0, 120 )</c>.</summary>
    public const float MaximumSpin = 120f;

    /// <summary>The throw every piece of one corpse shares, before the per-piece scatter.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <returns>The capped break velocity, in Source units a second.</returns>
    /// <remarks>
    /// **`m_vecForce + m_vecRagdollVelocity`, normalised by the CALLER and again here.** The first
    /// normalise is `CreateTFGibs`', which throws the magnitude away — so how hard the kill hit
    /// changes the DIRECTION a body comes apart in and not how far the pieces fly. The distance is
    /// `tf_playersgib_force` alone.
    ///
    /// **A corpse with neither force nor velocity is thrown straight up**, because the `z += 1`
    /// happens after a zero vector has been normalised to zero — which is the engine's behaviour
    /// for a body that simply fell apart.
    /// </remarks>
    public static (float X, float Y, float Z) Throw(SceneRagdoll corpse)
    {
        (float x, float y, float z) = Sum(corpse);

        // `VectorNormalize( vecVelocity )` in CreateTFGibs, before the throw is sized.
        (x, y, z) = Unit(x, y, z);

        z += ForceUp;

        (x, y, z) = Unit(x, y, z);

        x *= Force;
        y *= Force;
        z *= Force;

        float speed = MathF.Sqrt((x * x) + (y * y) + (z * z));

        if (speed > MaximumSpeed)
        {
            float scale = MaximumSpeed / speed;

            x *= scale;
            y *= scale;
            z *= scale;
        }

        return (x, y, z);
    }

    /// <summary>One piece's own velocity — the shared throw, scattered.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <param name="piece">Which piece, so two of them scatter differently.</param>
    /// <returns>The piece's velocity, in Source units a second.</returns>
    /// <remarks>
    /// **The speed is preserved exactly and only the direction moves**, which is what the engine's
    /// normalise-scatter-normalise-rescale does. A piece thrown at the cap is still at the cap
    /// afterwards.
    /// </remarks>
    public static (float X, float Y, float Z) Velocity(SceneRagdoll corpse, int piece)
    {
        (float x, float y, float z) = Throw(corpse);

        float speed = MathF.Sqrt((x * x) + (y * y) + (z * z));

        (x, y, z) = Unit(x, y, z);

        (float sx, float sy, float sz) = Scatter(corpse, piece);

        x += sx;
        y += sy;
        z += sz;

        (x, y, z) = Unit(x, y, z);

        return (x * speed, y * speed, z * speed);
    }

    /// <summary>What one piece adds to the throw's direction before it is renormalised.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <param name="piece">Which piece.</param>
    /// <returns>The three offsets the engine draws.</returns>
    /// <remarks>
    /// <code>
    /// objectVelocity.x += RandomFloat( -1.f, 1.0f );
    /// objectVelocity.y += RandomFloat( -1.0f, 1.0f );
    /// objectVelocity.z += RandomFloat( 0.0f, 1.0f );
    /// </code>
    ///
    /// **The asymmetry is the engine's and it is the thing worth asserting** — z draws from 0..1
    /// where x and y draw from −1..1, so the scatter can only ever tilt a piece UP.
    ///
    /// **Exposed because the property it carries is not observable from
    /// <see cref="Velocity"/>.** `Throw` has already added `tf_playersgib_forceup` and renormalised,
    /// so every piece leaves steeply upward whatever the scatter does — a test on the finished
    /// velocity passes with this range broken, which is exactly what a sabotage run showed. The
    /// range has to be asserted where it is drawn.
    /// </remarks>
    public static (float X, float Y, float Z) Scatter(SceneRagdoll corpse, int piece) =>
        (
            (Draw(corpse, piece, 0) * 2f) - 1f,
            (Draw(corpse, piece, 1) * 2f) - 1f,
            Draw(corpse, piece, 2));

    /// <summary>The angular impulse every piece of one corpse is given.</summary>
    /// <param name="corpse">The corpse.</param>
    /// <returns>Radians a second about each axis.</returns>
    /// <remarks>
    /// **Drawn once per corpse, not once per piece**, because `CreatePlayerGibs` builds it before
    /// the list is walked and hands the same `breakablepropparams_t` to every one. **Zero about z**
    /// in the engine's own literal.
    /// </remarks>
    public static (float X, float Y, float Z) Spin(SceneRagdoll corpse) =>
        (Draw(corpse, -1, 0) * MaximumSpin, Draw(corpse, -1, 1) * MaximumSpin, 0f);

    /// <summary><c>m_vecForce + m_vecRagdollVelocity</c>, with either half absent treated as zero.</summary>
    private static (float X, float Y, float Z) Sum(SceneRagdoll corpse)
    {
        (float fx, float fy, float fz) = corpse.Force ?? (0f, 0f, 0f);
        (float vx, float vy, float vz) = corpse.Velocity ?? (0f, 0f, 0f);

        return (fx + vx, fy + vy, fz + vz);
    }

    /// <summary>Normalises, answering zero for a zero vector as <c>VectorNormalize</c> does.</summary>
    private static (float X, float Y, float Z) Unit(float x, float y, float z)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));

        return length > 0f ? (x / length, y / length, z / length) : (0f, 0f, 0f);
    }

    /// <summary>One draw in [0, 1), standing in for <c>RandomFloat</c>.</summary>
    /// <remarks>
    /// **The same shape as <see cref="RagdollDeath"/>'s, with two more keys.** It has to be
    /// recomputable from the corpse alone at any tick in any order — a viewer may draw tick 50,000
    /// before tick 10 — so it is a hash and not a sequence generator. The piece and the channel
    /// join the slot and serial so that one corpse's nine pieces scatter independently and a
    /// reused entity index does not repeat another corpse's throw.
    /// </remarks>
    private static float Draw(SceneRagdoll corpse, int piece, int channel)
    {
        uint mixed = (uint)(
            (corpse.EntityIndex * 73856093) ^
            (corpse.Serial * 19349663) ^
            (piece * 83492791) ^
            (channel * 2971215073));

        mixed ^= mixed >> 16;
        mixed *= 2246822519u;
        mixed ^= mixed >> 13;
        mixed *= 3266489917u;
        mixed ^= mixed >> 16;

        return mixed / (float)uint.MaxValue;
    }
}
