using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>The world's decals at a tick: every bullet impact and decal event before it, shot in order (B415).</summary>
/// <remarks>
/// **Decals are state, not one-shots**: the pool at a tick is the result of every shot before it, oldest pushed out
/// by `r_decals`. So playing forward shoots what the tick passed, and a seek backwards clears the pool and replays
/// from the start — as a demo client rewinding does, by restarting and fast-forwarding.
///
/// *Interpolated:* within one tick a decal event is shot before a bullet's. Both arrive as temp entities in one packet
/// and their order in it is not recorded.
/// </remarks>
public sealed class DecalReplay
{
    private readonly IReadOnlyList<ShotImpact> _impacts;
    private readonly IReadOnlyList<SceneDecal> _events;
    private readonly Func<ShotImpact, DecalMaterial?> _impactMaterial;
    private readonly Func<int, DecalMaterial?> _eventMaterial;
    private int _impact;
    private int _event;
    private int _tick = int.MinValue;

    /// <summary>A replay into one pool.</summary>
    /// <param name="decals">The pool.</param>
    /// <param name="impacts">Every bullet the world stopped, in tick order.</param>
    /// <param name="events">Every decal temp entity, in tick order.</param>
    /// <param name="impactMaterial">The decal a bullet leaves, or null for none.</param>
    /// <param name="eventMaterial">A <c>decalprecache</c> index's material, or null.</param>
    /// <param name="brushOf">
    /// Where the brush entity an impact stopped on stood at its tick, or null — a door's model is shot in its own frame.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public DecalReplay(
        WorldDecals decals,
        IReadOnlyList<ShotImpact> impacts,
        IReadOnlyList<SceneDecal> events,
        Func<ShotImpact, DecalMaterial?> impactMaterial,
        Func<int, DecalMaterial?> eventMaterial,
        Func<int, int, SolidBrush?>? brushOf = null)
    {
        ArgumentNullException.ThrowIfNull(decals);
        ArgumentNullException.ThrowIfNull(impacts);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(impactMaterial);
        ArgumentNullException.ThrowIfNull(eventMaterial);

        Decals = decals;
        _impacts = impacts;
        _events = events;
        _impactMaterial = impactMaterial;
        _eventMaterial = eventMaterial;
        _brushOf = brushOf;
    }

    private readonly Func<int, int, SolidBrush?>? _brushOf;

    /// <summary>The pool.</summary>
    public WorldDecals Decals { get; }

    /// <summary>Shoots everything up to and including a tick, first clearing the pool when the tick went backwards.</summary>
    /// <param name="tick">The tick now shown.</param>
    /// <param name="struckPlayer">
    /// Whether a player stopped this bullet before the world did — `UTIL_PlayerBulletTrace`'s half, which needs the
    /// players posed, so the caller answers it; a bullet a player stopped leaves no decal on the world.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="struckPlayer"/> is null.</exception>
    public void AdvanceTo(int tick, Func<ShotImpact, bool> struckPlayer)
    {
        ArgumentNullException.ThrowIfNull(struckPlayer);

        if (tick < _tick)
        {
            Decals.Clear();
            _impact = 0;
            _event = 0;
        }

        _tick = tick;

        while (true)
        {
            bool eventDue = _event < _events.Count && _events[_event].Tick <= tick;
            bool impactDue = _impact < _impacts.Count && _impacts[_impact].Tick <= tick;

            if (eventDue && (!impactDue || _events[_event].Tick <= _impacts[_impact].Tick))
            {
                Shoot(_events[_event++]);
            }
            else if (impactDue)
            {
                ShotImpact impact = _impacts[_impact++];

                if (!struckPlayer(impact) && _impactMaterial(impact) is { } material)
                {
                    Vector3 end = new(impact.End.X, impact.End.Y, impact.End.Z);

                    if (impact.BrushEntity >= 0 && _brushOf?.Invoke(impact.BrushEntity, impact.Tick) is { } brush)
                    {
                        Decals.Shoot(material, end - brush.Origin, brush.HeadNode, brush.Entity);
                    }
                    else
                    {
                        Decals.Shoot(material, end);
                    }
                }
            }
            else
            {
                return;
            }
        }
    }

    /// <remarks>
    /// `C_TEWorldDecal` shoots at its origin; `C_TEDecal` on the world with no hitbox reaches `AddBrushModelDecal`, which
    /// shoots at the origin too. *Not built:* a static prop (the world with a hitbox), another entity, and a spray.
    /// </remarks>
    private void Shoot(SceneDecal decal)
    {
        bool world = decal.Kind == SceneDecalKind.World ||
            (decal.Kind == SceneDecalKind.Entity && decal.Entity == 0 && decal.Hitbox == 0);

        if (world && _eventMaterial(decal.Index) is { } material)
        {
            Decals.Shoot(material, new Vector3(decal.Origin.X, decal.Origin.Y, decal.Origin.Z));
        }
    }
}
