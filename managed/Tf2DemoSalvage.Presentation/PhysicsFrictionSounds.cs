using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>A corpse's scrape loop — the client's `friction_t` slots, started, kept and stopped as TF2 does.</summary>
/// <remarks>
/// <code>
/// Friction:          energy &lt; 0.05 or no surface → nothing
///                    a slot playing for the entity, last effect within 0.5 s (multiplayer) → last update = now, nothing else
/// PhysFrictionSound: energy &lt; 75 or either surface 'X' → nothing;  volume = (energy / 15500)²
///                    scraperough, or scrapesmooth when the struck surface's roughness is under this one's threshold
///                    volume ≤ 1/128, no free slot, or no script → nothing
///                    no loop yet: start one (CHAN_BODY, from the entity) unless its script volume · volume ≤ 0.1
///                    last update = last effect = now
/// UpdateFrictionSounds, each frame after the simulation: a loop not updated in the last 0.1 s stops
/// </code>
/// (`game/client/physics.cpp:661-730, 976-1022`; `physics_shared.cpp:982`). *Not carried:* the loop's volume and pitch
/// ramps once it plays — `SoundChangeVolume`/`SoundChangePitch` over 0.1 s — since a started sound here keeps its level.
/// </remarks>
public sealed class PhysicsFrictionSounds
{
    /// <summary>`m_current[8]`.</summary>
    private const int SlotCount = 8;

    /// <summary>`CHAN_BODY`.</summary>
    private const int BodyChannel = 4;

    /// <summary>`CCollisionEvent::Friction`'s floor.</summary>
    private const float QuietestFriction = 0.05f;

    /// <summary>`PhysFrictionSound`'s floor.</summary>
    private const float QuietestScrape = 75f;

    /// <summary>`ENERGY_VOLUME_SCALE`, `physics_shared.h:43`.</summary>
    private const float EnergyVolumeScale = 1f / 15500f;

    /// <summary>`flVolume > (1.0f/128.0f)`.</summary>
    private const float QuietestVolume = 1f / 128f;

    /// <summary>"don't create really quiet scrapes".</summary>
    private const float QuietestStart = 0.1f;

    /// <summary>Multiplayer's "once every 500 msecs".</summary>
    private const double EffectInterval = 0.5;

    /// <summary>"friction wasn't updated the last 100msec, assume fiction finished".</summary>
    private const double StaleAfter = 0.1;

    private readonly Slot?[] _slots = new Slot?[SlotCount];

    /// <summary>Forgets every loop without stopping it — a seek, after which nothing the old frames started is playing.</summary>
    public void Clear() => Array.Clear(_slots);

    /// <summary>One client frame: its friction reports, then the stale loops stopped.</summary>
    /// <param name="tick">The frame's tick, which the sounds carry.</param>
    /// <param name="time">`gpGlobals->curtime`, in seconds.</param>
    /// <param name="frictions">The frame's `Friction` reports, in order.</param>
    /// <param name="at">Where an entity stands at a tick.</param>
    /// <param name="surfaces">The surface properties.</param>
    /// <param name="scripts">The sound scripts.</param>
    /// <returns>The loops started and stopped.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<SceneSound> Step(
        int tick,
        double time,
        IReadOnlyList<CorpseFriction> frictions,
        Func<int, int, (float X, float Y, float Z)> at,
        VphysicsSurfaceProps surfaces,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        ArgumentNullException.ThrowIfNull(frictions);
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(scripts);

        List<SceneSound> sounds = [];

        foreach (CorpseFriction friction in frictions)
        {
            if (Friction(tick, time, friction, at, surfaces, scripts) is { } started)
            {
                sounds.Add(started);
            }
        }

        for (int index = 0; index < SlotCount; index++)
        {
            if (_slots[index] is { } slot && slot.LastUpdate < time - StaleAfter)
            {
                sounds.Add(slot.Playing with { Tick = tick, IsStop = true });
                _slots[index] = null;
            }
        }

        return sounds;
    }

    private SceneSound? Friction(
        int tick,
        double time,
        CorpseFriction friction,
        Func<int, int, (float X, float Y, float Z)> at,
        VphysicsSurfaceProps surfaces,
        IReadOnlyDictionary<string, SoundScriptEntry> scripts)
    {
        if (friction.Energy < QuietestFriction || friction.SurfaceProps < 0)
        {
            return null;
        }

        int index = Find(friction.Entity);

        if (index >= 0 && _slots[index] is { } kept && kept.LastEffect + EffectInterval > time)
        {
            _slots[index] = kept with { LastUpdate = time };
            return null;
        }

        if (friction.Energy < QuietestScrape ||
            surfaces.GetIVPMaterial(friction.SurfaceProps) is not { } sliding ||
            (surfaces.GetIVPMaterial(friction.SurfacePropsHit) ?? surfaces.GetIVPMaterial(0)) is not { } struck ||
            sliding.GameMaterial == 'X' || struck.GameMaterial == 'X')
        {
            return null;
        }

        float energy = friction.Energy * EnergyVolumeScale;
        float volume = Math.Clamp(energy * energy, 0f, 1f);

        string? name = sliding.Sounds.ScrapeSmooth is not null && struck.Audio.RoughnessFactor < sliding.Audio.RoughThreshold
            ? sliding.Sounds.ScrapeSmooth
            : sliding.Sounds.ScrapeRough;

        if (!(volume > QuietestVolume) || index < 0 || name is null)
        {
            return null;
        }

        SceneSound? started = null;

        if (_slots[index] is not { } playing)
        {
            if (EntitySounds.Emit(tick, friction.Entity, name, at(friction.Entity, tick), scripts) is not { } sound ||
                sound.Volume * volume <= QuietestStart)
            {
                return null;
            }

            started = sound with { Channel = BodyChannel, Volume = sound.Volume * volume };
            playing = new Slot(friction.Entity, started.Value, time, time);
        }

        _slots[index] = playing with { LastUpdate = time, LastEffect = time };
        return started;
    }

    /// <summary>`FindFriction`: the entity's slot, or the first free one, or none.</summary>
    private int Find(int entity)
    {
        int free = -1;

        for (int index = 0; index < SlotCount; index++)
        {
            if (_slots[index] is null && free < 0)
            {
                free = index;
            }

            if (_slots[index]?.Entity == entity)
            {
                return index;
            }
        }

        return free;
    }

    private sealed record Slot(int Entity, SceneSound Playing, double LastUpdate, double LastEffect);
}
