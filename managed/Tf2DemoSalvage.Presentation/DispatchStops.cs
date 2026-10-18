using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The tick each entity-owned `"ParticleEffect"` dispatch is stopped at, by Valve's own two messages.</summary>
/// <remarks>
/// <code>
/// // particle_parse.cpp:500-519  StopParticleEffects( pEntity ) → DispatchEffect( "ParticleEffectStop", data )
/// // c_particle_system.cpp:269   ParticleEffectStopCallback: pEnt->ParticleProp()->StopEmission()   — every effect it owns
/// // c_particle_system.cpp:193   PARTICLE_DISPATCH_RESET_PARTICLES (particle_parse.h:38): StopEmission() before Create
/// </code>
/// An effect from no entity (`PARTICLE_DISPATCH_FROM_ENTITY` clear) is `CNewParticleEffect::Create( NULL, … )` —
/// nobody's to stop, so it ends by itself. **The owner's deletion ends it too**, and an index used again after it is another
/// entity whose stops are not this effect's. Dormancy is not a stop: it hides the effect (`OwnerSetDormantTo`).
/// </remarks>
public static class DispatchStops
{
    /// <summary>`PARTICLE_DISPATCH_FROM_ENTITY`, particle_parse.h:37.</summary>
    private const int FromEntity = 1 << 0;

    /// <summary>`PARTICLE_DISPATCH_RESET_PARTICLES`, particle_parse.h:38.</summary>
    private const int ResetParticles = 1 << 1;

    /// <summary>Each dispatch's stop tick, or null for one nothing stops.</summary>
    /// <param name="all">Every dispatch, in fire order.</param>
    /// <param name="nameOf">A dispatch's effect name by its index in the name table.</param>
    /// <param name="deletedAfter">
    /// When the entity at an index is deleted after a tick (<see cref="EntityDormancy.DeletedAfter"/>), or null for never: the
    /// owner going away ends its effects, and a stop sent to the index after that is another entity's.
    /// </param>
    /// <returns>One entry per dispatch.</returns>
    public static int?[] Compute(
        IReadOnlyList<SceneEffectDispatch> all, Func<int, string?> nameOf, Func<int, int, int?>? deletedAfter = null)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(nameOf);

        int?[] stops = new int?[all.Count];

        // Walked backwards: each entity's next stop is known by the time an earlier effect of it is met.
        Dictionary<int, int> nextStop = [];

        for (int index = all.Count - 1; index >= 0; index--)
        {
            SceneEffectDispatch dispatch = all[index];
            string? name = nameOf(dispatch.Name);
            bool owned = (dispatch.Flags & FromEntity) != 0;

            if (string.Equals(name, "ParticleEffect", StringComparison.Ordinal) && owned)
            {
                int? stopped = nextStop.TryGetValue(dispatch.Entity, out int stop) ? stop : null;
                int? deleted = deletedAfter?.Invoke(dispatch.Entity, dispatch.Tick);

                // A stop past the owner's deletion was sent to whatever holds the index next.
                stops[index] = deleted is { } gone && (stopped is null || stopped > gone) ? gone : stopped;

                if ((dispatch.Flags & ResetParticles) != 0)
                {
                    nextStop[dispatch.Entity] = dispatch.Tick;
                }
            }
            else if (string.Equals(name, "ParticleEffectStop", StringComparison.Ordinal))
            {
                nextStop[dispatch.Entity] = dispatch.Tick;
            }
        }

        return stops;
    }
}
