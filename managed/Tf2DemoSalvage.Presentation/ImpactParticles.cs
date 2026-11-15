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
/// <param name="DrawMonitors">`cl_drawmonitors`: whether the monitors' views run, and so their <c>DrawEffects</c> (B736).</param>
public readonly record struct ImpactFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    int Tick,
    BulletScene Bullets,
    FreeCamera Viewing,
    int ViewportWidth,
    Func<int, float> StyleScale,
    Func<LoadedMap, DecalWorld> Walk,
    bool DrawMonitors);

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

    /// <summary>The main view's emitters, singleton particles and client-side effects (B734).</summary>
    private readonly ImpactRenderables _main = new();

    /// <summary>The 3D sky's, each water view's and each monitor's own (B734).</summary>
    private readonly ImpactRenderables _sky = new();
    private readonly List<ImpactRenderables> _water = [];
    private readonly List<ImpactRenderables> _monitors = [];
    private readonly List<IReadOnlyList<ParticleBatch>> _waterBuilt = [];
    private readonly List<IReadOnlyList<ParticleBatch>> _monitorsBuilt = [];

    /// <summary>Each view's collation of an emitter's box and its place there, made once per view (B599).</summary>
    private readonly BoxCollation _boxes = new();
    private readonly Func<(Vector3 Min, Vector3 Max), bool> _mainSees;
    private readonly Func<(Vector3 Min, Vector3 Max), int> _mainPlace;
    private readonly Func<(Vector3 Min, Vector3 Max), bool> _skySees;
    private readonly Func<(Vector3 Min, Vector3 Max), int> _skyPlace;
    private readonly List<(Func<(Vector3 Min, Vector3 Max), bool> Sees, Func<(Vector3 Min, Vector3 Max), int> Place)> _waterViews = [];
    private readonly List<(Func<(Vector3 Min, Vector3 Max), bool> Sees, Func<(Vector3 Min, Vector3 Max), int> Place)> _monitorViews = [];

    /// <summary>The 3D sky's impact batches from the last build (B734); empty with no sky view.</summary>
    public IReadOnlyList<ParticleBatch> SkyBuilt { get; private set; } = [];

    /// <summary>Each water view's impact batches from the last build, in the views' order (B734).</summary>
    public IReadOnlyList<IReadOnlyList<ParticleBatch>> WaterBuilt => _waterBuilt;

    /// <summary>Each monitor view's impact batches from the last build, in the monitors' order (B734).</summary>
    public IReadOnlyList<IReadOnlyList<ParticleBatch>> MonitorsBuilt => _monitorsBuilt;

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

    /// <summary><see cref="DrawEffectsAt"/>, made once.</summary>
    private readonly Func<int, int> _drawEffectsAt;

    /// <summary>
    /// How many views run <c>DrawEffects</c> at a tick, each stepping every client-side effect once (B736): the main view,
    /// and every camera <c>DrawMonitors</c> walks while `cl_drawmonitors` is on.
    /// </summary>
    /// <remarks>
    /// <c>ViewDrawScene</c> ends with <c>clienteffects->DrawEffects( gpGlobals->frametime )</c> (`viewrender.cpp:1462-1463`),
    /// and its two callers are <c>RenderView</c>'s main view and <c>DrawOneMonitor</c> (`:3224`), which <c>DrawMonitors</c>
    /// runs for every active, non-dormant camera whether or not a monitor shows it (`:3240-3293`). Each effect's
    /// <c>Draw</c> runs its own <c>Update( frametime )</c> (`fx_quad.cpp:42-47`, `fx_discreetline.cpp:64-72`), so a camera
    /// in play ages them twice a frame. *Not reproduced:* <c>DrawOneMonitor</c>'s refusal of a fogged camera with no local
    /// player, as a demo always has one; and the monitor's own draw at the earlier step, as the effects step by ticks.
    /// </remarks>
    private int DrawEffectsAt(int tick) =>
        1 + (_frame.DrawMonitors && _frame.Timeline is { } timeline ? timeline.PointCameras.At(tick).Count : 0);

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
        _drawEffectsAt = DrawEffectsAt;
        _mainSees = box => _models.WorldView is not { } view || _boxes.Sees(view, box.Min, box.Max);
        _mainPlace = box => _models.WorldView is { } view ? _boxes.Place(view, box.Min, box.Max) : 0;
        _skySees = box => _models.WorldView is { } view && _boxes.SeesInSky(view, box.Min, box.Max);
        _skyPlace = box => _models.WorldView is { } view ? _boxes.PlaceInSky(view, box.Min, box.Max) : 0;
    }

    /// <summary>The `cl_drawmonitors` the last frame was stepped or drawn with, for a test of its wiring.</summary>
    internal bool DrawMonitors => _frame.DrawMonitors;

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
                        // The sun from the state's own slot (B170): asked apart, a sun refused its slot would be counted in the cube and again.
                        PointLighting propLight = impact.StaticProp >= 0 ? loaded.Lighting.LightingAt(end.X, end.Y, end.Z) : PointLighting.None;
                        (float r, float g, float b) = impact.StaticProp >= 0
                            ? SurfaceColour.OfStaticProp(propLight, LevelLighting.SunOf(propLight.State), end, normal)
                            : ColourOf(loaded, samples, thumbnail).At(start, end);

                        return new Vector3(r, g, b);
                    },
                    WorldTrace(level),
                    random);
            },
            WorldTrace(level),
            _drawEffectsAt);
    }

    private IReadOnlyList<ParticleBatch> WithImpactEffects(IReadOnlyList<ParticleBatch> batches, FreeCamera viewing)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_map?.ParticleMaterials is not { } materials)
        {
            // The sub-views' lists are joined every frame: a frame that builds none must not hand them the last one's.
            ClearSubViews();

            return batches;
        }

        (float fx, float fy, float fz) = AngleVectors.Forward(viewing.Angles.Pitch, viewing.Angles.Yaw);
        (float rx, float ry, float rz) = AngleVectors.Right(viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);
        (float ux, float uy, float uz) = AngleVectors.Up(viewing.Angles.Pitch, viewing.Angles.Yaw, viewing.Angles.Roll);
        Vector3 eye = new(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z);

        // **Nothing here is made per frame** (B599): the runners are named rather than put in an array, the alpha lookup
        // is one delegate over the frame's materials field, and the batch list is reused — `SetParticles` copies what it
        // keeps before the next frame refills it, as it already does for the corner lists.
        _materials = materials;

        // **Every view draws the emitters it collates, from its own camera** (B734): each emitter is a translucent
        // renderable of the leaf system, the simple singleton is drawn by every view but the 3D sky, and the client-side
        // quads and tracer lines by the views that run `ViewDrawScene` — the main view and each monitor.
        CollationView? collated = _models.WorldView;

        BuildView(
            _main, eye, new Vector3(fx, fy, fz), new Vector3(rx, ry, rz), new Vector3(ux, uy, uz),
            collated is null ? null : _mainSees, collated is null ? null : _mainPlace, clientEffects: true, singletons: true,
            _frame.ViewportWidth * 0.5f);
        BuildSubViews(collated, materials);

        if (!_main.Any)
        {
            return batches;
        }

        List<ParticleBatch> all = _withImpacts;

        all.Clear();
        all.AddRange(batches);

        // Indexed: the list is interface-typed, and a `foreach` boxed its enumerator every frame (B599).
        IReadOnlyList<ParticleBatch> own = _main.Batches(materials);

        for (int at = 0; at < own.Count; at++)
        {
            ParticleBatch built = own[at];

            all.Add(built);

            // What each impact batch drew and where, so a glow seen on screen can be named from the log.
            if (_renderLog.IsEnabled(LogLevel.Debug))
            {
                DetailSpriteVertex first = built.Corners[0];

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                _renderLog.LogDebug(
                    "{Message}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"impact batch {built.Pass} at {_frame.Tick}: {built.Corners.Count / 6} quads, first corner ({first.X:0} {first.Y:0} {first.Z:0}) rgba ({first.Red:0.00} {first.Green:0.00} {first.Blue:0.00} {first.Alpha:0.00})"));
            }
        }

        return all;
    }

    /// <summary>One view's emitters, singleton particles and client-side effects (B734).</summary>
    private void BuildView(
        ImpactRenderables into,
        Vector3 eye,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        Func<(Vector3 Min, Vector3 Max), bool>? sees,
        Func<(Vector3 Min, Vector3 Max), int>? place,
        bool clientEffects,
        bool singletons,
        float halfWidth)
    {
        into.Begin();
        _impactEffects.Render(into, eye, forward, right, up, _alphaOf, sees, place, clientEffects, singletons);
        _sparkEffects.Render(into, eye, forward, right, up, _alphaOf, sees, place, clientEffects, singletons);

        if (clientEffects)
        {
            AddTracerLines(into, eye, forward, halfWidth);
        }
    }

    /// <summary>Empties the sky's, the water views' and the monitors' lists.</summary>
    private void ClearSubViews()
    {
        SkyBuilt = [];
        _waterBuilt.Clear();
        _monitorsBuilt.Clear();
    }

    /// <summary>
    /// The 3D sky's, each water view's and each monitor's own (B734): every view collates the emitters over its own leaves,
    /// from its own camera (`viewrender.cpp:4869-5030`, `:5411-5440`, `:3168-3238`).
    /// </summary>
    private void BuildSubViews(CollationView? collated, IReadOnlyDictionary<string, ParticleMaterial> materials)
    {
        ClearSubViews();

        if (collated is null)
        {
            return;
        }

        if (collated.Views.SkyCamera is { } sky)
        {
            ((float X, float Y, float Z) f, (float X, float Y, float Z) r, (float X, float Y, float Z) u) = sky.Basis();

            BuildView(
                _sky, new Vector3(sky.Origin.X, sky.Origin.Y, sky.Origin.Z), new Vector3(f.X, f.Y, f.Z), new Vector3(r.X, r.Y, r.Z),
                new Vector3(u.X, u.Y, u.Z), _skySees, _skyPlace, clientEffects: false, singletons: false, 0f);
            SkyBuilt = _sky.Batches(materials);
        }

        IReadOnlyList<RenderableView> water = collated.Views.Water ?? [];

        for (int at = 0; at < water.Count; at++)
        {
            RenderableView own = water[at];
            (ImpactRenderables renderables, Func<(Vector3 Min, Vector3 Max), bool> sees, Func<(Vector3 Min, Vector3 Max), int> place) =
                ViewOf(_water, _waterViews, at, water: true);

            BuildView(
                renderables, new Vector3(own.Origin.X, own.Origin.Y, own.Origin.Z), new Vector3(own.Forward.X, own.Forward.Y, own.Forward.Z),
                new Vector3(own.Right.X, own.Right.Y, own.Right.Z), new Vector3(own.Up.X, own.Up.Y, own.Up.Z), sees, place,
                clientEffects: false, singletons: true, 0f);
            _waterBuilt.Add(renderables.Batches(materials));
        }

        IReadOnlyList<MonitorRenderableView> monitors = collated.Views.Monitors ?? [];

        for (int at = 0; at < monitors.Count; at++)
        {
            (ImpactRenderables renderables, Func<(Vector3 Min, Vector3 Max), bool> sees, Func<(Vector3 Min, Vector3 Max), int> place) =
                ViewOf(_monitors, _monitorViews, at, water: false);

            if (monitors[at].World.Camera is not { } camera)
            {
                _monitorsBuilt.Add([]);
                continue;
            }

            ((float X, float Y, float Z) f, (float X, float Y, float Z) r, (float X, float Y, float Z) u) = camera.Basis();

            // A tracer line's width is measured against the view's own width; a monitor's target is drawn at aspect one
            // unless the camera asks for the screen's (`viewrender.cpp:3218`). *Interpolated:* its height stands for its width.
            BuildView(
                renderables, new Vector3(camera.Origin.X, camera.Origin.Y, camera.Origin.Z), new Vector3(f.X, f.Y, f.Z),
                new Vector3(r.X, r.Y, r.Z), new Vector3(u.X, u.Y, u.Z), sees, place, clientEffects: true, singletons: true,
                monitors[at].World.ViewportHeight * 0.5f);
            _monitorsBuilt.Add(renderables.Batches(materials));
        }
    }

    /// <summary>A sub-view's renderables, cull and place by its index, made once per index (B599).</summary>
    private (ImpactRenderables Renderables, Func<(Vector3 Min, Vector3 Max), bool> Sees, Func<(Vector3 Min, Vector3 Max), int> Place) ViewOf(
        List<ImpactRenderables> renderables,
        List<(Func<(Vector3 Min, Vector3 Max), bool> Sees, Func<(Vector3 Min, Vector3 Max), int> Place)> views,
        int at,
        bool water)
    {
        while (views.Count <= at)
        {
            int index = views.Count;

            renderables.Add(new ImpactRenderables());
            views.Add(water
                ? (box => _models.WorldView is { } view && _boxes.SeesInWater(view, index, box.Min, box.Max),
                    box => _models.WorldView is { } view ? _boxes.PlaceInWater(view, index, box.Min, box.Max) : 0)
                : (box => _models.WorldView is { } view && _boxes.SeesInMonitor(view, index, box.Min, box.Max),
                    box => _models.WorldView is { } view ? _boxes.PlaceInMonitor(view, index, box.Min, box.Max) : 0));
        }

        return (renderables[at], views[at].Sees, views[at].Place);
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
    private void AddTracerLines(ImpactRenderables into, Vector3 eye, Vector3 forward, float halfWidth)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline)
        {
            return;
        }

        int tick = _frame.Tick;

        TickWindow.Between(
            timeline.Dispatches.All, static dispatch => dispatch.Tick, tick - TracerLineWindowTicks, tick, _tracerDispatchesNow);

        // A `CFXDiscreetLine` is a client-side effect, drawn after the view (B734).
        List<DetailSpriteVertex> corners = _tracerCorners;

        corners.Clear();

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
                // One `Update` for each view's `DrawEffects` on every tick it has flown, its own included (B736).
                DiscreetLines.Draw(
                    flying, StepsSince(dispatch.Tick, timeline, tick) * timeline.IntervalPerTick, eye, forward, halfWidth, corners);
            }
        }

        into.AddClientEffect(DiscreetLines.TracerMaterial, corners);
    }

    /// <summary>One view's tracer corners, reused.</summary>
    private readonly List<DetailSpriteVertex> _tracerCorners = [];

    /// <summary>
    /// The views' <c>DrawEffects</c> summed tick by tick over the tracer window ending at <see cref="_stepsTick"/>: entry
    /// <c>i</c> holds the sum from the window's first tick through its <c>i</c>th. Built once a frame, not once a line.
    /// </summary>
    private readonly int[] _stepsThrough = new int[TracerLineWindowTicks + 1];
    private int _stepsTick = int.MinValue;
    private DemoTimeline? _stepsOf;
    private bool _stepsMonitors;

    /// <summary>How many `Update`s a client-side effect born at a tick has had by this one, both ends included (B736).</summary>
    private int StepsSince(int born, DemoTimeline timeline, int tick)
    {
        int first = tick - TracerLineWindowTicks;

        if (tick != _stepsTick || !ReferenceEquals(timeline, _stepsOf) || _frame.DrawMonitors != _stepsMonitors)
        {
            int sum = 0;

            for (int i = 0; i < _stepsThrough.Length; i++)
            {
                sum += DrawEffectsAt(first + i);
                _stepsThrough[i] = sum;
            }

            _stepsTick = tick;
            _stepsOf = timeline;
            _stepsMonitors = _frame.DrawMonitors;
        }

        int from = born - first;

        return _stepsThrough[^1] - (from > 0 ? _stepsThrough[from - 1] : 0);
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
            trace,
            _drawEffectsAt);
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
