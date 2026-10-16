using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's particles are built against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Viewing">The camera this frame, whose basis the quads face.</param>
/// <param name="Drawn">The entities the frame drew.</param>
/// <param name="FrameSeconds">The frame's real duration, for the glow gate's fade.</param>
/// <param name="ViewportHeight">The viewport's height in pixels, for the glow proxies.</param>
/// <param name="DoPixelVisibility">`r_dopixelvisibility`.</param>
/// <param name="ShownTick">The moment the frame showed, fraction included, which the trails are stepped to (D221); null for <paramref name="Tick"/>.</param>
/// <param name="Seeked">Whether this frame landed by a seek, the frame's one skip decision — a transport jump, never a long frame (`FrameSkip`, D221).</param>
public readonly record struct ParticleFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    int Tick,
    FreeCamera Viewing,
    IReadOnlyList<SceneProp> Drawn,
    float FrameSeconds,
    int ViewportHeight,
    bool DoPixelVisibility,
    double? ShownTick = null,
    bool Seeked = false);

/// <summary>What a loaded map holds that a frame's particles read, as data (D214): a test supplies it by hand.</summary>
/// <param name="RocketTrail">The rocket trail's definition, or null when the map loaded none.</param>
/// <param name="Systems">The map's particle systems by name.</param>
/// <param name="ParticleMaterials">The particle materials by name.</param>
/// <param name="SpriteMaterials">The entity sprites by material.</param>
/// <param name="Level">The map's geometry, for a glow's line of sight.</param>
internal sealed record ParticleMap(
    ParticleSystem? RocketTrail,
    IReadOnlyDictionary<string, ParticleSystem>? Systems,
    IReadOnlyDictionary<string, ParticleMaterial>? ParticleMaterials,
    IReadOnlyDictionary<string, EngineSprite>? SpriteMaterials,
    MapLevel Level);

/// <summary>
/// A frame's particles in the order they are made: the rocket trails stepped, the effects' quads built against the
/// camera, the entity sprites and the strips gated by the glow occlusion, and the lot joined into one list (B373, B378).
/// </summary>
/// <remarks>
/// **Moved out of `MainForm.DrawParticles`** (D214, thin view slice 3): what is decided there — which projectile
/// carries a trail and from where, which batches there are, their order and what joins what — with the same
/// arithmetic and order. The view keeps the calls: <see cref="Trails"/>, the one-shot effects' step between,
/// <see cref="Build"/>, and handing the result to the device.
///
/// **The basis comes from the camera's ANGLES rather than from its matrix.** `AngleVectors.Right` and `Up` are the
/// engine's own decomposition and are what every other direction in this project is taken from; pulling rows out of
/// a view matrix would be a second route to the same number, free to disagree with the first (B243).
/// </remarks>
public sealed class FrameParticles
{
    /// <summary>What a machine with no TF2 has: no particle materials at all.</summary>
    private static readonly IReadOnlyDictionary<string, ParticleMaterial> NoParticleMaterials =
        new Dictionary<string, ParticleMaterial>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a machine with no TF2 has: no entity sprites either (B390).</summary>
    private static readonly IReadOnlyDictionary<string, EngineSprite> NoEngineSprites =
        new Dictionary<string, EngineSprite>(StringComparer.OrdinalIgnoreCase);

    private readonly ParticleEffects _particles;

    private readonly ILogger _renderLog;

    /// <summary>This tick's projectiles, reused.</summary>
    private readonly
        List<(int Entity, ParticleControlPoint At, ParticleControlPoint? From, int Ticks)>
        _projectilesNow = [];

    /// <summary>Every `env_sprite` in the moment, as quads (B378).</summary>
    private readonly EntitySpriteBatches _sprites = new();

    /// <summary>How much of each glow the scene lets through — <c>CPixelVisibilitySystem</c> (B378, B521).</summary>
    private readonly PixelVisibility _pixelVisibility = new();

    /// <summary>Every <c>env_sun</c>'s core and glow (B378).</summary>
    private readonly SunGlow _sun = new();

    /// <summary>What the frame's map holds.</summary>
    private ParticleMap? _map;

    /// <summary>The map <see cref="_map"/> was read out of.</summary>
    private LoadedMap? _mapOf;

    /// <summary>A frame's particles over the effects running for a demo.</summary>
    /// <param name="particles">The effects.</param>
    /// <param name="render">The render log.</param>
    public FrameParticles(ParticleEffects particles, ILogger render)
    {
        _particles = particles;
        _renderLog = render;
    }

    /// <summary>How many quads the effects built last frame, for an instrument.</summary>
    public int Quads { get; private set; }

    /// <summary>Whether every glow's brightness has reached what the scene lets through, for a capture that waits on it.</summary>
    public bool GlowsSettled => _pixelVisibility.Settled;

    /// <summary>The glows still moving, for the log of what a capture is waiting on.</summary>
    /// <returns>One clause a glow.</returns>
    public string GlowsUnsettled() => _pixelVisibility.Unsettled();

    /// <summary>A new level: every glow forgotten, and `r_dopixelvisibility` latched as the engine latches it at load.</summary>
    /// <param name="doPixelVisibility">`r_dopixelvisibility`.</param>
    public void LevelInit(bool doPixelVisibility) => _pixelVisibility.LevelInit(doPixelVisibility);

    /// <summary>Steps every rocket's trail to the frame's tick.</summary>
    /// <param name="frame">What this frame is built against.</param>
    /// <returns>
    /// Whether the map has a rocket trail at all. **A missing definition costs the trails and everything stepped with
    /// them** — no TF2 install means no `.pcf`, which is every CI run, and the rockets still draw.
    /// </returns>
    public bool Trails(in ParticleFrame frame) => TrailsOver(frame, MapOf(frame));

    /// <summary>The same, over what the frame's map holds as data (D214).</summary>
    /// <param name="frame">What this frame is built against; its map is not read here.</param>
    /// <param name="map">What the map holds, or null for none.</param>
    /// <returns>Whether the map has a rocket trail.</returns>
    internal bool TrailsOver(in ParticleFrame frame, ParticleMap? map)
    {
        _map = map;

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (map?.RocketTrail is not { } trail)
        {
            return false;
        }

        _projectilesNow.Clear();

        foreach (SceneProp prop in frame.Drawn)
        {
            if (string.Equals(
                prop.ClassName, ParticleEffects.RocketClass, StringComparison.Ordinal))
            {
                // **The rocket's own frame, not just its position.** `CreateTrails` attaches
                // with `PATTACH_POINT_FOLLOW`, so the control point carries the projectile's
                // orientation — and `rockettrail` pushes its particles ten units per second
                // down that point's LOCAL Z. Passing a bare position would blow every trail in
                // the game the same way regardless of where its rocket pointed.
                // Forward takes no roll, because rolling about the forward axis cannot move it.
                (float fx, float fy, float fz) = AngleVectors.Forward(
                    prop.Pose.Pitch, prop.Pose.Yaw);

                (float px, float py, float pz) = AngleVectors.Right(
                    prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll);

                (float qx, float qy, float qz) = AngleVectors.Up(
                    prop.Pose.Pitch, prop.Pose.Yaw, prop.Pose.Roll);

                ParticleControlPoint here = new(
                    new Vector3(prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                    new Vector3(fx, fy, fz),
                    new Vector3(px, py, pz),
                    new Vector3(qx, qy, qz));

                // **Where it started and how long ago**, so a trail seen for the first time
                // mid-flight can be replayed along the path instead of piling up at the rocket.
                // The track is the demo's own record of where the thing was; nothing is guessed.
                ParticleControlPoint? born = null;
                int age = 0;

                // **`TrackFor` cannot answer this, because entity indices are REUSED.** This
                // demo has 1,669 rocket tracks across a couple of thousand edict slots, and
                // `_trackByEntity` keeps only the last track written for an index — so asking
                // it for entity 407 returns a rocket from later in the match, whose `FirstTick`
                // is past the current tick, so the age clamped to zero and the replay never ran.
                // That is a lookup keyed on something that does not identify the thing
                // (`docs/memory/key-a-lookup-on-the-question.md#lookups-must-match-exactly`).
                //
                // The track wanted is the one for this entity that CONTAINS this tick. Scanned
                // rather than indexed because it happens once per projectile, when its effect
                // is created, and not per frame.
                if (TrackAt(frame.Timeline, prop.EntityIndex, frame.Tick) is { } track)
                {
                    // The track's birth, not `Keyframes[0]`: a window's track is trimmed and index 0 is gone (D217 step 3).
                    ScenePose pose = track.FirstPose;

                    age = Math.Max(0, frame.Tick - track.FirstTick);
                    born = new ParticleControlPoint(
                        new Vector3(pose.X, pose.Y, pose.Z),
                        here.Forward,
                        here.Right,
                        here.Up);
                }

                _projectilesNow.Add((prop.EntityIndex, here, born, age));
            }
        }

        _particles.Update(
            _projectilesNow,
            trail,
            frame.Timeline?.IntervalPerTick ?? (1f / 66f),
            map.Systems,
            frame.Tick,
            frame.ShownTick,
            frame.Seeked);

        return true;
    }

    /// <summary>
    /// The frame's batches after <see cref="Trails"/> and the one-shot effects' step: the effects' quads, then the entity
    /// sprites, then the strips <paramref name="effectEntities"/> answers, the last two behind the glow occlusion gate.
    /// </summary>
    /// <param name="frame">What this frame is built against.</param>
    /// <param name="device">The device that counts the glows' pixels.</param>
    /// <param name="effectEntities">
    /// The entities drawn as strips rather than models — beams, trails, ropes — given the camera's forward, right and up
    /// and the gate their halos pass through.
    /// </param>
    /// <returns>The batches, in draw order.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<ParticleBatch> Build(
        in ParticleFrame frame,
        IGlowOcclusion device,
        Func<Vector3, Vector3, Vector3, Func<GlowQuery, float>, IReadOnlyList<ParticleBatch>> effectEntities)
    {
        ArgumentNullException.ThrowIfNull(device);

        return BuildOver(frame, MapOf(frame), device, effectEntities);
    }

    /// <summary>The same, over what the frame's map holds as data and any device (D214).</summary>
    /// <param name="frame">What this frame is built against; its map is not read here.</param>
    /// <param name="map">What the map holds, or null for none.</param>
    /// <param name="occluding">The device that counts the glows' pixels.</param>
    /// <param name="effectEntities">The entities drawn as strips.</param>
    /// <returns>The batches, in draw order.</returns>
    internal IReadOnlyList<ParticleBatch> BuildOver(
        in ParticleFrame frame,
        ParticleMap? map,
        IGlowOcclusion occluding,
        Func<Vector3, Vector3, Vector3, Func<GlowQuery, float>, IReadOnlyList<ParticleBatch>> effectEntities)
    {
        ArgumentNullException.ThrowIfNull(effectEntities);

        _map = map;

        FreeCamera viewing = frame.Viewing;

        (float rx, float ry, float rz) = AngleVectors.Right(
            viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);

        (float ux, float uy, float uz) = AngleVectors.Up(
            viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);

        IReadOnlyList<ParticleBatch> batches = _particles.Build(
            new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
            new Vector3(rx, ry, rz),
            new Vector3(ux, uy, uz),
            map?.ParticleMaterials ?? NoParticleMaterials);

        Quads = 0;

        foreach (ParticleBatch built in batches)
        {
            Quads += built.Corners.Count / 6;

            // What each particle material drew and where, so a glow seen on screen can be named from the log.
            if (_renderLog.IsEnabled(LogLevel.Debug) && built.Corners.Count > 0)
            {
                string named = map?.ParticleMaterials?
                    .FirstOrDefault(pair => pair.Value.Equals(built.Material)).Key ?? "?";
                DetailSpriteVertex first = built.Corners[0];

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                _renderLog.LogDebug(
                    "{Message}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"particle batch {named} at {frame.Tick}: {built.Corners.Count / 6} quads, first corner ({first.X:0} {first.Y:0} {first.Z:0}) rgba ({first.Red:0.00} {first.Green:0.00} {first.Blue:0.00} {first.Alpha:0.00})"));
            }
        }

        // **Entity sprites join the particle batches rather than getting a pass of their own**
        // (B378). A sprite IS a particle with one quad — a texture, a blend and six corners — so
        // it goes through the same renderer, and a second pass would agree with this one only
        // until one of them gained a feature.
        (float ahead, float across, float above) = AngleVectors.Forward(
            viewing.Angles.Pitch, viewing.Angles.Yaw);

        // **The occlusion gate: `PixelVisibility_FractionVisible`** (B378, B521) — how much of each glow's proxy
        // the last frame's depth let through, counted by the device and approached over the fade time. One gate
        // for the entity sprites and the beams' halos. It was a line of sight, all or nothing (`GlowSight`, the
        // engine's fallback), which left a ceiling lamp seen at a slant a fifth brighter than TF2's.
        //
        // **The frame's real duration, where the engine's is the demo's** and so zero while paused: this camera
        // flies while paused, and a glow held where the pause found it would then be wrong for good.
        //
        // **`r_dopixelvisibility 0` is the engine's line of sight** (`GlowSight`, `c_pixel_visibility.cpp:567-570`),
        // and a user's config may say so (D69): brushes AND static props, as `MASK_OPAQUE` is — on
        // `koth_harvest_final` a lamp's halo sits under a roof of `corrugated_metal` props. Latched at the level's
        // load (`LevelInit`); the live reading only stops new queries.
        _pixelVisibility.DoPixelVisibility = frame.DoPixelVisibility;
        _pixelVisibility.BeginFrame(
            frame.FrameSeconds,
            occluding.OcclusionResult,
            glow => map?.Level is not { } level ||
                GlowSight.Visible(
                    new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
                    new Vector3(ahead, across, above),
                    glow,
                    (from, to) => level.Trace((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 0f).Fraction >= 1f));

        float Sees(GlowQuery glow) => _pixelVisibility.FractionVisible(glow);

        IReadOnlyList<ParticleBatch> glows = _sprites.Build(
            frame.Drawn,
            new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
            new Vector3(rx, ry, rz),
            new Vector3(ux, uy, uz),
            new Vector3(ahead, across, above),
            map?.SpriteMaterials ?? NoEngineSprites,
            Sees);

        // **Beams join the same batches**, for the reason sprites do: a beam is a strip of sprite-material quads.
        IReadOnlyList<ParticleBatch> drawnByClass = effectEntities(
            new Vector3(ahead, across, above), new Vector3(rx, ry, rz), new Vector3(ux, uy, uz), Sees);

        // **`CGlowOverlay::DrawOverlays`, after the world and its entities and before `PixelVisibility_EndCurrentView`**
        // (`viewrender.cpp:1444-1450`): the sun's core and glow, asking the same gate with screen-space proxies (B378).
        IReadOnlyList<ParticleBatch> sunGlow = _sun.Build(
            frame.Timeline?.Suns.At(frame.Tick) ?? [],
            new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
            new Vector3(ahead, across, above),
            viewing.FarZ,
            map?.SpriteMaterials ?? NoEngineSprites,
            Sees);

        if (sunGlow.Count > 0)
        {
            drawnByClass = [.. drawnByClass, .. sunGlow];
        }

        // `PixelVisibility_EndCurrentView`: the proxies this frame's view counts, read back by the next.
        occluding.SetOcclusionProxies(
            _pixelVisibility.EndView(PixelVisibilityView.From(viewing, Math.Max(1, frame.ViewportHeight))));

        if (drawnByClass.Count > 0)
        {
            glows = [.. glows, .. drawnByClass];
        }

        // **The value the builder USED, carried here rather than recounted** (B243). A picture
        // can only show a glow the camera happens to face, so the count is what says the pass
        // ran at all — and DRAWN beside SKIPPED is what separates "no sprites in this moment"
        // from "every sprite refused for a reason nobody printed".
        if (_renderLog.IsEnabled(LogLevel.Debug) &&
            (_sprites.Drawn > 0 || _sprites.Skipped > 0))
        {
            _renderLog.LogDebug(
                "{Message}",
                $"entity sprites: {_sprites.Drawn} drawn in {glows.Count} batches, " +
                $"{_sprites.Skipped} skipped");
        }

        if (glows.Count > 0)
        {
            List<ParticleBatch> both = [.. batches, .. glows];

            batches = both;
        }

        return batches;
    }

    /// <summary>What a frame's map holds, read out once a map.</summary>
    private ParticleMap? MapOf(in ParticleFrame frame)
    {
        if (!ReferenceEquals(frame.Loaded, _mapOf))
        {
            _mapOf = frame.Loaded;
            _map = frame.Loaded is { } loaded
                ? new ParticleMap(
                    loaded.Assets?.RocketTrail,
                    loaded.Assets?.ParticleSystemsByName,
                    loaded.Assets?.ParticleMaterials,
                    loaded.Assets?.SpriteMaterials,
                    loaded.Level)
                : null;
        }

        return _map;
    }

    /// <summary>The track for one entity that covers a given tick.</summary>
    /// <remarks>
    /// **An entity index does not identify a track**, because the engine reuses edict slots: this
    /// demo has 1,669 rocket tracks and a couple of thousand slots, so one index names many rockets
    /// across a match. Only the index PLUS the tick picks one.
    /// </remarks>
    private static ScenePropTrack? TrackAt(DemoTimeline? timeline, int entity, int tick)
    {
        if (timeline is null)
        {
            return null;
        }

        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.EntityIndex != entity || track.Keyframes.Count == 0)
            {
                continue;
            }

            if (track.FirstTick <= tick && tick <= track.Keyframes[^1].Tick)
            {
                return track;
            }
        }

        return null;
    }
}
