using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>A player a bullet could strike: alive, not dormant, not the shooter.</summary>
/// <param name="Entity">Their entity index.</param>
/// <param name="Origin">`GetAbsOrigin()`, at their feet.</param>
/// <param name="Ducked">`FL_DUCKING`, which lowers their collision hull and so their `WorldSpaceCenter`.</param>
public readonly record struct BulletTarget(int Entity, Vector3 Origin, bool Ducked);

/// <summary>Where a bullet stops once players are counted — TF2's <c>UTIL_PlayerBulletTrace</c> (B415).</summary>
/// <remarks>
/// `tf_player_shared.cpp:10240`, in its own order:
///
/// <code>
/// UTIL_TraceLine( start, end, mask | CONTENTS_HITBOX, filter, trace );
/// if ( !trace-&gt;startsolid &amp;&amp; !trace-&gt;DidHitNonWorldEntity() )
/// {
///     UTIL_ClipTraceToPlayers( start, end + dir * 40, mask | CONTENTS_HITBOX, filter, &amp;playerClipTrace );
///     if ( playerClipTrace.m_pEnt )
///         validation: UTIL_TraceLine( hit, ( origin.x, origin.y, hit.z ), mask, only that player )
///         if ( validation.m_pEnt == playerClipTrace.m_pEnt )  trace = playerClipTrace;
/// }
/// </code>
///
/// **The first trace's entity pass only reaches players the spatial partition finds along the ray CLIPPED to the
/// world**, and a TF2 player's partition box is the standing hull whatever their stance —
/// `SetSurroundingBoundsType( USE_SPECIFIED_BOUNDS, VEC_HULL_MIN, VEC_HULL_MAX )`, `tf_player.cpp:3743`, "helps with
/// parts of the hitboxes that extend out of the crouching hitbox". A hitbox outside that box is reached only by the
/// second pass, which is the whole of Josh's fix: *"It's unfair if a player is poking through a thin wall and gets
/// shot!"*
///
/// **`UTIL_ClipTraceToPlayers` compares fractions of two different rays**: the candidate's is along the ray extended
/// by 40 units and the trace it must beat is along the original. Reproduced as Valve has it.
///
/// **A world trace here answers a fraction and no entity**, so the validation is decided by geometry: it succeeds
/// when the world lets it reach the player's collision hull, which is the first thing a trace towards their origin
/// meets. *Interpolated:* that nothing else — a building, a prop — stands between; the world sweep sees neither.
/// </remarks>
public static class PlayerBulletTrace
{
    /// <summary>`rayExtension`, how far past the end the player pass looks.</summary>
    private const float Extension = 40f;

    /// <summary>`maxRange` in `UTIL_ClipTraceToPlayers`.</summary>
    private const float MaximumRange = 60f;

    /// <summary>`VEC_HULL_MIN` / `MAX` for TF2, and the ducked top — `g_TFViewVectors`.</summary>
    private static readonly Vector3 HullMin = new(-24f, -24f, 0f);
    private static readonly Vector3 HullMax = new(24f, 24f, 82f);
    private static readonly Vector3 DuckHullMax = new(24f, 24f, 62f);

    /// <summary>Where the bullet stops, and whom it struck.</summary>
    /// <param name="start">`vecStart`, the bullet's origin.</param>
    /// <param name="end">`vecEnd`, the origin plus the whole range.</param>
    /// <param name="worldFraction">How far along start→end the world let it get.</param>
    /// <param name="players">Everyone it could strike.</param>
    /// <param name="hitboxes">A player's posed hitboxes against a ray (start, delta): the fraction, or null.</param>
    /// <param name="world">The world alone between two points: the fraction, 1 when clear.</param>
    /// <returns>`trace.endpos`, and the player struck or null.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static (Vector3 End, int? Struck) Clip(
        Vector3 start,
        Vector3 end,
        float worldFraction,
        IReadOnlyList<BulletTarget> players,
        Func<int, Vector3, Vector3, float?> hitboxes,
        Func<Vector3, Vector3, float> world)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(hitboxes);
        ArgumentNullException.ThrowIfNull(world);

        Vector3 delta = end - start;
        Vector3 clipped = start + (delta * worldFraction);

        float best = worldFraction;
        int? struck = null;

        // The main trace: players whose partition box the world-clipped ray reaches, by their hitboxes.
        foreach (BulletTarget player in players)
        {
            if (SegmentHitsBox(start, clipped, player.Origin + HullMin, player.Origin + HullMax) is not null &&
                hitboxes(player.Entity, start, delta) is { } fraction && fraction < best)
            {
                best = fraction;
                struck = player.Entity;
            }
        }

        if (struck is not null)
        {
            return (start + (delta * best), struck);
        }

        // `UTIL_ClipTraceToPlayers`, down a ray 40 units longer.
        Vector3 direction = Vector3.Normalize(delta);
        Vector3 extendedEnd = end + (direction * Extension);
        Vector3 extended = extendedEnd - start;

        float smallest = worldFraction;
        BulletTarget? chosen = null;

        foreach (BulletTarget player in players)
        {
            float range = DistanceToRay(Centre(player), start, extendedEnd);

            if (range < 0f || range > MaximumRange)
            {
                continue;
            }

            if (hitboxes(player.Entity, start, extended) is { } fraction && fraction < smallest)
            {
                smallest = fraction;
                chosen = player;
            }
        }

        if (chosen is not { } target)
        {
            return (clipped, null);
        }

        Vector3 hit = start + (extended * smallest);
        Vector3 towards = new(target.Origin.X, target.Origin.Y, hit.Z);

        float reachesPlayer = SegmentHitsBox(hit, towards, target.Origin + HullMin, target.Origin + Top(target)) ?? 1f;

        return world(hit, towards) >= reachesPlayer ? (hit, target.Entity) : (clipped, null);
    }

    /// <summary>The top of a player's collision hull, ducked or not.</summary>
    private static Vector3 Top(BulletTarget player) => player.Ducked ? DuckHullMax : HullMax;

    /// <summary>`WorldSpaceCenter()`: the middle of the collision hull.</summary>
    private static Vector3 Centre(BulletTarget player) => player.Origin + ((HullMin + Top(player)) * 0.5f);

    /// <summary>`DistanceToRay` (`util_shared.h:399`): the distance to the segment, negated past either end.</summary>
    private static float DistanceToRay(Vector3 position, Vector3 rayStart, Vector3 rayEnd)
    {
        Vector3 to = position - rayStart;
        Vector3 direction = rayEnd - rayStart;
        float length = direction.Length();

        direction /= length;

        float along = Vector3.Dot(direction, to);

        if (along < 0f)
        {
            return -(position - rayStart).Length();
        }

        if (along > length)
        {
            return -(position - rayEnd).Length();
        }

        return (position - (rayStart + (direction * along))).Length();
    }

    /// <summary>Where a segment enters an axis-aligned box, as a fraction of it; 0 from inside; null when it misses.</summary>
    private static float? SegmentHitsBox(Vector3 from, Vector3 to, Vector3 min, Vector3 max)
    {
        Vector3 delta = to - from;
        float enter = 0f;
        float leave = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = At(from, axis);
            float step = At(delta, axis);
            float low = At(min, axis);
            float high = At(max, axis);

            if (step == 0f)
            {
                if (origin < low || origin > high)
                {
                    return null;
                }

                continue;
            }

            float first = (low - origin) / step;
            float second = (high - origin) / step;

            enter = MathF.Max(enter, MathF.Min(first, second));
            leave = MathF.Min(leave, MathF.Max(first, second));
        }

        return enter <= leave ? enter : null;
    }

    private static float At(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}
