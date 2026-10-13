using System;
using System.Collections.Generic;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's target ID and weapon pickup traces are run against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Game">The opened install, for a dropped weapon's collide, or null.</param>
/// <param name="PlayersShown">The players as the frame drew them at this tick, or null when it drew another tick.</param>
/// <param name="Eye">The first-person camera, asked for only once the local player is known to be able to look.</param>
public readonly record struct IdTargetFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    GameContent? Game,
    IReadOnlyList<ScenePlayer>? PlayersShown,
    Func<FreeCamera?> Eye);

/// <summary>
/// What the local player is looking at, for the HUD: `C_TFPlayer::GetIDTarget()` and `GetDroppedWeaponInRange`'s trace.
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 4), where it was `WithIdTraces` and the cache of dropped
/// weapon collides: the same targets, boxes and traces. The view's side is the constructor and one <see cref="With"/> a
/// HUD frame.
///
/// **A presenter (D90): it decides WHAT can be looked at** — which players, buildings and dropped entities are in the
/// trace, and from which eye. The trace is `IdTargetTrace` and `IdTargetTraces`, Scene's.
/// </remarks>
public sealed class IdTargets
{
    private readonly BulletStrikes _strikes;

    private readonly ILogger _mapLog;

    /// <summary>The frame being traced, for <see cref="WithIdTraces"/>.</summary>
    private IdTargetFrame _frame;

    /// <summary>That frame's map geometry.</summary>
    private MapLevel? _level;

    /// <summary>Target ID over the posed hitboxes the bullets are traced against.</summary>
    /// <param name="strikes">The players' posed hitboxes.</param>
    /// <param name="map">The map log, for a collide that would not read.</param>
    public IdTargets(BulletStrikes strikes, ILogger map)
    {
        _strikes = strikes;
        _mapLog = map;
    }

    /// <summary>The HUD's state with what the local player is looking at, or with no target where he cannot look.</summary>
    /// <param name="state">The HUD's state this frame.</param>
    /// <param name="tick">The HUD's tick.</param>
    /// <param name="frame">What this frame is traced against.</param>
    /// <returns>The state with its target, pickup trace and eye.</returns>
    public HudState With(HudState state, int tick, in IdTargetFrame frame) => WithOver(state, tick, frame, frame.Loaded?.Level);

    /// <summary>The same, over the map's geometry as data (D214).</summary>
    /// <param name="state">The HUD's state this frame.</param>
    /// <param name="tick">The HUD's tick.</param>
    /// <param name="frame">What this frame is traced against; its map is not read here.</param>
    /// <param name="level">The map's geometry, or null for none.</param>
    /// <returns>The state.</returns>
    internal HudState WithOver(HudState state, int tick, in IdTargetFrame frame, MapLevel? level)
    {
        _frame = frame;
        _level = level;

        return WithIdTraces(state, tick);
    }

    /// <summary>`LIFE_ALIVE`.</summary>
    private const int Alive = 0;

    /// <summary>`FL_DUCKING`.</summary>
    private const int Ducking = 1 << 1;

    /// <summary>`SOLID_VPHYSICS`.</summary>
    private const int SolidVphysics = 6;

    /// <summary>Each dropped weapon model's collide, read once.</summary>
    private readonly Dictionary<string, Tf2DemoSalvage.Animation.Animating.IvpStaticPropCollide?> _modelCollides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>`C_TFPlayer::GetIDTarget()` for the local player this frame — <see cref="IdTargetTrace"/> from the view.</summary>
    /// <remarks>
    /// Traced only where `CMainTargetID` can draw: a point-of-view demo's living recorder outside any observer mode. The
    /// view is the first-person camera, `MainViewOrigin()` and `MainViewForward()`; the world is the BSP, the players
    /// their hulls and posed hitboxes (<see cref="IdTargetTraces"/>). The same traces answer `GetDroppedWeaponInRange`'s
    /// (tf_player_shared.cpp:14761), `TF_WEAPON_PICKUP_RANGE` long.
    /// </remarks>
    private HudState WithIdTraces(HudState state, int tick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (!state.HasLocalPlayer || !state.Alive || state.ObserverMode > Core.Scene.ObserverModes.None
            || _frame.Timeline is not { HasRecordedView: true } timeline || _level is not { } level
            || _frame.Eye() is not { } eye)
        {
            return state with { IdTarget = null };
        }

        List<BulletTarget> targets = [];
        Dictionary<int, int> teams = [];

        // Their hulls where the frame drew them — `GetCollisionOrigin()` — beside the hitboxes posed there (B526); the
        // recording's newest frame put a running player's hull an interpolation delay ahead of his body.
        foreach (ScenePlayer player in _frame.PlayersShown ?? timeline.PlayersAt(tick))
        {
            if ((player.LifeState ?? Alive) == Alive)
            {
                targets.Add(new BulletTarget(player.EntityIndex, new Vector3(player.X, player.Y, player.Z), ((player.Flags ?? 0) & Ducking) != 0));
                teams[player.EntityIndex] = player.Team ?? 0;
            }
        }

        // A standing building's `SOLID_BBOX`: its origin plus the networked collision box; a blueprint is `FSOLID_NOT_SOLID`.
        List<IdTargetBox> boxes = [];

        foreach (Core.Scene.SceneBuilding building in timeline.BuildingsAt(tick))
        {
            if (building is { IsSolid: true, Position: { } at, Mins: { } mins, Maxs: { } maxs })
            {
                boxes.Add(new IdTargetBox(
                    building.EntityIndex,
                    building.Team ?? 0,
                    new Vector3(at.X + mins.X, at.Y + mins.Y, at.Z + mins.Z),
                    new Vector3(at.X + maxs.X, at.Y + maxs.Y, at.Z + maxs.Z)));
            }
        }

        // A `SOLID_BBOX` — a revive marker, or a dropped weapon whose `VPhysicsInitNormal` failed (tf_dropped_weapon.cpp:113-116)
        // — is its box, world-aligned; a `SOLID_VPHYSICS` weapon its model's collide, placed (`ClipRayToCollideable`).
        foreach (Core.Scene.SceneIdEntity entity in timeline.IdEntitiesAt(tick))
        {
            if (entity is { IsSolid: true, Position: { } at, Mins: { } mins, Maxs: { } maxs })
            {
                Vector3 origin = new(at.X, at.Y, at.Z);
                StaticPropCollision? hull = null;

                if (entity is { SolidType: SolidVphysics, Model: { } model } && _frame.Game is { } game)
                {
                    (float pitch, float yaw, float roll) = entity.Angles ?? (0f, 0f, 0f);

                    hull = StaticPropCollision.From(
                        [new BspStaticProp(model, at.X, at.Y, at.Z, pitch, yaw, roll, 1f, Solid: SolidVphysics)],
                        path =>
                        {
                            if (!_modelCollides.TryGetValue(path, out Tf2DemoSalvage.Animation.Animating.IvpStaticPropCollide? known))
                            {
                                known = IvpMapWorld.ModelCollide(path, game, _mapLog);
                                _modelCollides[path] = known;
                            }

                            return known;
                        },
                        _ => -1);
                }

                boxes.Add(new IdTargetBox(
                    entity.EntityIndex, entity.Team ?? 0, origin + new Vector3(mins.X, mins.Y, mins.Z), origin + new Vector3(maxs.X, maxs.Y, maxs.Z), hull));
            }
        }

        IdTargetTraces traces = new(
            targets,
            teams,
            (from, to) => level.Sweep((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 0f),
            _strikes.HitboxesOrUntested,
            boxes);
        (float x, float y, float z) = AngleVectors.Forward(eye.Angles.Pitch, eye.Angles.Yaw);
        Vector3 from = new(eye.Origin.X, eye.Origin.Y, eye.Origin.Z);
        Vector3 forward = new(x, y, z);

        return state with
        {
            IdTarget = IdTargetTrace.GetIdTarget(
                from, forward, state.LocalIndex, state.Team, isObserver: false, observerTarget: 0, traces.Solid, traces.Shot),

            // `EyePosition() + vecForward * TF_WEAPON_PICKUP_RANGE`, the same mask and filter as the ID trace.
            WeaponPickupTraceHit = traces.Solid(from, from + (forward * 150f), state.LocalIndex).Entity,
            EyePosition = (eye.Origin.X, eye.Origin.Y, eye.Origin.Z),
        };
    }
}
