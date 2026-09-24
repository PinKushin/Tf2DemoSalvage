using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A physics impact's sound — `physicssound::PlayImpactSounds` (`game/shared/vphysics_sound.h:40`), for corpses (B172).</summary>
/// <remarks>
/// <code>
/// name = surface.impactHard;  if hit and surface.impactSoft and ( hit.hardness &lt; surface.hardThreshold
///                                                            or 0 &lt; surface.hardVelocityThreshold &gt; impactSpeed ): impactSoft
/// EmitSound( CPASAttenuationFilter, 0, { CHAN_STATIC, volume: params.volume · min( volume, 1 ), origin } )
/// </code>
/// `CPhysicsSystem::AddImpactSound` names `CHAN_STATIC` (`physics.cpp:423`), and the entity is the world: *"If this entity gets
/// deleted, the sound comes out at the world origin — Play on ent 0 for now."*
/// </remarks>
public static class PhysicsImpactSounds
{
    /// <summary>`CHAN_STATIC`.</summary>
    private const int StaticChannel = 6;

    /// <summary>The sound one frame's impact plays, or null where `PlayImpactSounds` plays none.</summary>
    /// <param name="tick">The tick the frame ended on.</param>
    /// <param name="impact">The frame's impact.</param>
    /// <param name="surfaces">The game's surfaces.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <returns>The sound, or null for a surface with no `impacthard` or a name no script declares.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SceneSound? For(
        int tick, PhysicsImpactSound impact, VphysicsSurfaceProps surfaces, IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(scripts);

        if (surfaces.GetSurfaceData(impact.SurfaceProps) is not { Sounds.ImpactHard: { } hard } surface)
        {
            return null;
        }

        string name = hard;

        if (surfaces.GetSurfaceData(impact.SurfacePropsHit) is { } hit && surface.Sounds.ImpactSoft is { } soft &&
            (hit.Audio.HardnessFactor < surface.Audio.HardThreshold ||
             (surface.Audio.HardVelocityThreshold > 0f && surface.Audio.HardVelocityThreshold > impact.ImpactSpeed)))
        {
            name = soft;
        }

        return EntitySounds.Emit(tick, ExplosionSounds.FromWorld, name, (impact.Origin.X, impact.Origin.Y, impact.Origin.Z), scripts)
            is { } drawn
            ? drawn with { Channel = StaticChannel, Volume = drawn.Volume * MathF.Min(impact.Volume, 1f) }
            : null;
    }
}
