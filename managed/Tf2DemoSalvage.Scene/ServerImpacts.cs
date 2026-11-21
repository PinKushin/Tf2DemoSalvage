using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>The server's `Impact` dispatches on the world — a sentry's bullets, mostly — as impacts (B415).</summary>
/// <remarks>
/// The server ran `UTIL_ImpactTrace` itself and dispatched `"Impact"` with the trace's end, start, surfaceprop and
/// entity; the client's `ImpactCallback` then runs the same `Impact` a client bullet does. For the world with no hitbox:
///
/// <code>
/// traceExt = vecStart + dir · ( | vecOrigin − vecStart | + 8 )
/// AddDecal → AddBrushModelDecal: ClipRayToEntity( vecStart → traceExt, bloated · 1.1 ) — nothing hit, no decal,
///                                  and Impact returns false, so no effects
///                                  DecalShoot at vecOrigin
/// </code>
///
/// So each becomes a <see cref="ShotImpact"/> ending at `vecOrigin`, carrying that decal trace's surface and normal and
/// the dispatch's own surfaceprop. The world with a hitbox is a static prop (`physics.cpp:1314-1316`), which `Impact`
/// decals by `AddDecalToStaticProp( …, iHitbox − 1, … )` (`fx_impact.cpp:149-152`), so its impact carries
/// <see cref="ShotImpact.StaticProp"/> and takes the client bullet's prop path (B797); *interpolated:* the world trace's
/// first hit stands in for `AddDecalToStaticProp`'s trace against that one prop. An impact on an entity is
/// <see cref="OnEntities"/>'s.
/// </remarks>
public static class ServerImpacts
{
    /// <summary>Every server impact on an entity — a player, mostly — in tick order, with its index in the dispatch feed.</summary>
    /// <param name="dispatches">The dispatch feed, in tick order.</param>
    /// <param name="name">A dispatch's name by its index in the <c>EffectDispatch</c> table.</param>
    /// <returns>The dispatches, each beside its index.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)> OnEntities(
        IReadOnlyList<SceneEffectDispatch> dispatches, Func<int, string?> name)
    {
        ArgumentNullException.ThrowIfNull(dispatches);
        ArgumentNullException.ThrowIfNull(name);

        List<(int, SceneEffectDispatch)> found = [];

        for (int index = 0; index < dispatches.Count; index++)
        {
            SceneEffectDispatch dispatch = dispatches[index];

            if (dispatch.Entity > 0 && string.Equals(name(dispatch.Name), ImpactEffect, StringComparison.Ordinal))
            {
                found.Add((index, dispatch));
            }
        }

        return found;
    }

    /// <summary>`Impact`'s ray against an entity: from `vecStart` to 8 units past `vecOrigin`, bloated by 1.1 — `AddDecal`.</summary>
    /// <param name="dispatch">The impact.</param>
    /// <returns>The ray's start and its delta, or null when the shot has no length.</returns>
    public static (Vector3 Start, Vector3 Delta)? DecalRay(SceneEffectDispatch dispatch)
    {
        Vector3 start = new(dispatch.Start.X, dispatch.Start.Y, dispatch.Start.Z);
        Vector3 shot = new Vector3(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z) - start;
        float length = shot.Length();

        return length > 0f ? (start, shot / length * (length + 8f) * 1.1f) : null;
    }

    /// <summary>`ImpactCallback`'s gate: whether an `"Impact"` on an entity does anything at all (B796).</summary>
    /// <param name="damageType">The dispatch's `m_nDamageType` — for a bullet, the damage BITS, not a team.</param>
    /// <param name="team">The struck entity's `GetTeamNumber()`, null read as 0.</param>
    /// <param name="disguiseTeam">A disguised player's `GetDisguiseTeam()`; null for anyone else.</param>
    /// <returns>False where the callback plays no `Impact`, no ricochet and no impact sound.</returns>
    /// <remarks>
    /// <code>
    /// bImpact = ( m_nDamageType != GetTeamNumber() || GetTeamNumber() &lt; FIRST_GAME_TEAM )
    /// if ( m_nDamageType != GetTeamNumber() &amp;&amp; IsPlayer() &amp;&amp; InCond( TF_COND_DISGUISED ) &amp;&amp; GetDisguiseTeam() == m_nDamageType )
    ///     bImpact = false
    /// </code>
    /// (`tf_fx_impacts.cpp:35-49`). `DMG_BULLET` is 2, `TF_TEAM_RED`: exactly-`DMG_BULLET` bits on a RED entity are silent.
    /// </remarks>
    public static bool Impacts(int damageType, int? team, int? disguiseTeam)
    {
        int number = team ?? 0;

        return damageType == number ? number < FirstGameTeam : disguiseTeam != damageType;
    }

    /// <summary>The same for a struck player, his disguise asked only while he is disguised.</summary>
    /// <param name="damageType">The dispatch's `m_nDamageType`.</param>
    /// <param name="struck">The player as he stood at the impact.</param>
    /// <returns>Whether the impact does anything.</returns>
    public static bool Impacts(int damageType, ScenePlayer struck) =>
        Impacts(damageType, struck.Team, struck.Conditions.Has(PlayerConditions.Disguised) ? struck.DisguiseTeam : null);

    /// <summary>The same for a server impact on an entity, its team and disguise read at the impact's tick.</summary>
    /// <param name="impact">The dispatch.</param>
    /// <param name="timeline">The loaded demo.</param>
    /// <returns>Whether the impact does anything.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    /// <remarks>
    /// A player from the frame at the tick; anything else from its track alive then, whose team is the last it stated —
    /// *interpolated*: a non-player entity is taken never to change team mid-life.
    /// </remarks>
    public static bool Impacts(SceneEffectDispatch impact, DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        IReadOnlyList<ScenePlayer> players = timeline.PlayersAt(impact.Tick);

        for (int at = 0; at < players.Count; at++)
        {
            if (players[at].EntityIndex == impact.Entity)
            {
                return Impacts(impact.DamageType, players[at]);
            }
        }

        return Impacts(impact.DamageType, timeline.TrackFor(impact.Entity, impact.Tick)?.TeamNumber, null);
    }

    /// <summary>`FIRST_GAME_TEAM`: unassigned and spectator are below it.</summary>
    private const int FirstGameTeam = 2;

    /// <summary>The effect name `CBaseEntity::ImpactTrace` dispatches.</summary>
    public const string ImpactEffect = "Impact";

    /// <summary>Every server impact on the world's brushes.</summary>
    /// <param name="dispatches">The dispatch feed, in tick order.</param>
    /// <param name="name">A dispatch's name by its index in the <c>EffectDispatch</c> table.</param>
    /// <param name="trace">The world trace.</param>
    /// <param name="into">Added to, in tick order.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void From(
        IReadOnlyList<SceneEffectDispatch> dispatches,
        Func<int, string?> name,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), BspTrace> trace,
        ICollection<ShotImpact> into)
    {
        ArgumentNullException.ThrowIfNull(dispatches);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(into);

        for (int index = 0; index < dispatches.Count; index++)
        {
            SceneEffectDispatch dispatch = dispatches[index];

            // The world with a hitbox is a static prop, the hitbox its index plus one (B797).
            if (dispatch.Entity != 0 || !string.Equals(name(dispatch.Name), ImpactEffect, StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 start = new(dispatch.Start.X, dispatch.Start.Y, dispatch.Start.Z);
            Vector3 origin = new(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z);
            Vector3 shot = origin - start;
            float length = shot.Length();

            if (length <= 0f)
            {
                continue;
            }

            Vector3 extended = start + (shot / length * (length + 8f));
            Vector3 end = start + ((extended - start) * 1.1f);
            BspTrace hit = trace(dispatch.Start, (end.X, end.Y, end.Z));

            if (hit.Fraction >= 1f)
            {
                continue;
            }

            into.Add(new ShotImpact(
                -1 - index, 0, dispatch.Tick, -1, 0, dispatch.Start, dispatch.Origin, (end.X, end.Y, end.Z), hit.Texinfo,
                hit.Normal, dispatch.SurfaceProp, dispatch.DamageType)
            {
                StaticProp = dispatch.HitBox - 1,

                // The trace's own surface, which `PlayImpactSound` takes when the server named none (B797 review 1).
                StudioSurfaceProp = hit.StudioSurfaceProp,
            });
        }
    }
}
