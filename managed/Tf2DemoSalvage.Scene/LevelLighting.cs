using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

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
    private readonly BspWorldLight? _sun;
    private readonly ILogger _render;

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
    /// <remarks>The lightcache multiplies a world light's falloff by it before ranking (`engine.dll` `0x1801b8e20`).</remarks>
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
    private readonly LightCache<CachedLight> _cache = new(CachedLight.Unbuilt);

    /// <summary>Bumped when a style a world light answers to changes; an entry built before it is relit on its next hit.</summary>
    private int _styleVersion;

    /// <summary>`r_framecount`: advance once per rendered frame, before the frame's models are lit.</summary>
    public int Frame { get; set; }

    /// <summary>A light style a world light answers to changed: every cache entry is relit where it was built when next used.</summary>
    public void StylesChanged() => _styleVersion++;

    /// <summary>A model's light, through the light cache: see <see cref="LightCache{T}"/>.</summary>
    /// <param name="x">The model's lighting origin.</param>
    /// <param name="y">The model's lighting origin.</param>
    /// <param name="z">The model's lighting origin.</param>
    /// <returns>The cube and local lights of the entry lighting this point.</returns>
    /// <remarks>
    /// **The entry is built at the first point to miss its cell and leaf**, and every model after it in that cell is lit
    /// with it — model lighting passes flags `0xf` (`0x1800f1bd0`), so past `lightcache_maxmiss` it takes the nearest entry.
    /// </remarks>
    public PointLighting ModelLightingAt(float x, float y, float z) => Entry(x, y, z)?.Lighting ?? LightingAt(x, y, z);

    /// <summary>The sun a model's light cache entry sees: see <see cref="ModelLightingAt"/>.</summary>
    /// <param name="x">The model's lighting origin.</param>
    /// <param name="y">The model's lighting origin.</param>
    /// <param name="z">The model's lighting origin.</param>
    /// <returns>The entry's sun, or null.</returns>
    public SunLight? ModelSunAt(float x, float y, float z) => Entry(x, y, z) is { } entry ? entry.Sun : SunAt(x, y, z);

    private CachedLight? Entry(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return null;
        }

        CachedLight entry = _cache.Get(
            new System.Numerics.Vector3(x, y, z),
            tree.LeafAt(x, y, z),
            Frame,
            allowFast: true,
            () => new CachedLight(x, y, z, LightingAt(x, y, z), SunAt(x, y, z), _styleVersion));

        if (entry.Built && entry.StyleVersion != _styleVersion)
        {
            entry.Lighting = LightingAt(entry.X, entry.Y, entry.Z);
            entry.StyleVersion = _styleVersion;
        }

        return entry;
    }

    /// <summary>What a cache entry holds: where it was built, and what it lit there.</summary>
    private sealed class CachedLight(float x, float y, float z, PointLighting lighting, SunLight? sun, int styleVersion)
    {
        /// <summary>A zeroed pool entry (`0x1801bb640`): no cube, no lights, no sun.</summary>
        public static readonly CachedLight Unbuilt = new(0f, 0f, 0f, PointLighting.None, null, -1) { Built = false };

        public float X { get; } = x;

        public float Y { get; } = y;

        public float Z { get; } = z;

        public bool Built { get; private init; } = true;

        public PointLighting Lighting { get; set; } = lighting;

        public SunLight? Sun { get; } = sun;

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
    /// <exception cref="ArgumentNullException">An argument other than the map data is null.</exception>
    public LevelLighting(
        BspLeafTree? leaves,
        IReadOnlyList<AmbientSamples> ambient,
        IReadOnlyList<BspWorldLight> worldLights,
        BspWorldLight? sun,
        ILogger render,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? strikesSky = null,
        Func<(float X, float Y, float Z), (float X, float Y, float Z), bool>? reaches = null,
        BspVisibility? visibility = null)
    {
        _reaches = reaches;
        _visibility = visibility;
        ArgumentNullException.ThrowIfNull(ambient);
        ArgumentNullException.ThrowIfNull(worldLights);
        ArgumentNullException.ThrowIfNull(render);

        _leaves = leaves;
        _ambient = ambient;
        _worldLights = worldLights;
        _sun = sun;
        _render = render;
        _strikesSky = strikesSky;
    }

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
    /// <returns>The source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is null.</exception>
    /// <remarks>
    /// Built from <see cref="MapLevel"/> rather than from the file, because the lighting lumps are
    /// read once with everything else — the engine's <c>LevelInitPreEntity</c> shape
    /// (<c>igamesystem.h:39</c>), where a system initialises itself from the level it is handed.
    /// </remarks>
    public static LevelLighting From(MapLevel level, ILogger render)
    {
        ArgumentNullException.ThrowIfNull(level);

        return new LevelLighting(
            level.Leaves,
            level.Ambient,
            level.WorldLights,
            level.Sun,
            render,
            level.StrikesSky,
            (from, to) => level.TraceBrushOnly(from, to, 0f, MaskOpaque).Fraction >= 1f,
            level.Visibility);
    }

    /// <summary>The PVS, for a static prop's styled-light list; null or empty admits every cluster.</summary>
    private readonly BspVisibility? _visibility;

    /// <summary>Each baked static prop's styled-light list, by entity, built on first ask — once per map, as the handle's.</summary>
    private readonly Dictionary<int, int[]> _styledByEntity = [];

    /// <summary>Whether a light style animates now, `DAT_18069dd40[style] &gt; 1` (<see cref="LightStyleValues.Animates"/>); none does unless set.</summary>
    public Func<int, bool>? StyleAnimates { get; set; }

    /// <summary>The world lights a static prop's lighting handle lists: `engine.dll` `FUN_1801b6bf0`.</summary>
    /// <param name="x">The prop's lighting origin.</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <returns>Indices into the world lights, in lump order.</returns>
    /// <remarks>
    /// Every light whose style (`+0x2c`) is nonzero — style 0 is what vrad baked — whose cluster (`+0x24`) is in the
    /// PVS of the leaf holding the origin, and whose contribution there is positive (<see cref="LocalLights.Reaches"/>).
    /// A map without vis data admits every cluster.
    /// </remarks>
    public int[] StyledLights(float x, float y, float z)
    {
        List<int> listed = [];
        int from = _leaves?.ClusterAt(x, y, z) ?? -1;
        bool anyCluster = _visibility is not { HasData: true };

        for (int index = 0; index < _worldLights.Count; index++)
        {
            BspWorldLight light = _worldLights[index];

            if (light.Style != 0 &&
                (anyCluster || _visibility!.Visible(from, light.Cluster)) &&
                LocalLights.Reaches(light, x, y, z))
            {
                listed.Add(index);
            }
        }

        return [.. listed];
    }

    /// <summary>Whether a baked static prop drops its colours this frame for full lighting: `engine.dll` `FUN_1801bb830`.</summary>
    /// <param name="entity">The prop's entity index, which keys its list.</param>
    /// <param name="x">The prop's lighting origin.</param>
    /// <param name="y">The prop's lighting origin.</param>
    /// <param name="z">The prop's lighting origin.</param>
    /// <returns>True when a listed light's style animates now.</returns>
    /// <remarks>
    /// The list is built once (<see cref="StyledLights"/>, `FUN_1801b8350` from `CStaticPropMgr::PrecacheLighting`
    /// `0x180205b20`); each frame only the listed styles are asked. The one-frame-style rebake (`FUN_1801bb8b0`) is not here.
    /// </remarks>
    public bool TakesFullLighting(int entity, float x, float y, float z)
    {
        if (StyleAnimates is not { } animates)
        {
            return false;
        }

        if (!_styledByEntity.TryGetValue(entity, out int[]? listed))
        {
            listed = StyledLights(x, y, z);
            _styledByEntity[entity] = listed;
        }

        foreach (int index in listed)
        {
            if (animates(_worldLights[index].Style))
            {
                return true;
            }
        }

        return false;
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
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return (PointLighting.None, null);
        }

        int leaf = tree.LeafAt(x, y, z);
        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count ? _ambient[leaf].At(x, y, z) : default;

        LocalLight[] nearest = new LocalLight[LocalLights.MaximumLocalLights];
        int found = LocalLights.Strongest(_worldLights, x, y, z, nearest, static style => style == 0 ? 1f : 0f);
        Array.Resize(ref nearest, found);

        SunLight? sun = _sun is { } light && _strikesSky is { } strikesSky ? SunFrom(light, strikesSky, x, y, z) : null;

        return (found == 0 ? PointLighting.Bounce(bounced) : new PointLighting(bounced, nearest), sun);
    }

    /// <summary>The ambient light at a world position.</summary>
    /// <param name="x">World position.</param>
    /// <param name="y">World position.</param>
    /// <param name="z">World position.</param>
    /// <returns>The cube, or a default one where the map cannot say.</returns>
    /// <remarks>
    /// **The leaf decides, which is how the engine does it.** A model takes the light measured
    /// inside the leaf it stands in, so two crates either side of a doorway are lit differently
    /// without either carrying a lightmap.
    ///
    /// An unlit answer is returned as a default cube, which the shader reads as "no cube supplied"
    /// and draws at full brightness rather than black — a model lit by a measurement nobody made is
    /// worse than one that is merely too bright.
    /// </remarks>
    public AmbientCube ComputeLighting(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return default;
        }

        int leaf = tree.LeafAt(x, y, z);

        // **Blended, as Mod_LeafAmbientColorAtPos blends it.** vrad thins a leaf's samples down to
        // the ones an inverse-squared-distance average cannot already predict, so the stored set
        // only reconstructs the original lighting when it is interpolated. Taking the nearest read
        // back whichever survivor of that thinning was closest, which is why one capture point on
        // cp_process drew at 0.10 while its mirror image on a symmetric map drew at 0.39.
        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        // **And the direct term, which is the other half of what the engine gives a model.**
        // istudiorender.h describes the cube as "ambient, and lights that aren't in locallight[]",
        // so a cube carrying a nearby lamp's light is the shape the engine itself produces for
        // every light past the nearest four. Without this a prop out of daylight is lit by the
        // bounce alone, which is why anything indoors read as though it were in shade (B95).
        //
        // **Kept for callers that want one number**, and NOT what a model is drawn with any more —
        // see LightingAt, which hands the nearest four to the shader instead so they can shade
        // against a normal. A caller must take one or the other: the engine adds the cube and the
        // local lights, so a light in both is counted twice.
        AmbientCube lit = LocalLights.AddTo(bounced, _worldLights, x, y, z, StyleScale);

        // **The two terms reported apart, because one number cannot say which is missing.** Every
        // model on z1800 sampled between 0.09 and 0.12 in a room with three ceiling lamps overhead,
        // and the single figure is consistent with two unrelated faults: no light near enough to be
        // chosen, or lights chosen that contribute nothing once attenuated. A log that names only
        // the total makes those indistinguishable — see
        // docs/memory/logs-are-the-debugger.md#a-log-must-name-what-it-measured.
        ReportLightTerms(bounced, lit, x, y, z);

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
    /// surfaces — bounce — plus the dim `emit_surface` lights, and Valve's comment there says why
    /// only those. Point and spot lamps are absent from the lump because they are meant to arrive
    /// at runtime, which is what the second half of this is.
    ///
    /// **The difference is not brightness, it is direction.** A lamp folded into a cube arrives
    /// from all six faces at once, so a model takes no N·L falloff from it and can cast no
    /// highlight from it — which is why our phong is gated on the sun and why a weapon indoors has
    /// no specular term at all (B170).
    ///
    /// **Nothing here folds anything in**, deliberately. `PixelShaderDoLightingLinear` accumulates
    /// the cube and then each light, so a light appearing in both would be counted twice — and that
    /// mistake reads as a lighting change rather than as a bug.
    /// </remarks>
    public PointLighting LightingAt(float x, float y, float z)
    {
        if (_leaves is not { } tree || _ambient.Count == 0)
        {
            return PointLighting.None;
        }

        // The entry is keyed on the point's own leaf and lit at its cell's point (`0x1801b8270` takes both).
        int leaf = tree.LeafAt(x, y, z);

        (x, y, z) = CachePoint(x, y, z);

        AmbientCube bounced = leaf >= 0 && leaf < _ambient.Count
            ? _ambient[leaf].At(x, y, z)
            : default;

        if (_worldLights.Count == 0)
        {
            return PointLighting.Bounce(bounced);
        }

        LocalLight[] nearest = new LocalLight[LocalLights.MaximumLocalLights];

        int found = LocalLights.Strongest(_worldLights, x, y, z, nearest, StyleScale);

        if (found == 0)
        {
            return PointLighting.Bounce(bounced);
        }

        // Trimmed rather than passed with a count, so a consumer cannot read past what was found —
        // the shader takes a live flag per slot and this keeps the two saying the same thing.
        Array.Resize(ref nearest, found);

        // **Reported from what is already in hand.** `ComputeLighting`'s ReportLightTerms compares
        // the cube against the folded one, which would mean computing the fold here purely to log
        // it — the expensive-argument case CA1873 exists for. What matters on this path is a
        // different question anyway: how many lights were chosen and how near the strongest is,
        // because "no lamp near enough" and "lamps chosen that contribute nothing" look identical
        // in a single brightness figure.
        if (_render.IsEnabled(LogLevel.Debug))
        {
            _render.LogDebug(
                "local lights at ({X}, {Y}, {Z}): {Count} chosen, strongest at ({LX}, {LY}, {LZ}) " +
                "intensity {Intensity}",
                x, y, z, found,
                nearest[0].X, nearest[0].Y, nearest[0].Z,
                Math.Max(nearest[0].Red, Math.Max(nearest[0].Green, nearest[0].Blue)));
        }

        return new PointLighting(bounced, nearest);
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
    public SunLight? SunAt(float x, float y, float z)
    {
        if (_sun is not { } sun || _strikesSky is not { } strikesSky)
        {
            return null;
        }

        (x, y, z) = CachePoint(x, y, z);

        return SunFrom(sun, strikesSky, x, y, z);
    }

    /// <summary>The sun reaching exactly this point: <see cref="SunAt"/> without the cache cell.</summary>
    private static SunLight? SunFrom(
        BspWorldLight sun, Func<(float X, float Y, float Z), (float X, float Y, float Z), bool> strikesSky, float x, float y, float z)
    {
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

    /// <summary>Says what the bounce gave and what the direct lights added, once per place.</summary>
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
    private void ReportLightTerms(AmbientCube bounced, AmbientCube lit, float x, float y, float z)
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
                $"light terms at ({x:0},{y:0},{z:0}): bounce {AmbientCube.Luminance(bounced):0.####}, " +
                $"with direct {AmbientCube.Luminance(lit):0.####}, " +
                $"{_worldLights.Count} world lights on the map"));

        // Stryker restore all
    }
}
