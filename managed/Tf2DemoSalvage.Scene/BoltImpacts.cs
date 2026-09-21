using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>A crossbow bolt's impact on the world — `StickyBoltCallbackTF` (B415).</summary>
/// <remarks>
/// <code>
/// // c_tf_stickybolt.cpp:106 — StickRagdollNowTF( m_vOrigin, m_vNormal, … )
/// UTIL_TraceLine( origin − dir · 16, origin + dir · 64, MASK_SOLID_BRUSHONLY, … )
/// if ( tr.surface.flags &amp; SURF_SKY ) return
/// … pin the struck ragdoll's bone …
/// UTIL_ImpactTrace( &amp;tr, 0 )                          // nothing on a fraction of 1
/// CreateCrossbowBoltTF( origin, dir, m_fFlags, m_nColor )  // the arrow, a 30-second temp model
/// </code>
/// **The impact is a client trace against brushes**, so its surface is the traced texinfo's, as a hitscan bullet's is,
/// and no player is judged. *Not built:* the ragdoll pin and the arrow model.
/// </remarks>
public static class BoltImpacts
{
    /// <summary>The effect name `CTFProjectile_Arrow` dispatches.</summary>
    public const string BoltEffect = "TFBoltImpact";

    /// <summary>Where a bolt's `Shot` numbers start, below every server impact's `−1 − index`.</summary>
    private const int FirstShot = -1_000_000;

    /// <summary>Every bolt's impact on the world's brushes.</summary>
    /// <param name="dispatches">The dispatch feed, in tick order.</param>
    /// <param name="name">A dispatch's name by its index in the <c>EffectDispatch</c> table.</param>
    /// <param name="trace">The world trace against brushes.</param>
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

            if (!string.Equals(name(dispatch.Name), BoltEffect, StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 origin = new(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z);
            Vector3 direction = new(dispatch.Normal.X, dispatch.Normal.Y, dispatch.Normal.Z);
            Vector3 start = origin - (direction * 16f);
            Vector3 reach = origin + (direction * 64f);
            BspTrace hit = trace((start.X, start.Y, start.Z), (reach.X, reach.Y, reach.Z));

            if (hit.Fraction >= 1f)
            {
                continue;
            }

            Vector3 end = start + ((reach - start) * hit.Fraction);

            into.Add(new ShotImpact(
                FirstShot - index, 0, dispatch.Tick, -1, 0, (start.X, start.Y, start.Z), (end.X, end.Y, end.Z),
                (reach.X, reach.Y, reach.Z), hit.Texinfo, hit.Normal, BrushOnly: true));
        }
    }
}
