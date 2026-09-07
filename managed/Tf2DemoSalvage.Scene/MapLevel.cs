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

    /// <summary>The map's baked physics collision, as something a corpse can land on (B58).</summary>
    /// <remarks>
    /// **This is the world the engine itself collides ragdolls against**, not a second
    /// approximation of the geometry: `LUMP_PHYSCOLLIDE` is what the compiler baked and what
    /// `CreatePolyObjectStatic` is fed at level load (`physics_shared.cpp:602-667`). It is a
    /// different thing from <see cref="Displacements"/> and <see cref="Sweep"/>, which answer a
    /// camera's question about brush faces.
    ///
    /// **Built once, at load, like the terrain beside it** — deferring it would turn the first
    /// corpse into a parse (`docs/memory/a-lazy-cache-makes-reading-a-write.md`).
    ///
    /// **Empty rather than null for a map with no lump**, so a corpse falls through an unreadable
    /// map instead of the viewer branching at every step.
    /// </remarks>
    public IvpWorldCollision Physics { get; init; } = new();

    /// <summary>Adds this map's terrain to its physics world.</summary>
    /// <remarks>
    /// **Separate from <see cref="PhysicsWorld"/> because it comes from a different lump and a
    /// different mechanism.** The brush hulls are baked; the terrain is not in
    /// `LUMP_PHYSCOLLIDE` at all and the engine rebuilds it at load from the displacement lump —
    /// `PhysCreateVirtualTerrain` runs only when the collision text declares a `virtualterrain`
    /// block (`physics_shared.cpp:682`), and it feeds vphysics a triangle soup through
    /// `CDispCollTree::GetVirtualMeshList`.
    ///
    /// **Built from <see cref="Displacements"/>, which already exists for the chase camera**, so
    /// the corpse and the camera cannot disagree about where a hillside is.
    /// </remarks>
    private void AddTerrain()
    {
        foreach (DisplacementTriangle triangle in Displacements.Triangles())
        {
            Physics.AddTriangle(
                new Vector3(triangle.A.X, triangle.A.Y, triangle.A.Z),
                new Vector3(triangle.B.X, triangle.B.Y, triangle.B.Z),
                new Vector3(triangle.C.X, triangle.C.Y, triangle.C.Z));
        }
    }

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
        (float X, float Y, float Z) from, (float X, float Y, float Z) to, float halfExtent)
    {
        float brushes = Leaves is { } tree
            ? tree.Sweep(from.X, from.Y, from.Z, to.X, to.Y, to.Z, halfExtent)
            : 1f;

        float terrain = Displacements.Sweep(
            from.X, from.Y, from.Z, to.X, to.Y, to.Z, halfExtent);

        return MathF.Min(brushes, terrain);
    }

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
            Physics = PhysicsWorld(bytes, assets),
        };

        // **After construction, because the terrain comes from `Displacements`**, which the record
        // builds from its own `Surfaces` and `Terrain` and so does not exist until it does.
        level.AddTerrain();

        return level;
    }

    /// <summary>Turns the map's baked collision into ledges the simulation can collide with.</summary>
    /// <remarks>
    /// **Every brush model, not only the world.** Index 0 is the world and the rest are `func_`
    /// brush entities — doors, platforms, the fences a corpse drapes over. They are static here
    /// because this project does not simulate a moving door, and a corpse resting on a closed one
    /// is right for as long as it stays closed; a corpse on a moving platform is not, and that is
    /// written down rather than discovered later.
    ///
    /// **A lump that will not read costs the collision and nothing else**, which is how every other
    /// optional lump in this reader behaves.
    /// </remarks>
    private static IvpWorldCollision PhysicsWorld(ReadOnlyMemory<byte> bytes, ILogger assets)
    {
        IvpWorldCollision world = new();

        try
        {
            BspHeader header = BspHeader.Parse(bytes.Span);

            foreach (MapPhysicsModel model in
                BspPhysicsCollision.Read(BspLumpData.Read(bytes, header.Lump(PhysCollideLump))))
            {
                // **Only the WORLD, and that is the engine's own split.** `PhysCreateWorld` builds
                // model 0 as the static world (`physics_shared.cpp:602-667`); every other entry is
                // a `func_` brush ENTITY, whose hull is stored in its own model space and placed by
                // that entity's origin. Adding those here put each of them at the map origin —
                // geometry missing where the door actually is and phantom geometry where it is not,
                // which is how a corpse collected contacts thirty-nine thousand units below the map.
                //
                // **So a corpse does not yet rest on a door or a moving platform**, stated rather
                // than left to look deliberate: that needs the entity lump's origin for each `*N`
                // model, which `BrushModelClasses` already joins for drawing.
                if (model.ModelIndex != 0)
                {
                    continue;
                }

                foreach (IReadOnlyList<PhysicsLedge> hull in model.Hulls)
                {
                    foreach (PhysicsLedge ledge in hull)
                    {
                        world.Add(ledge.Points, ledge.Triangles, ledge.Center, ledge.Radius);
                    }
                }
            }
        }
        catch (InvalidDataException failure)
        {
            assets.LogWarning(failure, "{Message}", "reading the map's physics collision");
        }

        return world;
    }

    /// <summary><c>LUMP_PHYSCOLLIDE</c>, <c>bspfile.h:310</c>.</summary>
    private const int PhysCollideLump = 29;
}
