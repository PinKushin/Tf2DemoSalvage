using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Everything a map's BSP carries that the viewer reads once and keeps.</summary>
/// <param name="Terrain">Displacement geometry, or null when the lump would not read.</param>
/// <param name="Overlays">Decals, or null when the lump would not read.</param>
/// <param name="BrushModels">The submodels a <c>*N</c> reference names.</param>
/// <param name="BrushModelClasses">Which submodel index belongs to which entity class.</param>
/// <param name="Leaves">The BSP tree, for finding which leaf a point is in.</param>
/// <param name="Visibility">The PVS, for restricting soundscape selection (B177).</param>
/// <param name="LeafFaces">
/// Which faces each leaf touches — the indirection that turns a visible leaf into surfaces to draw.
/// </param>
/// <param name="Entities">The entity lump, already parsed.</param>
/// <param name="Surfaces">The world faces.</param>
/// <param name="Ambient">Per-leaf ambient samples, which light anything that moves.</param>
/// <param name="WorldLights">Every light, not only the sun (B95, D37).</param>
/// <param name="Sun">The single directional light, when the map has one.</param>
/// <param name="Normals">
/// Per-vertex normals and the indices faces use. **Read but not yet drawn (D93):** nothing consumes
/// them today, because the world is lit by its baked lightmaps and Valve's bumped path takes its
/// normal from the bump map rather than from a vertex. They are decoded because decoding is total
/// and rendering is not — and because the plane normal is NOT a substitute: vrad replaces the
/// compiler's plane normals with true smoothed ones wherever a smoothing group applies (B194).
/// </param>
/// <remarks>
/// **One read, because the engine gives each system a level-load hook rather than making the
/// window build them all.** <c>IGameSystem</c> declares <c>LevelInitPreEntity()</c> and
/// <c>LevelInitPostEntity()</c> (<c>igamesystem.h:39</c>, <c>:41</c>) and the engine calls
/// <c>LevelInitPreEntityAllSystems( mapName )</c> — a system initialises ITSELF from the level.
///
/// `MainForm.ReadMap` did all of it inline: ten lumps, three try/catch shapes and the brush-class
/// join, none of which is window work and none of which could be tested without an STA and a device
/// (B188, B184).
///
/// **The failure behaviour is preserved exactly, including that it differs per lump**, because each
/// difference was paid for:
///
/// <list type="bullet">
/// <item><b>Terrain</b> costs itself and nothing else — a map with unreadable displacements still
/// draws its walls.</item>
/// <item><b>Overlays and the entity-derived data</b> fail together, because the same read produces
/// the models lump, the entity lump and the tree. Losing them costs the decals rather than the map,
/// and is reported rather than swallowed: the engine reads this lump on every map it opens.</item>
/// <item><b>Surfaces and lighting</b> are NOT guarded. A map with no readable faces is not a map,
/// so a failure there is not something to continue past.</item>
/// </list>
/// </remarks>
public sealed record MapLevel(
    BspTerrain? Terrain,
    IReadOnlyList<BspOverlay>? Overlays,
    IReadOnlyList<BspModel>? BrushModels,
    IReadOnlyDictionary<int, string> BrushModelClasses,
    BspLeafTree? Leaves,
    BspVisibility? Visibility,
    BspLeafFaces? LeafFaces,
    IReadOnlyList<BspEntity> Entities,
    IReadOnlyList<BspSurface> Surfaces,
    IReadOnlyList<AmbientSamples> Ambient,
    IReadOnlyList<BspWorldLight> WorldLights,
    BspWorldLight? Sun,
    VertexNormals Normals)
{
    /// <summary>The map's terrain, as something a swept box can be stopped by.</summary>
    /// <remarks>
    /// **Built once, when the map is read, which is what <c>CDispCollTree</c> does.** The engine
    /// builds every displacement's collision tree at load; deferring it would turn the first sweep
    /// into a parse on a path the render loop calls every frame
    /// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`).
    ///
    /// Empty when the map has no terrain or its faces would not read — which makes
    /// <see cref="Sweep"/> a single expression rather than a null check at every call site.
    /// </remarks>
    public DisplacementCollision Displacements { get; } =
        Terrain is { } ground ? DisplacementCollision.From(Surfaces, ground) : DisplacementCollision.Empty;

    /// <summary>How far a box may travel through this map before something solid stops it.</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>The fraction of the way it got, 1 when nothing stopped it.</returns>
    /// <remarks>
    /// **Both halves of the world, in one place, because they are two lumps and one question.**
    /// Brushes come from the BSP tree and terrain does not appear in it at all (B227), so a caller
    /// asking only one of them gets an answer that is right about half the map. The chase camera
    /// asked only about brushes until 2026-08-29, and passed through every hillside in the game.
    ///
    /// **The smaller fraction wins**, which is what makes the two independent: neither trace needs
    /// to know about the other, and adding a third kind of geometry later is another term here
    /// rather than a change to either.
    /// </remarks>
    public float Sweep(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent) =>
        SweepSurface(from, to, halfExtent).Fraction;

    /// <summary><see cref="Sweep"/>, with the texinfo of what stopped it — `trace.surface` (B415).</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>The fraction, and the struck brush side's texinfo; −1 when nothing, or terrain, stopped it.</returns>
    /// <remarks>*Not built:* a displacement's surface. Terrain answers where it stops a sweep and not what it is.</remarks>
    public (float Fraction, int Texinfo) SweepSurface(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent)
    {
        BspTrace trace = Trace(from, to, halfExtent);

        return (trace.Fraction, trace.Texinfo);
    }

    /// <summary>`MASK_SOLID_BRUSHONLY`: brushes and terrain, no props.</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>The trace; terrain that stops it first answers its fraction and its texdata.</returns>
    /// <remarks>
    /// **"Brush only" leaves out props, not terrain** — a displacement is `CONTENTS_SOLID`. The legacy impact particles
    /// collide through this (`CParticleCollision`); handed the BSP tree alone they fell through harvest's ground.
    /// </remarks>
    /// <param name="mask">The brush contents that stop it; terrain is `CONTENTS_SOLID` and stops any mask that includes that.</param>
    public BspTrace TraceBrushOnly(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent, int mask = BspLeafTree.MaskSolid) =>
        TraceBrushOnly(from, to, (halfExtent, halfExtent, halfExtent), mask);

    /// <summary><see cref="TraceBrushOnly(ValueTuple{float, float, float}, ValueTuple{float, float, float}, float, int)"/> for a box with three extents.</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="extents">Half the box's size on each axis — `Ray_t::m_Extents`.</param>
    /// <param name="mask">The contents that stop it, carried to the terrain as well as the brushes.</param>
    /// <returns>The trace.</returns>
    public BspTrace TraceBrushOnly(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, (float X, float Y, float Z) extents, int mask)
    {
        BspTrace brushes = Leaves is { } tree
            ? tree.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, 0, mask)
            : new BspTrace(1f, -1, default, false);

        (float terrain, int texdata, bool second, (float X, float Y, float Z) normal, float distance) =
            Displacements.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, mask);

        // The struck triangle's plane, which `CBaseSimpleCollision::TestForPlane` builds a particle's collision plane from.
        return terrain < brushes.Fraction
            ? new BspTrace(terrain, -1, normal, false, distance, DisplacementTexdata: texdata, SurfaceProp2: second)
            : brushes;
    }

    /// <summary>The map's texinfo, which says what a trace's struck brush side is — `SURF_SKY` among it.</summary>
    public IReadOnlyList<BspTexinfo> Texinfo { get; init; } = [];

    /// <summary>Whether a world-only trace strikes sky before anything else.</summary>
    /// <param name="from">Where the trace starts.</param>
    /// <param name="to">Where it would end.</param>
    /// <returns>True when the first thing struck is a brush side whose texinfo carries `SURF_SKY`.</returns>
    /// <remarks>
    /// **The light cache's skylight test** (`engine.dll` 0x1801b8e20): `TRACE_WORLD_ONLY`, `MASK_OPAQUE`, then
    /// `tr.surface.flags &amp; SURF_SKY`. Terrain is world and is not sky, so a hillside shades what lies behind it.
    /// </remarks>
    public bool StrikesSky((float X, float Y, float Z) from, (float X, float Y, float Z) to) =>
        TraceBrushOnly(from, to, 0f) is { Fraction: < 1f, Texinfo: >= 0 } hit &&
        hit.Texinfo < Texinfo.Count &&
        (Texinfo[hit.Texinfo].Flags & SurfaceProperties.Sky) != 0;

    /// <summary><see cref="Sweep"/>, with what the brushes say about what stopped it — `trace_t` (B415).</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>The trace; terrain that stops it first answers its fraction and no surface.</returns>
    public BspTrace Trace((float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent) =>
        Trace(from, to, (halfExtent, halfExtent, halfExtent), BspLeafTree.MaskSolid, []);

    /// <summary><see cref="Trace(ValueTuple{float, float, float}, ValueTuple{float, float, float}, float)"/>, with brush entities too.</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <param name="brushes">The brush entities standing at the trace's moment (<see cref="SolidBrushEntities"/>).</param>
    /// <returns>The nearest trace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="brushes"/> is null.</exception>
    public BspTrace Trace(
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent, IReadOnlyList<SolidBrush> brushes) =>
        Trace(from, to, (halfExtent, halfExtent, halfExtent), BspLeafTree.MaskSolid, brushes);

    /// <summary>The whole world for a box with three extents and a mask: brushes, terrain, static props and brush entities.</summary>
    /// <param name="from">Where the box's centre starts.</param>
    /// <param name="to">Where it would end unobstructed.</param>
    /// <param name="extents">Half the box's size on each axis.</param>
    /// <param name="mask">The contents that stop it, carried to every kind of geometry.</param>
    /// <param name="brushes">The brush entities standing at the trace's moment.</param>
    /// <returns>The nearest trace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="brushes"/> is null.</exception>
    public BspTrace Trace(
        (float X, float Y, float Z) from,
        (float X, float Y, float Z) to,
        (float X, float Y, float Z) extents,
        int mask,
        IReadOnlyList<SolidBrush> brushes) =>
        Trace(from, to, extents, mask, brushes, default);

    /// <summary>The whole-world trace with the box's centre offset from its origin — `Ray_t::m_StartOffset`, negated.</summary>
    private BspTrace Trace(
        (float X, float Y, float Z) from,
        (float X, float Y, float Z) to,
        (float X, float Y, float Z) extents,
        int mask,
        IReadOnlyList<SolidBrush> brushes,
        Vector3 centre)
    {
        ArgumentNullException.ThrowIfNull(brushes);

        BspTrace nearest = TraceBrushOnly(from, to, extents, mask);

        // The static props too — `CONTENTS_SOLID`, through `CTraceFilterSimple` (StaticPropCollision), a line or a box.
        if (StaticProps.Trace(from, to, extents, mask) is { } prop && prop.Fraction < nearest.Fraction)
        {
            nearest = new BspTrace(
                prop.Fraction, -1, (prop.Normal.X, prop.Normal.Y, prop.Normal.Z), false, StudioSurfaceProp: prop.SurfaceProp, StaticProp: prop.Prop);
        }

        if (Leaves is not { } tree)
        {
            return nearest;
        }

        foreach (SolidBrush brush in brushes)
        {
            BspTrace entity = brush.Angles == Vector3.Zero
                ? tree.Trace(
                    from.X - brush.Origin.X, from.Y - brush.Origin.Y, from.Z - brush.Origin.Z,
                    to.X - brush.Origin.X, to.Y - brush.Origin.Y, to.Z - brush.Origin.Z,
                    extents,
                    brush.HeadNode,
                    mask)
                : TransformedTrace(tree, from, to, extents, mask, brush, centre);

            if (entity.Fraction < nearest.Fraction)
            {
                nearest = entity with { BrushEntity = brush.Entity };
            }
        }

        return nearest;
    }

    /// <summary>
    /// `UTIL_TraceHull` against the world: brushes, terrain, static props and brush entities, for a box placed by its origin and
    /// bounds — `Ray_t::Init( start, end, mins, maxs )` (`cmodel.h:84`), which centres the box and halves its size.
    /// </summary>
    /// <param name="from">The box's origin at the start — a player's feet.</param>
    /// <param name="to">Its origin at the end.</param>
    /// <param name="mins">The box's low corner relative to its origin; a standing player's is (−24, −24, 0).</param>
    /// <param name="maxs">Its high corner; a standing player's is (24, 24, 82) (`tf_gamerules.cpp:1313`).</param>
    /// <param name="mask">The contents that stop it — `MASK_PLAYERSOLID` for movement.</param>
    /// <param name="brushes">The brush entities standing at the trace's moment.</param>
    /// <returns>The trace, or null when the map has no tree — no world, which is not the same answer as a clear path.</returns>
    /// <remarks>
    /// **Null and not "clear" for a map with no tree**, unlike the camera's sweep. A camera told everything was clear passes a
    /// corner; a movement simulation told so falls forever, and its caller has to know to take the networked state instead.
    /// </remarks>
    public BspTrace? TraceHull(
        (float X, float Y, float Z) from,
        (float X, float Y, float Z) to,
        (float X, float Y, float Z) mins,
        (float X, float Y, float Z) maxs,
        int mask,
        IReadOnlyList<SolidBrush> brushes)
    {
        if (Leaves is null)
        {
            return null;
        }

        (float X, float Y, float Z) centre = ((mins.X + maxs.X) * 0.5f, (mins.Y + maxs.Y) * 0.5f, (mins.Z + maxs.Z) * 0.5f);

        return Trace(
            (from.X + centre.X, from.Y + centre.Y, from.Z + centre.Z),
            (to.X + centre.X, to.Y + centre.Y, to.Z + centre.Z),
            ((maxs.X - mins.X) * 0.5f, (maxs.Y - mins.Y) * 0.5f, (maxs.Z - mins.Z) * 0.5f),
            mask,
            brushes,
            new Vector3(centre.X, centre.Y, centre.Z));
    }

    /// <summary>
    /// `CM_TransformedBoxTrace` for a turned brush entity, read in `engine.dll` (x64) `FUN_18016ab60` (B450).
    /// </summary>
    /// <remarks>
    /// The ray's own start — the box's origin, `m_Start + m_StartOffset` — goes into the model's frame through
    /// `VectorITransform` and the centre offset is added back UNROTATED; the delta goes through `VectorIRotate`; the extents
    /// are kept, so the box turns with the model. A hit's normal comes back through `VectorRotate`; `plane.dist`,
    /// `startsolid` and `allsolid` stay the local trace's. *Each sum is in the binary's order (`FUN_180279620`,
    /// `FUN_1802795a0`, `FUN_1802796c0`); the matrix is `StaticPropCollision.AngleMatrix`, the SDK's `AngleMatrix` — whether
    /// `FUN_180276390`'s sine rounds the same is not read.*
    /// </remarks>
    private static BspTrace TransformedTrace(
        BspLeafTree tree,
        (float X, float Y, float Z) from,
        (float X, float Y, float Z) to,
        (float X, float Y, float Z) extents,
        int mask,
        SolidBrush brush,
        Vector3 centre)
    {
        (Vector3 f, Vector3 l, Vector3 u) = StaticPropCollision.AngleMatrix(brush.Angles.X, brush.Angles.Y, brush.Angles.Z);
        Vector3 d = new Vector3(from.X, from.Y, from.Z) - centre - brush.Origin;
        Vector3 v = new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        Vector3 start = new Vector3(
            (d.Y * f.Y) + (d.X * f.X) + (d.Z * f.Z),
            (d.X * l.X) + (d.Y * l.Y) + (d.Z * l.Z),
            (d.X * u.X) + (d.Y * u.Y) + (d.Z * u.Z)) + centre;
        Vector3 delta = new(
            (f.Y * v.Y) + (v.X * f.X) + (f.Z * v.Z),
            (l.X * v.X) + (l.Y * v.Y) + (l.Z * v.Z),
            (u.X * v.X) + (u.Y * v.Y) + (u.Z * v.Z));

        BspTrace local = tree.Trace(
            start.X, start.Y, start.Z, start.X + delta.X, start.Y + delta.Y, start.Z + delta.Z, extents, brush.HeadNode, mask);
        (float x, float y, float z) = local.Normal;

        return local with
        {
            Normal = (
                (l.X * y) + (x * f.X) + (u.X * z),
                (l.Y * y) + (f.Y * x) + (u.Y * z),
                (l.Z * y) + (f.Z * x) + (u.Z * z)),
        };
    }

    /// <summary>The solid static props a line meets, set once their models are read; empty until then.</summary>
    public StaticPropCollision StaticProps { get; init; } = StaticPropCollision.Empty;

    /// <summary>How to decide what of this map's world to draw, or null when it cannot be decided.</summary>
    /// <param name="spans">Where each face's triangles are, from the world build.</param>
    /// <returns>The map's culling, or null when a lump it needs is missing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spans"/> is null.</exception>
    /// <remarks>
    /// **Null rather than a culling object that culls nothing**, so the renderer's fallback is one
    /// decision made in one place. A map with no tree, no leaf-face lump, or a world build that
    /// recorded no spans cannot be culled at all, and drawing every batch is the correct answer for
    /// each of them.
    ///
    /// **A missing PVS is NOT one of those cases.** `BspVisibility.None` is a legitimate input:
    /// visibility falls back to the frustum alone, which still removes most of a map. Refusing to
    /// cull a map compiled without `vvis` would give up the larger half of the saving over the
    /// smaller.
    /// </remarks>
    public WorldCulling? Culling(IReadOnlyList<WorldFaceSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (Leaves is not { } tree || LeafFaces is not { } faces)
        {
            return null;
        }

        // **Which AREA the 3D skybox room is, found once from the map's own `sky_camera`.** The
        // engine sets exactly that area's bit for the sky pass and the ordinary bits for the main
        // one (`viewrender.cpp:4877`), which is how the miniature room is drawn separately instead
        // of sitting in the world at its literal size (B152).
        //
        // **−1 for a map with no `sky_camera`**, and that is not an error: an indoor map has
        // nothing to put in a 3D skybox. It gives every leaf to the main pass, which is what every
        // map did before this existed.
        int skyArea = BspEntities.SkyCamera(Entities) is { } sky
            ? tree.AreaAt(sky.Origin.X, sky.Origin.Y, sky.Origin.Z)
            : -1;

        WorldCulling culling = new(tree, Visibility ?? BspVisibility.None, faces, spans)
        {
            SkyArea = skyArea,
        };

        return culling.CanCull ? culling : null;
    }

    /// <summary>Reads every lump the viewer keeps, once.</summary>
    /// <param name="bytes">The whole BSP.</param>
    /// <param name="assets">Where lump failures are reported.</param>
    /// <returns>The level.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is null.</exception>
    /// <exception cref="InvalidDataException">The surfaces or lighting would not read.</exception>
    public static MapLevel Read(ReadOnlyMemory<byte> bytes, ILogger assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        // **Read once here rather than per face inside the world builder.** Every call reads the
        // header and decompresses both displacement lumps, and the builder asks 578 times on
        // cp_process_final — which was most of an 830 ms rebuild, paid again on every resize.
        BspTerrain? terrain = null;

        try
        {
            terrain = BspTerrain.Create(bytes);
        }
        catch (InvalidDataException failure)
        {
            assets.LogWarning(failure, "{Message}", "reading the map's terrain");
        }

        IReadOnlyList<BspOverlay>? overlays = null;
        IReadOnlyList<BspModel>? brushModels = null;
        BspLeafTree? leaves = null;
        BspVisibility? visibility = null;
        BspLeafFaces? leafFaces = null;
        IReadOnlyList<BspEntity> entities = [];
        Dictionary<int, string> classes = [];

        try
        {
            overlays = BspOverlays.Read(bytes);
            brushModels = BspModels.Read(bytes);

            // **The map's soundscapes need the entity lump (B173).** A SourceTV recording carries
            // the SourceTV camera's soundscape rather than the spectated player's, so the map is
            // the source — and it works for every map without anyone running
            // `soundscape_dumpclient` in the game first.
            entities = BspEntities.ReadFrom(bytes);

            // **The tree and the PVS are read here rather than with the lighting, because the
            // soundscapes need them.** Each placement resolves its visibility cluster once, the way
            // `LevelInitPostEntity` does — asking per frame would walk the BSP tree forty-four
            // times for values that cannot change.
            leaves = BspLeafTree.Read(bytes);
            visibility = BspVisibility.Read(bytes);

            // **And the leaf-face lump beside them, which is what turns visibility into drawing.**
            // A leaf carries a range into this array and each entry names a face; without it a leaf
            // knows where it is and nothing about what is drawn there. Read with the tree because
            // it is useless without one.
            leafFaces = BspLeafFaces.Read(bytes);

            // **Which submodel belongs to which class.** A brush entity names its geometry as `*N`,
            // so this is the join between the models lump — which carries faces and nothing else —
            // and the classname, the only place the map says what a piece of geometry IS.
            foreach (BspEntity entity in entities)
            {
                if (entity.TryGetValue("model", out string name) &&
                    entity.TryGetValue("classname", out string classname) &&
                    name.Length > 1 &&
                    // Qualified, because this record has a `BrushModels` property of its own and it
                    // shadows the type.
                    name[0] == Tf2DemoSalvage.Scene.BrushModels.SubmodelPrefix &&
                    int.TryParse(
                        name[1..],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int model))
                {
                    classes[model] = classname;
                }
            }

            assets.LogInformation(
                "{Message}",
                $"{classes.Count.ToString(CultureInfo.InvariantCulture)} brush entities named a class");
        }
        catch (InvalidDataException failure)
        {
            // Costs the decals, not the map. Reported rather than swallowed: the engine reads this
            // lump on every map it opens.
            overlays = null;
            assets.LogWarning(failure, "{Message}", "reading the map's decals");
        }

        // **Not guarded, deliberately.** A map whose faces or lighting will not read is not a map,
        // and continuing past that produces a black world rather than an error.
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);

        // **What lights anything that moves.** A model has no lightmap, so it takes the ambient cube
        // of the leaf it stands in — which needs the tree to find the leaf and the samples to light
        // it. Read with the map, since both come from the same file and neither changes afterwards.
        IReadOnlyList<AmbientSamples> ambient = BspAmbientLight.Read(bytes);

        // The direct term. The ambient cube is the shade; this is what makes daylight bright, and it
        // is the reason a pack outdoors looked like one indoors. Kept whole rather than just the
        // sun: the sun is the only light applied to world surfaces, but a model also takes direct
        // light from the point and spot lights around it (B95, D37) — the other 475 entries on
        // cp_process.
        IReadOnlyList<BspWorldLight> lights = BspWorldLights.Read(bytes);

        MapLevel level = new(
            terrain,
            overlays,
            brushModels,
            classes,
            leaves,
            visibility,
            leafFaces,
            entities,
            surfaces,
            ambient,
            lights,
            BspWorldLights.Sun(lights),

            // **Unguarded like the surfaces and the lighting**, because a map whose vertex normals
            // will not read is malformed in the same way — and unlike the decals, nothing degrades
            // gracefully without them once something does consume them (D93).
            BspVertexNormals.Read(bytes))
        {
            Texinfo = BspMaterials.ReadTexinfo(bytes),
        };

        return level;
    }

    /// <summary>Each brush model's placement, by submodel index.</summary>
    /// <remarks>
    /// **Absent means the origin, and that is the common case rather than a fallback.** A brush
    /// entity's geometry is compiled in world coordinates unless the mapper gave it an `origin`
    /// brush, so most `func_` entities correctly offset by nothing at all.
    /// </remarks>
    internal static Dictionary<int, Vector3> BrushModelOrigins(IReadOnlyList<BspEntity> entities)
    {
        Dictionary<int, Vector3> origins = [];

        foreach (BspEntity entity in entities)
        {
            // Stryker disable all : a mutant that empties the guard body leaves 'model'
            // unassigned below (CS0165), and Safe Mode then drops every mutation in this
            // method — B410.
            if (!entity.TryGetValue("model", out string name) ||
                name.Length < 2 ||
                name[0] != Tf2DemoSalvage.Scene.BrushModels.SubmodelPrefix ||
                !int.TryParse(
                    name[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int model))
            {
                continue;
            }

            // Stryker restore all

            if (entity.TryGetValue("origin", out string origin) &&
                Vector(origin) is { } placed)
            {
                origins[model] = placed;
            }
        }

        return origins;
    }

    /// <summary>Three space-separated floats, or null when the value is not one.</summary>
    private static Vector3? Vector(string value)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 3 &&
            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            ? new Vector3(x, y, z)
            : null;
    }
}
