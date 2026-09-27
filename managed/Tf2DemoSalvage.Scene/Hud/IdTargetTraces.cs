using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>The two traces <see cref="IdTargetTrace"/> asks for, answered from the world and the players.</summary>
/// <remarks>
/// `MASK_SOLID` stops on the world or a player's collision hull (`CONTENTS_SOLID` on the player's bounding box);
/// `MASK_SHOT` on the world or a player's hitboxes (`CONTENTS_HITBOX`), each tested only where the hull is reached, as
/// `ClipRayToEntity` does. The nearest hit wins; a player is always `IsPlayer`.
/// A building is a `SOLID_BBOX` — its networked collision box, axis-aligned — whose `CONTENTS_SOLID` both masks stop on.
/// **Not modelled:** props, flags, dropped weapons and revive markers, which stop neither trace here.
/// </remarks>
/// <param name="players">Everyone the traces can hit: alive and present.</param>
/// <param name="teams">Each player's team by entity index.</param>
/// <param name="world">The world alone between two points: the fraction, 1 when clear.</param>
/// <param name="hitboxes">A player's posed hitboxes against a ray (start, delta): the fraction, 1 or more for a miss, or
/// null when they cannot be tested.</param>
/// <param name="boxes">The buildings' world boxes, or null for none.</param>
public sealed class IdTargetTraces(
    IReadOnlyList<BulletTarget> players,
    IReadOnlyDictionary<int, int> teams,
    Func<Vector3, Vector3, float> world,
    Func<int, Vector3, Vector3, float?> hitboxes,
    IReadOnlyList<IdTargetBox>? boxes = null)
{
    /// <summary>`UTIL_TraceLine( start, end, MASK_SOLID…, ignore )`.</summary>
    /// <param name="start">The ray's start.</param>
    /// <param name="end">The ray's end.</param>
    /// <param name="ignore">The entity the filter passes over.</param>
    /// <returns>What it hit.</returns>
    public IdTraceHit Solid(Vector3 start, Vector3 end, int ignore) =>
        Nearest(start, end, ignore, player => PlayerBulletTrace.ClipRayToHull(player, start, end));

    /// <summary>`UTIL_TraceLine( start, end, MASK_SHOT, ignore )`.</summary>
    /// <param name="start">The ray's start.</param>
    /// <param name="end">The ray's end.</param>
    /// <param name="ignore">The entity the filter passes over.</param>
    /// <returns>What it hit.</returns>
    public IdTraceHit Shot(Vector3 start, Vector3 end, int ignore) =>
        Nearest(start, end, ignore, player => PlayerBulletTrace.ClipRayToHull(player, start, end) is null
            ? null
            : PlayerBulletTrace.ClipRayToEntity(player, start, end - start, hitboxes));

    private IdTraceHit Nearest(Vector3 start, Vector3 end, int ignore, Func<BulletTarget, float?> clip)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(hitboxes);

        float best = world(start, end);
        BulletTarget? struck = null;

        foreach (BulletTarget player in players)
        {
            if (player.Entity != ignore && clip(player) is { } fraction && fraction < best)
            {
                best = fraction;
                struck = player;
            }
        }

        IdTargetBox? box = null;

        foreach (IdTargetBox candidate in boxes ?? [])
        {
            if (candidate.Entity != ignore && ClipRayToBox(candidate, start, end) is { } fraction && fraction < best)
            {
                best = fraction;
                box = candidate;
                struck = null;
            }
        }

        if (box is { } building)
        {
            return new IdTraceHit(building.Entity, false, building.Team, best <= 0f, true);
        }

        if (struck is not { } hit)
        {
            return new IdTraceHit(null, false, 0, false, false);
        }

        return new IdTraceHit(hit.Entity, true, teams.GetValueOrDefault(hit.Entity), best <= 0f, true);
    }

    /// <summary>A ray against an axis-aligned box by slabs: the entry fraction, 0 when it starts inside, null for a miss.</summary>
    private static float? ClipRayToBox(IdTargetBox box, Vector3 start, Vector3 end)
    {
        Vector3 delta = end - start;
        float enter = 0f;
        float leave = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float origin = start[axis];
            float step = delta[axis];
            float low = box.Mins[axis];
            float high = box.Maxs[axis];

            if (MathF.Abs(step) < 1e-9f)
            {
                if (origin < low || origin > high)
                {
                    return null;
                }

                continue;
            }

            float t0 = (low - origin) / step;
            float t1 = (high - origin) / step;

            (enter, leave) = (MathF.Max(enter, MathF.Min(t0, t1)), MathF.Min(leave, MathF.Max(t0, t1)));

            if (enter > leave)
            {
                return null;
            }
        }

        return enter;
    }
}

/// <summary>A building's world-space collision box for the ID traces: `GetAbsOrigin()` plus `m_Collision`'s mins and maxs.</summary>
/// <param name="Entity">Its entity index.</param>
/// <param name="Team">Its team.</param>
/// <param name="Mins">The box's low corner.</param>
/// <param name="Maxs">The box's high corner.</param>
public readonly record struct IdTargetBox(int Entity, int Team, Vector3 Mins, Vector3 Maxs);
