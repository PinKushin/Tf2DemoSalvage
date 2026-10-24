using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Audio;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Render;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>What one frame's decals are stepped against.</summary>
/// <param name="Timeline">The loaded demo, or null.</param>
/// <param name="Loaded">The loaded map, or null.</param>
/// <param name="Game">The game's content, or null without an install.</param>
/// <param name="Tick">The whole tick being shown.</param>
/// <param name="PlayersShown">The players as the frame drew them when the frame is of <paramref name="Tick"/>; null otherwise.</param>
/// <param name="Bullets">What a bullet is traced against this frame.</param>
/// <param name="MaxDecals">`r_decals`.</param>
/// <param name="MaxModelDecals">`r_maxmodeldecal`.</param>
/// <param name="Scripts">The soundscripts the game loaded, or null.</param>
/// <param name="Silent">Whether this pass is a seek's replay, which nobody hears.</param>
public readonly record struct DecalFrame(
    DemoTimeline? Timeline,
    LoadedMap? Loaded,
    GameContent? Game,
    int Tick,
    IReadOnlyList<ScenePlayer>? PlayersShown,
    BulletScene Bullets,
    int MaxDecals,
    int MaxModelDecals,
    SoundScriptCatalog? Scripts,
    bool Silent);

/// <summary>What a seek's replay of the model decals did, for the view's log of what it cost.</summary>
/// <param name="Stepped">How many ticks were posed.</param>
/// <param name="Skipping">The time spent on ticks nothing landed in.</param>
/// <param name="Held">How many model decals are held afterwards.</param>
public readonly record struct DecalReplayReport(int Stepped, TimeSpan Skipping, int Held);

/// <summary>What a map's assets hold that the decals read, as data (D214).</summary>
/// <param name="DecalMaterials">Decal material names to their table indices.</param>
/// <param name="Shaders">Each material's shader, by index.</param>
/// <param name="Lightmaps">The lightmap atlas's rectangles, by face.</param>
/// <param name="PlacedProps">The static props, by their index in the map's lump.</param>
internal sealed record DecalAssets(
    IReadOnlyDictionary<string, int> DecalMaterials,
    IReadOnlyList<string> Shaders,
    IReadOnlyList<AtlasRect> Lightmaps,
    IReadOnlyDictionary<int, PlacedProp> PlacedProps);

/// <summary>What a loaded map holds that the decals read, as data (D214): a test supplies it by hand.</summary>
/// <param name="Level">The map's geometry.</param>
/// <param name="Impacts">Every bullet the map stops.</param>
/// <param name="ImpactDecals">The decal a struck surface takes, or null when the map's content would not load.</param>
/// <param name="Doors">The map's solid brush entities.</param>
/// <param name="Assets">The map's assets, or null when they would not load.</param>
internal sealed record DecalMap(
    MapLevel Level,
    IReadOnlyList<ShotImpact> Impacts,
    ImpactDecals? ImpactDecals,
    SolidBrushEntities Doors,
    DecalAssets? Assets);

/// <summary>What a frame's map and game hold that the decals read.</summary>
/// <param name="Map">The map, or null.</param>
/// <param name="Surfaces">The game's surface properties, or null without a game.</param>
/// <param name="Scripts">The soundscripts the game loaded, or null.</param>
internal readonly record struct DecalContent(
    DecalMap? Map,
    VphysicsSurfaceProps? Surfaces,
    IReadOnlyDictionary<string, SoundScriptEntry>? Scripts);

/// <summary>
/// The decals a demo leaves: bullet holes and the demo's own decal events on the world and its static props, and the
/// server's impacts and the client's bullets on players' models (B415, B421).
/// </summary>
/// <remarks>
/// **Moved out of `MainForm` whole** (D214, thin view slice 2), where it was `StepDecals`, `StepModelDecals`,
/// `ReplayModelDecals` and the twenty methods they called: the same arithmetic, the same order, the same seeds. The
/// view's side is the constructor, one <see cref="Step"/> a frame, <see cref="ReplayStart"/> and <see cref="Replay"/>
/// on a seek, and <see cref="ClearMap"/>.
///
/// **A presenter (D90): it decides WHICH decals exist and where.** Clipping one to the world is `WorldDecals`,
/// projecting one onto a model is `ModelDecals`, both Scene's; drawing them is the device's, reached through
/// <see cref="IDecalUpload"/>.
///
/// **It emits no sound itself** (D92). A client bullet's landing sound is decided here, because whether a player
/// stopped it is, and is returned from <see cref="Step"/> for the caller to hand to the sound presenter.
/// </remarks>
public sealed class DecalEffects
{
    /// <summary>How far behind the frame an impact may be and still be projected: a frame's worth at any playback rate this runs.</summary>
    private const int ModelDecalArrivalTicks = 8;

    /// <summary>A jump further than this replays the model decals it passed over; a second of demo.</summary>
    /// <remarks>
    /// Past the per-frame <see cref="ModelDecalArrivalTicks"/> on purpose: at 8x a frame advances about nine ticks, and
    /// replaying on every frame of fast playback would redo work the ordinary step already does.
    /// </remarks>
    private const int ModelDecalSeekTicks = 66;

    /// <summary>
    /// How far back a seek's replay may start: two minutes. *Interpolated:* no life in f12 keeps a decal that long (the
    /// full heal on spawn wipes every one); ponytail: raise it if a demo shows a decal older than this surviving.
    /// </summary>
    private const int ModelDecalLookbackTicks = 66 * 120;

    /// <summary>
    /// How many ticks before a landing the replay poses from, so the sequence transitions the landing sees have run: 16,
    /// past the 0.2 s `fadeouttime` most sequences blend over. *Interpolated:* a longer fade poses slightly short.
    /// </summary>
    private const int ModelDecalWarmupTicks = 16;

    /// <summary>How near a bullet's path must come to a player's hull centre to be able to reach his hitboxes, in units.</summary>
    /// <remarks>
    /// A player's hitboxes lie inside his hull, whose corners are 53 units from its centre (24 across, 41 up); the rest
    /// covers a limb or a hat reaching past it. Generous on purpose: a bullet let through is only posed for nothing.
    /// </remarks>
    private const float ReachOfAPlayer = 72f;

    /// <summary>`DMG_BULLET` — the only damage type `ImpactCallback` offers the ricochet, which no TF gun has.</summary>
    private const int Bullet = 1 << 1;

    /// <summary>`FL_DUCKING`.</summary>
    private const int Ducking = 1 << 1;

    private readonly IPosedEntities _models;

    private readonly BulletStrikes _strikes;

    private readonly ILogger _renderLog;

    /// <summary>The frame being stepped, for the methods <see cref="Step"/> calls.</summary>
    private DecalFrame _frame;

    /// <summary>What that frame's map and game hold.</summary>
    private DecalContent _content;

    /// <summary>The map <see cref="_map"/> was read out of.</summary>
    private LoadedMap? _mapOf;

    /// <summary>That map as data, made once a map.</summary>
    private DecalMap? _map;

    /// <summary>The landing sounds the step being run has made, handed back by <see cref="Step"/>; reused.</summary>
    private readonly List<SceneSound> _landed = [];

    /// <summary>The world's decals, replayed from the demo — built on the first frame of a map, dropped with it.</summary>
    private DecalReplay? _decalReplay;

    /// <summary>The map as both engine walks see it — the decal walk and `R_LightVec` — built once per map.</summary>
    private DecalWorld? _decalWorld;

    /// <summary>Every entity's model decals, drawn by the device after each model.</summary>
    private readonly ModelDecals _modelDecals = new() { UnlitLight = Device3D.DecalModulateLight };

    /// <summary>The server's impacts on entities, found once per timeline.</summary>
    private IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)>? _entityImpacts;

    /// <summary>The impact and bullet lists last checked for tick order, and whether each was in it.</summary>
    private object? _impactsOrderOf;

    private bool _impactsInOrder;

    private object? _bulletsOrderOf;

    private bool _bulletsInOrder;

    /// <summary>The tick model decals were last brought up to; a step back clears them.</summary>
    private int _modelDecalTick = int.MinValue;

    /// <summary>Each player's state at the last step, for the changes that clear its decals.</summary>
    private readonly Dictionary<int, ScenePlayer> _decalHolders = [];

    /// <summary>The shot whose pellets are landing now, and the impact sounds it has played.</summary>
    private readonly ShotSoundGroup _shotSounds = new();

    /// <summary>The interpolated half of <see cref="PlayersABulletMayMeet"/>, reused.</summary>
    private readonly List<ScenePlayer> _interpolatedThen = [];

    /// <summary>The pool version last uploaded, so an unchanged pool costs nothing.</summary>
    private int _decalVersion = -1;

    private readonly List<PlacedDecal> _placedDecals = [];
    private readonly List<WorldVertex> _decalVertices = [];
    private readonly List<WorldBatch> _decalBatches = [];

    /// <summary>The static props' decal pool version last uploaded.</summary>
    private int _propDecalVersion = -1;

    /// <summary>Whether the first static prop decal's place has been logged, the control that they are made at all.</summary>
    private bool _reportedPropDecal;

    /// <summary>Whether the terrain decals' first count has been logged.</summary>
    private bool _reportedTerrainDecals;

    /// <summary>Whether the static prop decals' refusals have been logged.</summary>
    private bool _reportedPropRefusals;

    /// <summary>The summed origins of the doors carrying decals when the mesh was last built.</summary>
    private Vector3 _decalDoorsAt;

    /// <summary>The decal runs on brush entities, drawn after them; reused.</summary>
    private readonly List<WorldBatch> _entityDecalBatches = [];

    /// <summary>The view's own: the model set it draws with.</summary>
    /// <param name="models">The posed entities, for hitboxes and skinning.</param>
    /// <param name="strikes">Which bullets a player stopped.</param>
    /// <param name="render">The render log.</param>
    public DecalEffects(EntityModelSet models, BulletStrikes strikes, ILogger render)
        : this((IPosedEntities)models, strikes, render)
    {
    }

    /// <summary>Over any posed entities.</summary>
    /// <param name="models">The posed entities, for hitboxes and skinning.</param>
    /// <param name="strikes">Which bullets a player stopped.</param>
    /// <param name="render">The render log.</param>
    public DecalEffects(IPosedEntities models, BulletStrikes strikes, ILogger render)
    {
        _models = models;
        _strikes = strikes;
        _renderLog = render;
    }

    /// <summary>How many model decals are held, for an instrument.</summary>
    internal int ModelDecalCount => _modelDecals.Count;

    /// <summary>How many server impacts and client bullets the last model-decal step looked at, counted in its loops.</summary>
    internal (int Impacts, int Bullets) ModelDecalExamined { get; private set; }

    /// <summary>Forgets what belonged to the map that is going: its decal pool and its walk.</summary>
    public void ClearMap()
    {
        // The decal pool is the map's; the device's copy goes with ClearWorld.
        _decalReplay = null;
        _decalVersion = -1;
        _decalWorld = null;
    }

    /// <summary>The map as both engine walks see it, built once per map and shared with whoever else walks it.</summary>
    /// <param name="loaded">The loaded map.</param>
    /// <returns>The walk.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loaded"/> is null.</exception>
    public DecalWorld WorldOf(LoadedMap loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        return DecalWorldOf(loaded.Level);
    }

    private DecalWorld DecalWorldOf(MapLevel level) =>
        _decalWorld ??= level.Leaves is { } tree && level.LeafFaces is { } leafFaces
            ? DecalWorld.From(tree, leafFaces, level.Surfaces, level.Terrain)
            : DecalWorld.Empty;

    /// <summary>Brings the world's and the models' decals to the frame's tick, uploading what changed.</summary>
    /// <param name="frame">What this frame is stepped against.</param>
    /// <param name="device">The device that draws them.</param>
    /// <returns>The landing sounds of the client bullets met this frame, to be emitted in this order; valid until the next call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
    public IReadOnlyList<SceneSound> Step(in DecalFrame frame, IDecalUpload device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return StepOver(frame, ContentOf(frame), device);
    }

    /// <summary>The same, over what the frame's map and game hold as data and any device (D214).</summary>
    /// <param name="frame">What this frame is stepped against; its map, game and scripts are not read here.</param>
    /// <param name="content">What they hold.</param>
    /// <param name="device">The device that draws them.</param>
    /// <returns>The landing sounds, as <see cref="Step"/> returns them.</returns>
    internal IReadOnlyList<SceneSound> StepOver(in DecalFrame frame, in DecalContent content, IDecalUpload device)
    {
        _frame = frame;
        _content = content;
        _landed.Clear();

        // With the tracers' player pass, after the model pass has posed this frame's hitboxes.
        StepDecals(frame.Tick, device);
        StepModelDecals(frame.Tick, device);

        return _landed;
    }

    /// <summary>What a frame's map and game hold, the map read out once.</summary>
    private DecalContent ContentOf(in DecalFrame frame)
    {
        if (!ReferenceEquals(frame.Loaded, _mapOf))
        {
            _mapOf = frame.Loaded;
            _map = frame.Loaded is { } loaded ? MapOf(loaded) : null;
        }

        return new DecalContent(_map, frame.Game?.Surfaces, frame.Scripts?.Entries);
    }

    private static DecalMap MapOf(LoadedMap loaded) =>
        new(
            loaded.Level,
            loaded.Impacts,
            loaded.ImpactDecals,
            loaded.Doors,
            loaded.Assets is { } assets
                ? new DecalAssets(assets.DecalMaterials, assets.Shaders, assets.Lightmaps.Rectangles, assets.PlacedProps)
                : null);

    /// <summary>Puts every server impact on a player since the last step onto that player's model (B415).</summary>
    /// <remarks>
    /// <code>
    /// ImpactCallback:    m_nDamageType ≠ the entity's team (TF carries the shooter's team there), and not a spy disguised
    ///                    as that team
    /// Impact:            GetImpactDecal → DamageDecal → TranslateDecalForGameMaterial → the weighted pick
    /// C_TFPlayer::AddDecal: nothing while stealthed or disguised; an übercharge ricochets instead
    /// AddStudioDecal:    ClipRayToEntity( MASK_SHOT ), which tests the hitboxes; a miss places nothing
    /// CModelRender::AddDecal: radius = max( w, h ) · $decalScale / 2; no `$decalFadeDuration` decal; the bones now
    /// </code>
    /// And the clears `C_TFPlayer` makes: a heal to full, an übercharge, a new class (a new model), a cloak, a new
    /// disguise class. A seek replays what it skipped (<see cref="Replay"/>). *Not built:* the invulnerable
    /// ricochet; a building's decal.
    /// </remarks>
    private void StepModelDecals(int tick, IDecalUpload device)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline ||
            _content.Map is not { ImpactDecals: { } impacts, Assets: { } assets } loaded)
        {
            return;
        }

        device.ModelDecals = _modelDecals;
        _modelDecals.PerModel = _frame.MaxModelDecals;

        if (tick < _modelDecalTick)
        {
            // Entities' only: the static props' share of the pool is the decal replay's to rebuild (B421).
            _modelDecals.ClearWhere(static key => !DecalReplay.IsStaticPropKey(key));
            _decalHolders.Clear();
            _modelDecalTick = tick;

            return;
        }

        // **Only what arrived just now is projected** — the engine places a decal as its effect arrives, with the bones as
        // they stand then. After a jump the poses of the ticks passed over were never built, so their impacts are skipped
        // rather than projected onto a pose they never met.
        int from = Math.Max(_modelDecalTick, tick - ModelDecalArrivalTicks);

        _modelDecalTick = tick;

        if (from == int.MinValue)
        {
            return;
        }

        // **Where the frame DREW them, when the frame is of this tick** (B526): a decal's ray is clipped to the struck
        // player's box at his abs origin, the interpolated one his hitboxes were posed at. The recording's newest frame
        // stands in only for a replayed tick no frame showed.
        IReadOnlyList<ScenePlayer> players = _frame.PlayersShown ?? timeline.PlayersAt(tick);

        foreach (ScenePlayer player in players)
        {
            ClearOnChange(player);
        }

        // **Only the landings of this window are looked at** (`measurements/2026-10-22-model-decal-window.md`): the engine
        // places a decal as its event arrives, and walking the whole match each frame to find this frame's (12,518 client
        // bullets on f12) is a cost it never pays. Both lists are in tick order, so the window is found by search; a list
        // that is not is walked whole, which visits the same items in the same order, since the guards below still run.
        IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)> entityImpacts = EntityImpacts(timeline);
        (int firstImpact, int impactsEnd) = LandedWindow.Landed(
            entityImpacts, static impact => impact.Dispatch.Tick, ref _impactsOrderOf, ref _impactsInOrder, from, tick);
        (int firstBullet, int bulletsEnd) = LandedWindow.Landed(
            loaded.Impacts, static bullet => bullet.Tick, ref _bulletsOrderOf, ref _bulletsInOrder, from, tick);

        ModelDecalExamined = (impactsEnd - firstImpact, bulletsEnd - firstBullet);

        for (int at = firstImpact; at < impactsEnd; at++)
        {
            (int index, SceneEffectDispatch impact) = entityImpacts[at];

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (impact.Tick <= from || impact.Tick > tick || Holder(players, impact.Entity) is not { } struck)
            {
                continue;
            }

            PlaceModelDecal(impact, struck, SeededDraw.For(SeededDraw.Of(-1 - index, 0)), impacts, assets, device);
        }

        // **The client's own bullets, where one ends on an enemy's hitboxes** — `FireBullet` calls `UTIL_ImpactTrace` on
        // any struck entity not on the shooter's team (`tf_player_shared.cpp:10524`), and `C_BaseEntity::ImpactTrace`
        // dispatches `"Impact"` with the trace's end, start and surface. *Interpolated:* the surface is `flesh`, what TF2's
        // player bones declare, rather than read from the struck hitbox's bone.
        // ponytail: a scan of every landing per frame; a cursor if a demo ever carries far more than f12's 12,518.
        //
        // **Its sound is decided here too, for the same reason** (`tf_player_shared.cpp:10510-10527`): the trace stops at
        // any player, a teammate included outside MvM (`CTraceFilterSimple`), and only an enemy gets `UTIL_ImpactTrace` — so
        // a bullet a teammate stopped is silent, an enemy's sounds on him, and only a miss sounds on the world behind.
        int flesh = _content.Surfaces?.GetSurfaceIndex("flesh") ?? -1;

        for (int at = firstBullet; at < bulletsEnd; at++)
        {
            ShotImpact bullet = loaded.Impacts[at];

            // Into water: a splash and nothing else — no impact sound, no decal on whoever stood in it (`BulletWater`).
            if (bullet.FromServer || bullet.WaterEntry is not null || bullet.Tick <= from || bullet.Tick > tick)
            {
                continue;
            }

            ((float X, float Y, float Z) End, bool Judged, int? Struck) hit = bullet.BrushOnly ? (bullet.End, true, null) : _strikes.End(bullet, _frame.Bullets);

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (hit.Struck is not { } who)
            {
                EmitLanding(bullet, _content.Surfaces is { } surfaces ? WorldLanding(bullet, impacts, surfaces) : null);
                continue;
            }

            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (Holder(players, who) is not { } victim || victim.Team == bullet.Team)
            {
                continue;
            }

            EmitLanding(
                bullet,
                new BulletLanding(bullet.Tick, hit.End, _content.Surfaces?.GetSurfaceData(flesh)?.BulletImpactSound, bullet.DamageType == Bullet));

            PlaceModelDecal(
                new SceneEffectDispatch(
                    bullet.Tick, 0, hit.End, bullet.Start, default, default, 0, 0f, 0, flesh, 0, bullet.DamageType, 0, who, 0),
                victim,
                SeededDraw.For(SeededDraw.Of(bullet.Shot, bullet.Bullet)),
                impacts,
                assets,
                device);
        }
    }

    /// <summary>Puts one impact's decal on the struck player's model, or logs why not.</summary>
    private void PlaceModelDecal(
        SceneEffectDispatch impact,
        ScenePlayer struck,
        Func<float, float, float> draw,
        ImpactDecals impacts,
        DecalAssets assets,
        IDecalUpload device)
    {
        (ModelDecalShot? shot, string refused) = ModelDecalFor(draw, impact, struck, impacts, assets);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (shot is not { } s)
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(CultureInfo.InvariantCulture, $"model decal tick {impact.Tick} entity {impact.Entity}: none, {refused}"));

            return;
        }

        float scale = s.Decal.DecalScale > 0f ? s.Decal.DecalScale : 1f;
        float radius = MathF.Max(s.Decal.Width * scale * 0.5f, s.Decal.Height * scale * 0.5f);

        bool placed = _modelDecals.Add(
            impact.Entity,
            s.Vertices,
            s.Bones,
            s.Start,
            s.Delta,
            radius,
            s.Material,
            DrawnVertices(device.ModelBatches(s.Model), s.Vertices.Count, s.Body, s.Parts));

        string census = string.Empty;

        if (!placed)
        {
            (int facing, int inside, float nearest) = StudioDecalProjection.Census(s.Vertices, s.Bones, s.Start, s.Delta, radius);

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            census = string.Create(
                CultureInfo.InvariantCulture,
                $" ({facing} of {s.Vertices.Count} vertices face it, {inside} inside the square, the nearest {nearest:0.#} off its axis)");
        }

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        _renderLog.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"model decal tick {impact.Tick} entity {impact.Entity} at ({struck.X:0} {struck.Y:0} {struck.Z:0}), shot from " +
                $"({impact.Start.X:0} {impact.Start.Y:0} {impact.Start.Z:0}): {s.Decal.ModelMaterial ?? s.Decal.Name} radius {radius:0.##} " +
                $"{(placed ? "placed" : "took no triangle")}{census}; {_modelDecals.Count} held"));
    }

    /// <summary>Everything one model decal needs once every guard has passed.</summary>
    private readonly record struct ModelDecalShot(
        Vector3 Start,
        Vector3 Delta,
        string Model,
        IReadOnlyList<float[]> Bones,
        int Body,
        IReadOnlyList<(int Base, int Count)>? Parts,
        IReadOnlyList<WorldVertex> Vertices,
        DecalMaterial Decal,
        int Material);

    /// <summary>The guards between an impact and a decal on the struck model, in the engine's order; the reason when one refuses.</summary>
    private (ModelDecalShot? Shot, string Refused) ModelDecalFor(
        Func<float, float, float> draw, SceneEffectDispatch impact, ScenePlayer struck, ImpactDecals impacts, DecalAssets assets)
    {
        if (impact.DamageType == struck.Team)
        {
            return (null, "same team");
        }

        if (struck.Conditions.IsStealthed || struck.Conditions.Has(PlayerConditions.Disguised) || struck.Conditions.IsInvulnerable)
        {
            return (null, "stealthed, disguised or invulnerable");
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (ServerImpacts.DecalRay(impact) is not { } ray)
        {
            return (null, "no ray");
        }

        // `AddStudioDecal`'s `ClipRayToEntity( ray, MASK_SHOT )`: `MASK_SHOT` holds `CONTENTS_HITBOX`, so the hitboxes, and a
        // miss is a miss — the collision box answers only for a player whose hitboxes could not be tested. *This read "the
        // box when they miss" until 2026-09-24*, and decaled bolts that stopped on the hull where TF2 draws nothing.
        BulletTarget clipped = new(
            impact.Entity, new Vector3(struck.X, struck.Y, struck.Z), ((struck.Flags ?? 0) & Ducking) != 0);

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (PlayerBulletTrace.ClipRayToEntity(clipped, ray.Start, ray.Delta, _strikes.HitboxesOrUntested) is not { } decalAt || decalAt >= 1f)
        {
            float away = MathF.Sqrt(
                ((impact.Origin.X - struck.X) * (impact.Origin.X - struck.X)) + ((impact.Origin.Y - struck.Y) * (impact.Origin.Y - struck.Y)));

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            return (null, _models.IsPosed(impact.Entity)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"the ray misses the hitboxes, the hit {away:0.#} across from the networked origin, {impact.Origin.Z - struck.Z:0.#} up; " +
                    $"the server's box {impact.HitBox} is {Gap(_models.HitboxGap(impact.Entity, impact.HitBox, new Vector3(impact.Origin.X, impact.Origin.Y, impact.Origin.Z)))}; {StateOf(struck)}")
                : "not posed");
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_models.SkinningOf(impact.Entity) is not { } pose || _models.PackedVertices(pose.Model) is not { } vertices)
        {
            return (null, "no skinned model");
        }

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (impacts.ForEntity(impact.SurfaceProp, impact.DamageType, 0, draw) is not
                { Fades: false } decal ||
            !assets.DecalMaterials.TryGetValue(decal.ModelMaterial ?? decal.Draws ?? decal.Name, out int material))
        {
            return (null, "no decal material loaded");
        }

        return (new ModelDecalShot(
            ray.Start, ray.Delta, pose.Model, pose.Bones, pose.Body, pose.Parts, vertices, decal, material), string.Empty);
    }

    /// <summary>Where every bullet the loaded map stops lands, for its sounds (B415), in tick order.</summary>
    /// <param name="loaded">The loaded map, or null.</param>
    /// <param name="game">The game's content, or null.</param>
    /// <param name="timeline">The loaded demo, or null.</param>
    /// <returns>The landings; empty without any of the three.</returns>
    /// <remarks>
    /// The world's: every server impact `UTIL_ImpactTrace` lets through (not sky, not nodraw). An entity's: the server's
    /// `Impact` dispatches, at their origin with their surfaceprop — `PlayImpactSound` takes the server's surfaceprop
    /// whenever the client's trace hit the same entity, and its end is the hitbox a few units away. The client's own
    /// bullets are not here: whether a player stopped one is known only once he is posed, so <see cref="Step"/> emits them.
    /// </remarks>
    public static IReadOnlyList<BulletLanding> Landings(LoadedMap? loaded, GameContent? game, DemoTimeline? timeline)
    {
        if (loaded?.ImpactDecals is null || game is null || timeline is null)
        {
            return [];
        }

        return LandingsOver(loaded.Impacts, loaded.ImpactDecals, game.Surfaces, timeline);
    }

    /// <summary>The same over the map's impacts as data, which a test can supply (D214).</summary>
    /// <param name="impacts">Every bullet the map stops.</param>
    /// <param name="decals">The map's surfaces.</param>
    /// <param name="surfaces">The game's surface properties.</param>
    /// <param name="timeline">The loaded demo.</param>
    /// <returns>The landings, in tick order.</returns>
    internal static IReadOnlyList<BulletLanding> LandingsOver(
        IReadOnlyList<ShotImpact> impacts, ImpactDecals decals, VphysicsSurfaceProps surfaces, DemoTimeline timeline)
    {
        List<BulletLanding> landings = [];

        foreach (ShotImpact impact in impacts)
        {
            if (impact.FromServer && WorldLanding(impact, decals, surfaces) is { } landing)
            {
                int dispatch = -1 - impact.Shot;

                landings.Add(landing with
                {
                    Order = BulletStrikes.ShotOrder(timeline, impact.Shot, impact.Bullet),
                    Reliable = dispatch < timeline.Dispatches.All.Count && timeline.Dispatches.All[dispatch].Reliable,
                });
            }
        }

        foreach ((int _, SceneEffectDispatch hit) in ServerImpacts.OnEntities(timeline.Dispatches.All, timeline.Dispatches.Names.Name))
        {
            landings.Add(new BulletLanding(
                hit.Tick, hit.Origin, surfaces.GetSurfaceData(hit.SurfaceProp)?.BulletImpactSound, hit.DamageType == Bullet)
            {
                Order = new ClientSoundOrder(ClientSoundPhase.TempEntities, hit.TempEntity, 0),
                Reliable = hit.Reliable,
            });
        }

        // Within a tick, the order `CL_FireEvents` fires their dispatches in (B505).
        return [.. landings.OrderBy(static landing => landing.Tick).ThenBy(static landing => landing.Order)];
    }

    /// <summary>A bullet's landing on the world, or null where `UTIL_ImpactTrace` refuses it (sky, nodraw).</summary>
    private static BulletLanding? WorldLanding(ShotImpact impact, ImpactDecals decals, VphysicsSurfaceProps surfaces)
    {
        // Terrain's `surface.flags` is 0 (engine.dll `FUN_18016f290`): never sky, never nodraw.
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (impact.DisplacementTexdata < 0 &&
            (decals.Surface(impact) is not { } surface ||
             (surface.Flags & (Tf2DemoSalvage.Content.Bsp.SurfaceProperties.Sky | Tf2DemoSalvage.Content.Bsp.SurfaceProperties.NoDraw)) != 0))
        {
            return null;
        }

        return new BulletLanding(
            impact.Tick,
            impact.End,
            surfaces.GetSurfaceData(decals.SurfacePropOf(impact))?.BulletImpactSound,
            impact.DamageType == Bullet);
    }

    /// <summary>Plays one of the client's own bullets where it landed (B415).</summary>
    private void EmitLanding(ShotImpact bullet, BulletLanding? landing)
    {
        // A seek's replay reaches bullets that landed seconds ago; TF2's fast-forward is heard no more than ours.
        if (!_frame.Silent && landing is { } l && _content.Scripts is { } scripts)
        {
            // **One shot's pellets share a sound group** (`ImpactSoundGroup`, `tf_fx_shared.cpp:40`): the same impact
            // sound within 300 units of one this shot already played is not played again.
            _shotSounds.Start(bullet.Shot);

            if (l.ImpactSound is { } name && !_shotSounds.Allows(name, new Vector3(l.At.X, l.At.Y, l.At.Z)))
            {
                return;
            }

            foreach (SceneSound sound in ImpactSounds.For(
                l with { Order = BulletStrikes.ShotOrder(_frame.Timeline, bullet.Shot, bullet.Bullet) }, ImpactSounds.SeedFor((bullet.Shot * 32) + bullet.Bullet), scripts))
            {
                _landed.Add(sound);
            }
        }
    }

    /// <summary>A hitbox gap as a log fragment.</summary>
    // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
    private static string Gap((float Outside, Vector3 FromCentre)? gap) =>
        gap is { } g
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{g.Outside:0.#} outside it, ({g.FromCentre.X:0.#} {g.FromCentre.Y:0.#} {g.FromCentre.Z:0.#}) from its centre in its bone's frame")
            : "not posed here";

    /// <summary>What a struck player's pose was built from, for a decal that missed him: the questions a 30-unit gap asks.</summary>
    private string StateOf(ScenePlayer struck)
    {
        string crouch = struck.IsCrouched ? "crouched" : "standing";
        string air = struck.IsAirborne ? "airborne" : "grounded";

        // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
        return string.Create(
            CultureInfo.InvariantCulture,
            $"he is {crouch}, {air}, duck-jump offset {_models.DuckJumpOffsetOf(struck.EntityIndex) ?? 0f:0.#}, " +
            $"frame {_models.FrameOf(struck.EntityIndex)?.ToString() ?? "none"}");
    }

    /// <summary>The player an impact names, when it is one playing now.</summary>
    private static ScenePlayer? Holder(IReadOnlyList<ScenePlayer> players, int entity)
    {
        foreach (ScenePlayer player in players)
        {
            if (player.EntityIndex == entity)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>`RemoveAllDecals` where `C_TFPlayer` calls it: full heal, übercharge, new model, cloak, new disguise class.</summary>
    private void ClearOnChange(ScenePlayer player)
    {
        if (_decalHolders.TryGetValue(player.EntityIndex, out ScenePlayer was) && WipesDecals(was, player))
        {
            _modelDecals.Clear(player.EntityIndex);
        }

        _decalHolders[player.EntityIndex] = player;
    }

    /// <summary>Whether going from one state of a player to the next makes `C_TFPlayer` call `RemoveAllDecals`.</summary>
    internal static bool WipesDecals(ScenePlayer was, ScenePlayer now) =>
        (now.Health > was.Health && now.Health >= now.MaxHealth) || now.Conditions.IsInvulnerable ||
        now.PlayerClass != was.PlayerClass || (now.Conditions.IsStealthed && !was.Conditions.IsStealthed) ||
        now.DisguiseClass != was.DisguiseClass;

    /// <summary>Where a seek's replay of the model decals must start, or null when the jump needs none.</summary>
    /// <param name="frame">The frame at the target, for its demo and map.</param>
    /// <param name="target">The tick sought.</param>
    /// <returns>The tick to replay from, or null: no demo, or a step forward of a second or less from the last one.</returns>
    public int? ReplayStart(in DecalFrame frame, int target)
    {
        _frame = frame;
        _content = ContentOf(frame);

        return ReplayStartNow(target);
    }

    /// <summary>The same, over what the frame's map and game hold as data (D214).</summary>
    /// <param name="frame">The frame at the target.</param>
    /// <param name="content">What its map and game hold.</param>
    /// <param name="target">The tick sought.</param>
    /// <returns>The tick to replay from, or null.</returns>
    internal int? ReplayStartOver(in DecalFrame frame, in DecalContent content, int target)
    {
        _frame = frame;
        _content = content;

        return ReplayStartNow(target);
    }

    private int? ReplayStartNow(int target)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline ||
            (_modelDecalTick != int.MinValue && target >= _modelDecalTick && target - _modelDecalTick <= ModelDecalSeekTicks))
        {
            return null;
        }

        return OldestDecalLife(timeline, target);
    }

    /// <summary>
    /// After a seek, runs the scene forward over the ticks it skipped so their impacts land on the poses they met — what
    /// TF2 does, since it cannot seek at all: it replays the demo up to the tick (`demo_gototick`).
    /// </summary>
    /// <param name="start">Where to replay from — <see cref="ReplayStart"/>'s answer.</param>
    /// <param name="target">The tick sought.</param>
    /// <param name="device">The device that draws the decals.</param>
    /// <param name="pose">Shows a tick and poses its players, then answers that tick's frame.</param>
    /// <returns>What the replay did.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Bounded by the decals that could still be on a model**: the replay starts at the latest tick where every player
    /// playing at the target had his decals wiped (<see cref="WipesDecals"/>) or was not yet alive, so nothing earlier can
    /// show. Each posed tick is a full moment and a model decal step, the same path playback takes.
    /// </remarks>
    public DecalReplayReport Replay(int start, int target, IDecalUpload device, Func<int, DecalFrame> pose)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pose);

        return ReplayOver(
            start,
            target,
            device,
            tick =>
            {
                DecalFrame frame = pose(tick);

                return (frame, ContentOf(frame));
            });
    }

    /// <summary>The same over any device, each posed tick answering its frame and what its map and game hold (D214).</summary>
    /// <param name="start">Where to replay from.</param>
    /// <param name="target">The tick sought.</param>
    /// <param name="device">The device that draws the decals.</param>
    /// <param name="pose">Shows a tick and poses its players, then answers that tick's frame and content.</param>
    /// <returns>What the replay did.</returns>
    internal DecalReplayReport ReplayOver(int start, int target, IDecalUpload device, Func<int, (DecalFrame Frame, DecalContent Content)> pose)
    {
        TimeSpan skipping = TimeSpan.Zero;

        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline)
        {
            return new DecalReplayReport(0, skipping, _modelDecals.Count);
        }

        _modelDecals.ClearWhere(static key => !DecalReplay.IsStaticPropKey(key));
        _decalHolders.Clear();
        _modelDecalTick = start;

        bool[] needed = ReplayTicks(timeline, start, target);
        int stepped = 0;

        // **The props' decals up to the window first, and then tick by tick with the players'** (B421): the pool is
        // shared and retires its oldest, so the two must arrive in the order they landed. A player judged only on his own
        // tick is the world's answer here, as a replay's always is.
        DecalReplay? props = DecalReplayOf();

        props?.AdvanceTo(start, static _ => false);

        for (int tick = start + 1; tick < target; tick++)
        {
            if (!needed[tick - start])
            {
                long skipped = Stopwatch.GetTimestamp();

                // Nothing lands here: only the wipes are brought up to date, from the demo without posing anyone.
                foreach (ScenePlayer player in timeline.PlayersAt(tick))
                {
                    ClearOnChange(player);
                }

                props?.AdvanceTo(tick, static _ => false);
                _modelDecalTick = tick;
                skipping += Stopwatch.GetElapsedTime(skipped);
                continue;
            }

            // Simulated, not drawn: `CModelRender::AddDecal` sets up the struck player's bones itself (`SkinningOf`).
            (_frame, _content) = pose(tick);

            props?.AdvanceTo(tick, bullet => _strikes.StruckPlayer(bullet, tick, _frame.Bullets));
            StepModelDecals(tick, device);
            stepped++;
        }

        return new DecalReplayReport(stepped, skipping, _modelDecals.Count);
    }

    /// <summary>The server's impacts on entities, selected once per demo.</summary>
    private IReadOnlyList<(int Index, SceneEffectDispatch Dispatch)> EntityImpacts(DemoTimeline timeline)
    {
        if (_entityImpacts is null)
        {
            _entityImpacts = ServerImpacts.OnEntities(timeline.Dispatches.All, timeline.Dispatches.Names.Name);

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(CultureInfo.InvariantCulture, $"model decals: {_entityImpacts.Count} server impacts on entities"));
        }

        return _entityImpacts;
    }

    /// <summary>Which ticks of a replay must be posed: every bullet or impact landing, with its warm-up before it.</summary>
    private bool[] ReplayTicks(DemoTimeline timeline, int start, int target)
    {
        bool[] needed = new bool[target - start + 1];

        void Land(int tick)
        {
            for (int t = Math.Max(start + 1, tick - ModelDecalWarmupTicks); t <= Math.Min(target - 1, tick + ModelDecalArrivalTicks); t++)
            {
                needed[t - start] = true;
            }
        }

        foreach ((int _, SceneEffectDispatch impact) in EntityImpacts(timeline))
        {
            if (impact.Tick > start && impact.Tick < target)
            {
                Land(impact.Tick);
            }
        }

        foreach (ShotImpact bullet in _content.Map?.Impacts ?? [])
        {
            if (!bullet.FromServer && !bullet.BrushOnly && bullet.Tick > start && bullet.Tick < target &&
                PassesAnEnemy(bullet, PlayersABulletMayMeet(timeline, bullet.Tick)))
            {
                Land(bullet.Tick);
            }
        }

        return needed;
    }

    /// <summary>Both places a player may be when a past tick's bullet is judged: the recording's newest frame, and where
    /// a playing frame draws him — an interpolation delay behind it (B526).</summary>
    /// <remarks>
    /// **A filter must not be narrower than what it filters for.** The replay judges a bullet against hitboxes posed
    /// where the frame draws the player; this pre-filter measured from the recording's origin alone, which for anyone
    /// running is up to his speed times the interpolation delay away — more than the 19 units of slack
    /// <see cref="ReachOfAPlayer"/> leaves. Either sample reaching is enough to replay the tick: "a bullet let through is
    /// only posed for nothing".
    /// </remarks>
    private List<ScenePlayer> PlayersABulletMayMeet(DemoTimeline timeline, int tick)
    {
        _interpolatedThen.Clear();
        timeline.PlayersAt((double)tick, _interpolatedThen);
        _interpolatedThen.AddRange(timeline.PlayersAt(tick));

        return _interpolatedThen;
    }

    /// <summary>
    /// Whether a bullet's path passes close enough to an enemy to hit his hitboxes — decided from the demo's origins,
    /// without posing, so the replay poses only for bullets that could leave a decal. `FireBullet` decals only an
    /// entity not on the shooter's team (`tf_player_shared.cpp:10524`).
    /// </summary>
    private static bool PassesAnEnemy(ShotImpact bullet, IReadOnlyList<ScenePlayer> players)
    {
        foreach (ScenePlayer player in players)
        {
            if (Reaches(bullet, player))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a bullet could decal this player: he is a living enemy and its path passes within reach of him.</summary>
    internal static bool Reaches(ShotImpact bullet, ScenePlayer player)
    {
        if (!player.IsAlive || player.Team == bullet.Team)
        {
            return false;
        }

        Vector3 from = new(bullet.Start.X, bullet.Start.Y, bullet.Start.Z);
        Vector3 path = new Vector3(bullet.End.X, bullet.End.Y, bullet.End.Z) - from;
        float length = path.LengthSquared();
        Vector3 centre = new(player.X, player.Y, player.Z + 41f);
        float along = length > 0f ? Math.Clamp(Vector3.Dot(centre - from, path) / length, 0f, 1f) : 0f;

        return Vector3.DistanceSquared(from + (path * along), centre) <= ReachOfAPlayer * ReachOfAPlayer;
    }

    /// <summary>Where a seek's replay must start: just before the earliest landing that could still show on someone.</summary>
    /// <remarks>
    /// **A landing matters only if it came after its victim's last wipe** (<see cref="WipesDecals"/>): anything before
    /// that is gone however it landed. So each player's last wipe is found walking back from the target, then the
    /// earliest server impact or client bullet that could have reached him after it — less the warm-up its pose needs.
    /// Players nobody could have hit since their wipe do not hold the replay back at all.
    /// </remarks>
    private int OldestDecalLife(DemoTimeline timeline, int target)
    {
        int floor = Math.Max(0, target - ModelDecalLookbackTicks);
        Dictionary<int, int> wipedAt = LastWipes(timeline, target, floor);
        int earliest = target;

        foreach ((int _, SceneEffectDispatch impact) in EntityImpacts(timeline))
        {
            if (impact.Tick < earliest && impact.Tick < target &&
                wipedAt.TryGetValue(impact.Entity, out int wiped) && impact.Tick > wiped)
            {
                earliest = impact.Tick;
            }
        }

        foreach (ShotImpact bullet in _content.Map?.Impacts ?? [])
        {
            if (bullet.FromServer || bullet.BrushOnly || bullet.Tick >= earliest || bullet.Tick <= floor)
            {
                continue;
            }

            foreach (ScenePlayer player in PlayersABulletMayMeet(timeline, bullet.Tick))
            {
                if (wipedAt.TryGetValue(player.EntityIndex, out int wiped) && bullet.Tick > wiped && Reaches(bullet, player))
                {
                    earliest = bullet.Tick;
                    break;
                }
            }
        }

        return Math.Max(floor, earliest - ModelDecalWarmupTicks - 1);
    }

    /// <summary>Each player's last decal wipe before the target — or the floor, when none is found that far back.</summary>
    internal static Dictionary<int, int> LastWipes(DemoTimeline timeline, int target, int floor)
    {
        Dictionary<int, ScenePlayer> later = [];
        Dictionary<int, int> wipedAt = [];
        HashSet<int> open = [];

        foreach (ScenePlayer player in timeline.PlayersAt(target))
        {
            if (player.IsAlive && player.IsPlaying)
            {
                later[player.EntityIndex] = player;
                wipedAt[player.EntityIndex] = floor;
                open.Add(player.EntityIndex);
            }
        }

        for (int tick = target - 1; tick > floor && open.Count > 0; tick--)
        {
            foreach (ScenePlayer player in timeline.PlayersAt(tick))
            {
                if (!open.Contains(player.EntityIndex))
                {
                    continue;
                }

                if (!player.IsAlive || WipesDecals(player, later[player.EntityIndex]))
                {
                    open.Remove(player.EntityIndex);
                    wipedAt[player.EntityIndex] = tick;
                }

                later[player.EntityIndex] = player;
            }
        }

        return wipedAt;
    }

    /// <summary>Which of a model's vertices its body number draws, as the model pass chooses body parts.</summary>
    internal static Func<int, bool> DrawnVertices(
        IReadOnlyList<WorldBatch> batches, int count, int body, IReadOnlyList<(int Base, int Count)>? parts)
    {
        bool[] drawn = new bool[count];

        foreach (WorldBatch batch in batches)
        {
            bool chosen = parts is null || batch.BodyPart >= parts.Count || parts[batch.BodyPart].Count <= 0 ||
                          body / Math.Max(1, parts[batch.BodyPart].Base) % parts[batch.BodyPart].Count == batch.BodyModel;

            for (int at = batch.FirstVertex; chosen && at < batch.FirstVertex + batch.VertexCount && at < count; at++)
            {
                drawn[at] = true;
            }
        }

        return vertex => vertex >= 0 && vertex < drawn.Length && drawn[vertex];
    }

    /// <summary>Brings the world's decals to this tick and uploads them when they changed (B415).</summary>
    /// <remarks>
    /// **Players are judged only for a bullet met on its own tick**, as a tracer's are: then this frame's bones are the
    /// ones the engine traced against. Met later — a seek, or a replay after one — the world's answer stands, and a
    /// bullet a player took leaves a hole in the wall behind them. That is the stated cost of replaying without
    /// re-posing every player at every shot.
    /// </remarks>
    private void StepDecals(int tick, IDecalUpload device)
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_content.Map is not { Assets: { } assets } loaded || DecalReplayOf() is not { } replay)
        {
            return;
        }

        replay.AdvanceTo(tick, bullet => _strikes.StruckPlayer(bullet, tick, _frame.Bullets));

        if (!_reportedPropRefusals && replay.PropRefusals.Count + replay.PropDecals.Count > 0)
        {
            _reportedPropRefusals = true;
            _renderLog.LogInformation(
                "{Message}",
                $"static prop decals by tick {tick}: {loaded.Impacts.Count(impact => impact.StaticProp >= 0 && impact.Tick <= tick)} prop hits due, " +
                $"{replay.PropDecals.Models.Count(DecalReplay.IsStaticPropKey)} props and {replay.PropDecals.Models.Count(static key => key >= 0)} entities decaled, {replay.PropDecals.Count} model decals held in all; refused " +
                string.Join("; ", replay.PropRefusals.Select(static pair => $"{pair.Value} {pair.Key}")));
        }

        // **A decal on a door rides it** (`R_DecalShoot` into the brush model), so a door that moved rebuilds the mesh too.
        Vector3 doorsAt = Vector3.Zero;

        foreach (PlacedDecal placed in _placedDecals)
        {
            if (placed.Entity >= 0 && loaded.Doors.Of(placed.Entity, tick) is { } door)
            {
                doorsAt += door.Origin;
            }
        }

        if (replay.Decals.Version == _decalVersion &&
            replay.PropDecals.Version == _propDecalVersion &&
            doorsAt == _decalDoorsAt)
        {
            return;
        }

        _decalVersion = replay.Decals.Version;
        _propDecalVersion = replay.PropDecals.Version;
        _decalDoorsAt = doorsAt;
        replay.Decals.Placed(_placedDecals);

        // What the displacement path placed (B415): the first time any terrain bullet is due, the fragments on displacements.
        if (!_reportedTerrainDecals && loaded.Impacts.Any(impact => impact.DisplacementTexdata >= 0 && impact.Tick <= tick))
        {
            _reportedTerrainDecals = true;

            DecalWorld world = DecalWorldOf(loaded.Level);
            List<PlacedDecal> onTerrain = [.. _placedDecals.Where(placed =>
                placed.Face >= 0 && placed.Face < world.Faces.Count && world.Faces[placed.Face] is { Displacement: true })];

            // A bullet's own hole where there is one, the control that the impact path reaches terrain.
            PlacedDecal? shown = onTerrain.Find(static placed => placed.Material.Name.Contains("shot", StringComparison.OrdinalIgnoreCase)) ??
                (onTerrain.Count > 0 ? onTerrain[0] : null);
            Vector3 first = shown?.Polygon[0].Position ?? Vector3.Zero;
            Vector3 plane = shown is not null && world.Faces[shown.Face] is { } parent ? parent.PlaneNormal : Vector3.Zero;

            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogInformation(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"terrain decals at {tick}: {onTerrain.Count} fragments of {onTerrain.Select(static placed => placed.Slot).Distinct().Count()} decals, over {world.Displacements.Count} displacements; the first at ({first.X:0.00} {first.Y:0.00} {first.Z:0.00}) on a face whose plane is ({plane.X:0.00} {plane.Y:0.00} {plane.Z:0.00}); materials {string.Join(", ", onTerrain.Select(static placed => placed.Material.Name).Distinct())}; {loaded.Impacts.Count(impact => impact.DisplacementTexdata >= 0 && impact.Tick <= tick)} terrain bullets due"));
        }

        if (_renderLog.IsEnabled(LogLevel.Debug))
        {
            // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
            _renderLog.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"decals rebuilt at {tick}: {_placedDecals.Count} placed, {_placedDecals.Count(static placed => placed.Entity >= 0)} on brush entities, {replay.PropDecals.Count} model decals held, props {string.Join(",", replay.PropDecals.Models.Where(DecalReplay.IsStaticPropKey))}"));

            foreach (PlacedDecal placed in _placedDecals.Where(static placed => placed.Entity >= 0))
            {
                Vector3 at = placed.Polygon[0].Position + (loaded.Doors.Of(placed.Entity, tick)?.Origin ?? Vector3.Zero);

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                _renderLog.LogDebug(
                    "{Message}",
                    string.Create(CultureInfo.InvariantCulture, $"decal on entity {placed.Entity}, face {placed.Face}: corner at ({at.X:0} {at.Y:0} {at.Z:0})"));
            }
        }

        Func<int, float?> unlit = index => index < assets.Shaders.Count &&
                                           string.Equals(assets.Shaders[index], "DecalModulate", StringComparison.OrdinalIgnoreCase)
            ? Device3D.ModulateTwiceLight
            : null;

        DecalMesh.Build(
            _placedDecals,
            material => material.Draws is { } drawn && assets.DecalMaterials.TryGetValue(drawn, out int index) ? index : -1,
            unlit,
            assets.Lightmaps,
            _decalVertices,
            _decalBatches,
            entity => loaded.Doors.Of(entity, tick)?.Origin,
            _entityDecalBatches);

        // **A static prop's decals draw with the prop** (B421): already in the world, after the world's own decals.
        foreach (int prop in replay.PropDecals.Models)
        {
            // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
            if (!DecalReplay.IsStaticPropKey(prop) || replay.PropDecals.For(prop) is not { } held)
            {
                continue;
            }

            if (!_reportedPropDecal)
            {
                _reportedPropDecal = true;

                WorldVertex corner = held.Vertices[0];

                // Stryker disable once all : a mutated interpolated string cannot bind to its handler (CS1620) and Safe Mode drops the method - B410
                _renderLog.LogInformation(
                    "{Message}",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"first static prop decal at tick {tick}, prop {prop}: corner ({corner.X:0} {corner.Y:0} {corner.Depth:0}), material {held.Batches[0].MaterialIndex}"));
            }

            foreach (WorldBatch batch in held.Batches)
            {
                float light = unlit(batch.MaterialIndex) ?? 1f;
                int first = _decalVertices.Count;

                for (int at = batch.FirstVertex; at < batch.FirstVertex + batch.VertexCount; at++)
                {
                    _decalVertices.Add(held.Vertices[at] with { Red = light, Green = light, Blue = light });
                }

                _entityDecalBatches.Add(batch with { FirstVertex = first });
            }
        }

        device.UploadShotDecals(_decalVertices, _decalBatches, _entityDecalBatches);
    }

    /// <summary>The prop corners a decal shot projects onto, reused across shots and only grown (finding 80).</summary>
    private WorldVertex[] _propInWorld = [];

    /// <summary>
    /// A static prop's triangles in the world, written into <see cref="_propInWorld"/>: valid until the next call, which is
    /// all <c>DecalReplay</c> asks, since the projection copies the corners it keeps. A new copy per shot was 33 MB of
    /// large-object garbage per 20 s of f12 (finding 80).
    /// </summary>
    private ArraySegment<WorldVertex> PropInWorld(PlacedProp placed)
    {
        if (_propInWorld.Length < placed.Shape.Positions.Length)
        {
            _propInWorld = new WorldVertex[placed.Shape.Positions.Length];
        }

        return new ArraySegment<WorldVertex>(_propInWorld, 0, placed.InWorld(_propInWorld));
    }

    /// <summary>The world's decal replay, made on first use once a map and a timeline are loaded.</summary>
    private DecalReplay? DecalReplayOf()
    {
        // Stryker disable once all : a mutated guard leaves its pattern variables unassigned (CS0165) and Safe Mode drops the method - B410
        if (_frame.Timeline is not { } timeline || _content.Map is not { Assets: { } assets, ImpactDecals: { } impacts } loaded)
        {
            return null;
        }

        return _decalReplay ??= new DecalReplay(
            new WorldDecals(DecalWorldOf(loaded.Level), _frame.MaxDecals),
            loaded.Impacts,
            timeline.Decals.All,
            impacts.For,
            index => timeline.Decals.Names.Name(index) is { } name ? impacts.Materials.Resolve(name) : null,
            loaded.Doors.Of,
            new StaticPropDecalSource(
                prop => assets.PlacedProps.TryGetValue(prop, out PlacedProp placed) ? PropInWorld(placed) : (IReadOnlyList<WorldVertex>?)null,
                material => assets.DecalMaterials.TryGetValue(material.ModelMaterial ?? material.Draws ?? material.Name, out int index)
                    ? index
                    : -1),

            // **One pool for every model**, as `CStudioRender` keeps: a prop's decal and a player's share the limits.
            _modelDecals);
    }
}
