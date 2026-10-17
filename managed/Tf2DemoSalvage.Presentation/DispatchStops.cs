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
/// nobody's to stop, so it ends by itself. **Not covered**: the owning entity being deleted or going dormant.
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
    /// <returns>One entry per dispatch.</returns>
    public static int?[] Compute(IReadOnlyList<SceneEffectDispatch> all, Func<int, string?> nameOf)
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
                stops[index] = nextStop.TryGetValue(dispatch.Entity, out int stop) ? stop : null;

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
