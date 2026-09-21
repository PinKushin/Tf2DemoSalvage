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
/// the dispatch's own surfaceprop. *Not built:* an impact on an entity (a player's blood decal, a building's), and a
/// static prop's (the world with a hitbox).
/// </remarks>
public static class ServerImpacts
{
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

            if (dispatch.Entity != 0 || dispatch.HitBox != 0 ||
                !string.Equals(name(dispatch.Name), ImpactEffect, StringComparison.Ordinal))
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
                hit.Normal, dispatch.SurfaceProp, dispatch.DamageType));
        }
    }
}
