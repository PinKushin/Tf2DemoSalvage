using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The sun's work since a map loaded (<see cref="LevelLighting.SunWork"/>), each part counted where it ran.</summary>
/// <param name="Calls">Every call into the sun's leaf test and sky trace.</param>
/// <param name="LightCache">Of those, a model light cache entry being built.</param>
/// <param name="StaticProp">Of those, a static prop's lighting state being built.</param>
/// <param name="Direct">Of those, a direct <see cref="LevelLighting.SunAt"/>: an impact's colour, a model with no tree.</param>
/// <param name="Ticks">Stopwatch ticks inside them, while timed.</param>
public readonly record struct SunWork(long Calls, long LightCache, long StaticProp, long Direct, long Ticks)
{
    /// <summary>The work done since <paramref name="earlier"/>, of the same map.</summary>
    /// <param name="earlier">An earlier reading.</param>
    /// <returns>The difference, part by part.</returns>
    public SunWork Since(SunWork earlier) => new(
        Calls - earlier.Calls, LightCache - earlier.LightCache, StaticProp - earlier.StaticProp,
        Direct - earlier.Direct, Ticks - earlier.Ticks);
}

/// <summary>The light a map casts at a world position: its bounce, its lamps and its sun.</summary>
/// <remarks>
/// **This is the engine's query, and it belongs behind an interface rather than on the window.**
/// <c>IVEngineClient</c> declares
///
/// <code>
/// // Computes light due to dynamic lighting at a point
/// // If the normal isn't specified, then it'll return the maximum lighting
/// // If pBoxColors is specified (it's an array of 6), then it'll copy the light contribution at each box side.
/// virtual void ComputeLighting( const Vector&amp; pt, const Vector* pNormal, bool bClamp, Vector&amp; color, Vector *pBoxColors=NULL ) = 0;
/// </code>
///
/// at <c>src/public/cdll_int.h:392</c> — six box sides IS an ambient cube — and client code asks
/// for it as <c>engine-&gt;ComputeLighting( pos, NULL, true, vecColor )</c>
/// (<c>c_impact_effects.cpp:486</c>, <c>c_rope.cpp:2053</c>, <c>proxypupil.cpp:89</c>). Not one
/// caller owns the lighting data; they ask the level for it.
///
/// Ours was <c>MainForm.LightAt</c> and <c>MainForm.SunAt</c>, handed to <see cref="MapAssets"/> and
/// <see cref="EntityModelSet"/> as delegates. Three fields of map state lived on the form for them,
/// nothing could test them without an STA thread and a device, and a second frontend would have had
/// to reimplement both (B188, B184, D90).
///
/// **The two halves are separate because the sun is conditional and the rest is not.** Valve
/// defines a sky light as a "directional light with no falloff (surface must trace to SKY
/// texture)", so <see cref="SunAt"/> can answer "no sun here" for a point that
/// <see cref="ComputeLighting"/> still lights.
/// </remarks>
public sealed class LevelLighting
{
    private readonly BspLeafTree? _leaves;
    private readonly IReadOnlyList<AmbientSamples> _ambient;
    private readonly IReadOnlyList<BspWorldLight> _worldLights;

    /// <summary>
    /// The world lights a caller whose cube came from the leaf adds: all but those vrad put in the leaf cubes, as
    /// `engine.dll` `FUN_1801b5a50` passes over `flags &amp; 1` when `FUN_1801bb4b0` read the leaf (B669).
    /// </summary>
    private readonly IReadOnlyList<BspWorldLight> _leafCubeLights;

    private readonly BspWorldLight? _sun;
    private readonly ILogger _render;

    /// <summary>The map's sky light, the sun's slot: its origin and falloff are what `r_ambientboost` weighs it by (B672).</summary>
    public BspWorldLight? Sky => _sun;

    /// <summary>Places already reported, so the line does not repeat per frame.</summary>
    /// <remarks>
    /// **Bounded at the report limit rather than growing for the life of the map.** The original
    /// added every place it was asked about and only then checked the limit, so the set kept
    /// growing after it had stopped reporting — one entry per distinct integer position of every
    /// model, for as long as playback ran. The reported lines are identical either way; this
    /// version simply stops remembering places it will never report.
    /// </remarks>
    private readonly HashSet<(int X, int Y, int Z)> _reportedLightTerms = [];

    /// <summary>Each light style's value over 264, which the world lights are scaled by; one for every style unless set.</summary>
    /// <remarks>
    /// The lightcache multiplies a world light's falloff by it before ranking (`engine.dll` `0x1801b8e20`). **Setting it
    /// stamps nothing**, as a value changing stamps nothing until `R_AnimateLight` does: what was built stands until
    /// <see cref="StylesChanged(IReadOnlyCollection{int})"/> — which the first <see cref="LightStyleValues.Advance"/> calls
    /// for every style.
    /// </remarks>
    public Func<int, float>? StyleScale { get; set; }

    /// <summary>Whether any of these styles is one a world light answers to — whether model lighting must be sampled again.</summary>
    /// <param name="styles">Styles whose values changed.</param>
    /// <returns>True when a world light's brightness changed with them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="styles"/> is null.</exception>
    public bool Answers(IReadOnlyCollection<int> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        foreach (BspWorldLight light in _worldLights)
        {
            if (styles.Contains(light.Style))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>`MASK_OPAQUE`: solid, opaque and moveable contents, which `0x1801b6860` keeps a cell centre out of.</summary>
    private const int MaskOpaque = 0x4081;

    /// <summary>Where the light cache lights a model standing at a point — <see cref="LightCacheCell"/>.</summary>
    /// <remarks>
    /// The traces are `0x1801b6860`'s: `MASK_OPAQUE`, world only, so they meet terrain (<see cref="_reaches"/>). Without a
    /// reach test — a light source built without a level — the point is lit where it stands.
    /// </remarks>
    private (float X, float Y, float Z) CachePoint(float x, float y, float z)
    {
        if (_leaves is not { } tree || _reaches is not { } reaches)
        {
            return (x, y, z);
        }

        System.Numerics.Vector3 at = LightCacheCell.Position(
            new System.Numerics.Vector3(x, y, z),
            point => (tree.ContentsAt(point.X, point.Y, point.Z) & MaskOpaque) != 0,
            (from, to) => reaches((from.X, from.Y, from.Z), (to.X, to.Y, to.Z)));

        return (at.X, at.Y, at.Z);
    }

    /// <summary>Whether a `MASK_OPAQUE` world trace between two points is clear; see the constructor.</summary>
    private readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? _reaches;

    /// <summary>The engine's model light cache (<see cref="LightCache{T}"/>), one per map as a map load flushes it.</summary>
    private LightCache<CachedLight> _cache = new(CachedLight.Unbuilt);

    /// <summary>
    /// `min( r_worldlights, MaxNumLights )`: how many lights take a slot, what `FUN_1801b79e0`, `FUN_1801b6d40` and `FUN_1801b7e90` each read
    /// before they append, the rest folding. A hardware of four lights here, as this viewer's shader is.
    /// </summary>
    /// <remarks>
    /// **A change flushes the cache and relights every handle** (`FUN_1801bcbb0`, each frame, against `DAT_18047de20`), so nothing keeps the
    /// old count. A colour mesh baked already keeps it, there too: `FUN_1801bbed0` rebuilds the handle's state, not the mesh.
    /// </remarks>
    public int WorldLightSlots
    {
        get;
        set
        {
            int clamped = Math.Min(value, LocalLights.MaximumLocalLights);

            if (clamped == field)
            {
                return;
            }

            field = clamped;
            _cache = new LightCache<CachedLight>(CachedLight.Unbuilt);
            _entryAsked = null;
            _staticStates.Clear();

            // Reranked at their next ask, with the list and the world box they were made with (`FUN_1801bbed0( handle, 0, 1 )`).
            foreach (StyledHandle handle in _styledByEntity.Values)
            {
                handle.BuiltAt = -1;
                handle.Full = null;
            }
        }
    } = LocalLights.MaximumLocalLights;

    /// <summary>What each dlight's slot holds, as the last conversion wrote it (`DAT_180775730`, `FUN_1801b9350`), which a model's average reads for a dlight that left (B676).</summary>
    public BspWorldLight[] ConvertedDlights { get; } = new BspWorldLight[DynamicLights.MaxDlights + DynamicLights.MaxElights];

    /// <summary>Bumped when a style a world light answers to changes; an entry built before it is relit on its next hit.</summary>
    private int _styleVersion;

    /// <summary>`r_framecount`: advance once per rendered frame, before the frame's models are lit.</summary>
    public int Frame { get; set; }

    /// <summary>Since the map loaded: calls into the sun's leaf test and sky trace, counted inside it (B594).</summary>
    public long SunCalls { get; private set; }

    /// <summary>
    /// Since the map loaded: <see cref="SunCalls"/> by caller and the stopwatch ticks spent inside them, every count and the
    /// time taken where the sun is asked for (B243). The ticks are taken only while <see cref="TimeSun"/> is on.
    /// </summary>
    public SunWork SunWork => new(SunCalls, _sunBy[(int)SunCaller.LightCache], _sunBy[(int)SunCaller.StaticProp],
        _sunBy[(int)SunCaller.Direct], _sunTicks);

    /// <summary>Whether <see cref="SunWork"/> times each call — `--measure` only, since it costs two clock reads a call.</summary>
    public bool TimeSun { get; set; }

    private readonly long[] _sunBy = new long[3];
    private long _sunTicks;

    /// <summary>Who asked for the sun: a light cache entry built, a static prop's state built, or a direct <see cref="SunAt"/>.</summary>
    private enum SunCaller
    {
        LightCache,
        StaticProp,
        Direct,
    }

    /// <summary>
    /// These light styles changed: every light cache entry is relit when next used, and each style is stamped, as
    /// `R_AnimateLight` stamps `DAT_1806998a0[style]`, for a static prop's handle to compare with its `+0xf8` (B607).
    /// </summary>
    /// <param name="styles">The styles whose values changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="styles"/> is null.</exception>
    public void StylesChanged(IReadOnlyCollection<int> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        _styleVersion++;

        foreach (int style in styles)
        {
            _styleStamps[style] = _styleVersion;
        }
    }

    /// <summary>When each style last changed, in <see cref="_styleVersion"/>s; a style never changed is absent.</summary>
    private readonly Dictionary<int, int> _styleStamps = [];

    /// <summary>A model's light, through the light cache: see <see cref="LightCache{T}"/>.</summary>
    /// <param name="x">The model's lighting origin.</param>
    /// <param name="y">The model's lighting origin.</param>
    /// <param name="z">The model's lighting origin.</param>
    /// <returns>The cube and local lights of the entry lighting this point.</returns>
    /// <remarks>
    /// **The entry is built at the first point to miss its cell and leaf**, and every model after it in that cell is lit
    /// with it — model lighting passes flags `0xf` (`0x1800f1bd0`), so past `lightcache_maxmiss` it takes the nearest entry.
    ///
    /// **That nearest can be a zeroed one, and this is the engine's copy of it**: a zero cube, no slots, and the dlights seen from its origin (0 0 0)
    /// (`FUN_1801b7a10`, no PVS row handed in). It wins only over a built entry further away than the pool's key 0 is, so a point in the map's low corner with
    /// every built entry at the far side, some 16,000 units off (arithmetic: 32-unit cells from key 0). **The engine then does not draw it as it stands:** a
    /// non-static model's draw blends it with the instance's last state (`FUN_1800f4310`, <see cref="ModelLightAverage"/>), so it fades there and here.
    /// </remarks>
    public PointLighting ModelLightingAt(float x, float y, float z)
    {
        CachedLight? entry = Entry(x, y, z);
        PointLighting lit = entry?.Lighting ?? LightingAt(x, y, z);

        return Dynamic is { } dynamic && _leaves is { } tree
            ? WithDynamicLights(lit, entry?.State, dynamic, tree, entry?.X ?? x, entry?.Y ?? y, entry?.Z ?? z)
            : lit;
    }

    /// <summary>The client's dynamic lights (B425), which every model draw ranks with the world lights; null for none.</summary>
    public DynamicLights? Dynamic { get; set; }

    /// <summary>The dynamic lights a model draw sees, reused.</summary>
    private readonly List<DynamicLight> _dynamic = [];

    /// <summary>
    /// `LightcacheGet` flag 2 (`engine.dll` `0x1801b9cd0` → `0x1801b7a10`, merged by `0x1801b5890`): each dynamic light
    /// whose cluster is in the PVS of the model's leaf is converted (<see cref="DynamicLights.ToWorldLight"/>) and
    /// ranked against the entry's state at the entry's point, so it takes a local-light slot from a weaker world light.
    /// </summary>
    /// <remarks>
    /// **Added to a copy of the entry's state, as `0x1801b5890` adds to the copy `LightcacheGet` made**: no re-ranking of the world lights, no
    /// trace, and an evicted light or a loser folded (`FUN_1801b60a0`, B453). No precedence: that is `FUN_1801b6550`, a static prop's.
    /// ponytail: the engine ranks dlights among themselves first and merges that state's cube; added one at a time the set differs only
    /// past four dlights at one model.
    /// </remarks>
    private PointLighting WithDynamicLights(
        PointLighting lit, LightingState? state, DynamicLights dynamic, BspLeafTree tree, float x, float y, float z)
    {
        dynamic.ModelLights(_dynamic);
        _dynamicChecked++;

        if (_dynamic.Count == 0)
        {
            return lit;
        }

        long started = Stopwatch.GetTimestamp();

        try
        {
            _dynamicAsked++;

            return WithDynamicLightsFrom(lit, state, tree, x, y, z);
        }
        finally
        {
            _dynamicTicks += Stopwatch.GetTimestamp() - started;
        }
    }

    private long _dynamicChecked;
    private long _dynamicAsked;
    private long _dynamicMerged;
    private long _dynamicTicks;

    /// <summary>
    /// Since the map loaded: model draws that asked for dynamic lights (the instrument's control), those that saw one,
    /// how many of them re-ranked the lights, and the stopwatch ticks the ones that saw one spent here. Each count is
    /// taken where the work runs.
    /// </summary>
    public (long Checked, long Asked, long Merged, long Ticks) DynamicMerges =>
        (_dynamicChecked, _dynamicAsked, _dynamicMerged, _dynamicTicks);

    private PointLighting WithDynamicLightsFrom(PointLighting lit, LightingState? state, BspLeafTree tree, float x, float y, float z)
    {
        int from = tree.ClusterAt(x, y, z);
        LightingState? merged = null;

        // At the entry's point, as LightingAt lights it. Neither `0x1801b7a10` nor `0x1801b5890` traces a dlight (B666).
        (float atX, float atY, float atZ) = CachePoint(x, y, z);

        foreach (DynamicLight light in _dynamic)
        {
            int cluster = tree.ClusterAt(light.X, light.Y, light.Z);

            // `0x1801b7a10` tests the same row: a cluster of 0 or more, with its bit set.
            if (InPvs(from, cluster))
            {
                merged ??= state?.Clone() ?? new LightingState(default);

                BspWorldLight converted = DynamicLights.ToWorldLight(light, cluster);

                if (light.Slot >= 0)
                {
                    ConvertedDlights[light.Slot] = converted;
                }

                LocalLights.Add(merged, converted, atX, atY, atZ, StyleScale, slots: WorldLightSlots);
            }
        }

        if (merged is null)
        {
            return lit;
        }

        _dynamicMerged++;

        return merged.LightingOnDemand(StyleScale);
    }

    /// <summary>The sun a model's light cache entry sees: see <see cref="ModelLightingAt"/>.</summary>
    /// <param name="x">The model's lighting origin.</param>
    /// <param name="y">The model's lighting origin.</param>
    /// <param name="z">The model's lighting origin.</param>
    /// <returns>The sun when it holds one of the model's slots, or null.</returns>
    /// <remarks>
    /// **The sun is a slot of the state, ranked with the lamps** (B170, B676): `0x1801b79e0` takes the sky light as it takes a lamp, so
    /// the sun a model draws is the one its final state holds, after its dlights, which can evict it. <see cref="ModelLighting.For"/> reads
    /// it from the state it was handed; this asks the same question for a caller with only a point.
    /// </remarks>
    public SunLight? ModelSunAt(float x, float y, float z) => SunOf(ModelLightingAt(x, y, z).State);

    /// <summary>The sky light a state holds in a slot, as the shader's sun takes it (`FUN_1800f2010` case 3), or null.</summary>
    /// <param name="state">The state; null holds none.</param>
    /// <returns>The sun.</returns>
    public static SunLight? SunOf(LightingState? state)
    {
        for (int slot = 0; state is not null && slot < state.Count; slot++)
        {
            if (state.SlotAt(slot) is { Kind: WorldLightKind.SkyLight } sky)
            {
                return new SunLight(sky.Intensity.Red, sky.Intensity.Green, sky.Intensity.Blue, sky.Normal.X, sky.Normal.Y, sky.Normal.Z);
            }
        }

        return null;
    }

    /// <remarks>
    /// **One lookup a model, as the engine's model path makes one `LightcacheGet`** (`0x1800f1bd0`, `0x1801b9cd0`) and reads its
    /// lights and sun from the entry, the sun being one of its slots (`0x1801b79e0`, B170); ours can be asked twice,
    /// <see cref="ModelLightingAt"/> and <see cref="ModelSunAt"/>, so the last answer is remembered. **Repeating the lookup could not answer differently (arithmetic), when the memo is used:** every
    /// lookup comes through here and replaces it, so a match means the last lookup was this point's. After a hit the entry is at the
    /// front, after a build it is at the front and a hit, and past the budget <see cref="LightCache{T}.Get"/> has changed nothing and
    /// would scan the same list. **A model's two questions are not guaranteed one lookup:** a <see cref="ComputeLighting"/> at another
    /// point between them replaces the memo, so the second looks again, and past the budget it can take a different entry than the
    /// first, where the engine's one `LightcacheGet` could not. The frame is part of the question because the budget is per frame.
    /// The relight below is not remembered, so a style change between the two is still seen.
    /// </remarks>
    private CachedLight? Entry(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return null;
        }

        System.Numerics.Vector3 point = new(x, y, z);

        if (_entryAsked is null || point != _entryPoint || Frame != _entryFrame)
        {
            _buildAt = point;
            LightCacheLookups++;

            // Forgotten before it looks: a build that throws answered nothing and has moved its victim, so no later question may skip a look (B675).
            _entryAsked = null;
            _entryAsked = _cache.Get(point, tree.LeafAt(x, y, z), Frame, allowFast: true, _buildEntry);
            _entryPoint = point;
            _entryFrame = Frame;
        }

        CachedLight entry = _entryAsked;

        if (entry.Built && entry.StyleVersion != _styleVersion)
        {
            entry.State = StateAt(entry.X, entry.Y, entry.Z, SunCaller.LightCache);
            entry.Lighting = entry.State.Lighting(StyleScale);
            entry.StyleVersion = _styleVersion;
        }

        return entry;
    }

    /// <summary>Lights a new entry at <see cref="_buildAt"/>; one delegate, so a hit allocates no closure.</summary>
    private readonly Func<CachedLight> _buildEntry;

    /// <summary>The point the lookup under way asks about, which <see cref="_buildEntry"/> reads.</summary>
    private System.Numerics.Vector3 _buildAt;

    /// <summary>The point <see cref="_entryAsked"/> answered.</summary>
    private System.Numerics.Vector3 _entryPoint;

    /// <summary>The frame <see cref="_entryAsked"/> answered in.</summary>
    private int _entryFrame;

    /// <summary>The cache's last answer; see <see cref="Entry"/>.</summary>
    private CachedLight? _entryAsked;

    /// <summary>Since the map loaded: times <see cref="Entry"/> looked in the cache, counted where it does.</summary>
    public long LightCacheLookups { get; private set; }

    /// <summary>What a cache entry holds: where it was built, and what it lit there.</summary>
    private sealed class CachedLight(float x, float y, float z, LightingState state, PointLighting lighting, int styleVersion)
    {
        /// <summary>A zeroed pool entry (`0x1801bb640`): no cube, no lights, no sun.</summary>
        public static readonly CachedLight Unbuilt = new(0f, 0f, 0f, new LightingState(default), PointLighting.None, -1) { Built = false };

        /// <summary>The state itself, for a dlight to be ranked against and `ComputeLighting` to fold.</summary>
        public LightingState State { get; set; } = state;

        public float X { get; } = x;

        public float Y { get; } = y;

        public float Z { get; } = z;

        public bool Built { get; private init; } = true;

        public PointLighting Lighting { get; set; } = lighting;

        public int StyleVersion { get; set; } = styleVersion;
    }

    /// <summary>How many places to report light terms for before falling silent.</summary>
    /// <remarks>Public so the test asserts against this value rather than a copy of it.</remarks>
    public const int LightTermReportLimit = 40;

    /// <summary>Creates a light source for one map.</summary>
    /// <param name="leaves">The BSP tree, or null when the map carried none.</param>
    /// <param name="ambient">Per-leaf ambient samples.</param>
    /// <param name="worldLights">Every light the compiler recorded, not only the sun.</param>
    /// <param name="sun">The single directional light, when the map has one.</param>
    /// <param name="render">Where the light terms are reported.</param>
    /// <param name="strikesSky">
    /// Whether a world-only trace between two points strikes sky first (<see cref="MapLevel.StrikesSky"/>); without
    /// one no point sees the sun.
    /// </param>
    /// <param name="reaches">
    /// Whether a `MASK_OPAQUE` world trace between two points is clear, for placing the light cache's cell point
    /// (<see cref="LightCacheCell"/>); without one a model is lit where it stands.
    /// </param>
    /// <param name="visibility">The PVS, for <see cref="StyledLights"/>; without one every cluster is admitted.</param>
    /// <param name="staticPropAmbient">
    /// A static prop's computed ambient cube at its lighting origin (<see cref="StaticPropAmbient"/>); without one a
    /// static prop takes its leaf's samples, as a model that moves does.
    /// </param>
    /// <param name="lightTrace">
    /// The fraction of a `MASK_OPAQUE` world trace between two points that is clear, for each world light's visibility
    /// trace (<see cref="LocalLights.TraceKeeps"/>, B666); without one no light is traced.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument other than the map data is null.</exception>
    public LevelLighting(
        BspLeafTree? leaves,
        IReadOnlyList<AmbientSamples> ambient,
        IReadOnlyList<BspWorldLight> worldLights,
        BspWorldLight? sun,
        ILogger render,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? strikesSky = null,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? reaches = null,
        BspVisibility? visibility = null,
        Func<float, float, float, AmbientCube>? staticPropAmbient = null,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), float>? lightTrace = null)
    {
        _reaches = reaches;
        _lightTrace = lightTrace;
        _ask = new TraceAsk(this);
        _buildEntry = () =>
        {
            (float x, float y, float z) = (_buildAt.X, _buildAt.Y, _buildAt.Z);

            LightingState state = StateAt(x, y, z, SunCaller.LightCache);

            return new CachedLight(x, y, z, state, state.Lighting(StyleScale), _styleVersion);
        };
        _visibility = visibility;
        _staticPropAmbient = staticPropAmbient;
        ArgumentNullException.ThrowIfNull(ambient);
        ArgumentNullException.ThrowIfNull(worldLights);
        ArgumentNullException.ThrowIfNull(render);

        _leaves = leaves;
        _ambient = ambient;
        _worldLights = worldLights;
        _leafCubeLights = [.. worldLights.Where(static light => !light.InAmbientCube)];
        _sun = sun;
        _render = render;
        _strikesSky = strikesSky;
    }

    /// <summary>A static prop's computed ambient; see the constructor.</summary>
    private readonly Func<float, float, float, AmbientCube>? _staticPropAmbient;

    /// <summary>
    /// Each static state already built, by lighting origin: the handle's `+0x18`, computed once
    /// (`FUN_1801ba590`'s `+0x174 &amp; 4`) — style-0 lights, the sun and the cube, none of which change.
    /// </summary>
    private readonly Dictionary<(float X, float Y, float Z), (PointLighting Lighting, SunLight? Sun, LightingState State)> _staticStates = [];

    /// <summary>The skylight's visibility trace; see the constructor.</summary>
    private readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? _strikesSky;

    /// <summary>How far the light cache traces toward a skylight: `engine.dll` 0x1801b8e20's constant.</summary>
    private const float SkyTraceLength = 57016.32f;

    /// <summary>The light a map that has not been read casts, which is none.</summary>
    /// <param name="render">Where the light terms would be reported.</param>
    /// <returns>A source that answers unlit everywhere.</returns>
    /// <remarks>
    /// **A real object rather than a null field**, so every caller asks the same question of the
    /// same type whether or not a map is open — the null-object shape D83 settled on after a null
    /// default hid a missed wiring for 193 call sites.
    /// </remarks>
    public static LevelLighting Unlit(ILogger render) => new(null, [], [], null, render);

    /// <summary>The light one level casts.</summary>
    /// <param name="level">The lumps already read.</param>
    /// <param name="render">Where the light terms are reported.</param>
    /// <param name="find">
    /// The level's file lookup, the pakfile ahead of the archives, which the static prop cube asks for each struck
    /// material's reflectivity (<see cref="MaterialReflectivity"/>, B683). Null keeps the texdata's copy.
    /// </param>
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
    /// <remarks>
    /// Built from <see cref="MapLevel"/> rather than from the file, because the lighting lumps are
    /// read once with everything else — the engine's <c>LevelInitPreEntity</c> shape
    /// (<c>igamesystem.h:39</c>), where a system initialises itself from the level it is handed.
    /// </remarks>
    public static LevelLighting From(MapLevel level, ILogger render, Func<string, byte[]?>? find)
    {
        ArgumentNullException.ThrowIfNull(level);

        // One trace for both: `0x1801b6860`'s cell placement and `FUN_1801b9570`'s light visibility are each `MASK_OPAQUE`,
        // world only — brushes and displacements, no entity or static prop.
        Func<(float X, float Y, float Z), (float X, float Y, float Z), float> opaque =
            (from, to) => level.TraceBrushOnly(from, to, 0f, MaskOpaque).Fraction;

        return new LevelLighting(
            level.Leaves,
            level.Ambient,
            level.WorldLights,
            level.Sun,
            render,
            level.StrikesSky,
            (from, to) => opaque(from, to) >= 1f,
            level.Visibility,
            StaticPropAmbientOf(level, find),
            opaque);
    }

    /// <summary>The visibility trace's fraction; see the constructor.</summary>
    private readonly Func<(float X, float Y, float Z), (float X, float Y, float Z), float>? _lightTrace;

    /// <summary>
    /// The light cache's PVS gate and visibility trace (<see cref="LocalLights.TraceKeeps"/>) over <paramref name="lights"/> at a point.
    /// Null only with neither to apply. A caller that sets `FUN_1801b9570`'s flags bit 0, a dlight's, does not come through here.
    /// </summary>
    /// <remarks>
    /// *Read in disassembly* (B666), the callers of `AddWorldLightToLightingState` (`0x1801b60a0`) and the `skipTrace`
    /// each passes: traced are the style-0 world lights (`0x1801b5a50`, from `ComputeStaticLightingForCacheEntry`), the
    /// light cache's styled lights (`0x1801ba322`), its stored lists (`0x1801b5890`) and a static prop handle's styled
    /// list (`0x1801b5400`); not traced are live dlights (`0x1801b7a10`, `0x1801bb040`) and the handle list's admission
    /// (`FUN_1801b6bf0`, flags `9|2`, <see cref="StyledLights"/>). The point is the caller's: the cache cell's, or a
    /// static prop's own lighting origin.
    ///
    /// **One delegate, made once, reading the call's arguments from fields** (<see cref="TraceAsk"/>), so a ranking
    /// allocates no closure — a static prop under a live dlight ranks every frame.
    ///
    /// **The contract, because the delegate is shared:** one thread, and one outstanding at a time. <see cref="Traced"/> and
    /// <see cref="AddListed"/> are its only takers, and each releases it in a `finally`, so a trace that throws leaves it free. A second
    /// taken before the first is done would retarget the first; a debug build asserts against it. This object is used from one thread,
    /// as its caches are.
    ///
    /// **The PVS first, and a rule of its own** (B669): `FUN_1801b60a0` returns before it traces when the light's cluster
    /// (`+0x24`) is negative or out of the PVS `FUN_1801b8270` built from the entry's own leaf, so such a light takes no slot
    /// and is not folded. A level with no trace wired still takes this gate. `cluster` is that leaf's, for a caller whose lit
    /// point is a cell rather than the point itself. `FUN_1801b5400` has no gate of its own, since its list passed the same row when it
    /// was built (<see cref="StyledLights"/>), so asking again answers the same.
    /// </remarks>
    private Func<int, bool>? TraceSeen(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, SunCaller caller, int? cluster = null)
    {
        if (_lightTrace is null && _leaves is not { HasLeaves: true } && _strikesSky is null)
        {
            return null;
        }

        Debug.Assert(!_ask.Outstanding, "a TraceSeen delegate was taken before the last one was done");

        _ask.Outstanding = true;
        _ask.Lights = lights;
        _ask.Point = (x, y, z);
        _ask.Caller = caller;

        // Read even with no vis data: `CM_Vis` tests for cluster −1 before it looks for any (`FUN_18016b230`).
        _ask.Cluster = cluster ?? _leaves?.ClusterAt(x, y, z) ?? -1;

        return _ask.Keeps;
    }

    /// <summary>A state from a whole list, gated and traced.</summary>
    private LightingState Traced(
        IReadOnlyList<BspWorldLight> lights, float x, float y, float z, AmbientCube cube, Func<int, float>? style, SunCaller caller,
        int? cluster = null)
    {
        Func<int, bool>? keeps = TraceSeen(lights, x, y, z, caller, cluster);

        try
        {
            return LocalLights.Build(lights, x, y, z, cube, style, keeps, WorldLightSlots);
        }
        finally
        {
            TraceDone();
        }
    }

    /// <summary>`FUN_1801b5400`: each listed lamp seen from the handle's origin, added to a state at its current style value.</summary>
    private void AddListed(LightingState state, BspWorldLight[] listed, float x, float y, float z)
    {
        Func<int, bool>? keeps = TraceSeen(listed, x, y, z, SunCaller.StaticProp);

        try
        {
            for (int index = 0; index < listed.Length; index++)
            {
                if (keeps is null || keeps(index))
                {
                    LocalLights.Add(state, listed[index], x, y, z, StyleScale, slots: WorldLightSlots);
                }
            }
        }
        finally
        {
            TraceDone();
        }
    }

    /// <summary>
    /// `FUN_1801b60a0`'s gate. A light in cluster −1 is out whatever the map holds (`JS 0x1801b6523` at `0x1801b616e`). A point
    /// in cluster −1 sees nothing: `FUN_18016b230` zeroes the row for exactly −1, before it looks for vis data. With no vis
    /// data at all, the row is all ones and every other pair is in. **The one copy of that rule:** `FUN_1801b7a10` (a model's dlight)
    /// tests the same row and a cluster of 0 or more. `FUN_1801b6bf0` (the handle's list) tests the row with no negative test, so a lamp
    /// in −1 reads the byte before the row, which is not a rule to port; every real lump's light but two of koth_toxic's has a cluster.
    /// `FUN_1801b5260` (a static prop under a dlight) asks from the dlight's row whether the prop's leaves are in it, and the prop's
    /// origin stands for its leaves here. ponytail: a prop spanning leaves, or with its origin in solid, differs; ask per leaf if that shows.
    /// **Inferred, not read:** <see cref="StyledLights"/> and `WithDynamic` taking this −1 rule. `FUN_1801b6bf0` has no negative test, and
    /// `FUN_1801b5260` was read for its row and not for a dlight or prop in −1; the rule is carried over from `FUN_1801b60a0` and `CM_Vis`.
    /// </summary>
    private bool InPvs(int from, int cluster) =>
        _leaves is not { HasLeaves: true } ||
        (cluster >= 0 && from != -1 && (_visibility is not { HasData: true } || _visibility.Visible(from, cluster)));

    /// <summary>What a static state ranks: all of them over a computed cube, which holds none; the leaf path's list where the cube is the leaf's.</summary>
    private IReadOnlyList<BspWorldLight> StaticStateLights => _staticPropAmbient is null ? _leafCubeLights : _worldLights;

    /// <summary>The <see cref="TraceSeen"/> delegate's call is over: drop the list it held.</summary>
    private void TraceDone()
    {
        _ask.Outstanding = false;
        _ask.Lights = [];
    }

    /// <summary>The arguments of the ranking <see cref="TraceSeen"/> was last asked for, and the one delegate that reads them.</summary>
    private sealed class TraceAsk
    {
        // A sky light has its own rule in `0x1801b79e0`: the leaf's sky flags in place of the PVS, and `0x1801baeb0`'s sky trace (B170).
        public TraceAsk(LevelLighting owner) => Keeps = index =>
            Lights[index].Kind == WorldLightKind.SkyLight
                ? owner._strikesSky is { } strikesSky && owner.SunFrom(Caller, Lights[index], strikesSky, Point.X, Point.Y, Point.Z) is not null
                : owner.InPvs(Cluster, Lights[index].Cluster) &&
                  (owner._lightTrace is null || LocalLights.Kept(owner.Blocked(Lights[index].Origin, Point)));

        public SunCaller Caller { get; set; }

        public Func<int, bool> Keeps { get; }

        public IReadOnlyList<BspWorldLight> Lights { get; set; } = [];

        public bool Outstanding { get; set; }

        public (float X, float Y, float Z) Point { get; set; }

        public int Cluster { get; set; }
    }

    private readonly TraceAsk _ask;

    /// <summary>
    /// <see cref="LocalLights.Blocked"/> from a point to a light, remembered: the world does not move, so a point lit again
    /// — an entry relit on a style change, a ranking redone — traces nothing twice. One map's, as this object is.
    /// </summary>
    /// <remarks>The ONE place a light's visibility is read, by the ranking and by <see cref="Occlusion"/> alike.</remarks>
    private float Blocked((float X, float Y, float Z) light, (float X, float Y, float Z) point)
    {
        if (_blocked.TryGetValue((light, point), out float blocked))
        {
            return blocked;
        }

        // ponytail: dropped whole at the cap rather than evicted, which only costs traces again; LRU if that shows up.
        if (_blocked.Count >= BlockedLimit)
        {
            _blocked.Clear();
        }

        blocked = LocalLights.Blocked(light, point.X, point.Y, point.Z, _lightTrace!);
        _blocked[(light, point)] = blocked;

        return blocked;
    }

    /// <summary>
    /// What `FUN_1801baeb0` asks given flags 0, for a light a moving model's average folds: the sky along the sun's way, or a lamp by the
    /// ranking's slack. With no trace wired, every light is seen.
    /// </summary>
    /// <param name="light">The light.</param>
    /// <param name="x">The model's illumination point.</param>
    /// <param name="y">The model's illumination point.</param>
    /// <param name="z">The model's illumination point.</param>
    /// <returns>Whether it reaches the point.</returns>
    public bool FoldSees(BspWorldLight light, float x, float y, float z)
    {
        if (light.Kind == WorldLightKind.SkyLight)
        {
            return _strikesSky is not { } strikesSky || strikesSky(
                (x, y, z),
                (x - (light.Normal.X * SkyTraceLength), y - (light.Normal.Y * SkyTraceLength), z - (light.Normal.Z * SkyTraceLength)));
        }

        return _lightTrace is null || LocalLights.Kept(Blocked(light.Origin, (x, y, z)));
    }

    /// <summary>The most answers <see cref="Blocked"/> keeps: about 10 MB of entries.</summary>
    private const int BlockedLimit = 1 << 18;

    /// <summary>Each traced (light, point)'s blocked length.</summary>
    /// <remarks>
    /// **Keyed on the light's ORIGIN and the point, not on a light's index**: the lists ranked are several (the world's,
    /// a handle's, the world's with dlights appended), so an index names different lights in each. Two lights at one origin
    /// share an entry, which is correct, since the trace reads nothing of a light but its origin.
    /// </remarks>
    private readonly Dictionary<((float X, float Y, float Z) Light, (float X, float Y, float Z) Point), float> _blocked = [];

    /// <summary>How far short of a light its visibility trace from a point stops, as the ranking read it.</summary>
    /// <param name="light">The light.</param>
    /// <param name="x">The point being lit.</param>
    /// <param name="y">The point being lit.</param>
    /// <param name="z">The point being lit.</param>
    /// <returns>
    /// The blocked length, whether the PVS gate lets it through, and whether the ranking keeps it, which takes both; NULL when this map has
    /// no trace wired, so no caller can read an untraced light as seen.
    /// </returns>
    public (float Blocked, bool InPvs, bool Kept)? Occlusion(BspWorldLight light, float x, float y, float z)
    {
        if (_lightTrace is null)
        {
            return null;
        }

        float blocked = Blocked(light.Origin, (x, y, z));
        bool inPvs = InPvs(_leaves?.ClusterAt(x, y, z) ?? -1, light.Cluster);

        return (blocked, inPvs, inPvs && LocalLights.Kept(blocked));
    }

    /// <summary><see cref="StaticPropAmbient"/> over one level, or null when it lacks the lumps the rays read.</summary>
    /// <remarks>
    /// ponytail: its own <see cref="DecalWorld"/>, built on the first static prop that asks; the viewer builds a second
    /// for decals and impacts. Share one if the build time shows up.
    /// </remarks>
    /// <param name="level">The level.</param>
    /// <param name="find">The level's file lookup for each material's reflectivity; null keeps the texdata's (B683).</param>
    /// <returns>The cube at a point, or null.</returns>
    public static Func<float, float, float, AmbientCube>? StaticPropAmbientOf(MapLevel level, Func<string, byte[]?>? find)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (level.Leaves is not { } tree || level.LeafFaces is not { } leafFaces || level.LightSamples is not { } samples)
        {
            return null;
        }

        return AmbientOver(level, new Lazy<SurfaceColour>(() => Rays(level, tree, leafFaces, samples)), find);
    }

    /// <summary><see cref="StaticPropAmbient"/> casting against a walker the caller holds, so a probe's table and cube read one instance.</summary>
    /// <param name="level">The level.</param>
    /// <param name="rays">The walker, from <see cref="StaticPropRays"/>.</param>
    /// <param name="find">The level's file lookup for each material's reflectivity; null keeps the texdata's (B683).</param>
    /// <returns>The cube at a point.</returns>
    public static Func<float, float, float, AmbientCube> StaticPropAmbientOf(
        MapLevel level, SurfaceColour rays, Func<string, byte[]?>? find)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(rays);

        return AmbientOver(level, new Lazy<SurfaceColour>(rays), find);
    }

    /// <summary>Each texdata's reflectivity as the cube multiplies by it: the material's, else the texdata's copy (B683).</summary>
    /// <param name="materials">The level's texdata.</param>
    /// <param name="find">The level's file lookup, or null to keep every texdata's copy.</param>
    /// <returns>A texdata index to its reflectivity, zero outside the table; each read once, when first struck.</returns>
    /// <remarks>
    /// *Interpolated:* the texdata fallback for a material that will not resolve. The engine would ask the error material
    /// or one with no base texture, whose reflectivity this does not read. ponytail: unsynchronised, as the static prop
    /// states it feeds are (`_staticStates`); a lock if the cube is ever computed off the load thread.
    /// </remarks>
    public static Func<int, Vector3> ReflectivityOf(IReadOnlyList<BspMaterial> materials, Func<string, byte[]?>? find)
    {
        ArgumentNullException.ThrowIfNull(materials);

        Vector3?[] known = new Vector3?[materials.Count];

        return texdata =>
        {
            if (texdata < 0 || texdata >= known.Length)
            {
                return Vector3.Zero;
            }

            if (known[texdata] is { } cached)
            {
                return cached;
            }

            BspMaterial material = materials[texdata];
            Vector3 value = (find is null ? null : MaterialReflectivity.Of(material.Name, find))
                ?? new Vector3(material.Reflectivity.Red, material.Reflectivity.Green, material.Reflectivity.Blue);
            known[texdata] = value;
            return value;
        };
    }

    private static Func<float, float, float, AmbientCube> AmbientOver(MapLevel level, Lazy<SurfaceColour> rays, Func<string, byte[]?>? find)
    {
        Func<int, Vector3> reflectivity = ReflectivityOf(level.Materials, find);

        // `FUN_1801b74f0`'s first loop: the first world light of type 5.
        Vector3? skyAmbient = null;

        foreach (BspWorldLight light in level.WorldLights)
        {
            if (light.Kind == WorldLightKind.SkyAmbient)
            {
                skyAmbient = new Vector3(light.Intensity.Red, light.Intensity.Green, light.Intensity.Blue);
                break;
            }
        }

        return (x, y, z) => StaticPropAmbient.Compute(
            new Vector3(x, y, z),
            (start, end) => rays.Value.LightVec(start, end, styles: false),
            reflectivity,
            skyAmbient);
    }

    /// <summary>What <see cref="StaticPropAmbientOf(MapLevel, Func{string, byte[]})"/> casts its rays against, so a probe of them reads the production walker.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The walker, or null when the level lacks the lumps.</returns>
    public static SurfaceColour? StaticPropRays(MapLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        return level.Leaves is { } tree && level.LeafFaces is { } leafFaces && level.LightSamples is { } samples
            ? Rays(level, tree, leafFaces, samples)
            : null;
    }

    private static SurfaceColour Rays(MapLevel level, BspLeafTree tree, BspLeafFaces leafFaces, BspLightSamples samples) =>
        new(DecalWorld.From(tree, leafFaces, level.Surfaces, level.Terrain), samples, static _ => null);

    /// <summary>The PVS, for a static prop's styled-light list; null or empty admits every cluster.</summary>
    private readonly BspVisibility? _visibility;

    /// <summary>Each static prop's handle — its styled-light list and bit 4's ranked cache — by entity, built once per map.</summary>
    private readonly Dictionary<int, StyledHandle> _styledByEntity = [];

    /// <summary>The part of a static prop's light cache handle bit 4 reads (`FUN_1801ba590`, finding 67, B607).</summary>
    /// <param name="listed">`+0x1b0`: <see cref="StyledLights"/> at the lighting origin, built once.</param>
    /// <param name="bounds">The prop's world box; see <see cref="Bounds"/>.</param>
    private sealed class StyledHandle(BspWorldLight[] listed, LightBounds? bounds)
    {
        /// <summary>`+0x1d4` and `+0x1e0`: the box `FUN_1801b6bf0` and `FUN_1801b6550` test a light against. Null for a prop asked about before its model is known.</summary>
        public LightBounds? Bounds { get; } = bounds;

        /// <summary>`+0x1b0`, as the lights themselves.</summary>
        public BspWorldLight[] Listed { get; } = listed;

        /// <summary>The styles of <see cref="Listed"/>, each once: whose stamps make <see cref="Ranked"/> stale.</summary>
        public int[] Styles { get; } = [.. listed.Select(static light => light.Style).Distinct()];

        /// <summary>
        /// `+0x88` under flags 6: the listed lights added to an empty state at their style values when <see cref="BuiltAt"/> was stamped.
        /// </summary>
        public LightingState Ranked { get; set; } = new(LevelLighting.NoStaticState);

        /// <summary><see cref="Ranked"/> as a draw takes it; every draw of the prop is handed this one.</summary>
        public PointLighting RankedLighting { get; set; } = PointLighting.Bounce(LevelLighting.NoStaticState);

        /// <summary>`+0xf8`: the <see cref="_styleVersion"/> <see cref="Ranked"/> was built at; never, until the first draw.</summary>
        public int BuiltAt { get; set; } = -1;

        /// <summary>
        /// `+0x88` under flags 7: the listed lights added to a copy of the static state, and the later of <see cref="BuiltAt"/> and style 0's stamp
        /// it was built at; null until a flags-7 draw asks.
        /// </summary>
        public (LightingState State, PointLighting Lighting, int At)? Full { get; set; }
    }

    /// <summary>Times bit 4 ranked a handle's styled lights, ever: the conformance tests' count of the `+0x88` cache's misses.</summary>
    internal int StyledRankings { get; private set; }

    /// <summary>Times a flags-7 draw ranked its static four with its styled lights, ever (the `Full` cache's misses).</summary>
    internal int FullRankings { get; private set; }

    /// <summary>Times a flags-7 state was asked for, ever: a draw with no colour mesh or a decal-holding prop's decal light (B642, B674).</summary>
    internal int FullStateRequests { get; private set; }

    /// <summary>Static-prop handles' styled lists built, ever (`FUN_1801b6bf0`): once a prop, at load (B607).</summary>
    public int StyledListsBuilt { get; private set; }

    /// <summary>Static states built, ever (`ComputeStaticLightingForCacheEntry`): once a lighting origin, at load (B607).</summary>
    public int StaticStateBuilds { get; private set; }

    /// <summary>Stopwatch ticks spent building <see cref="StaticStateBuilds"/>, for `--measure`'s first-use line.</summary>
    public long StaticStateBuildTicks { get; private set; }

    /// <summary>
    /// Builds one static prop's light cache handle, as the level loads: `CStaticPropMgr::PrecacheLighting`
    /// (`engine.dll` `0x180205b20`) → `FUN_1801b8350`, B427, B607.
    /// </summary>
    /// <param name="entity">The prop's entity index, which keys its handle.</param>
    /// <param name="x">The prop's lighting origin (the handle's `+0x184`).</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <param name="bounds">The prop's world box, which `FUN_1801b6bf0` tests each light against; null tests the point's cell.</param>
    /// <remarks>
    /// The styled list (`+0x1b0`, `FUN_1801b6bf0`) and the static state (`+0x18`, <see cref="StaticPropLightingAt"/>). Bit
    /// 4's ranked cache (`+0x88`) is left to the first draw, whose stamp test finds it never built — *interpolated*: whether
    /// `FUN_1801b8350` fills `+0x88` itself is unread. Either way the first draw's light is the same, and a prop that lists
    /// nothing ranks nothing.
    ///
    /// **The static state for every handle**: a bump-mapped model's flags 7 reads it, the CPU bake of a `$staticprop`
    /// without a `.vhv` reads it, and a colour-mesh prop's decal light reads it (B642; `engine.dll` `0x1800eac60` copies it to
    /// the record's `+0x88` with the colour mesh). *Interpolated:* the engine builds it for every handle.
    /// </remarks>
    public void PrecacheStaticProp(int entity, float x, float y, float z, LightBounds? bounds = null)
    {
        _ = Handle(entity, x, y, z, bounds);
        _ = StaticPropLightingAt(x, y, z);
    }

    /// <summary>A static prop's handle, built on the first ask — at load, by <see cref="PrecacheStaticProp"/>.</summary>
    private StyledHandle Handle(int entity, float x, float y, float z, LightBounds? bounds = null)
    {
        if (!_styledByEntity.TryGetValue(entity, out StyledHandle? handle))
        {
            handle = new StyledHandle([.. StyledLights(x, y, z, bounds).Select(index => _worldLights[index])], bounds);
            _styledByEntity[entity] = handle;
            StyledListsBuilt++;
        }

        return handle;
    }

    /// <summary>Bit 4 (`FUN_1801b5400`): the handle's ranked styled lights, ranked again only when a listed style changed.</summary>
    /// <remarks>
    /// **Cached at `+0x88`, rebuilt when a listed style's stamp passes `+0xf8`** (finding 67): a style no listed light
    /// answers to leaves it. Both stamps are <see cref="_styleVersion"/>s, set by
    /// <see cref="StylesChanged(IReadOnlyCollection{int})"/>.
    /// </remarks>
    private StyledHandle Ranked(StyledHandle handle, float x, float y, float z)
    {
        if (handle.Listed.Length == 0 || !Stale(handle))
        {
            return handle;
        }

        LightingState state = new(NoStaticState);

        // Traced at the handle's origin (`FUN_1801b5400`, flags 0; B666).
        AddListed(state, handle.Listed, x, y, z);

        handle.Ranked = state;
        handle.RankedLighting = state.Lighting(StyleScale);
        handle.BuiltAt = _styleVersion;
        StyledRankings++;

        return handle;
    }

    /// <summary>Whether a handle's `+0x88` was never built, or predates a stamp of a style it lists.</summary>
    private bool Stale(StyledHandle handle)
    {
        if (handle.BuiltAt < 0)
        {
            return true;
        }

        foreach (int style in handle.Styles)
        {
            if (_styleStamps.TryGetValue(style, out int stamp) && stamp > handle.BuiltAt)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The world lights a static prop's lighting handle lists: `engine.dll` `FUN_1801b6bf0`.</summary>
    /// <param name="x">The prop's lighting origin.</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <param name="bounds">The prop's bounds, `FUN_1801b6bf0`'s `param_2` and `param_3`; null for the point's cell.</param>
    /// <returns>Indices into the world lights, in lump order.</returns>
    /// <remarks>
    /// Every light whose style (`+0x2c`) is nonzero — style 0 is what vrad baked — whose cluster (`+0x24`) is in the
    /// PVS of the leaf holding the origin (<see cref="InPvs"/>), and that `FUN_1801b99a0` passes against those bounds with flags 9 (<see cref="LocalLights.Reaches"/>).
    /// </remarks>
    public int[] StyledLights(float x, float y, float z, LightBounds? bounds = null)
    {
        List<int> listed = [];
        int from = _leaves?.ClusterAt(x, y, z) ?? -1;

        for (int index = 0; index < _worldLights.Count; index++)
        {
            BspWorldLight light = _worldLights[index];

            if (light.Style != 0 &&
                InPvs(from, light.Cluster) &&
                LocalLights.Reaches(light, x, y, z, bounds))
            {
                listed.Add(index);
            }
        }

        return [.. listed];
    }

    /// <summary>The cube a colour-mesh static prop's lighting state starts from: zero, since flags 6 omit bit 1.</summary>
    public static readonly AmbientCube NoStaticState;

    /// <summary>
    /// The local lights a static prop with a colour mesh draws with on top of its colours: `engine.dll` `0x1800f1bd0`'s
    /// DX9 branch, `FUN_1801ba590(handle, …, 6 + FUN_1801bb8a0(handle))` (B424, B425).
    /// </summary>
    /// <param name="entity">The prop's entity index, which keys its handle's list.</param>
    /// <param name="x">The prop's lighting origin (the handle's `+0x184`).</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <returns>The cube the losers made and at most four lights; neither when none reaches, which leaves the colours alone.</returns>
    /// <remarks>
    /// **The DX9 path only.** Hardware config `+0x150` (`SupportsStaticPlusDynamicLighting`) is true on every DX9 part, so
    /// the colour mesh is kept and this state is ADDED by the shader. The other branch — drop the colours for full lighting
    /// under a dlight or an animated style (`FUN_1801bb830`) — is what non-DX9 hardware does, and this port does not
    /// target it.
    ///
    /// Without bit 1 the state holds no leaf cube and no style-0 light (<see cref="NoStaticState"/>). Bit 4
    /// (`FUN_1801b5400`) adds every light of the handle's list (<see cref="StyledLights"/>, built once, as
    /// `FUN_1801b8350` builds it) at its current style value; bit 2 adds each LIVE dlight (`DAT_1806996c4`) whose bit
    /// `FUN_1801b5260` set — the prop in the dlight's PVS — through `FUN_1801bb940` and `FUN_1801b6550`. Both fill the
    /// four slots and then evict, and what is evicted or refused goes to the cube (<see cref="LocalLights.Add"/>), which is why this answers
    /// a cube as well as lights.
    /// </remarks>
    public PointLighting StaticPlusDynamicLights(int entity, float x, float y, float z)
    {
        StyledHandle handle = Ranked(Handle(entity, x, y, z), x, y, z);

        return WithDynamic(handle.Ranked, x, y, z, handle.Bounds)?.Lighting(StyleScale) ?? handle.RankedLighting;
    }

    /// <summary>
    /// The whole lighting state of a static prop that draws WITHOUT a colour mesh — a model flagged
    /// `STUDIOHDR_FLAGS_USES_BUMPMAPPING` (B514), or with no mesh for another reason (B674): `FUN_1801ba590(handle, …, 7)`, `engine.dll` `0x1800f1bd0` at
    /// `LAB_1800f1f3b`.
    /// </summary>
    /// <param name="entity">The prop's entity index, which keys its handle's list.</param>
    /// <param name="x">The prop's lighting origin (the handle's `+0x184`).</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <returns>The leaf cube at the origin, at most four lights — style 0, styled and dynamic — and the sun.</returns>
    /// <remarks>
    /// *Read in disassembly.* The model draw's caller (`FUN_1800eb1a0`) refuses static lighting when the DX support
    /// level is at least 80 and the model carries `MODELFLAG_STUDIOHDR_USES_BUMPMAPPING` (`0x200`); `*param_7` is then
    /// false, no colour mesh is handed to the studio render, and with the prop's lighting handle the state is flags 7:
    /// bit 1 the static state (<see cref="StaticPropLightingAt"/>), bit 4 the handle's styled lights at their current
    /// values, bit 2 its live dlights (<see cref="StaticPlusDynamicLights"/>). `FUN_1801ba590` copies the static state and adds to the
    /// copy, so the static four are not ranked again: a styled lamp or a dlight evicts or loses against them, and what falls out is folded.
    /// </remarks>
    public (PointLighting Lighting, SunLight? Sun) StaticPropFullLightingAt(int entity, float x, float y, float z)
    {
        FullStateRequests++;

        (PointLighting lighting, _, LightingState state) = StaticState(x, y, z);
        StyledHandle handle = Ranked(Handle(entity, x, y, z), x, y, z);

        if (handle.Listed.Length != 0)
        {
            // **The finished state is the handle's `+0x88`, and the draw only adds to it**: built again only when bit 4 was, or
            // style 0 was stamped, so a frame with nothing stamped allocates and ranks nothing.
            int at = Math.Max(handle.BuiltAt, _styleStamps.GetValueOrDefault(0, -1));

            if (handle.Full is not { } full || full.At != at)
            {
                LightingState added = state.Clone();

                AddListed(added, handle.Listed, x, y, z);
                full = (added, added.Lighting(StyleScale), at);
                handle.Full = full;
                FullRankings++;
            }

            state = full.State;
            lighting = full.Lighting;
        }

        // The sun as the final state holds it: a styled lamp or a dlight can evict it, and an evicted sun is gone (B170).
        return WithDynamic(state, x, y, z, handle.Bounds) is { } dynamic
            ? (dynamic.Lighting(StyleScale), SunOf(dynamic))
            : (lighting, SunOf(state));
    }

    /// <summary>Dlight slots a static prop's bit-2 gather has examined, ever: the conformance tests' count of the live-set walk.</summary>
    internal int DlightsExamined { get; private set; }

    /// <summary>
    /// A copy of a handle's state with the live dlights that reach it added (bit 2); null when no dlight reaches, which leaves the
    /// state the whole answer and allocates nothing.
    /// </summary>
    /// <remarks>
    /// **Bit 2 walks the live set, `r_dlightactive`, and nothing else** (`FUN_1801ba590` masks the handle's `+0x1a4` by
    /// `DAT_1806996c4`). This walked all 32 slots and looked up a cluster for each, and for the prop, on every static prop
    /// every frame: ~4,000 slot reads a frame on f12 with no dlight live (measurement 2026-10-27-static-prop-lighting).
    /// The live slots are visited in slot order, as the walk over every slot visited them.
    ///
    /// **Added to the state bit 4 left, with `FUN_1801b6550`'s precedence** (<see cref="LocalLights.Add"/>): not traced (flags 1), and once the
    /// slots are full a dlight evicts the weakest whatever its own strength, unless all four rank over 100000.
    /// </remarks>
    private LightingState? WithDynamic(LightingState state, float x, float y, float z, LightBounds? bounds)
    {
        uint live = Dynamic?.ActiveMask ?? 0u;

        if (live == 0u || Dynamic is not { } dynamic || _leaves is not { } tree)
        {
            return null;
        }

        LightingState? added = null;
        int propCluster = tree.ClusterAt(x, y, z);

        for (; live != 0u; live &= live - 1u)
        {
            int index = System.Numerics.BitOperations.TrailingZeroCount(live);
            DlightsExamined++;
            DynamicLight light = dynamic.Dlights[index];
            int lightCluster = tree.ClusterAt(light.X, light.Y, light.Z);

            // `FUN_1801b5260` asks from the DLIGHT's row, so the two clusters are this way round.
            if (dynamic.LightsModels(index) && InPvs(lightCluster, propCluster))
            {
                added ??= state.Clone();

                // Into the same table as a moving model's merge: `FUN_1801bbed0` hands `FUN_1801bd280` `DAT_180775730` too (`0x1801bc569`).
                BspWorldLight converted = DynamicLights.ToWorldLight(light, lightCluster);

                if (light.Slot >= 0)
                {
                    ConvertedDlights[light.Slot] = converted;
                }

                LocalLights.Add(added, converted, x, y, z, StyleScale, dynamicLight: true, bounds, WorldLightSlots);
            }
        }

        return added;
    }

    /// <summary>
    /// The static lighting state a static prop's handle holds, which its CPU colour mesh is lit with (B429):
    /// `ComputeStaticLightingForCacheEntry`, `engine.dll` `0x1801b8270`.
    /// </summary>
    /// <param name="x">The prop's lighting origin (the handle's `+0x184`).</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <returns>The leaf's cube and the strongest style-0 lights at the point, and the sun where it reaches.</returns>
    /// <remarks>
    /// `FUN_1800f36e0` takes the handle's lighting through `FUN_1801ba590(handle, 0, 1)` — flags 1, because
    /// `SupportsStaticPlusDynamicLighting` (hardware config `+0x150`) is true on every DX9 part — so it reads the
    /// static state at `+0x18`: the leaf cube (`FUN_1801bb4b0`) at the handle's own origin, not a cache cell, and
    /// `FUN_1801b5a50`'s lights, which adds a light only when its style (`+0x2c`) is 0. A styled light only sets its
    /// bit in the handle's mask; bit 4 (`FUN_1801b5400`), which would add it, is not asked for.
    /// </remarks>
    public (PointLighting Lighting, SunLight? Sun) StaticPropLightingAt(float x, float y, float z)
    {
        (PointLighting lighting, SunLight? sun, _) = StaticState(x, y, z);

        return (lighting, sun);
    }

    /// <summary><see cref="StaticPropLightingAt"/> with the state itself, for bit 4 and bit 2 to copy and add to.</summary>
    private (PointLighting Lighting, SunLight? Sun, LightingState State) StaticState(float x, float y, float z)
    {
        // **An empty ambient lump costs only the leaf's cube** (B674 review): `FUN_1801b8270` runs `FUN_1801b5a50` over the world lights
        // whatever `FUN_1801bb4b0` found, and a static caller's cube is cast by rays (`FUN_1801b74f0`), not read from the lump.
        if (_leaves is not { } tree)
        {
            return (PointLighting.None, null, new LightingState(default));
        }

        if (_staticStates.TryGetValue((x, y, z), out (PointLighting Lighting, SunLight? Sun, LightingState State) built))
        {
            return built;
        }

        long buildAt = System.Diagnostics.Stopwatch.GetTimestamp();

        // **Computed, not the leaf's** (B514): `FUN_1801bb4b0` gives a static caller `ComputeAmbientFromSphericalSamples`
        // under `r_radiosity` 4. The leaf's samples stand in only where the level cannot cast the rays.
        int leaf = tree.LeafAt(x, y, z);
        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count ? _ambient[leaf].At(x, y, z) : default;

        if (_staticPropAmbient is { } computed)
        {
            bounced = computed(x, y, z);
        }

        // **Every style-0 light, surface lights included, and the losers folded** (B669): `FUN_1801b5a50` hands each to
        // `FUN_1801b60a0`, which ranks it into the four slots or adds it to this cube. It passes over a light vrad put in the
        // leaf cubes only when the cube came from the leaf; a computed cube holds none of them (`FUN_1801b74f0`).
        // The sun among them, ranked for a slot like a lamp (B170).
        LightingState state = Traced(StaticStateLights, x, y, z, bounced, StyleZeroOnly, SunCaller.StaticProp);

        // Read-only: this one state is every later draw's and bake's (B607).
        built = (state.Lighting(StyleZeroOnly), SunOf(state), state);
        _staticStates[(x, y, z)] = built;
        StaticStateBuilds++;
        StaticStateBuildTicks += System.Diagnostics.Stopwatch.GetTimestamp() - buildAt;

        return built;
    }

    /// <summary>A static state's style scale: `FUN_1801b5a50` adds a light only when its style is 0.</summary>
    private static readonly Func<int, float> StyleZeroOnly = static style => style == 0 ? 1f : 0f;

    /// <summary>`IVEngineClient::ComputeLighting` with no normal: all the world light at a point, as a cube.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cube, or a default one where the map cannot say.</returns>
    /// <remarks>
    /// *Read in disassembly*, `engine.dll` `FUN_1801b7fd0` (B668): `FUN_1801b9cd0` (LightcacheGet) with flags `0xf`, a moving model's own path,
    /// so the leaf's cube, the flag's skip, the PVS gate and the trace, and it is built at the entry's cell. With no normal, each light in a
    /// slot but a dlight is then folded into the cube at the ASKED point (`FUN_1801b5db0`, `FUN_1801b9570` flags 1, so not traced
    /// again). ponytail: the entry's state without its dlights. The engine ranks them first, but it folds only the world lights, and an
    /// evicted one arrives through the cube either way, at the entry's point rather than this one.
    ///
    /// **A zero cube is an answer here, not "no cube supplied".** With no map data there is nothing to ask. Past `lightcache_maxmiss` the
    /// entry is the nearest (`0x1801b9cd0` under flag 8), and a zeroed pool entry nearest a point in the map's far corner gives zero,
    /// as it does in the engine, which copies that entry's empty state out. The readers are `RopeLight`, which averages the faces, and a
    /// log line; no shader sees this cube. `FUN_1801b7fd0` has no time average; the model draw does, `FUN_1800f3110`, unported (B676).
    /// </remarks>
    public AmbientCube ComputeLighting(float x, float y, float z)
    {
        if (Entry(x, y, z) is not { } entry)
        {
            return default;
        }

        AmbientCube lit = LocalLights.FoldSlots(entry.State, x, y, z, StyleScale);

        // **The two terms reported apart, because one number cannot say which is missing.** Every
        // model on z1800 sampled between 0.09 and 0.12 in a room with three ceiling lamps overhead,
        // and the single figure is consistent with two unrelated faults: no light near enough to be
        // chosen, or lights chosen that contribute nothing once attenuated. A log that names only
        // the total makes those indistinguishable — see
        // docs/memory/logs-are-the-debugger.md#a-log-must-name-what-it-measured.
        // The entry's own cube, carried from it: the answer was built from that one, which past the budget is not the asked point's
        // leaf (B243). It holds the bounce and the lights that took no slot, so the second number is what the slots add.
        ReportLightTerms(entry.State.Cube, lit, x, y, z);

        return lit;
    }

    /// <summary>The bounce cube and the direct lights at a point, as the engine keeps them.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cube without direct light folded in, and the nearest lights beside it.</returns>
    /// <remarks>
    /// **This is what a model is drawn with, and <see cref="ComputeLighting"/> is not.** The cube
    /// here is vrad's own: `ComputeAmbientFromSphericalSamples` builds it from rays cast at
    /// surfaces — bounce — plus the `emit_surface` lights dim enough to flag (`IsLeafAmbientSurfaceLight`). Everything else
    /// arrives at runtime, which is what the second half of this is.
    ///
    /// **The difference is not brightness, it is direction.** A lamp folded into a cube arrives
    /// from all six faces at once, so a model takes no N·L falloff from it and can cast no
    /// highlight from it. Handed over as lights, each one shades the model and adds its highlight
    /// and rim, which is how a weapon indoors gets a specular term at all (B170).
    ///
    /// **Only the lights that take no slot are folded in** (B453, `LocalLights.Split`), which is the engine's own
    /// split. `PixelShaderDoLightingLinear` accumulates the cube and then each light, so a light appearing in both
    /// would be counted twice — and that mistake reads as a lighting change rather than as a bug.
    /// </remarks>
    public PointLighting LightingAt(float x, float y, float z) => StateAt(x, y, z, SunCaller.Direct).Lighting(StyleScale);

    /// <summary><see cref="LightingAt"/> as a state, which a cache entry keeps for a dlight to be ranked against.</summary>
    /// <remarks>The sun is ranked among the lights, its trace counted against <paramref name="caller"/> (<see cref="SunWork"/>).</remarks>
    private LightingState StateAt(float x, float y, float z, SunCaller caller)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return new LightingState(default);
        }

        // The entry is keyed on the point's own leaf and lit at its cell's point (`0x1801b8270` takes both).
        int leaf = tree.LeafAt(x, y, z);
        int cluster = tree.ClusterAt(x, y, z);

        (x, y, z) = CachePoint(x, y, z);

        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        // **The lights that take no slot are folded into the cube** (B453), as the lightcache does.
        LightingState state = Traced(_leafCubeLights, x, y, z, bounced, StyleScale, caller, cluster);

        // **What matters on this path is how many lights were chosen**, because "no lamp near enough" and "lamps chosen that
        // contribute nothing" look identical in a single brightness figure.
        // The slots can hold the sun alone, which draws no local light, so the list is asked rather than the count.
        if (state.Count > 0 && _render.IsEnabled(LogLevel.Debug) && state.Lighting(StyleScale).Locals is [var strongest, ..])
        {

            _render.LogDebug(
                "local lights at ({X}, {Y}, {Z}): {Count} chosen, strongest at ({LX}, {LY}, {LZ}) " +
                "intensity {Intensity}",
                x, y, z, state.Count,
                strongest.X, strongest.Y, strongest.Z,
                Math.Max(strongest.Red, Math.Max(strongest.Green, strongest.Blue)));
        }

        return state;
    }

    /// <summary>The sun reaching a world position, or null when it does not.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The sun where the sky is visible, otherwise null.</returns>
    /// <remarks>
    /// **The trace is the feature, not an optimisation.** Valve describes a sky light as a
    /// "directional light with no falloff (surface must trace to SKY texture)" — applied without
    /// that condition it lights the inside of every building, which is worse than the shade this
    /// is meant to fix.
    ///
    /// **The engine's trace** (`engine.dll` 0x1801b8e20): from the point, <see cref="SkyTraceLength"/> against the light's
    /// normal, `TRACE_WORLD_ONLY` and `MASK_OPAQUE`, and the light counts only when it strikes `SURF_SKY`. This used to
    /// step through leaves until one was solid and call open air sky — which walked through every hillside.
    /// </remarks>
    public SunLight? SunAt(float x, float y, float z) => CellSun(SunCaller.Direct, x, y, z);

    /// <summary><see cref="SunAt"/>, counted against the caller that asked.</summary>
    private SunLight? CellSun(SunCaller caller, float x, float y, float z)
    {
        if (_sun is not { } sun || _strikesSky is not { } strikesSky)
        {
            return null;
        }

        (x, y, z) = CachePoint(x, y, z);

        return SunFrom(caller, sun, strikesSky, x, y, z);
    }

    /// <summary>The sun reaching exactly this point, counted and — measuring — timed where it runs.</summary>
    private SunLight? SunFrom(
        SunCaller caller,
        BspWorldLight sun,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> strikesSky,
        float x,
        float y,
        float z)
    {
        SunCalls++;
        _sunBy[(int)caller]++;

        if (!TimeSun)
        {
            return SunFrom(sun, strikesSky, x, y, z);
        }

        long started = Stopwatch.GetTimestamp();
        SunLight? reached = SunFrom(sun, strikesSky, x, y, z);

        _sunTicks += Stopwatch.GetTimestamp() - started;
        return reached;
    }

    /// <summary>The sun reaching exactly this point: <see cref="SunAt"/> without the cache cell.</summary>
    /// <remarks>
    /// **The leaf is asked before the ray** (B514). *Read in disassembly*, `engine.dll` `0x1801b60a0`, the light cache's
    /// add-one-light: an `emit_skylight` is dropped unless the leaf holding the point has `flags &amp; 5` —
    /// `LEAF_FLAGS_SKY | LEAF_FLAGS_SKY2D` (`bspfile.h:791-793`), vrad's "sky in this leaf's PVS". Then the trace.
    /// </remarks>
    private SunLight? SunFrom(
        BspWorldLight sun, Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> strikesSky, float x, float y, float z)
    {
        if (_leaves is { } tree && !tree.AdmitsSkyLight(x, y, z))
        {
            return null;
        }

        (float X, float Y, float Z) toward = (
            x - (sun.Normal.X * SkyTraceLength),
            y - (sun.Normal.Y * SkyTraceLength),
            z - (sun.Normal.Z * SkyTraceLength));

        if (!strikesSky((x, y, z), toward))
        {
            return null;
        }

        return new SunLight(
            sun.Intensity.Red,
            sun.Intensity.Green,
            sun.Intensity.Blue,
            sun.Normal.X,
            sun.Normal.Y,
            sun.Normal.Z);
    }

    /// <summary>Says what the entry's cube gave, bounce and the lights that took no slot, and what its slots added, once per place.</summary>
    /// <remarks>
    /// **Debug, and the work is behind the same guard as the write** (B191). This runs for every
    /// model every time one moves, and the stall that froze playback for 120 ms every few seconds
    /// was one per-frame line at <c>Information</c> reaching a per-line disk flush. A release run
    /// (<c>developer 0</c>) does not admit <c>Debug</c>, so with the guard in place this costs a
    /// level check and nothing else — no string, and no set entry.
    ///
    /// Sampled rather than per call: the question it answers is about a PLACE rather than about a
    /// frame.
    /// </remarks>
    private void ReportLightTerms(AmbientCube cube, AmbientCube lit, float x, float y, float z)
    {
        if (_worldLights.Count == 0 ||
            _reportedLightTerms.Count >= LightTermReportLimit ||
            !_render.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (!_reportedLightTerms.Add(((int)x, (int)y, (int)z)))
        {
            return;
        }

        // Stryker disable all : the String mutator wraps the interpolated literal in a
        // ternary that cannot bind to string.Create's interpolated-string handler (CS1620),
        // and Safe Mode then drops every mutation in this method — B410.
        _render.LogDebug(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"light terms at ({x:0},{y:0},{z:0}): cube {AmbientCube.Luminance(cube):0.####}, " +
                $"with direct {AmbientCube.Luminance(lit):0.####}, " +
                $"{_worldLights.Count} world lights on the map"));

        // Stryker restore all
    }
}
