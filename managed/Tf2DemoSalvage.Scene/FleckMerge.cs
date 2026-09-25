using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// `g_FleckMergeList` (`fx_fleck.cpp:99-145`): a new `FX_DebrisFlecks` burst joins a live one near it rather than
/// starting its own emitter (B415).
/// </summary>
/// <remarks>
/// <code>
/// for ( pMerge = m_pHead; … )                       // newest first: AddParticleSystem puts each at the head
///     if same name:
///         box = pMerge's box ∪ ( center ± extents )   // extents ( 5, 5, 5 )
///         if |box.max − box.min| &lt; 120: pMerge's box = box; return pMerge
/// </code>
/// `CreateFleckParticles` then runs `m_ParticleCollision.Setup` at the new spawn on whichever emitter came back, so a
/// merged emitter's older flecks bounce off the newer impact's planes. An emitter is in the list while it lives, which is
/// while it holds flecks.
/// </remarks>
public static class FleckMerge
{
    /// <summary>`MAX_RADIUS_BBOX_MERGE`: "merge anything within 10 feet".</summary>
    private const float MergeRadius = 120f;

    /// <summary>Folds a new effect's flecks into the newest live fleck emitter in reach, if any.</summary>
    /// <param name="newer">The effect just spawned; its fleck emitter is removed when it joins another.</param>
    /// <param name="older">The effects already running, newest first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Join(ImpactEffect newer, IEnumerable<ImpactEffect> older)
    {
        ArgumentNullException.ThrowIfNull(newer);
        ArgumentNullException.ThrowIfNull(older);

        for (int each = newer.Emitters.Count - 1; each >= 0; each--)
        {
            ImpactEmitter burst = newer.Emitters[each];

            if (burst.Kind == ImpactEmitterKind.Fleck && Into(burst, older) is { } joined)
            {
                foreach (ImpactParticle fleck in burst.Particles)
                {
                    joined.Particles.Add(fleck);
                }

                joined.Collision = burst.Collision;
                newer.Emitters.RemoveAt(each);
            }
        }
    }

    /// <summary>The newest live fleck emitter whose box, grown by the burst's, stays under 120 across — its box grown.</summary>
    private static ImpactEmitter? Into(ImpactEmitter burst, IEnumerable<ImpactEffect> older)
    {
        foreach (ImpactEffect effect in older)
        {
            foreach (ImpactEmitter emitter in effect.Emitters)
            {
                if (emitter.Kind != ImpactEmitterKind.Fleck || emitter.Particles.Count == 0)
                {
                    continue;
                }

                Vector3 mins = Vector3.Min(emitter.Mins, burst.Mins);
                Vector3 maxs = Vector3.Max(emitter.Maxs, burst.Maxs);

                if ((maxs - mins).Length() < MergeRadius)
                {
                    emitter.Mins = mins;
                    emitter.Maxs = maxs;

                    return emitter;
                }
            }
        }

        return null;
    }
}
