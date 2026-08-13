using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Core.Assets;
using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>One triangle corner of a placed prop, already in world space.</summary>
/// <param name="X">Where it stands in the map.</param>
/// <param name="Y">Where it stands in the map.</param>
/// <param name="Z">Where it stands in the map.</param>
/// <param name="U">Texture coordinate.</param>
/// <param name="V">Texture coordinate.</param>
/// <param name="MaterialIndex">Which material paints it, in the map's combined table.</param>
/// <param name="OriginX">Where the placement stands, east-west.</param>
/// <param name="OriginY">Where the placement stands, north-south.</param>
/// <param name="Red">Baked lighting, one where the placement has none.</param>
/// <param name="Green">Baked lighting, one where the placement has none.</param>
/// <param name="Blue">Baked lighting, one where the placement has none.</param>
internal readonly record struct PropVertex(
    float X, float Y, float Z, float U, float V, int MaterialIndex,
    float OriginX = 0f, float OriginY = 0f,
    float Red = 1f, float Green = 1f, float Blue = 1f);

/// <summary>
/// The models a map places, loaded and put where the map says.
/// </summary>
/// <remarks>
/// **This is what fills the map's remaining holes.** A displacement painted with
/// <c>tools/toolsinvisibledisplacement</c> is collision-only terrain the engine never draws, and
/// what a player sees standing there is a prop on top of it — the rock at cp_process mid being the
/// case that named the problem. Reading the placements was not enough; they have to be drawn.
///
/// **Each model is loaded once and placed many times.** A map names a few hundred distinct models
/// and places several thousand instances of them, so the three files behind each one are read once
/// and the vertices are then transformed per placement. On a real map that is the difference
/// between hundreds of archive reads and tens of thousands.
///
/// **Materials join the map's own table rather than living beside it.** A prop's material index
/// continues where the BSP's texture table ends, so the renderer's existing per-material batching
/// and texture array serve both without knowing which is which — one binding path, not two.
///
/// **A prop that cannot be loaded costs itself and nothing else.** A missing model, an unreadable
/// one, a mismatched checksum: each is logged and skipped, because a map is largely drawable
/// without any given rock and not drawable at all if one bad file stops the load.
/// </remarks>
internal static class PropModels
{
    /// <summary>The most placements to draw from one map.</summary>
    /// <remarks>
    /// A map is untrusted input (D32). Real maps place a few thousand props; the ceiling is well
    /// clear of that and still refuses a file claiming millions.
    /// </remarks>
    private const int MaximumPlacements = 100_000;

    /// <summary>What the engine multiplies static prop vertex lighting by, from its own shader.</summary>
    private const float Overbright = 2f;

    /// <summary>Loads a map's props and places them.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="pak">The map's own embedded content, searched before the game's.</param>
    /// <param name="archives">The game's archives and folders.</param>
    /// <param name="materials">The map's material table, extended in place with the props'.</param>
    /// <param name="textures">The decoded textures, extended in step with the table.</param>
    /// <param name="load">Loads a material's texture, or returns null.</param>
    /// <returns>Every placed triangle corner, three per triangle.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<PropVertex> Load(
        ReadOnlyMemory<byte> map,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        Func<string, MapTexture?> load)
    {
        ArgumentNullException.ThrowIfNull(pak);
        ArgumentNullException.ThrowIfNull(archives);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(load);

        IReadOnlyList<BspStaticProp> placements;

        try
        {
            placements = BspStaticProps.Read(map);
        }
        catch (InvalidDataException failure)
        {
            ViewerLog.Warn("props", "reading the map's static props", failure);
            return [];
        }

        if (placements.Count == 0)
        {
            return [];
        }

        int brushMaterialCount = textures.Count;
        Dictionary<string, LoadedModel?> loaded = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> materialIndices = new(StringComparer.OrdinalIgnoreCase);
        List<PropVertex> world = [];

        int placed = 0;
        int skipped = 0;
        int unlit = 0;

        for (int index = 0; index < placements.Count; index++)
        {
            BspStaticProp placement = placements[index];

            if (placed >= MaximumPlacements)
            {
                ViewerLog.Warn(
                    "props", $"stopping at {MaximumPlacements} placements; the map declares more");
                break;
            }

            if (!loaded.TryGetValue(placement.Model, out LoadedModel? model))
            {
                model = Read(
                    placement.Model, pak, archives, materials, textures, materialIndices, load);
                loaded[placement.Model] = model;
            }

            if (model is null)
            {
                skipped++;
                continue;
            }

            PropTransform transform = new(placement);

            IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? lighting =
                Lighting(pak, placed: index, model.Checksum);

            if (lighting is null)
            {
                unlit++;
            }

            for (int at = 0; at < model.Corners.Count; at++)
            {
                PropVertex corner = model.Corners[at];

                (float x, float y, float z) = transform.Apply(corner.X, corner.Y, corner.Z);

                (float red, float green, float blue) = Colour(
                    lighting, model.Meshes[at], model.Vertices[at]);

                // **The placement's own origin rides along**, so a prop can be kept or dropped as
                // one thing. Judging its triangles individually cannot tell a 3D skybox prop from
                // a real one: both are made of ordinary triangles, and the skybox's are a valid
                // shape at a valid position - just a position that is nowhere near where the
                // player sees it.
                world.Add(corner with
                {
                    X = x,
                    Y = y,
                    Z = z,
                    OriginX = placement.X,
                    OriginY = placement.Y,
                    Red = red,
                    Green = green,
                    Blue = blue,
                });
            }

            placed++;
        }

        int transparent = 0;

        for (int index = brushMaterialCount; index < textures.Count; index++)
        {
            if (textures[index] is { IsTransparent: true })
            {
                transparent++;
            }
        }

        ViewerLog.Write(
            "props",
            $"{placed} props placed from {loaded.Count} models ({skipped} skipped, " +
            $"{unlit} without baked lighting), {world.Count / 3} triangles, " +
            $"{transparent} of {textures.Count - brushMaterialCount} prop materials alpha tested");

        return world;
    }

    /// <summary>Reads one placement's baked lighting, or nothing when it has none.</summary>
    /// <remarks>
    /// **Named by the placement's index in the static prop lump**, which is how the compiler wrote
    /// it. A prop with no lighting is normal rather than an error - a map compiled without static
    /// prop lighting has none for any of them - so this reports absence quietly and the caller
    /// counts it.
    /// </remarks>
    private static IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? Lighting(
        PakFile pak, int placed, int checksum)
    {
        foreach (string path in StudioVertexLighting.PathsFor(placed))
        {
            byte[]? file;

            try
            {
                file = pak.ReadFile(path);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                return null;
            }

            if (file is null)
            {
                continue;
            }

            try
            {
                return StudioVertexLighting.Read(file, checksum);
            }
            catch (InvalidDataException failure)
            {
                // Includes the checksum guard: lighting baked against a different build of the
                // model would light the wrong parts of it. Unlit is the honest fallback.
                ViewerLog.Warn("props", $"reading {path}", failure);
                return null;
            }
        }

        return null;
    }

    /// <summary>One vertex's baked colour, or white where there is none to apply.</summary>
    /// <remarks>
    /// **Applied only where the counts agree**, which is the check the engine makes before
    /// uploading vertex colours. vrad counts a mesh's colours from its strip group, which may
    /// duplicate vertices past the model mesh's own count; measured at one model in two hundred on
    /// cp_process_final. Applying a short run anyway would shift every colour after it onto the
    /// wrong vertex, which lights the prop convincingly and wrongly.
    /// </remarks>
    private static (float Red, float Green, float Blue) Colour(
        IReadOnlyList<IReadOnlyList<(byte Red, byte Green, byte Blue)>>? lighting,
        int mesh,
        int vertex)
    {
        if (lighting is null || mesh < 0 || mesh >= lighting.Count)
        {
            return (1f, 1f, 1f);
        }

        IReadOnlyList<(byte Red, byte Green, byte Blue)> colours = lighting[mesh];

        if (vertex < 0 || vertex >= colours.Count)
        {
            return (1f, 1f, 1f);
        }

        (byte red, byte green, byte blue) = colours[vertex];

        // **Doubled, because the engine doubles it.** vrad builds its vertex-light table as
        // pow(linear, 1/gamma) * overbrightFactor, storing HALF the light when overbright is 2, and
        // the vertex-lit shader multiplies it back:
        //
        // its vertex-lit shader defines an overbright of two and multiplies the stored colour by
        // it before converting to linear.
        //
        // Without it every prop draws at half brightness - dark rocks and near-black foliage on a
        // sunlit map, which is what the owner kept reporting as blobs.
        //
        // The measurement had said so before the source did: prop colours averaged 0.2309 against
        // the world's lightmaps at 0.4704, a ratio of 2.04. That was first explained as a missing
        // gamma step, because 0.23 ^ (1/2.2) is 0.495 and also lands near 0.47 - two different
        // curves passing through one point, and the wrong one was picked. Only the shader settles
        // which.
        //
        // Clamped rather than carried, since this renderer works in display space and has no tone
        // map to give over-range light anywhere to go.
        return (
            Math.Min(1f, red / 255f * Overbright),
            Math.Min(1f, green / 255f * Overbright),
            Math.Min(1f, blue / 255f * Overbright));
    }

    /// <summary>Reads one model's three files and turns them into triangles.</summary>
    private static LoadedModel? Read(
        string path,
        PakFile pak,
        GameArchives archives,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        Dictionary<string, int> materialIndices,
        Func<string, MapTexture?> load)
    {
        byte[]? Find(string file)
        {
            try
            {
                return pak.ReadFile(file) ?? archives.Read(file);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                return null;
            }
        }

        // The index file is named by replacing the extension, and the game writes several - dx80,
        // dx90, sw. dx90 is the one every machine that can run this viewer has.
        string stem = path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)
            ? path[..^4]
            : path;

        if (Find(path) is not { } modelFile ||
            Find(stem + ".vvd") is not { } vertexFile ||
            Find(stem + ".dx90.vtx") is not { } indexFile)
        {
            ViewerLog.Warn("props", $"{path}: one of its three files is missing");
            return null;
        }

        try
        {
            StudioModelInfo model = StudioModel.Read(modelFile);
            IReadOnlyList<StudioVertex> vertices = StudioVertices.Read(vertexFile);
            IReadOnlyList<IReadOnlyList<StudioCorner>> meshes = StudioTriangles.Read(indexFile, model);

            List<PropVertex> corners = [];
            List<int> cornerMeshes = [];
            List<int> cornerVertices = [];

            for (int index = 0; index < meshes.Count && index < model.Meshes.Count; index++)
            {
                StudioMesh mesh = model.Meshes[index];

                if (mesh.FirstVertex + mesh.VertexCount > vertices.Count)
                {
                    continue;
                }

                int material = Register(
                    model, mesh.MaterialIndex, materials, textures, materialIndices, load);

                foreach (StudioCorner corner in meshes[index])
                {
                    StudioVertex vertex = vertices[mesh.FirstVertex + corner.Vertex];

                    corners.Add(new PropVertex(
                        vertex.X, vertex.Y, vertex.Z, vertex.U, vertex.V, material));

                    // **Position by mesh vertex, colour by strip group vertex.** They are different
                    // orderings of the same surface, and using one for both speckles the prop.
                    cornerMeshes.Add(corner.LightingGroup);
                    cornerVertices.Add(corner.LightingVertex);
                }
            }

            return new LoadedModel(corners, cornerMeshes, cornerVertices, model.Checksum);
        }
        catch (InvalidDataException failure)
        {
            // Includes the checksum mismatch, which is the engine's own guard against a model
            // whose three files do not belong together.
            ViewerLog.Warn("props", $"reading {path}", failure);
            return null;
        }
    }

    /// <summary>Finds or creates the combined table's entry for one of a model's materials.</summary>
    /// <remarks>
    /// Keyed by the resolved path rather than by the model, because props share materials heavily —
    /// a dozen rocks off one texture — and a per-model entry would decode it a dozen times.
    /// </remarks>
    private static int Register(
        StudioModelInfo model,
        int materialIndex,
        List<BspMaterial> materials,
        List<MapTexture?> textures,
        Dictionary<string, int> indices,
        Func<string, MapTexture?> load)
    {
        if (materialIndex < 0 || materialIndex >= model.Materials.Count)
        {
            return -1;
        }

        foreach (string candidate in model.MaterialPaths(materialIndex))
        {
            if (indices.TryGetValue(candidate, out int existing))
            {
                return existing;
            }

            if (load(candidate) is not { } texture)
            {
                continue;
            }

            // **The table and the textures grow together**, because the renderer indexes both by
            // the same number. Appending to one without the other silently paints every prop from
            // that point on with the wrong image.
            int index = materials.Count;

            materials.Add(new BspMaterial(candidate, (0.5f, 0.5f, 0.5f), texture.Width, texture.Height));
            textures.Add(texture);
            indices[candidate] = index;

            return index;
        }

        return -1;
    }

    /// <summary>
    /// A model's triangles in its own space, ready to be placed.
    /// </summary>
    /// <param name="Corners">The triangle corners, three per triangle.</param>
    /// <param name="Meshes">Which strip group each corner came from, in .vhv header order.</param>
    /// <param name="Vertices">Which vertex of that strip group each corner is.</param>
    /// <param name="Checksum">The model's checksum, which its lighting must match.</param>
    /// <remarks>
    /// **The mesh and vertex are kept because the model is shared and the lighting is not.** One
    /// model stands in fifty places under fifty different bakes, so the geometry is cached once and
    /// the colours are looked up per placement — which needs to know, for each corner, which mesh
    /// and which vertex of it produced that corner.
    /// </remarks>
    private sealed record LoadedModel(
        IReadOnlyList<PropVertex> Corners,
        IReadOnlyList<int> Meshes,
        IReadOnlyList<int> Vertices,
        int Checksum);
}
