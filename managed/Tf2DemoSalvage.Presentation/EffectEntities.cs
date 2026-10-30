using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's beams, sprite trails and ropes are built against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="Viewing">The frame's camera.</param>
/// <param name="Drawn">The entities the frame drew.</param>
/// <param name="ViewportWidth">The viewport's width in pixels, which a rope's width is measured against.</param>
/// <param name="ViewerVisionFlags">The local player's vision filter flags, for Pyrovision's holiday lights.</param>
public readonly record struct EffectEntityFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    int Tick,
    FreeCamera Viewing,
    IReadOnlyList<SceneProp> Drawn,
    int ViewportWidth,
    int ViewerVisionFlags);

/// <summary>What a loaded map holds that the effect entities read, as data (D214): a test supplies it by hand.</summary>
/// <param name="SpriteMaterials">The entity sprite materials by path; null when the map's assets would not load.</param>
/// <param name="Lighting">The level's lights, for a rope's colour.</param>
/// <param name="Level">The map's geometry, for a rope's collision and the sky camera.</param>
internal sealed record EffectEntityMap(
    IReadOnlyDictionary<string, EngineSprite>? SpriteMaterials,
    LevelLighting Lighting,
    MapLevel Level);

/// <summary>
/// The entities whose class draws a strip rather than a model: every <c>CBeam</c>, <c>CSpriteTrail</c> and
/// <c>C_RopeKeyframe</c> in the moment, and the holiday lights the ropes dispatch (B231, B475, B477, B478).
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 4), where it was `BuildEffectEntities` and the fifteen
/// methods only it called, with their state: the same arithmetic, the same order. The view's side is the constructor
/// and one <see cref="Build"/> a frame, called by `FrameParticles.Build` where `DrawParticles` called it.
///
/// **A presenter (D90): it decides what each strip is built against** — the frame's clock, where a handle's entity is
/// now, what a rope end hangs from. Building a strip is `EntityBeams`, `EntityTrails` and `EntityRopes`, Scene's.
/// </remarks>
public sealed class EffectEntities
{
    private readonly IPosedEntities _models;

    private readonly ILogger _renderLog;

    /// <summary>The frame being built, for the callbacks the builders are handed.</summary>
    private EffectEntityFrame _frame;

    /// <summary>What that frame's map holds.</summary>
    private EffectEntityMap? _map;

    /// <summary>The map <see cref="_map"/> was read out of.</summary>
    private LoadedMap? _mapOf;

    /// <summary>The view's own: the model set it draws with.</summary>
    /// <param name="models">The posed entities, for an attachment or a hitbox a strip ends on.</param>
    /// <param name="render">The render log.</param>
    public EffectEntities(EntityModelSet models, ILogger render)
        : this((IPosedEntities)models, render)
    {
    }

    /// <summary>Over any posed entities.</summary>
    /// <param name="models">The posed entities.</param>
    /// <param name="render">The render log.</param>
    public EffectEntities(IPosedEntities models, ILogger render)
    {
        _models = models;
        _renderLog = render;

        // **Each callback made once** (B599): a method group or a lambda over `this` is a new delegate every time it is
        // passed, and these were passed every frame — the parent lookup once per prop placed.
        _drawnParent = entity => _drawnByEntity.TryGetValue(entity, out SceneProp? parent) ? parent : null;
        _absolutePose = AbsolutePose;
        _beamEnd = BeamEnd;
        _trailOrigin = TrailOrigin;
        _ropeEnd = RopeEnd;
        _ropeLight = RopeLight;
        _ropeCollide = RopeCollide;
        _ropeEndDormant = RopeEndDormantWithModel;
        _christmas = ChristmasActive;
    }

    private readonly Func<bool> _christmas;

    private readonly Func<int, SceneProp?> _drawnParent;
    private readonly Func<SceneProp, ScenePose> _absolutePose;
    private readonly BeamEntityPosition _beamEnd;
    private readonly Func<SceneProp, Vector3?> _trailOrigin;
    private readonly RopeEndPointResolver _ropeEnd;
    private readonly Func<Vector3, Vector3> _ropeLight;
    private readonly RopeCollision _ropeCollide;
    private readonly Func<int, bool> _ropeEndDormant;

    /// <summary>The frame's batches, ropes first, reused: the caller copies them before the next frame.</summary>
    private readonly List<ParticleBatch> _built = [];

    /// <summary>The frame's strips, ropes first.</summary>
    /// <param name="frame">What this frame is built against.</param>
    /// <param name="forward">The camera's forward axis.</param>
    /// <param name="right">Its right axis.</param>
    /// <param name="up">Its up axis.</param>
    /// <param name="sees">The halo occlusion gate the entity sprites use.</param>
    /// <returns>The batches; empty when the moment holds none.</returns>
    public IReadOnlyList<ParticleBatch> Build(
        in EffectEntityFrame frame, Vector3 forward, Vector3 right, Vector3 up, Func<GlowQuery, float> sees)
    {
        if (!ReferenceEquals(frame.Loaded, _mapOf))
        {
            _mapOf = frame.Loaded;
            _map = frame.Loaded is { } loaded
                ? new EffectEntityMap(loaded.Assets?.SpriteMaterials, loaded.Lighting, loaded.Level)
                : null;
        }

        return BuildOver(frame, _map, forward, right, up, sees);
    }

    /// <summary>The same, over what the frame's map holds as data (D214).</summary>
    /// <param name="frame">What this frame is built against; its map is not read here.</param>
    /// <param name="map">What the map holds, or null for none.</param>
    /// <param name="forward">The camera's forward axis.</param>
    /// <param name="right">Its right axis.</param>
    /// <param name="up">Its up axis.</param>
    /// <param name="sees">The halo occlusion gate.</param>
    /// <returns>The batches.</returns>
    internal IReadOnlyList<ParticleBatch> BuildOver(
        in EffectEntityFrame frame, EffectEntityMap? map, Vector3 forward, Vector3 right, Vector3 up, Func<GlowQuery, float> sees)
    {
        _frame = frame;
        _map = map;

        return BuildEffectEntities(frame.Viewing, forward, right, up, sees);
    }
    /// <summary>
    /// The entities whose class overrides <c>DrawModel</c> with a strip rather than a model — <c>CBeam</c> — as batches.
    /// </summary>
    /// <param name="viewing">The camera.</param>
    /// <param name="forward">Its forward axis.</param>
    /// <param name="right">Its right axis.</param>
    /// <param name="up">Its up axis.</param>
    /// <param name="sees">The halo occlusion gate the entity sprites use.</param>
    /// <returns>The batches; empty when the moment holds none.</returns>
    /// <remarks>
    /// **The clock is the demo's, and a frame that does not advance it has a frame time of zero** — which is what
    /// <c>gpGlobals-&gt;frametime</c> is while a demo is paused, and what makes <c>UpdateBeam</c> re-seed its noise so a
    /// paused frame repeats itself. A step backwards is a seek, not negative time, and counts as zero too.
    /// </remarks>
    private List<ParticleBatch> BuildEffectEntities(
        FreeCamera viewing, Vector3 forward, Vector3 right, Vector3 up, Func<GlowQuery, float> sees)
    {
        double interval = _frame.Timeline?.IntervalPerTick ?? (1d / 66d);
        double tick = _frame.Tick;
        float seconds = (float)(tick * interval);
        float elapsed = (float)Math.Max(0d, (tick - _effectEntitiesTick) * interval);
        int previousTick = (int)_effectEntitiesTick;

        _effectEntitiesTick = tick;

        _drawnByEntity.Clear();

        for (int p = 0; p < _frame.Drawn.Count; p++)
        {
            _drawnByEntity[_frame.Drawn[p].EntityIndex] = _frame.Drawn[p];
        }

        BeamView view = new(new Vector3(viewing.Origin.X, viewing.Origin.Y, viewing.Origin.Z), forward, right, up);

        _effectViewOrigin = view.Origin;

        IReadOnlyList<ParticleBatch> beams = _beams.Build(
            _frame.Drawn,
            view,
            _map?.SpriteMaterials ?? NoEngineSprites,
            _absolutePose,
            _beamEnd,
            sees,
            seconds,
            elapsed);

        IReadOnlyList<ParticleBatch> trails = _trails.Build(
            _frame.Drawn,
            view.Origin,
            _map?.SpriteMaterials ?? NoEngineSprites,
            _trailOrigin,
            seconds);

        IReadOnlyDictionary<string, EngineSprite> spriteMaterials = _map?.SpriteMaterials ?? NoEngineSprites;

        // Before the think, as the client reads them with the packet (B478): each rope's impulse and each ShakeRopes.
        if (elapsed > 0f)
        {
            KickRopes(previousTick + 1, (int)tick);
        }

        int? holidayStyle = RopeHolidayStyle((int)tick);

        // `CHolidayLightManager::Update`: the lights the last frame dispatched are made or moved now.
        _ropeLights.Update(seconds, holidayStyle ?? 0);

        IReadOnlyList<ParticleBatch> ropes = _ropes.Build(
            _frame.Drawn,
            new RopeView(view.Origin, forward, Math.Max(1, _frame.ViewportWidth)),
            spriteMaterials,
            _ropeEnd,
            _ropeLight,
            _ropeCollide,
            elapsed,
            _ropeEndDormant,
            holidayStyle);

        QueueHolidayLights((int)tick);

        IReadOnlyList<ParticleBatch> holidayLights = _ropeLights.Batches(spriteMaterials, forward, right, up);

        // The values the builders USED, carried rather than recounted (B243), so "none here" and "every one
        // refused" read differently in the log.
        if (_renderLog.IsEnabled(LogLevel.Debug) &&
            (_beams.Drawn + _beams.Skipped + _trails.Drawn + _trails.Skipped + _ropes.Drawn + _ropes.Skipped > 0))
        {
            _renderLog.LogDebug(
                "{Message}",
                $"beams: {_beams.Drawn} drawn in {beams.Count} batches, {_beams.Skipped} skipped; " +
                $"trails: {_trails.Drawn} drawn in {trails.Count} batches, {_trails.Skipped} skipped; " +
                $"ropes: {_ropes.Drawn} drawn in {ropes.Count} batches, {_ropes.Skipped} skipped");
        }

        // Ropes first: the rope manager draws its cache with the opaque renderables, before anything translucent.
        _built.Clear();
        _built.AddRange(ropes);
        _built.AddRange(beams);
        _built.AddRange(trails);
        _built.AddRange(holidayLights);

        return _built;
    }

    /// <summary>
    /// The rope events of the ticks played since the last frame (B478): <c>C_RopeKeyframe::ReceiveMessage</c>'s impulses and
    /// <c>ShakeRopesCallback</c>'s shakes, in arrival order within each kind.
    /// </summary>
    private void KickRopes(int fromTick, int toTick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || toTick < fromTick)
        {
            return;
        }

        TickWindow.Between(timeline.RopeImpulses.All, static impulse => impulse.Tick, fromTick, toTick, _ropeImpulsesNow);

        foreach ((int _, SceneRopeImpulse impulse) in _ropeImpulsesNow)
        {
            _ropes.Impulse(impulse.EntityIndex, new Vector3(impulse.Impulse.X, impulse.Impulse.Y, impulse.Impulse.Z));
        }

        TickWindow.Between(timeline.Dispatches.All, static dispatch => dispatch.Tick, fromTick, toTick, _ropeShakesNow);

        foreach ((int _, SceneEffectDispatch shake) in _ropeShakesNow)
        {
            if (string.Equals(timeline.Dispatches.Names.Name(shake.Name), "ShakeRopes", StringComparison.Ordinal))
            {
                _ropes.Shake(new Vector3(shake.Origin.X, shake.Origin.Y, shake.Origin.Z), shake.Radius, shake.Magnitude);
            }
        }
    }

    /// <summary>
    /// <c>CRopeManager::IsHolidayLightMode</c>'s answer this tick, as a style or null (B478): the game rules' gates, Christmas
    /// on the watcher's calendar, and the local player's Pyrovision.
    /// </summary>
    private int? RopeHolidayStyle(int tick)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline)
        {
            return null;
        }

        SceneGameRules rules = timeline.RulesAt(tick);

        // The Christmas question is asked lazily, through one delegate made once over these two fields rather than a
        // closure per frame (B599).
        _holidayTimeline = timeline;
        _holidayRules = rules;

        return _ropeHolidayMode.Style(
            rules.Present,
            rules.PowerupMode,
            rules.RopesHolidayLightsAllowed,
            _christmas,
            (_frame.ViewerVisionFlags & PyroVisionFlag) == PyroVisionFlag);
    }

    private DemoTimeline? _holidayTimeline;
    private SceneGameRules _holidayRules;

    /// <summary>`IsChristmasActive` for the timeline and rules <see cref="RopeHolidayStyle"/> last read.</summary>
    private bool ChristmasActive() =>
        _holidayTimeline is { } timeline &&
        TfHolidays.IsChristmasActive(timeline.ServerConVars.Value, _holidayRules.MapHolidayType, TfHolidays.WatcherClock);

    /// <summary>
    /// <c>CHolidayLightManager::AddHolidayLight</c> for each light the build just dispatched, against the local player's
    /// origin and the map's <c>sky_camera</c> — the origin <c>m_skybox3d</c> carries.
    /// </summary>
    private void QueueHolidayLights(int tick)
    {
        if (!ReferenceEquals(_ropeLightsTimeline, _frame.Timeline))
        {
            // `LevelShutdownPreEntity` and the temp entities' level shutdown.
            _ropeLights.Clear();
            _ropeLightsTimeline = _frame.Timeline;
        }

        if (_ropes.HolidayLights.Count == 0)
        {
            return;
        }

        Vector3? player = _frame.Timeline?.RecorderEntityIndex is { } local &&
            _frame.Timeline.TrackFor(local, tick)?.At(tick) is { } pose
                ? new Vector3(pose.X, pose.Y, pose.Z)
                : null;

        (float X, float Y, float Z) sky = _map?.Level is { } level
            ? BspEntities.SkyCamera(level.Entities)?.Origin ?? default
            : default;

        foreach (RopeHolidayDispatch dispatch in _ropes.HolidayLights)
        {
            _ropeLights.Add(dispatch, player, new Vector3(sky.X, sky.Y, sky.Z));
        }
    }

    /// <summary>
    /// Whether a rope end's <c>EHANDLE</c> names an entity that is dormant and has a model index — <c>DrawModel</c>'s two
    /// tests of each end (`c_rope.cpp:1462-1467`).
    /// </summary>
    internal bool RopeEndDormantWithModel(int handle)
    {
        const int InvalidHandle = (1 << 21) - 1;
        const int SlotMask = (1 << 11) - 1;

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (handle == InvalidHandle || _frame.Timeline is not { } timeline)
        {
            return false;
        }

        int slot = handle & SlotMask;
        int tick = _frame.Tick;

        return timeline.TrackFor(slot, tick) is { } track &&
            track.Continues(handle >> 11) &&
            track.FactsAt(tick).ModelPath.Length > 0 &&
            timeline.Dormancy.IsDormant(slot, tick);
    }

    /// <summary><c>TF_VISION_FILTER_PYRO</c> (`shareddefs.h:977`).</summary>
    private const int PyroVisionFlag = 1;

    /// <summary>The impulses of the ticks played since the last frame.</summary>
    private readonly List<(int Index, SceneRopeImpulse Impulse)> _ropeImpulsesNow = [];

    /// <summary>The effect dispatches of those ticks, of which the ShakeRopes are taken.</summary>
    private readonly List<(int Index, SceneEffectDispatch Dispatch)> _ropeShakesNow = [];

    /// <summary><c>s_RopeManager</c>'s holiday state, which lives as long as the client.</summary>
    private readonly RopeHolidayMode _ropeHolidayMode = new();

    /// <summary>The <c>TF_HolidayLight</c> temp entities.</summary>
    private readonly RopeHolidayLights _ropeLights = new();

    /// <summary>The demo the lights belong to; another one is another level.</summary>
    private DemoTimeline? _ropeLightsTimeline;

    /// <summary>
    /// <c>CalculateEndPointAttachment</c> (`c_rope.cpp:1923-1967`): with <c>ROPE_PLAYER_WPN_ATTACH</c> a player's weapon's
    /// <c>buff_attach</c>; else an attachment above zero; else the entity's <c>WorldSpaceCenter</c> — a rope keyframe's
    /// origin, a player's hull centre, anything else's origin.
    /// </summary>
    /// <remarks>
    /// **One answer is approximate, and the corpus does not reach it**: the angles are the entity's own even for an
    /// attachment, which only a direction lock reads. Every corpus rope ends on another rope keyframe.
    /// </remarks>
    internal RopeEndPoint? RopeEnd(int handle, int attachment, bool playerWeapon)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (Dereference(handle, out ScenePropTrack? track) is not { } prop || track is null)
        {
            return null;
        }

        ScenePose placed = AbsolutePose(prop);
        Vector3 forward = AngleForward(placed.Pitch, placed.Yaw);
        bool player = string.Equals(track.ClassName, PlayerClassName, StringComparison.Ordinal);

        if (playerWeapon && player)
        {
            return _drawnByEntity.Values.FirstOrDefault(weapon => weapon.OwnedBy == prop.EntityIndex && weapon.WeaponState == 2)
                    is { } weapon && _models.AttachmentPosition(weapon.EntityIndex, "buff_attach") is { } buff
                ? new RopeEndPoint(new Vector3(buff.X, buff.Y, buff.Z), forward)
                : null;
        }

        if (attachment > 0 && _models.AttachmentPosition(prop.EntityIndex, attachment) is { } at)
        {
            return new RopeEndPoint(new Vector3(at.X, at.Y, at.Z), forward);
        }

        // `WorldSpaceCenter()`: a rope keyframe's override is its origin (`c_rope.cpp:1486-1489`); anything else's is its
        // collision box's centre (`collisionproperty.h:391-396`, B478).
        if (player)
        {
            return new RopeEndPoint(new Vector3(placed.X, placed.Y, placed.Z + PlayerHull.CenterHeight(placed.Flags)), forward);
        }

        return new RopeEndPoint(
            string.Equals(track.ClassName, RopeImpulseFeed.RopeClassName, StringComparison.Ordinal)
                ? new Vector3(placed.X, placed.Y, placed.Z)
                : WorldSpaceCenter.Of(placed, track.CollisionAt(_frame.Tick)),
            forward);
    }

    /// <summary>The forward of a pitch and yaw, in degrees — <c>AngleVectors</c>' first output.</summary>
    private static Vector3 AngleForward(float pitch, float yaw)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(float.DegreesToRadians(pitch));
        (float sinYaw, float cosYaw) = MathF.SinCos(float.DegreesToRadians(yaw));

        return new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
    }

    /// <summary>
    /// <c>engine-&gt;ComputeLighting( pos, NULL, true, … )</c> with <c>rope_averagelight</c> 1: the six faces of the light
    /// cube averaged, each channel clamped to one.
    /// </summary>
    internal Vector3 RopeLight(Vector3 at)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_map?.Lighting is not { } lighting)
        {
            return Vector3.One;
        }

        AmbientCube cube = lighting.ComputeLighting(at.X, at.Y, at.Z);
        Vector3 sum = Face(cube.PositiveX) + Face(cube.NegativeX) + Face(cube.PositiveY) +
            Face(cube.NegativeY) + Face(cube.PositiveZ) + Face(cube.NegativeZ);

        return Vector3.Min(sum / 6f, Vector3.One);

        static Vector3 Face((float Red, float Green, float Blue) face) => new(face.Red, face.Green, face.Blue);
    }

    /// <summary>A rope node's ±2 hull sweep, world brushes only — <c>MASK_SOLID_BRUSHONLY</c>.</summary>
    internal RopeHit RopeCollide(Vector3 from, Vector3 to)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_map?.Level is not { } level)
        {
            return new RopeHit(1f, Vector3.Zero, 0f, false);
        }

        BspTrace trace = level.TraceBrushOnly((from.X, from.Y, from.Z), (to.X, to.Y, to.Z), 2f);

        return new RopeHit(
            trace.Fraction,
            new Vector3(trace.Normal.X, trace.Normal.Y, trace.Normal.Z),
            trace.Distance,
            trace.AllSolid || trace.StartSolid);
    }

    /// <summary>
    /// <c>CSpriteTrail::GetRenderOrigin</c> (`SpriteTrail.cpp:537-553`): the attached entity's attachment — or, for an
    /// attachment that does not exist, that entity's origin, as <c>C_BaseAnimating::GetAttachment</c> answers
    /// (`c_baseanimating.cpp:2129-2134`) — and the trail's own place when it is attached to nothing.
    /// </summary>
    /// <returns>
    /// The head, or null when the entity the trail hangs from is not in this moment. Measured on z1800: 34 of its 41
    /// trails outlive their projectile's track while still naming it (`trails` probe), and the client still holds that
    /// entity — dormant, or unlinked from its children at its last place — so the head stops there. Placed through the
    /// missing parent instead, the trail's local zero is the world origin.
    /// </returns>
    internal Vector3? TrailOrigin(SceneProp prop)
    {
        const int InvalidHandle = (1 << 21) - 1;

        if (prop.Pose.SpriteTrail is { AttachedTo: not InvalidHandle } trail)
        {
            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (Dereference(trail.AttachedTo) is not { } attached)
            {
                return null;
            }

            if (_models.AttachmentPosition(attached.EntityIndex, trail.Attachment) is { } at)
            {
                return new Vector3(at.X, at.Y, at.Z);
            }

            ScenePose entity = AbsolutePose(attached);

            return new Vector3(entity.X, entity.Y, entity.Z);
        }

        if (prop.AttachedTo is { } parent && !_drawnByEntity.ContainsKey(parent))
        {
            return null;
        }

        ScenePose own = AbsolutePose(prop);

        return new Vector3(own.X, own.Y, own.Z);
    }

    /// <summary>
    /// The drawn prop an <c>EHANDLE</c> names this tick, or null — <c>CBaseHandle::Get</c>, whose serial must match the
    /// slot's occupant now (B231).
    /// </summary>
    private SceneProp? Dereference(int handle) => Dereference(handle, out _);

    /// <inheritdoc cref="Dereference(int)"/>
    private SceneProp? Dereference(int handle, out ScenePropTrack? track)
    {
        const int InvalidHandle = (1 << 21) - 1;
        const int SlotMask = (1 << 11) - 1;

        track = null;

        if (handle == InvalidHandle || _frame.Timeline is null)
        {
            return null;
        }

        int slot = handle & SlotMask;

        track = _frame.Timeline.TrackFor(slot, _frame.Tick);

        return track is not null && track.Continues(handle >> 11) &&
            _drawnByEntity.TryGetValue(slot, out SceneProp? prop)
            ? prop
            : null;
    }

    /// <summary>A drawn prop's place in the world, through its move parents — <c>GetAbsOrigin()</c>.</summary>
    private ScenePose AbsolutePose(SceneProp prop) => ParentChain.Absolute(prop, _drawnParent);

    /// <summary><c>ComputeBeamEntPosition</c> for one beam end, the handle dereferenced as the client does (B231).</summary>
    /// <remarks>
    /// **The serial decides, as <c>CBaseHandle::Get</c>'s does**: the slot's track AT THIS TICK must carry the handle's
    /// serial, or the handle names an entity that is gone and the end is "not found". Then the engine's three answers —
    /// the attachment (or the hitbox point nearest the view), a player's <c>WorldSpaceCenter</c>, anything else's render
    /// origin.
    /// </remarks>
    internal Vector3? BeamEnd(int handle, int attachment, bool hitboxes)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (Dereference(handle, out ScenePropTrack? track) is not { } prop || track is null)
        {
            return null;
        }

        int slot = prop.EntityIndex;

        if (!hitboxes)
        {
            if (_models.AttachmentPosition(slot, attachment) is { } at)
            {
                return new Vector3(at.X, at.Y, at.Z);
            }
        }
        else if (_models.HitboxPointNearest(slot, attachment, _effectViewOrigin) is { } nearest)
        {
            return nearest;
        }

        ScenePose placed = AbsolutePose(prop);

        // "Player origins are at their feet": a player answers the middle of its hull.
        return string.Equals(track.ClassName, PlayerClassName, StringComparison.Ordinal)
            ? new Vector3(placed.X, placed.Y, placed.Z + PlayerHull.CenterHeight(placed.Flags))
            : new Vector3(placed.X, placed.Y, placed.Z);
    }

    /// <summary>The one class <c>IsPlayer()</c> answers true for in TF2.</summary>
    private const string PlayerClassName = "CTFPlayer";

    /// <summary>The demo tick the last frame's effect entities were built at, for their frame time.</summary>
    private double _effectEntitiesTick;

    /// <summary>Where the camera was for the last effect-entity build — <c>MainViewOrigin()</c> for a hitbox end.</summary>
    private Vector3 _effectViewOrigin;

    /// <summary>This frame's drawn props by entity index, for a parent chain or a beam end.</summary>
    private readonly Dictionary<int, SceneProp> _drawnByEntity = [];

    /// <summary>Every <c>CBeam</c> in the moment, as strips.</summary>
    private readonly EntityBeams _beams = new();

    /// <summary>Every <c>CSpriteTrail</c>, sampled frame by frame as the client samples it (B475).</summary>
    private readonly EntityTrails _trails = new();

    /// <summary>Every <c>C_RopeKeyframe</c>, simulated frame by frame as the client simulates it (B477).</summary>
    private readonly EntityRopes _ropes = new();

    /// <summary>What a machine with no TF2 has: no entity sprites either (B390).</summary>
    private static readonly IReadOnlyDictionary<string, EngineSprite> NoEngineSprites =
        new Dictionary<string, EngineSprite>(StringComparer.OrdinalIgnoreCase);
}
