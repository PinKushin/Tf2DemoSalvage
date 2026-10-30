using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's impact effects, sparks and tracer lines are stepped and drawn against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Bullets">What a bullet is traced against this frame.</param>
/// <param name="Viewing">The frame's camera.</param>
/// <param name="ViewportWidth">The viewport's width in pixels, which a tracer line's width is measured against.</param>
/// <param name="StyleScale">A light style's value now, for a surface's colour.</param>
/// <param name="Walk">The map as the engine's light walk sees it, built once per map by whoever owns it.</param>
public readonly record struct ImpactFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    int Tick,
    BulletScene Bullets,
    FreeCamera Viewing,
    int ViewportWidth,
    Func<int, float> StyleScale,
    Func<LoadedMap, DecalWorld> Walk);

/// <summary>What a loaded map holds that the impact effects read, as data (D214): a test supplies it by hand.</summary>
/// <param name="Level">The map's geometry.</param>
/// <param name="Impacts">Every bullet the map stops.</param>
/// <param name="ImpactDecals">The surface a bullet struck, or null when the map's content would not load.</param>
/// <param name="LightSamples">The map's light samples, or null when they would not read.</param>
/// <param name="Lighting">The level's lights, for a static prop's colour.</param>
/// <param name="Thumbnail">A texdata's thumbnail, decoded; null when the map's assets would not load.</param>
/// <param name="ParticleMaterials">The particle materials by name; null when the map's assets would not load.</param>
/// <param name="Walk">The map as the engine's light walk sees it.</param>
internal sealed record ImpactMap(
    MapLevel Level,
    IReadOnlyList<ShotImpact> Impacts,
    ImpactDecals? ImpactDecals,
    BspLightSamples? LightSamples,
    LevelLighting Lighting,
    Func<int, SurfaceThumbnail?>? Thumbnail,
    IReadOnlyDictionary<string, ParticleMaterial>? ParticleMaterials,
    Func<DecalWorld> Walk);

/// <summary>
/// The legacy particles of a demo: the debris, dust and sparks of every bullet's impact (`ImpactCallback`'s
/// `PerformCustomEffects`), every spark temp entity (`IEffects::Sparks`, `::MetalSparks`, `::Ricochet`) and every
/// `"Tracer"` dispatch's line (`TracerCallback`, `CFXDiscreetLine`) — B415.
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 3), where it was `StepImpactEffects`, `StepSparks`,
/// `WithImpactEffects`, `AddTracerLines`, `ColourOf` and `WorldTrace` with their ten fields: the same arithmetic, the
/// same order, the same seeds. The view's side is the constructor, one <see cref="Step"/> and one <see cref="With"/> a
/// frame, and <see cref="ClearMap"/>.
///
/// **A presenter (D90): it decides WHICH effects run and what they are drawn against.** Simulating one is
/// `ImpactEffectRunner` and `ImpactEffects`, Scene's; the quads it returns are drawn by whoever draws particles.
/// </remarks>
public sealed class ImpactParticles
{
    /// <summary>How long an impact effect is offered: a fleck lives three seconds, which is 198 ticks.</summary>
    private const int ImpactWindowTicks = 200;

    /// <summary>How long a `"Tracer"` dispatch is offered: 8192 units at 5000 a second is 1.64 s.</summary>
    private const int TracerLineWindowTicks = 120;

    /// <summary>`TRACER_FLAG_USEATTACHMENT`.</summary>
    private const int TracerUseAttachment = 0x2;

    /// <summary>How long a spark is offered — ten seconds, past a magnitude-15 electric spark's thirty-second ceiling only in theory.</summary>
    private const int SparkWindowTicks = 660;

    private readonly IPosedEntities _models;

    private readonly BulletStrikes _strikes;

    private readonly ILogger _renderLog;

    /// <summary>The frame being stepped or drawn, for the methods <see cref="Step"/> and <see cref="With"/> call.</summary>
    private ImpactFrame _frame;

    /// <summary>What that frame's map holds.</summary>
    private ImpactMap? _map;

    /// <summary>The map <see cref="_map"/> was read out of.</summary>
    private LoadedMap? _mapOf;

    /// <summary>`GetColorForSurface` over this map, built with the first impact effect that asks.</summary>
    private SurfaceColour? _surfaceColour;

    /// <summary>Each texdata's thumbnail, decoded the first time a colour asks for it.</summary>
    private readonly Dictionary<int, SurfaceThumbnail?> _thumbnails = [];

    /// <summary>The impact effects in flight.</summary>
    private readonly ImpactEffectRunner _impactEffects = new();

    /// <summary>The impacts in the effects' window this tick, reused.</summary>
    private readonly List<(int Index, ShotImpact Impact)> _impactsNow = [];

    /// <summary>Each impact effect material's corners this frame, the lists reused.</summary>
    private readonly Dictionary<string, List<DetailSpriteVertex>> _impactCorners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every effect dispatch in the tracer window this frame, reused.</summary>
    private readonly List<(int Index, SceneEffectDispatch Dispatch)> _tracerDispatchesNow = [];

    /// <summary>Each `"Tracer"` dispatch's line by its index in the feed, fixed at first sight; null for one too short.</summary>
    private readonly Dictionary<int, DiscreetLine?> _tracerLines = [];

    /// <summary>Every spark temp entity's effect, stepped by ticks as the impacts' are.</summary>
    private readonly ImpactEffectRunner _sparkEffects = new();

    /// <summary>Every spark in the window this tick, reused.</summary>
    private readonly List<(int Index, SceneSpark Spark)> _sparksNow = [];

    /// <summary>The window's sparks as the runner's events, reused.</summary>
    private readonly List<(int Index, int Tick, int Seed)> _sparkEvents = [];

    /// <summary>The frame's batches with the impact batches after them, reused; see <see cref="WithImpactEffects"/>.</summary>
    private readonly List<ParticleBatch> _withImpacts = [];

    /// <summary>The materials of the frame being built, which <see cref="_alphaOf"/> reads.</summary>
    private IReadOnlyDictionary<string, ParticleMaterial>? _materials;

    /// <summary><see cref="AlphaOf"/>, made once rather than as a capturing lambda per frame.</summary>
    private readonly Func<string, float> _alphaOf;

    /// <summary>The view's own: the model set it draws with.</summary>
    /// <param name="models">The posed entities, for a tracer's barrel.</param>
    /// <param name="strikes">Which bullets a player stopped.</param>
    /// <param name="render">The render log.</param>
    public ImpactParticles(EntityModelSet models, BulletStrikes strikes, ILogger render)
        : this((IPosedEntities)models, strikes, render)
    {
    }

    /// <summary>Over any posed entities.</summary>
    /// <param name="models">The posed entities, for a tracer's barrel.</param>
    /// <param name="strikes">Which bullets a player stopped.</param>
    /// <param name="render">The render log.</param>
    public ImpactParticles(IPosedEntities models, BulletStrikes strikes, ILogger render)
    {
        _models = models;
        _strikes = strikes;
        _renderLog = render;
        _alphaOf = AlphaOf;
    }

    /// <summary>How many impact effects and how many spark effects are running, for an instrument.</summary>
    internal (int Impacts, int Sparks) Running => (_impactEffects.Count, _sparkEffects.Count);

    /// <summary>Forgets what belonged to the map that is going: its surface colours, its effects and its tracer lines.</summary>
    public void ClearMap()
    {
        _surfaceColour = null;
        _thumbnails.Clear();
        _impactEffects.Clear();
        _sparkEffects.Clear();
        _tracerLines.Clear();
    }

    /// <summary>Starts and steps every impact effect and spark in the frame's windows.</summary>
    /// <param name="frame">What this frame is stepped against.</param>
    public void Step(in ImpactFrame frame) => StepOver(frame, MapOf(frame));

    /// <summary>The same, over what the frame's map holds as data (D214).</summary>
    /// <param name="frame">What this frame is stepped against; its map is not read here.</param>
    /// <param name="map">What the map holds, or null for none.</param>
    internal void StepOver(in ImpactFrame frame, ImpactMap? map)
    {
        _frame = frame;
        _map = map;

        StepImpactEffects(frame.Tick);
        StepSparks(frame.Tick);
    }

    /// <summary>The impact effects' quads added to the frame's particle batches, one per material.</summary>
    /// <param name="batches">The frame's particle batches so far.</param>
    /// <param name="frame">What this frame is drawn against.</param>
    /// <returns>The batches with the impact effects', the sparks' and the tracer lines' after them; the same list when there are none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batches"/> is null.</exception>
    /// <remarks>The corner lists are handed to the device and read by this frame's draw, before the next refill.</remarks>
    public IReadOnlyList<ParticleBatch> With(IReadOnlyList<ParticleBatch> batches, in ImpactFrame frame)
    {
        ArgumentNullException.ThrowIfNull(batches);

        return WithOver(batches, frame, MapOf(frame));
    }

    /// <summary>The same, over what the frame's map holds as data (D214).</summary>
    /// <param name="batches">The frame's particle batches so far.</param>
    /// <param name="frame">What this frame is drawn against; its map is not read here.</param>
    /// <param name="map">What the map holds, or null for none.</param>
    /// <returns>The batches, as <see cref="With"/> returns them.</returns>
    internal IReadOnlyList<ParticleBatch> WithOver(IReadOnlyList<ParticleBatch> batches, in ImpactFrame frame, ImpactMap? map)
    {
        _frame = frame;
        _map = map;

        return WithImpactEffects(batches, frame.Viewing);
    }

    /// <summary>What a frame's map holds, read out once a map.</summary>
    private ImpactMap? MapOf(in ImpactFrame frame)
    {
        if (!ReferenceEquals(frame.Loaded, _mapOf))
        {
            _mapOf = frame.Loaded;
            _map = frame.Loaded is { } loaded ? Unpack(loaded, frame.Walk) : null;
        }

        return _map;
    }

    private static ImpactMap Unpack(LoadedMap loaded, Func<LoadedMap, DecalWorld> walk)
    {
        MapAssets? assets = loaded.Assets;

        return new ImpactMap(
            loaded.Level,
            loaded.Impacts,
            loaded.ImpactDecals,
            loaded.LightSamples,
            loaded.Lighting,
            assets is null ? null : texdata => Thumbnail(assets, texdata),
            assets?.ParticleMaterials,
            () => walk(loaded));
    }

    /// <summary>A texdata's thumbnail as `GetColorForSurface` samples it, or null for a texture with none.</summary>
    private static SurfaceThumbnail? Thumbnail(MapAssets assets, int texdata) =>
        texdata >= 0 && texdata < assets.Textures.Count &&
        assets.Textures[texdata] is { Thumbnail: { } small } texture
            ? new SurfaceThumbnail(
                small.Image.ToRgba(small.Width, small.Height), small.Width, small.Height,
                texture.MappingWidth, texture.MappingHeight)
            : null;

    /// <summary>Starts and steps the debris, dust and sparks of every impact in the window (B415).</summary>
    /// <remarks>
    /// **Only where `Impact` returned true**, as `ImpactCallback` asks: a bullet with a decal on the world, not one a
    /// player stopped. The surface's colour is asked only by the effects that tint.
    /// </remarks>
    private void StepImpactEffects(int tick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline ||
            _map is not { ImpactDecals: { } impacts, LightSamples: { } samples, Thumbnail: { } thumbnail } loaded)
        {
            return;
        }

        MapLevel level = loaded.Level;

        TickWindow.Between(loaded.Impacts, static impact => impact.Tick, tick - ImpactWindowTicks, tick, _impactsNow);

        _impactEffects.Advance(
            _impactsNow,
            tick,
            timeline.IntervalPerTick,
            (_, impact, random) =>
            {
                // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
                if (impact.WaterEntry is not null ||
                    _strikes.StruckPlayer(impact, tick, _frame.Bullets) ||
                    impacts.For(impact) is null ||
                    impacts.Surface(impact) is not { } surface)
                {
                    return null;
                }

                Vector3 start = new(impact.Start.X, impact.Start.Y, impact.Start.Z);
                Vector3 end = new(impact.End.X, impact.End.Y, impact.End.Z);
                Vector3 normal = new(impact.Normal.X, impact.Normal.Y, impact.Normal.Z);

                return ImpactEffects.Perform(
                    surface.GameMaterial,
                    surface.Flags,
                    end,
                    normal,
                    Vector3.Normalize(end - start),
                    () =>
                    {
                        // `GetColorForSurface`: a static prop asks the prop manager, the world asks `R_LightVec`.
                        (float r, float g, float b) = impact.StaticProp >= 0
                            ? SurfaceColour.OfStaticProp(
                                loaded.Lighting.LightingAt(end.X, end.Y, end.Z), loaded.Lighting.SunAt(end.X, end.Y, end.Z), end, normal)
                            : ColourOf(loaded, samples, thumbnail).At(start, end);

                        return new Vector3(r, g, b);
                    },
                    WorldTrace(level),
                    random);
            },
            WorldTrace(level));
    }

    private IReadOnlyList<ParticleBatch> WithImpactEffects(IReadOnlyList<ParticleBatch> batches, FreeCamera viewing)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_map?.ParticleMaterials is not { } materials)
        {
            return batches;
        }

        foreach (List<DetailSpriteVertex> corners in _impactCorners.Values)
        {
            corners.Clear();
        }

        (float fx, float fy, float fz) = AngleVectors.Forward(viewing.Angles.Pitch, viewing.Angles.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);
        Vector3 eye = new(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z);

        // **Nothing here is made per frame** (B599): the runners are named rather than put in an array, the alpha lookup
        // is one delegate over the frame's materials field, and the batch list is reused — `SetParticles` copies what it
        // keeps before the next frame refills it, as it already does for the corner lists.
        _materials = materials;

        BuildRunner(_impactEffects, eye, new Vector3(fx, fy, fz), new Vector3(rx, ry, rz), new Vector3(ux, uy, uz));
        BuildRunner(_sparkEffects, eye, new Vector3(fx, fy, fz), new Vector3(rx, ry, rz), new Vector3(ux, uy, uz));

        AddTracerLines(eye, new Vector3(fx, fy, fz));

        if (!AnyCorners())
        {
            return batches;
        }

        List<ParticleBatch> all = _withImpacts;

        all.Clear();
        all.AddRange(batches);

        foreach ((string name, List<DetailSpriteVertex> corners) in _impactCorners)
        {
            if (corners.Count > 0 && materials.TryGetValue(name, out ParticleMaterial material))
            {
                all.Add(new ParticleBatch(corners, material));

                // What each impact material drew and where, so a glow seen on screen can be named from the log.
                if (_renderLog.IsEnabled(LogLevel.Debug))
                {
                    DetailSpriteVertex first = corners[0];
                    DetailSpriteVertex lowest = corners.MinBy(static corner => corner.Z);

                    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                    _renderLog.LogDebug(
                        "{Message}",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"impact batch {name} at {_frame.Tick}: {corners.Count / 6} quads, first corner ({first.X:0} {first.Y:0} {first.Z:0}), lowest ({lowest.X:0} {lowest.Y:0} {lowest.Z:0}) rgba ({first.Red:0.00} {first.Green:0.00} {first.Blue:0.00} {first.Alpha:0.00})"));
                }
            }
        }

        return all;
    }

    /// <summary>One runner's quads, if it has effects in flight.</summary>
    private void BuildRunner(ImpactEffectRunner runner, Vector3 eye, Vector3 forward, Vector3 right, Vector3 up)
    {
        if (runner.Count > 0)
        {
            runner.Build(eye, forward, right, up, _alphaOf, _impactCorners);
        }
    }

    /// <summary>Whether any impact material has corners this frame.</summary>
    private bool AnyCorners()
    {
        foreach (List<DetailSpriteVertex> corners in _impactCorners.Values)
        {
            if (corners.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A material's alpha, or 1 for one the map does not name — read from this frame's materials.</summary>
    private float AlphaOf(string name) =>
        _materials is { } materials && materials.TryGetValue(name, out ParticleMaterial material) ? material.Alpha : 1f;

    /// <summary>Draws every `"Tracer"` dispatch in flight as `CFXDiscreetLine` quads — `TracerCallback` (B415).</summary>
    /// <remarks>
    /// **The start is read ONCE**, as `GetTracerOrigin` reads the attachment when the dispatch arrives: the line then
    /// flies its own path and does not follow the barrel. A server-side shooter's `m_vStart` is the multiplayer
    /// sentinel (999, 999, 999) from `ComputeTracerStartPosition`, "so the results … should never actually get used";
    /// so a dispatch whose entity is not posed yet is asked again next frame rather than fixed there. Stepped by
    /// ticks: `Draw` runs `Update` first, so the line is one tick old on the tick it arrives.
    /// </remarks>
    private void AddTracerLines(Vector3 eye, Vector3 forward)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline)
        {
            return;
        }

        int tick = _frame.Tick;

        TickWindow.Between(
            timeline.Dispatches.All, static dispatch => dispatch.Tick, tick - TracerLineWindowTicks, tick, _tracerDispatchesNow);

        if (!_impactCorners.TryGetValue(DiscreetLines.TracerMaterial, out List<DetailSpriteVertex>? corners))
        {
            corners = [];
            _impactCorners[DiscreetLines.TracerMaterial] = corners;
        }

        float halfWidth = _frame.ViewportWidth * 0.5f;

        foreach ((int index, SceneEffectDispatch dispatch) in _tracerDispatchesNow)
        {
            if (!string.Equals(timeline.Dispatches.Names.Name(dispatch.Name), "Tracer", StringComparison.Ordinal))
            {
                continue;
            }

            if (!_tracerLines.TryGetValue(index, out DiscreetLine? line))
            {
                (float X, float Y, float Z)? barrel = (dispatch.Flags & TracerUseAttachment) != 0
                    ? _models.AttachmentPosition(dispatch.Entity, dispatch.Attachment)
                    : null;

                if (barrel is null && (dispatch.Flags & TracerUseAttachment) != 0 && !_models.IsPosed(dispatch.Entity))
                {
                    continue;
                }

                (float X, float Y, float Z) start = barrel ?? dispatch.Start;

                line = DiscreetLines.Tracer(
                    new Vector3(start.X, start.Y, start.Z),
                    new Vector3(dispatch.Origin.X, dispatch.Origin.Y, dispatch.Origin.Z),
                    dispatch.Scale != 0f ? dispatch.Scale : DiscreetLines.TracerSpeed,
                    SeededDraw.For(SeededDraw.Of(index, 0, salt: 3)));

                _tracerLines[index] = line;
            }

            if (line is { } flying)
            {
                DiscreetLines.Draw(
                    flying, (tick - dispatch.Tick + 1) * timeline.IntervalPerTick, eye, forward, halfWidth, corners);
            }
        }
    }

    /// <summary>Brings every spark's effect to this tick — `IEffects::Sparks`, `::MetalSparks` and `::Ricochet` (B415).</summary>
    /// <remarks>*Not built:* `FX_RicochetSound` for an armor ricochet.</remarks>
    private void StepSparks(int tick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || _map?.Level is not { } level)
        {
            return;
        }

        IReadOnlyList<SceneSpark> sparks = timeline.Sparks.All;

        TickWindow.Between(sparks, static spark => spark.Tick, tick - SparkWindowTicks, tick, _sparksNow);

        _sparkEvents.Clear();

        foreach ((int index, SceneSpark spark) in _sparksNow)
        {
            _sparkEvents.Add((index, spark.Tick, SeededDraw.Of(index, 0, salt: 4)));
        }

        Func<Vector3, Vector3, BspTrace> trace = WorldTrace(level);

        _sparkEffects.Advance(
            _sparkEvents,
            tick,
            timeline.IntervalPerTick,
            (index, random) =>
            {
                SceneSpark spark = sparks[index];
                Vector3 at = new(spark.Position.X, spark.Position.Y, spark.Position.Z);
                Vector3 toward = new(spark.Direction.X, spark.Direction.Y, spark.Direction.Z);

                return spark.Kind == SparkKind.Electric
                    ? ImpactEffects.Sparks(at, spark.Magnitude, spark.TrailLength, toward, trace, random)
                    : ImpactEffects.MetalSparks(at, toward, random);
            },
            trace);
    }

    /// <summary>The world trace the legacy particles collide with — `MASK_SOLID_BRUSHONLY`, terrain included.</summary>
    private static Func<Vector3, Vector3, BspTrace> WorldTrace(MapLevel level) =>
        (from, to) => level.TraceBrushOnly((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 0f);

    private SurfaceColour ColourOf(ImpactMap loaded, BspLightSamples samples, Func<int, SurfaceThumbnail?> decode) =>
        _surfaceColour ??= new SurfaceColour(loaded.Walk(), samples, texdata =>
        {
            if (!_thumbnails.TryGetValue(texdata, out SurfaceThumbnail? thumbnail))
            {
                thumbnail = decode(texdata);

                _thumbnails[texdata] = thumbnail;
            }

            return thumbnail;
        })
        {
            StyleScale = _frame.StyleScale,
        };
}
