using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Everything a map's BSP carries that the viewer reads once and keeps.</summary>
/// <param name="Terrain">Displacement geometry, or null when the lump would not read.</param>
/// <param name="Overlays">Decals, or null when the lump would not read.</param>
/// <param name="BrushModels">The submodels a <c>*N</c> reference names.</param>
/// <param name="BrushModelClasses">Which submodel index belongs to which entity class.</param>
/// <param name="Leaves">The BSP tree, for finding which leaf a point is in.</param>
/// <param name="Visibility">The PVS, for restricting soundscape selection (B177).</param>
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
    IReadOnlyList<BspEntity> Entities,
    IReadOnlyList<BspSurface> Surfaces,
    IReadOnlyList<AmbientSamples> Ambient,
    IReadOnlyList<BspWorldLight> WorldLights,
    BspWorldLight? Sun,
    VertexNormals Normals)
{
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

        return new MapLevel(
            terrain,
            overlays,
            brushModels,
            classes,
            leaves,
            visibility,
            entities,
            surfaces,
            ambient,
            lights,
            BspWorldLights.Sun(lights),

            // **Unguarded like the surfaces and the lighting**, because a map whose vertex normals
            // will not read is malformed in the same way — and unlike the decals, nothing degrades
            // gracefully without them once something does consume them (D93).
            BspVertexNormals.Read(bytes));
    }
}
