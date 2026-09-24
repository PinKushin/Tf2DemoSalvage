using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>What a static prop's decal needs from the loaded map (B421).</summary>
/// <param name="Mesh">A prop's triangles in the world by its lump index, or null for one not drawn.</param>
/// <param name="Material">A decal's drawn model material's table index — its `$modelmaterial` — or −1 when not loaded.</param>
public sealed record StaticPropDecalSource(Func<int, IReadOnlyList<WorldVertex>?> Mesh, Func<DecalMaterial, int> Material);

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
    /// <param name="props">
    /// A static prop's triangles in the world and a decal's drawn model material, or null to leave props bare (B421).
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public DecalReplay(
        WorldDecals decals,
        IReadOnlyList<ShotImpact> impacts,
        IReadOnlyList<SceneDecal> events,
        Func<ShotImpact, DecalMaterial?> impactMaterial,
        Func<int, DecalMaterial?> eventMaterial,
        Func<int, int, SolidBrush?>? brushOf = null,
        StaticPropDecalSource? props = null)
    {
        _props = props;

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
    private readonly StaticPropDecalSource? _props;

    /// <summary>The pool.</summary>
    public WorldDecals Decals { get; }

    /// <summary>The static props' decals, keyed by the prop's lump index — `CStudioRender`'s lists (B421).</summary>
    public ModelDecals PropDecals { get; } = new();

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
            PropDecals.ClearAll();
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

                if (struckPlayer(impact) || _impactMaterial(impact) is not { } material)
                {
                    if (impact.StaticProp >= 0)
                    {
                        PropRefusals["player or no decal material"] = PropRefusals.GetValueOrDefault("player or no decal material") + 1;
                    }

                    continue;
                }

                // A static prop's hit goes to `AddDecalToStaticProp` alone — never into the brushes behind it.
                if (impact.StaticProp >= 0)
                {
                    ShootProp(impact, material);
                }
                else
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
    /// `fx_impact.cpp`'s `Impact` calls `AddDecalToStaticProp( vecStart, traceExt, hitbox − 1, decal, doTrace: true )`.
    /// *Interpolated from `C_BaseEntity::AddStudioDecal`* (`c_baseentity.cpp:3640`), the published half of the same
    /// shape — trace the model, then `betterRay` from the hit one unit into the face with `noPokeThru` — since
    /// `CStaticPropMgr::AddDecalToStaticProp` is engine code not read: without a start at the face, `noPokeThru`'s depth
    /// test (`studiorender.dll` `0x18000b690`, |depth| &lt; radius from the ray's start) would refuse every triangle. The
    /// delta is bloated by 1.1 as `AddDecal` bloats a player's (<see cref="ServerImpacts.DecalRay"/>).
    /// </remarks>
    private void ShootProp(ShotImpact impact, DecalMaterial material)
    {
        if (_props is not null && RefusalOfProp(_props, impact, material) is { } refused)
        {
            PropRefusals[refused] = PropRefusals.GetValueOrDefault(refused) + 1;
        }
    }

    /// <summary>Places a static prop's decal, or says why not.</summary>
    private string? RefusalOfProp(StaticPropDecalSource props, ShotImpact impact, DecalMaterial material)
    {
        if (props.Mesh(impact.StaticProp) is not { } mesh)
        {
            return "prop not drawn";
        }

        if (props.Material(material) is not (>= 0 and var index))
        {
            return $"model material not loaded: {material.ModelMaterial ?? material.Draws ?? material.Name}";
        }

        float scale = material.DecalScale > 0f ? material.DecalScale : 1f;
        float radius = MathF.Max(material.Width, material.Height) * scale * 0.5f;
        Vector3 normal = new(impact.Normal.X, impact.Normal.Y, impact.Normal.Z);

        return PropDecals.AddClipped(
            impact.StaticProp, mesh, new Vector3(impact.End.X, impact.End.Y, impact.End.Z), -normal * 1.1f, radius, index)
            ? null
            : "took no triangle";
    }

    /// <summary>Why static prop hits placed no decal, counted by reason — the control that a missing decal was refused.</summary>
    public Dictionary<string, int> PropRefusals { get; } = [];

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
