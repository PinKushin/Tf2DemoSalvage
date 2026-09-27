using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>The two traces <see cref="IdTargetTrace"/> asks for, answered from the world and the players.</summary>
/// <remarks>
/// `MASK_SOLID` stops on the world or a player's collision hull (`CONTENTS_SOLID` on the player's bounding box);
/// `MASK_SHOT` on the world or a player's hitboxes (`CONTENTS_HITBOX`), each tested only where the hull is reached, as
/// `ClipRayToEntity` does. The nearest hit wins; a player is always `IsPlayer`.
/// **Not modelled:** entities other than players — buildings, props and revive markers stop neither trace here.
/// </remarks>
/// <param name="players">Everyone the traces can hit: alive and present.</param>
/// <param name="teams">Each player's team by entity index.</param>
/// <param name="world">The world alone between two points: the fraction, 1 when clear.</param>
/// <param name="hitboxes">A player's posed hitboxes against a ray (start, delta): the fraction, 1 or more for a miss, or
/// null when they cannot be tested.</param>
public sealed class IdTargetTraces(
    IReadOnlyList<BulletTarget> players,
    IReadOnlyDictionary<int, int> teams,
    Func<Vector3, Vector3, float> world,
    Func<int, Vector3, Vector3, float?> hitboxes)
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

        if (struck is not { } hit)
        {
            return new IdTraceHit(null, false, 0, false, false);
        }

        return new IdTraceHit(hit.Entity, true, teams.GetValueOrDefault(hit.Entity), best <= 0f, true);
    }
}
