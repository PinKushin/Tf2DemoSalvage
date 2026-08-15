using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>A decoded texture ready to upload.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Pixels">The image, four bytes per pixel, red first.</param>
/// <param name="IsTransparent">Whether the material is cut out by a threshold.</param>
/// <param name="IsAdditive">Whether the engine ADDS this material rather than painting it.</param>
/// <param name="IsTranslucent">Whether it is BLENDED with what is behind it instead.</param>
/// <param name="SelfIllum">Tint for the self-illuminated part, or null when there is none.</param>
/// <param name="IsModulate">
/// Whether the material MULTIPLIES what is behind it rather than covering it — Source's Modulate
/// shader, which declares neither $translucent nor $additive and so was read as opaque, painting
/// over exactly what it exists to shade.
/// </param>
/// <param name="IsModulateTwice">Whether that multiply doubles, so mid grey changes nothing.</param>
/// <param name="IsNoCull">
/// Whether the material draws from both sides. $nocull sets MATERIAL_VAR_NOCULL in the engine
/// (imaterial.h:369) and shaders test it per material; everything else culls back faces.
/// </param>
/// <param name="MultipliesTextures">
/// Whether the material's two textures are MULTIPLIED rather than mixed by vertex alpha. That is
/// UnLitTwoTexture, whose pixel shader is baseColor * baseColor2 * g_DiffuseModulation.
/// </param>
/// <remarks>
/// **Alpha tested and translucent are different operations and never both.** A cut-out surface is
/// drawn in the opaque pass and needs no ordering; a blended one has to be drawn afterwards, back
/// to front, without writing depth. Source decides between them explicitly, and alpha test wins.
/// </remarks>
internal readonly record struct MapTexture(
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels,
    bool IsTransparent,
    bool IsAdditive = false,
    bool IsTranslucent = false,
    (float Red, float Green, float Blue)? SelfIllum = null,

    bool IsModulate = false,
    bool IsModulateTwice = false,
    bool IsNoCull = false,
    bool MultipliesTextures = false);

/// <summary>A material's detail texture and the numbers that say how to combine it.</summary>
/// <param name="Texture">The detail pattern itself.</param>
/// <param name="Scale">How many times it tiles per tile of the base texture, across and down.</param>
/// <param name="BlendFactor">How strongly it is applied.</param>
/// <param name="Mode">Which of the twelve combine modes to use.</param>
/// <param name="Tint">The colour the sampled detail is multiplied by first.</param>
/// <remarks>
/// **The mode here is the engine's, not the material's.** If the detail texture's own VTF carries
/// the self-shadowing bump flag, the engine overrides <c>$detailblendmode</c> — so this is resolved
/// once, at load, rather than left for the renderer to work out per frame.
/// </remarks>
internal readonly record struct MapDetail(
    MapTexture Texture,
    (float U, float V) Scale,
    float BlendFactor,
    int Mode,
    (float Red, float Green, float Blue) Tint);

/// <summary>A material's bump map and how to read it.</summary>
/// <param name="Texture">The normal map, or the self-shadowing weights.</param>
/// <param name="IsSelfShadowing">Whether it stores three light weights rather than a direction.</param>
/// <remarks>
/// **The two are indistinguishable by looking and combine completely differently.** A normal map is
/// decoded as <c>xyz * 2 - 1</c> and drives squared dot products against the bump basis; a
/// self-shadowing one is sampled raw and its channels ARE the weights. On cp_process_final it is 14
/// against 13, so neither can be treated as the special case.
/// </remarks>
internal readonly record struct MapBump(MapTexture Texture, bool IsSelfShadowing);

/// <summary>Everything one material resolved to.</summary>
/// <param name="Texture">The base texture, or null when it could not be found.</param>
/// <param name="Blend">The second layer of a blend material, or null.</param>
/// <param name="Detail">The detail pattern, or null.</param>
/// <param name="Bump">The bump map, or null.</param>
/// <param name="Declared">Every parameter the VMT named, for reporting the unimplemented ones.</param>
/// <remarks>
/// A record rather than a longer and longer tuple: at four members the positional form stops
/// saying which is which at the call site, and two of these are the same type.
/// </remarks>
internal readonly record struct ResolvedMaterial(
    MapTexture? Texture,
    MapTexture? Blend,
    MapDetail? Detail,
    MapBump? Bump,
    IReadOnlyCollection<string>? Declared = null);

/// <summary>
/// Everywhere the game's content can live, searched in the order the engine searches it.
/// </summary>
/// <remarks>
/// **VPK is not the only answer, and for old content it is the wrong one.** Source shipped its
/// content in Steam's GCF caches until Valve moved TF2 to VPK around 2013, and loose files have
/// worked the whole time. So the search covers all the shapes that still exist on a real machine:
///
/// | Where | Why |
/// |---|---|
/// | <c>tf/custom/*/materials</c> | where custom content goes today, and it OVERRIDES the game |
/// | <c>tf/materials</c> | loose files, including anything extracted from a pre-VPK install |
/// | <c>tf/*_dir.vpk</c> | the modern archives |
/// | <c>hl2/</c> and <c>hl2/*_dir.vpk</c> | what TF2's own gameinfo.txt mounts after its own |
///
/// That order is the engine's: custom beats loose, loose beats packed. A viewer that searched the
/// VPKs first would show the stock texture where the game shows the user's replacement.
///
/// **GCF is not read.** Extracting one needs a Steam cache format that has not shipped in over a
/// decade, and a machine with GCF-era content also has the files loose or has since been updated.
/// If a case ever turns up, the loose-file path is where it would be handled.
///
/// Opening the archives is not free — a 1.5 MB directory tree and a 2.4 MB one — and nothing about
/// them changes between maps, so they are read once and kept.
/// </remarks>
internal sealed class GameArchives
{
    /// <summary>Every place to look, in the order the game declares them.</summary>
    /// <remarks>
    /// **One ordered list rather than folders-then-archives**, because gameinfo.txt interleaves
    /// them and the order IS the priority. TF2 lists tf/custom/* first, then its VPKs, then the
    /// loose mod folder — so a VPK beats a loose file in tf/, which is the opposite of the
    /// folklore and is what the file says. Searching all folders before all archives, as this did,
    /// silently inverts that for anyone with content extracted into tf/.
    ///
    /// **And the folklore is half right, which is why it survives.** A custom HUD does override the
    /// game's copy — because it lives in <c>tf/custom/</c>, which the file lists FIRST, above the
    /// archives. Loose files dropped into <c>tf/</c> itself are listed LAST and do not. One file,
    /// both behaviours, and no contradiction once it is read rather than recalled.
    /// </remarks>
    private readonly List<(string Path, VpkArchive? Archive)> _sources = [];

    private GameArchives(IEnumerable<(string Path, VpkArchive? Archive)> sources) =>
        _sources.AddRange(sources);

    /// <summary>Whether nothing at all was found.</summary>
    public bool IsEmpty => _sources.Count == 0;

    /// <summary>How many loose content folders are being searched.</summary>
    public int FolderCount => _sources.Count(source => source.Archive is null);

    /// <summary>Opens the game's content, wherever it lives.</summary>
    /// <param name="gameFolder">The <c>tf</c> folder of a TF2 install, or null.</param>
    /// <returns>The content sources, empty when the game is not installed.</returns>
    /// <remarks>
    /// **A missing install is not an error.** Someone reviewing demos on a machine without TF2 gets
    /// the map's own content and untextured stock surfaces, which is worse than the alternative and
    /// far better than a viewer that refuses to open anything.
    /// </remarks>
    public static GameArchives Open(string? gameFolder)
    {
        List<(string Path, VpkArchive? Archive)> sources = [];

        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            return new GameArchives(sources);
        }

        IReadOnlyList<SearchPathEntry> declared = GameSearchPath.Read(gameFolder);

        if (declared.Count == 0)
        {
            // **No gameinfo.txt falls back to the behaviour that predates all of this**: the mod
            // folder's loose files, which is what Source read before VPKs and before tf/custom
            // existed. Nothing else can be assumed - custom is a convention the FILE declares, and
            // inventing it for a game that never declared it is the same hardcoding this type was
            // written to remove.
            sources.Add((gameFolder, null));

            return new GameArchives(sources);
        }

        // **Exactly the order the file declares**, including that TF2 lists its VPKs above the
        // loose mod folder. That ordering is easy to get backwards from memory - the folklore says
        // a loose file overrides its archived copy - and the file settles it without anyone having
        // to remember.
        foreach (SearchPathEntry entry in declared)
        {
            try
            {
                if (entry.IsArchive)
                {
                    if (File.Exists(entry.Path))
                    {
                        sources.Add((string.Empty, VpkArchive.Open(entry.Path)));
                    }
                }
                else if (Directory.Exists(entry.Path))
                {
                    sources.Add((entry.Path, null));
                }
            }
            catch (Exception failure) when (
                failure is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // A damaged archive or unreadable folder costs its content, not the viewer.
            }
        }

        ViewerLog.Write(
            "assets",
            $"search path: {sources.Count} sources from gameinfo.txt " +
            $"({sources.Count(source => source.Archive is not null)} archives)");

        // **Named, not counted.** "archives plus 8 folders" cannot answer which archive a
        // missing material should have come from, and TF2 splits its content: the VTFs live in
        // tf2_textures and the VMTs in tf2_misc, so losing one of them loses every material while
        // the other still resolves textures.
        ViewerLog.Write(
            "assets",
            "content: " + string.Join(
                ", ",
                sources.Select(source => source.Archive is null
                    ? "folder " + source.Path
                    : "archive")));

        return new GameArchives(sources);
    }

    /// <summary>Finds a file, searching every source in the order the game declares.</summary>
    /// <param name="path">Path such as <c>materials/concrete/x.vmt</c>.</param>
    /// <returns>The bytes, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    public byte[]? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach ((string folder, VpkArchive? archive) in _sources)
        {
            try
            {
                if (archive is not null)
                {
                    if (archive.ReadFile(path) is { } packed)
                    {
                        return packed;
                    }

                    continue;
                }

                // **The path is joined and then checked to be inside the folder.** It comes from a
                // material name in a map written by a stranger, and ".." in one would otherwise
                // read any file on the machine (D32).
                string candidate = Path.GetFullPath(Path.Combine(folder, path));

                if (!candidate.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // One unreadable source must not stop the search.
            }
        }

        return null;
    }
}

/// <summary>
/// Everything needed to draw one map as the game draws it.
/// </summary>
/// <remarks>
/// **Resolution order is the map first, then the game.** A community map ships overrides of stock
/// materials in its own pakfile, and the game's copy is not the one it was built against.
///
/// **A missing texture is normal and must stay cheap.** Of cp_process_final's 211 materials, three
/// resolve to nothing even with the game installed — and on a machine without TF2, most would.
/// Those faces draw with the material's <c>reflectivity</c> instead, which is the average colour the
/// map compiler recorded from the texture: not the texture, but the right colour, and free.
/// </remarks>
internal sealed class MapAssets
{
    private MapAssets(
        IReadOnlyList<MapTexture?> textures,
        IReadOnlyList<MapTexture?> blendTextures,
        IReadOnlyList<MapDetail?> details,
        IReadOnlyList<MapBump?> bumps,
        IReadOnlyList<BspMaterial> materials,
        LightmapAtlas lightmaps,
        IReadOnlyList<PropVertex> props,
        int resolved,
        int missing)
    {
        Textures = textures;
        BlendTextures = blendTextures;
        Details = details;
        Bumps = bumps;
        Materials = materials;
        Lightmaps = lightmaps;
        Props = props;
        Resolved = resolved;
        Missing = missing;
    }

    /// <summary>The map's placed models, in world space, three corners per triangle.</summary>
    /// <remarks>
    /// **Their materials continue the map's own table**, so a prop's material index indexes
    /// <see cref="Textures"/> exactly like a brush face's. That is what lets one renderer draw both.
    /// </remarks>
    public IReadOnlyList<PropVertex> Props { get; }

    /// <summary>Entity models, in their own coordinates, keyed by path.</summary>
    /// <remarks>
    /// Model space rather than world space, unlike <see cref="Props"/>: a static prop stands where
    /// the map put it and can be baked, while an entity moves and is posed by a matrix in the
    /// shader.
    /// </remarks>
    public IReadOnlyDictionary<string, PropModels.ModelFrames> EntityModels { get; private init; } =
        new Dictionary<string, PropModels.ModelFrames>(StringComparer.OrdinalIgnoreCase);

    /// <summary>One decoded texture per material, null where none was found.</summary>
    public IReadOnlyList<MapTexture?> Textures { get; }

    /// <summary>The second layer of a blend material, null for the great majority that have none.</summary>
    /// <remarks>
    /// **This is where grass comes from.** A <c>WorldVertexTransition</c> material names two
    /// textures — on cp_process_final, <c>dirtground009</c> and <c>grass_07</c> — and a
    /// displacement's per-vertex alpha mixes them. Sampling only the first draws every outdoor
    /// surface as bare dirt, which is exactly how the map looked.
    /// </remarks>
    public IReadOnlyList<MapTexture?> BlendTextures { get; }

    /// <summary>The detail pattern for each material, null for those without one.</summary>
    /// <remarks>
    /// **A detail texture is what stops a wall looking like a flat colour.** It is a small tiling
    /// pattern - concrete grain, brick speckle, noise - multiplied into the base texture at four
    /// times its frequency by default, and it is the difference between a surface and a swatch.
    /// </remarks>
    public IReadOnlyList<MapDetail?> Details { get; }

    /// <summary>The bump map for each material, null for those without one.</summary>
    /// <remarks>
    /// **A bump map does not change a surface's colour, it changes which of its four lightmaps are
    /// read.** vrad stored light arriving from three directions; the normal map says which way each
    /// pixel of the surface faces, and the three are mixed accordingly. That is what makes a flat
    /// wall look like brick rather than like a photograph of one.
    /// </remarks>
    public IReadOnlyList<MapBump?> Bumps { get; }

    /// <summary>The map's texture table, for reflectivity where a texture is missing.</summary>
    public IReadOnlyList<BspMaterial> Materials { get; }

    /// <summary>Every face's baked lighting, packed into one image.</summary>
    public LightmapAtlas Lightmaps { get; }

    /// <summary>How many materials resolved to a texture.</summary>
    public int Resolved { get; }

    /// <summary>How many did not.</summary>
    public int Missing { get; }

    /// <summary>Loads a map's textures and lighting.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <param name="archives">The game's archives.</param>
    /// <param name="entityModels">Model paths the demo uses, loaded with the map so the textures upload once.</param>
    /// <param name="wornModels">Of those, the ones bone-merged onto another entity, which must be skinned.</param>
    /// <param name="maximumTextureSize">Largest texture edge to decode; zero for full size.</param>
    /// <returns>The assets.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's lumps are malformed.</exception>
    public static MapAssets Load(
        ReadOnlyMemory<byte> map,
        GameArchives archives,
        int maximumTextureSize,
        IReadOnlyCollection<string>? entityModels = null,
        IReadOnlyCollection<string>? wornModels = null)
    {
        ArgumentNullException.ThrowIfNull(archives);

        PakFile pak = PakFile.ReadFrom(map);
        List<BspMaterial> materials = [.. BspMaterials.Read(map)];

        List<MapTexture?> textures = new(materials.Count);
        List<MapTexture?> blendTextures = new(materials.Count);
        List<MapDetail?> details = new(materials.Count);
        List<MapBump?> bumps = new(materials.Count);
        int resolved = 0;
        int missing = 0;

        IDisposable materialTiming = ViewerLog.Time("assets", "resolving materials");

        // **Resolved in parallel, written by index.** Each material is an independent chain of
        // VMT, patch and VTF, and both content sources are read-only once opened: VpkArchive opens
        // a fresh stream per read and PakFile reads an in-memory buffer, so neither has shared
        // mutable state.
        //
        // Index-addressed rather than appended, because the material table's ORDER is load-bearing
        // - every face in the map indexes into it. Appending from several threads would shuffle it
        // and repaint the map with the wrong textures, differently on each run.
        ResolvedMaterial[] found = new ResolvedMaterial[materials.Count];

        Parallel.For(0, materials.Count, index =>
            found[index] = Resolve(materials[index].Name, pak, archives, maximumTextureSize));

        foreach (ResolvedMaterial material in found)
        {
            textures.Add(material.Texture);
            blendTextures.Add(material.Blend);
            details.Add(material.Detail);
            bumps.Add(material.Bump);

            if (material.Texture is null)
            {
                missing++;
            }
            else
            {
                resolved++;
            }
        }

        // **What the map asked for that this renderer does not do.** Logged unconditionally,
        // because the alternative was measured: every material on cp_process resolved, so the log
        // stayed silent while every control point drew as a black disc, and the one gap that
        // mattered - $envmap on 43 of 189 materials, B55 - took an hour of throwaway probes.
        //
        // A report built only from failures reads clean while every instance quietly falls back.
        IReadOnlyList<(string Parameter, int Materials)> census = MaterialCensus.Unimplemented(
            found.Select(material => material.Declared ?? []));

        if (census.Count == 0)
        {
            ViewerLog.Write(
                "assets", "every parameter the map's materials declare is implemented");
        }
        else
        {
            ViewerLog.Write(
                "assets",
                $"{census.Count} unimplemented material parameters across {materials.Count} materials: " +
                string.Join(", ", census.Select(entry => $"{entry.Parameter} x{entry.Materials}")));
        }

        // **Props after the brushwork, deliberately.** They extend the same material table, so
        // every index the BSP already handed out keeps its meaning and the new ones continue from
        // the end. Inserting them first would renumber every face in the map.
        int brushMaterials = materials.Count;

        materialTiming.Dispose();

        IDisposable propTiming = ViewerLog.Time("assets", "loading props");

        IReadOnlyList<PropVertex> props = PropModels.Load(
            map,
            pak,
            archives,
            materials,
            textures,
            path => Resolve(path, pak, archives, maximumTextureSize, report: false).Texture);

        propTiming.Dispose();

        // **Entity models are loaded here, with the map's own props, and that is the point.**
        // Their materials go into the same table, so the textures upload once with everything in
        // them. Loading a model during playback instead would mean growing the texture array
        // mid-match and re-uploading it, which is a hitch exactly where the viewer is trying to
        // look smooth.
        //
        // Every model the demo uses is already known: the timeline is built before anything is
        // drawn, which is the same trade this project makes everywhere - know it all up front,
        // and playback costs nothing. TF2 launches a listen server to play a demo, so the budget
        // here is generous.
        Dictionary<string, PropModels.ModelFrames> models = new(StringComparer.OrdinalIgnoreCase);

        if (entityModels is { Count: > 0 })
        {
            using IDisposable modelTiming = ViewerLog.Time("assets", "loading entity models");

            int loaded = 0;

            foreach (string path in entityModels)
            {
                PropModels.ModelFrames? frames = PropModels.LoadFrames(
                    path,
                    pak,
                    archives,
                    materials,
                    textures,
                    file => Resolve(file, pak, archives, maximumTextureSize, report: false).Texture,

                    // **Worn models are skinned regardless of how cheap they are.** A bone-merged
                    // item has no transform of its own; it is placed entirely by its wearer's
                    // skeleton, so baking away its bones leaves nothing to hang it from.
                    mustSkin: wornModels?.Contains(path) == true);

                if (frames is { Geometry.Count: > 0 } && frames.Geometry[0].Count > 0)
                {
                    models[path] = frames;
                    loaded++;
                }
            }

            ViewerLog.Write(
                "assets", $"{loaded} of {entityModels.Count} entity models loaded");
        }

        // The blend list is indexed in step with the textures, and a prop material never has a
        // second layer - only a displacement's WorldVertexTransition does.
        while (blendTextures.Count < textures.Count)
        {
            blendTextures.Add(null);
        }

        // Prop materials are appended after the brushwork, so their detail slots have to be too or
        // every prop indexes a detail belonging to a different material.
        while (details.Count < textures.Count)
        {
            details.Add(null);
        }

        while (bumps.Count < textures.Count)
        {
            bumps.Add(null);
        }

        // **Measured rather than assumed.** A detail chain that loads nothing still draws a
        // perfectly reasonable map, so the count is the only thing that says it is working.
        ViewerLog.Write(
            "assets",
            $"{details.Count(detail => detail is not null)} materials carry a detail texture");

        ViewerLog.Write(
            "assets",
            $"{materials.Count - brushMaterials} prop materials added to {brushMaterials} the map's own");

        // **Measured, not assumed.** A bump chain that resolves nothing still draws a perfectly
        // reasonable map, because every bumped face already has a correct flat lightmap.
        ViewerLog.Write(
            "assets",
            $"{bumps.Count(bump => bump is not null)} materials carry a bump map, " +
            $"{bumps.Count(bump => bump is { IsSelfShadowing: true })} of them self-shadowing");

        return new MapAssets(
            textures,
            blendTextures,
            details,
            bumps,
            materials,
            PackLighting(map),
            props,
            resolved,
            missing)
        {
            EntityModels = models,
        };
    }

    private static LightmapAtlas PackLighting(ReadOnlyMemory<byte> map)
    {
        using (ViewerLog.Time("assets", "reading and packing lightmaps"))
        {
            return LightmapAtlas.PackAll(BspLightmaps.ReadAll(map));
        }
    }

    /// <summary>Follows a material to its texture.</summary>
    /// <remarks>
    /// The chain is VMT, then a patch's included VMT if there is one, then the VTF. Any step
    /// failing yields null, because a half-resolved material has nothing to draw.
    /// </remarks>
    private static ResolvedMaterial Resolve(
        string materialName,
        PakFile pak,
        GameArchives archives,
        int maximumTextureSize,
        bool report = true)
    {
        byte[]? Find(string path)
        {
            try
            {
                return pak.ReadFile(path) ?? archives.Read(path);
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                // **Reported rather than swallowed.** An unreadable archive entry is a defect in
                // this reader until shown otherwise; the engine opens all of these.
                ViewerLog.Warn("assets", $"reading {path}", failure);

                return null;
            }
        }

        if (Find("materials/" + materialName + ".vmt") is not { } vmt)
        {
            // **Silent only when the caller is guessing.** A model's material can be reached by
            // several candidate paths and all but one are expected to miss; reporting each would
            // bury the real failures, which the caller logs once it has run out of candidates.
            if (report)
            {
                ViewerLog.Warn("assets", $"material materials/{materialName}.vmt was not found");
            }

            return default;
        }

        VmtMaterial material;

        try
        {
            material = VmtMaterial.Parse(vmt);

            if (material.IsPatch && material.Include is { } include && Find(include) is { } based)
            {
                material = VmtMaterial.ApplyPatch(material, VmtMaterial.Parse(based));
            }
        }
        catch (InvalidDataException failure)
        {
            ViewerLog.Warn("assets", $"parsing materials/{materialName}.vmt", failure);

            return default;
        }

        // **PrimaryTexture, not BaseTexture**, because a material need not have a base one. TF2's
        // eyes use EyeRefract, which names an iris, a cornea and an occlusion map and no
        // $basetexture at all - so asking for the base drew the missing-texture chequer on every
        // player's eyes while the material itself resolved perfectly (B62).
        MapTexture? first = Decode(
            material.PrimaryTexture, material.IsAlphaTested, material.IsAdditive);

        // **Two shaders reach this slot and they combine differently.** A WorldVertexTransition
        // names $basetexture2 and MIXES by vertex alpha; UnLitTwoTexture names $texture2 and
        // MULTIPLIES. Both are "the material's second texture", so they share the slot, and the
        // material carries which operation to use — a capture point's beam is stripes times a
        // colour, and mixed by alpha instead it is whichever the vertices happen to ask for.
        MapTexture? second = Decode(
            material.Value("$basetexture2") ?? material.SecondTexture,
            material.IsAlphaTested,
            material.IsAdditive);

        // **The parameters carried out alongside the textures**, so the caller can report what
        // the map asked for rather than only what failed. Gathered here because this is the one
        // place the parsed VMT exists; the census itself runs on the single-threaded side.
        return new ResolvedMaterial(first, second, ResolveDetail(), ResolveBump(), material.Keys);

        MapBump? ResolveBump()
        {
            if (material.BumpMap is not { } name)
            {
                return null;
            }

            if (Load(name) is not { } decoded)
            {
                ViewerLog.Warn(
                    "assets",
                    $"bump map {name}, named by materials/{materialName}.vmt, could not be read");

                return null;
            }

            // **The texture's own flag outranks the material's declaration**, the same way it does
            // for a detail texture's blend mode. On cp_process_final the two agree on all 13
            // materials that use one, but the flag is data and $ssbump is a statement about it.
            return new MapBump(
                new MapTexture(
                    decoded.Width, decoded.Height, decoded.Pixels, IsTransparent: false),
                decoded.IsSelfShadowBump || material.IsSelfShadowingBump);
        }

        VtfTexture? Load(string name)
        {
            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } file)
            {
                return null;
            }

            try
            {
                return VtfTexture.Decode(file, maximumTextureSize);
            }
            catch (InvalidDataException failure)
            {
                // Reported, never silent: the engine reads every one of these, so anything that
                // will not decode is a defect here until shown otherwise.
                ViewerLog.Warn("assets", $"decoding materials/{bare}.vtf", failure);

                return null;
            }
        }

        MapDetail? ResolveDetail()
        {
            if (material.Detail is not { } name)
            {
                return null;
            }

            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } file)
            {
                ViewerLog.Warn(
                    "assets",
                    $"detail texture materials/{bare}.vtf, named by materials/{materialName}.vmt, was not found");

                return null;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Decode(file, maximumTextureSize);

                // **The texture's own flag outranks the material's mode.** Valve's helper forces
                // 10 or 11 when the detail is a self-shadowing bump map, whatever
                // $detailblendmode asked for. Mode 10 needs a bump map we do not have yet, so
                // ssbump detail resolves to 11, which is what the engine does without one.
                int mode = decoded.IsSelfShadowBump
                    ? DetailCombine.SelfShadowBumpNoBump
                    : material.DetailBlendMode;

                return new MapDetail(
                    new MapTexture(decoded.Width, decoded.Height, decoded.Pixels, IsTransparent: false),
                    material.DetailScale,
                    material.DetailBlendFactor,
                    mode,
                    material.DetailTint);
            }
            catch (InvalidDataException failure)
            {
                // **The base texture survives this.** A detail texture that will not decode, or a
                // $detailscale that is not a number, costs the surface its grain and nothing else
                // - it must never take the base texture with it and turn the surface purple.
                ViewerLog.Warn("assets", $"detail for materials/{materialName}.vmt", failure);

                return null;
            }
        }

        MapTexture? Decode(string? name, bool transparent, bool additive)
        {
            if (name is null)
            {
                return null;
            }

            // **Some materials name the texture WITH its extension.** Valve's own script-generated
            // VMTs do it - the props_hydro pipes carry
            // `$baseTexture "models/props_hydro/2pipe.vtf"` - and appending .vtf to that asks for
            // 2pipe.vtf.vtf, which exists nowhere. The engine tolerates both spellings, so this
            // must too; 19 of cp_process_final's prop materials resolved to nothing over it.
            string bare = name.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;

            if (Find("materials/" + bare + ".vtf") is not { } vtf)
            {
                ViewerLog.Warn("assets", $"texture materials/{bare}.vtf was not found");

                return null;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Decode(vtf, maximumTextureSize);

                return new MapTexture(
                    decoded.Width,
                    decoded.Height,
                    decoded.Pixels,
                    transparent,
                    additive,
                    material.IsTranslucent,
                    material.IsSelfIlluminated ? material.SelfIllumTint : null,
                    material.IsModulate,
                    material.IsModulateTwice,
                    material.IsNoCull,
                    material.IsTwoTexture);
            }
            catch (InvalidDataException failure)
            {
                // **Reported, never silent.** A texture that cannot be decoded is a defect in this
                // reader until shown otherwise - the engine reads every one of these - and a face
                // quietly falling back to a reflectivity colour is how that goes unnoticed.
                ViewerLog.Warn("assets", $"decoding materials/{bare}.vtf", failure);

                return null;
            }
        }
    }
}
