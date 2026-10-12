using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What a bullet trace is asked against this frame: the demo, the map, and the players as the frame posed them.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Level">The loaded map's collision, or null.</param>
/// <param name="Source">Where the frame's players came from, for the same sample the hitboxes were posed at.</param>
/// <param name="ShownTick">The moment the frame showed, fraction included.</param>
/// <param name="Playing">Whether playback is running, which decides whether that sample is interpolated.</param>
public readonly record struct BulletScene(
    DemoTimeline? Timeline, MapLevel? Level, IMomentSource? Source, double ShownTick, bool Playing);

/// <summary>
/// Where a client bullet stops once the players are counted, and which bullets a player stopped (B415) — moved out of
/// the view whole (D214), where it was `StruckEnd`, `StruckPlayer`, `HitboxesOrUntested` and `ShotOrder`.
/// </summary>
/// <remarks>
/// **In Presentation only while the view still calls it.** The tracers ask it from <see cref="BurstEffects"/>; the
/// decals and impact effects, still in the view, ask it too. Two presenters needing one thing is the model layer's
/// (D92): it goes down into Scene with the slice that takes the decals out of the view.
/// </remarks>
public sealed class BulletStrikes
{
    /// <summary>`MASK_SOLID | CONTENTS_HITBOX`, what `FireBullet` traces with.</summary>
    private const int BulletMask = 0x0200400B | 0x40000000;

    /// <summary>`LIFE_ALIVE`.</summary>
    private const int Alive = 0;

    /// <summary>`FL_DUCKING`.</summary>
    private const int Ducking = 1 << 1;

    private readonly IPosedEntities _models;

    private readonly ILogger _renderLog;

    /// <summary>The bullets a player was judged to stop, so the world's decal and effect skip them alike.</summary>
    private readonly HashSet<(int Shot, int Bullet)> _struckPlayers = [];

    /// <summary>The players at the shown moment, for a bullet's hull test; reused.</summary>
    private readonly List<ScenePlayer> _shotTargets = [];

    /// <summary>The view's own: the model set it draws with.</summary>
    /// <param name="models">The posed entities.</param>
    /// <param name="render">Where a bullet's trace is logged at debug.</param>
    public BulletStrikes(EntityModelSet models, ILogger render)
        : this((IPosedEntities)models, render)
    {
    }

    /// <summary>Over any posed entities.</summary>
    /// <param name="models">The posed entities.</param>
    /// <param name="render">Where a bullet's trace is logged at debug.</param>
    public BulletStrikes(IPosedEntities models, ILogger render)
    {
        _models = models;
        _renderLog = render;
    }

    /// <summary>Forgets every judged bullet, with the map they were judged on.</summary>
    public void Clear() => _struckPlayers.Clear();

    /// <summary>Whether a player stopped this bullet — judged only on its own tick, and remembered.</summary>
    /// <param name="bullet">The bullet.</param>
    /// <param name="tick">The tick being shown.</param>
    /// <param name="scene">What it is traced against.</param>
    /// <returns>True when a player stopped it.</returns>
    /// <remarks>Recorded so an impact's decal and its effect agree, whichever asks first.</remarks>
    public bool StruckPlayer(ShotImpact bullet, int tick, in BulletScene scene)
    {
        // The server already traced its own impacts; one it sent for the world struck the world. A bolt's trace is
        // `MASK_SOLID_BRUSHONLY`, which no player is in.
        if (bullet.FromServer || bullet.BrushOnly)
        {
            return false;
        }

        if (_struckPlayers.Contains((bullet.Shot, bullet.Bullet)))
        {
            return true;
        }

        if (tick - bullet.Tick > 1 || End(bullet, scene).Struck is null)
        {
            return false;
        }

        _struckPlayers.Add((bullet.Shot, bullet.Bullet));

        return true;
    }

    /// <summary>
    /// Where a bullet's sounds fall on its tick's frame: its temp entity's place in the stream — a client shot's
    /// `CTEFireBullets`, or for a negative <paramref name="shot"/> the server's `CTEEffectDispatch` — then the pellet (B505).
    /// </summary>
    /// <param name="timeline">The loaded demo, or null.</param>
    /// <param name="shot">The shot's index, or minus one minus a dispatch's.</param>
    /// <param name="bullet">The pellet.</param>
    /// <returns>The order.</returns>
    public static ClientSoundOrder ShotOrder(DemoTimeline? timeline, int shot, int bullet)
    {
        int place = 0;
        int dispatch = -1 - shot;

        if (timeline is null)
        {
            place = 0;
        }
        else if (shot >= 0 && shot < timeline.Shots.All.Count)
        {
            place = timeline.Shots.All[shot].TempEntity;
        }
        else if (shot < 0 && dispatch < timeline.Dispatches.All.Count)
        {
            place = timeline.Dispatches.All[dispatch].TempEntity;
        }

        return new ClientSoundOrder(ClientSoundPhase.TempEntities, place, bullet);
    }

    /// <summary>A player's hitboxes against a ray, as <see cref="PlayerBulletTrace.ClipRayToEntity"/> asks for them.</summary>
    /// <param name="entity">The player.</param>
    /// <param name="from">The ray's start.</param>
    /// <param name="delta">Its extent.</param>
    /// <returns>The fraction; 1 for a miss; null for a player no pass posed.</returns>
    /// <remarks>
    /// **A miss is a full-length trace, 1, not "untested"**: `ClipRayToHitboxes` (engine.dll `0x180190a40`) returns true for
    /// any line against a model with hitboxes, so the engine never falls to the collision box after missing them. Only a
    /// player no pass posed — whose hitboxes this viewer cannot test — answers null, for the box to stand in.
    /// </remarks>
    public float? HitboxesOrUntested(int entity, Vector3 from, Vector3 delta) =>
        _models.TraceHitboxes(entity, from, delta, BulletMask) ?? (_models.IsPosed(entity) ? 1f : null);

    /// <summary>Where a tracer's bullet stops once the players are counted — <see cref="PlayerBulletTrace"/>.</summary>
    /// <param name="bullet">The bullet.</param>
    /// <param name="scene">What it is traced against.</param>
    /// <returns>Its end, whether any player's hitboxes could be asked, and the player it struck.</returns>
    /// <remarks>
    /// The players are the timeline's at the shot's own tick; their hitboxes are posed as this frame's pass posed them,
    /// which is the tick the tracer is first drawn on. **A player no pass has posed has no hitboxes to hit**, so a bullet
    /// passes through someone culled from view — whose tracer end is most likely out of view too.
    /// </remarks>
    public ((float X, float Y, float Z) End, bool Judged, int? Struck) End(ShotImpact bullet, in BulletScene scene)
    {
        if (scene.Timeline is null || scene.Level is null)
        {
            return (bullet.End, true, null);
        }

        DemoTimeline timeline = scene.Timeline;
        MapLevel level = scene.Level;

        Vector3 start = new(bullet.Start.X, bullet.Start.Y, bullet.Start.Z);
        Vector3 reach = new(bullet.Reach.X, bullet.Reach.Y, bullet.Reach.Z);
        float full = Vector3.Distance(start, reach);
        float worldFraction = full > 0f
            ? Vector3.Distance(start, new Vector3(bullet.End.X, bullet.End.Y, bullet.End.Z)) / full
            : 1f;

        List<BulletTarget> targets = [];

        // **Where the players are DRAWN, the moment their hitboxes were posed at** — `GetCollisionOrigin()` is the
        // interpolated abs origin, so the collision box and the hitboxes are one entity in one place. The raw origin at
        // the shot's tick put the hull a running player's 0.12 s ahead of his hitboxes: pellets stopped on a hull his
        // body had left, and hit a body whose hull said it was elsewhere.
        _shotTargets.Clear();

        scene.Source?.PlayersAt(scene.ShownTick, _shotTargets, scene.Playing);

        foreach (ScenePlayer player in _shotTargets.Count > 0 ? _shotTargets : timeline.PlayersAt(bullet.Tick))
        {
            // `CTraceFilterSimple( this, … )` passes over the shooter; the rest must be alive and present.
            if (player.EntityIndex != bullet.Shooter && (player.LifeState ?? Alive) == Alive)
            {
                targets.Add(new BulletTarget(
                    player.EntityIndex,
                    new Vector3(player.X, player.Y, player.Z),
                    ((player.Flags ?? 0) & Ducking) != 0));
            }
        }

        int asked = 0;
        int answered = 0;
        System.Text.StringBuilder hits = new();

        (Vector3 end, int? struck) = PlayerBulletTrace.Clip(
            start,
            reach,
            worldFraction,
            targets,
            (entity, from, delta) =>
            {
                asked++;

                float? fraction = HitboxesOrUntested(entity, from, delta);

                if (fraction is { } at && at < 1f)
                {
                    answered++;
                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    hits.Append(CultureInfo.InvariantCulture, $" {entity}@{at:0.0000}");
                }
                else
                {
                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    hits.Append(CultureInfo.InvariantCulture, $" {entity} {(fraction is null ? "untested" : "missed")}");
                }

                return fraction;
            },
            (from, to) => level.Sweep((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 0f));

        int posed = 0;

        foreach (BulletTarget target in targets)
        {
            posed += _models.IsPosed(target.Entity) ? 1 : 0;
        }

        if (_renderLog.IsEnabled(LogLevel.Debug))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"bullet tick {bullet.Tick} (shot {bullet.Shot} pellet {bullet.Bullet}): {targets.Count} players, {posed} posed, " +
                    $"{asked} hitbox tests, {answered} hit{hits} (world {worldFraction:0.0000}); " +
                    $"struck {(struck is { } who ? who.ToString(CultureInfo.InvariantCulture) : "nobody")}; " +
                    $"from ({start.X:0} {start.Y:0} {start.Z:0}) to ({end.X:0} {end.Y:0} {end.Z:0})"));
        }

        return ((end.X, end.Y, end.Z), targets.Count == 0 || posed > 0, struck);
    }
}
