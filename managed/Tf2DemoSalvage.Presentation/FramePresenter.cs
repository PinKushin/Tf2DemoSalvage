using System;
using System.Collections.Generic;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What the window knows about the frame it is drawing: the demo, the map, the clock, the size, the settings.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Game">The opened install, or null.</param>
/// <param name="Tick">The transport's whole tick.</param>
/// <param name="ShownTick">The moment last shown, fraction included — what the players are posed at.</param>
/// <param name="Playing">Whether the demo is playing.</param>
/// <param name="FirstPerson">Whether the view is a player's own.</param>
/// <param name="Followed">The entity the camera follows, or null.</param>
/// <param name="ViewportWidth">The viewport's width in pixels.</param>
/// <param name="ViewportHeight">The viewport's height in pixels.</param>
/// <param name="FrameSeconds">The frame's real duration.</param>
/// <param name="DemoFrameSeconds">The demo time this frame covered: zero while paused.</param>
/// <param name="DoPixelVisibility">`r_dopixelvisibility`.</param>
/// <param name="DrawViewmodel">`r_drawviewmodel`.</param>
/// <param name="HealTargetMarker">`hud_medichealtargetmarker`.</param>
/// <param name="MaxDecals">`r_decals`.</param>
/// <param name="MaxModelDecals">`r_maxmodeldecal`.</param>
public readonly record struct ViewFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    GameContent? Game,
    int Tick,
    double ShownTick,
    bool Playing,
    bool FirstPerson,
    int? Followed,
    int ViewportWidth,
    int ViewportHeight,
    float FrameSeconds,
    double DemoFrameSeconds,
    bool DoPixelVisibility,
    bool DrawViewmodel,
    bool HealTargetMarker,
    int MaxDecals,
    int MaxModelDecals);

/// <summary>
/// Everything a frame of a demo makes around its models — light styles, decals, the client's own sounds, impacts,
/// particles, beams and ropes, the target ID — as ONE presenter that owns each of those (D92: a presenter composes
/// downward).
/// </summary>
/// <remarks>
/// **The composition left `MainForm`** (D214, thin view slice 5). The window constructed eight presenters, shared one
/// `BulletStrikes` among four of them by hand, and called them in order from `DrawParticles`; that order, and what is
/// handed from one to the next, was behaviour living in a view. The window now constructs this, hands it what it knows
/// of the frame (<see cref="ViewFrame"/>), and calls <see cref="Draw"/>.
///
/// **What each part decides is still that part's.** This is the order they run in, the sounds one hands back for the
/// sound presenter to emit, and the two things two of them share: the map's walk and a light style's value.
/// </remarks>
public sealed partial class FramePresenter
{
    private readonly EntityModelSet _models;

    private readonly MomentScene _moment;

    private readonly MomentPresenter _moments;

    private readonly SoundPresenter _sound;

    private readonly ILogger _renderLog;

    private readonly ILogger _audioLog;

    /// <summary>The effects running for this demo.</summary>
    private readonly ParticleEffects _particles = new();

    /// <summary>Where a bullet stops and which a player stopped — `FX_FireBullets`' trace (tf_fx_shared.cpp).</summary>
    private readonly BulletStrikes _strikes;

    /// <summary>Every one-shot particle effect the demo starts (B415).</summary>
    private readonly BurstEffects _bursts;

    /// <summary>The decals on the world, its props and the players' models (B415, B421).</summary>
    private readonly DecalEffects _decals;

    /// <summary>The light styles, the lightmaps they rebuild and the faces under the dynamic lights (B424, B425).</summary>
    private readonly LightStyleEffects _lights;

    /// <summary>Every impact's debris and dust, every spark and every tracer line (B415).</summary>
    private readonly ImpactParticles _impacts;

    /// <summary>A frame's particles in the order they are made: trails, effects, sprites, strips (B373, B378).</summary>
    private readonly FrameParticles _frameParticles;

    /// <summary>The beams, sprite trails and ropes, and the holiday lights the ropes dispatch (B231, B475, B477, B478).</summary>
    private readonly EffectEntities _effectEntities;

    /// <summary>What the local player is looking at, for the target ID and the weapon pickup prompt.</summary>
    private readonly IdTargets _idTargets;

    /// <summary>The map as the engine's walks see it — the decals', which the lights and the impact colours share. One delegate, made once.</summary>
    private readonly Func<LoadedMap, DecalWorld> _walk;

    /// <summary>A light style's value now, for the impact colours. One delegate, made once.</summary>
    private readonly Func<int, float> _styleScale;

    /// <summary>What the window knows of the frame being drawn or sought.</summary>
    private ViewFrame _frame;

    /// <summary>The particle batches the last frame drew, for <see cref="Pick"/>.</summary>
    private IReadOnlyList<ParticleBatch> _lastParticles = [];

    /// <summary>A frame's presenters over the model set, the moment and the sound presenter the window holds.</summary>
    /// <param name="models">The posed entities.</param>
    /// <param name="moment">The scene at the shown moment.</param>
    /// <param name="moments">What samples and poses that moment.</param>
    /// <param name="sound">Where a sound a part hands back is emitted.</param>
    /// <param name="render">The render log.</param>
    /// <param name="map">The map log.</param>
    /// <param name="audio">The audio log, where a seek's skip is reported.</param>
    public FramePresenter(
        EntityModelSet models,
        MomentScene moment,
        MomentPresenter moments,
        SoundPresenter sound,
        ILogger render,
        ILogger map,
        ILogger audio)
    {
        _models = models;
        _moment = moment;
        _moments = moments;
        _sound = sound;
        _renderLog = render;
        _audioLog = audio;
        _strikes = new BulletStrikes(models, render);
        _bursts = new BurstEffects(_particles, models, _strikes, render);
        _decals = new DecalEffects(models, _strikes, render);
        _lights = new LightStyleEffects(render);
        _impacts = new ImpactParticles(models, _strikes, render);
        _idTargets = new IdTargets(_strikes, map);
        _frameParticles = new FrameParticles(_particles, render);
        _effectEntities = new EffectEntities(models, render);
        _walk = _decals.WorldOf;
        _styleScale = _lights.Scale;
    }

    /// <summary>Whether every glow's brightness has settled, for a capture that waits on it.</summary>
    public bool GlowsSettled => _frameParticles.GlowsSettled;

    /// <summary>The glows still moving, for the log of what a capture is waiting on.</summary>
    /// <returns>One clause a glow.</returns>
    public string GlowsUnsettled() => _frameParticles.GlowsUnsettled();

    /// <summary>How many trails and bursts are running and how many quads the last frame built, for an instrument.</summary>
    public (int Trails, int Bursts, int Quads) ParticleCounts => (_particles.Count, _particles.BurstCount, _frameParticles.Quads);

    /// <summary>Forgets what belonged to the map that is going.</summary>
    public void ClearMap()
    {
        // Tracer paths, blood, dispatch points and beam starts were fixed against the map that is going.
        _bursts.ClearMap();

        // The decal pool is the map's; the device's copy goes with ClearWorld below. So are the impact effects and
        // everything they read of the map.
        _decals.ClearMap();
        _lights.ClearMap();
        _strikes.Clear();
        _impacts.ClearMap();
    }

    /// <summary>A new level: its glows start from nothing, and its particle materials' sheets are the ones a system reads.</summary>
    /// <param name="map">The map that was read.</param>
    /// <param name="doPixelVisibility">`r_dopixelvisibility`.</param>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
    public void LevelInit(LoadedMap map, bool doPixelVisibility)
    {
        ArgumentNullException.ThrowIfNull(map);

        // `CPixelVisibilitySystem::LevelInitPreEntity`: the level's glows start from nothing, and whether the
        // device counts their occlusion is decided here for the whole level (B378).
        _frameParticles.LevelInit(doPixelVisibility);

        // A collection's `m_Sheet` is its material's, which `Lifetime From Sequence` reads at birth; null for a
        // material with none, as the engine's pointer is.
        _particles.Sheets = map.Assets?.ParticleMaterials is { } sheetsFrom
            ? system => sheetsFrom.TryGetValue(ParticleEffects.MaterialOf(system), out ParticleMaterial material) &&
                        material.Sequences.Count > 0
                ? material.Sequences
                : null
            : null;
    }

    /// <summary>The HUD's state with what the local player is looking at.</summary>
    /// <param name="state">The HUD's state.</param>
    /// <param name="tick">The HUD's tick.</param>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="eye">The first-person camera, asked for only once the local player is known to be able to look.</param>
    /// <returns>The state with its target.</returns>
    public HudState WithIdTargets(HudState state, int tick, in ViewFrame frame, Func<FreeCamera?> eye) =>
        _idTargets.With(state, tick, new IdTargetFrame(frame.Timeline, frame.Loaded, frame.Game, _moments.PlayersShownAt(tick), eye));

    /// <summary>`TF2VIEW_PICK`: what lies under a pixel of the last frame, as one line — <see cref="PickReport"/>.</summary>
    /// <param name="asked">The request, or null.</param>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="viewing">The camera the last frame was set up with.</param>
    /// <returns>The line, or null.</returns>
    public string? Pick(string? asked, in ViewFrame frame, FreeCamera? viewing) =>
        PickReport.Describe(
            asked,
            new PickFrame(
                frame.Loaded,
                frame.Tick,
                viewing,
                frame.ViewportWidth,
                frame.ViewportHeight,
                _moment.Drawn,
                _lastParticles,
                _bursts.Bursts,
                _particles));

    /// <summary>What a bullet is traced against this frame.</summary>
    private BulletScene BulletSceneNow => new(_frame.Timeline, _frame.Loaded?.Level, _moments.Source, _frame.ShownTick, _frame.Playing);

    /// <summary>What the decals are stepped against at a tick this frame shows.</summary>
    private DecalFrame DecalFrameAt(int tick) =>
        new(
            _frame.Timeline,
            _frame.Loaded,
            _frame.Game,
            tick,
            _moments.PlayersShownAt(tick),
            BulletSceneNow,
            _frame.MaxDecals,
            _frame.MaxModelDecals,
            _sound.Scripts,
            Replaying);

    /// <summary>Steps this frame's particle effects and hands their quads to the device (B373).</summary>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="viewing">The camera this frame, whose basis the quads face.</param>
    /// <param name="device">The device the frame is drawn with.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Beside `SetCamera` because it needs the same camera** — a billboard faces the view, so the
    /// quads cannot be built until the frame's camera is known, and this is the one per-frame view
    /// setup. **What is decided here is the presenters'** (D214): this is the order they are called in, the sounds they
    /// hand back, and the device.
    /// </remarks>
    public void Draw(in ViewFrame frame, FreeCamera viewing, Device3D device)
    {
        ArgumentNullException.ThrowIfNull(viewing);
        ArgumentNullException.ThrowIfNull(device);

        DrawOver(frame, viewing, device);
    }

    /// <summary>The same, over any device (D214).</summary>
    /// <param name="frame">What the window knows of the frame.</param>
    /// <param name="viewing">The camera this frame.</param>
    /// <param name="device">The device the frame is drawn with.</param>
    internal void DrawOver(in ViewFrame frame, FreeCamera viewing, IFrameDevice device)
    {
        _frame = frame;

        int tick = _frame.Tick;

        _lights.Step(new LightStyleFrame(_frame.Timeline, _frame.Loaded, tick, _moment.WorldLights, _moment.Drawn, _walk), device);

        // With the tracers' player pass, after the model pass has posed this frame's hitboxes.
        // A client bullet's landing sound comes back for the sound presenter: presenters do not reach sideways (D92).
        foreach (SceneSound landed in _decals.Step(DecalFrameAt(tick), device))
        {
            _sound.Emit(landed);
        }

        _sound.StepClientSounds(
            new ClientSoundFrame(
                _frame.Timeline,
                _frame.Loaded,
                _frame.Game,
                tick,
                new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z),
                _moments.Source,
                _frame.ShownTick,
                _frame.Playing,
                Replaying,
                _frame.DemoFrameSeconds),
            _models);

        ImpactFrame impacts = new(
            _frame.Timeline, _frame.Loaded, tick, BulletSceneNow, viewing, _frame.ViewportWidth, _styleScale, _walk);

        _impacts.Step(impacts);

        IReadOnlyList<ParticleBatch> batches = [];
        ParticleFrame particles = new(
            _frame.Timeline,
            _frame.Loaded,
            tick,
            viewing,
            _moment.Drawn,
            _frame.FrameSeconds,
            _frame.ViewportHeight,
            _frame.DoPixelVisibility);

        if (_frameParticles.Trails(particles))
        {
            // The one-shot effects (D214). A tracer's near-miss whiz comes back for the sound presenter: presenters do
            // not reach sideways (D92).
            foreach (SceneSound whiz in _bursts.Step(new BurstFrame(
                         _frame.Timeline,
                         _frame.Loaded,
                         _frame.Game,
                         tick,
                         viewing,
                         _moments.Players,
                         _moments.Source,
                         _frame.ShownTick,
                         _frame.Playing,
                         _frame.FirstPerson,
                         _frame.Followed,
                         _frame.DrawViewmodel,
                         _frame.HealTargetMarker,
                         _sound.Scripts,
                         Replaying)))
            {
                _sound.Emit(whiz);
            }

            EffectEntityFrame strips = new(
                _frame.Timeline, _frame.Loaded, tick, viewing, _moment.Drawn, _frame.ViewportWidth, _moment.ViewerVisionFlags);

            batches = _frameParticles.Build(
                particles, device, (forward, right, up, sees) => _effectEntities.Build(strips, forward, right, up, sees));
        }

        batches = _impacts.With(batches, impacts);
        _lastParticles = batches;

        device.SetParticles(batches);
    }
}
