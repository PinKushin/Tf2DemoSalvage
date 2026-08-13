using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Assets;
using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>A decoded texture ready to upload.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Pixels">The image, four bytes per pixel, red first.</param>
/// <param name="IsTransparent">Whether the material asked for alpha blending.</param>
internal readonly record struct MapTexture(
    int Width, int Height, ReadOnlyMemory<byte> Pixels, bool IsTransparent);

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
        IReadOnlyList<BspMaterial> materials,
        LightmapAtlas lightmaps,
        IReadOnlyList<PropVertex> props,
        int resolved,
        int missing)
    {
        Textures = textures;
        BlendTextures = blendTextures;
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
    /// <param name="maximumTextureSize">Largest texture edge to decode; zero for full size.</param>
    /// <returns>The assets.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidDataException">The map's lumps are malformed.</exception>
    public static MapAssets Load(
        ReadOnlyMemory<byte> map, GameArchives archives, int maximumTextureSize)
    {
        ArgumentNullException.ThrowIfNull(archives);

        PakFile pak = PakFile.ReadFrom(map);
        List<BspMaterial> materials = [.. BspMaterials.Read(map)];

        List<MapTexture?> textures = new(materials.Count);
        List<MapTexture?> blendTextures = new(materials.Count);
        int resolved = 0;
        int missing = 0;

        foreach (BspMaterial material in materials)
        {
            (MapTexture? texture, MapTexture? blend) =
                Resolve(material.Name, pak, archives, maximumTextureSize);

            textures.Add(texture);
            blendTextures.Add(blend);

            if (texture is null)
            {
                missing++;
            }
            else
            {
                resolved++;
            }
        }

        // **Props after the brushwork, deliberately.** They extend the same material table, so
        // every index the BSP already handed out keeps its meaning and the new ones continue from
        // the end. Inserting them first would renumber every face in the map.
        int brushMaterials = materials.Count;

        IReadOnlyList<PropVertex> props = PropModels.Load(
            map,
            pak,
            archives,
            materials,
            textures,
            path => Resolve(path, pak, archives, maximumTextureSize).Texture);

        // The blend list is indexed in step with the textures, and a prop material never has a
        // second layer - only a displacement's WorldVertexTransition does.
        while (blendTextures.Count < textures.Count)
        {
            blendTextures.Add(null);
        }

        ViewerLog.Write(
            "assets",
            $"{materials.Count - brushMaterials} prop materials added to {brushMaterials} the map's own");

        return new MapAssets(
            textures,
            blendTextures,
            materials,
            LightmapAtlas.Pack(BspLightmaps.Read(map)),
            props,
            resolved,
            missing);
    }

    /// <summary>Follows a material to its texture.</summary>
    /// <remarks>
    /// The chain is VMT, then a patch's included VMT if there is one, then the VTF. Any step
    /// failing yields null, because a half-resolved material has nothing to draw.
    /// </remarks>
    private static (MapTexture? Texture, MapTexture? Blend) Resolve(
        string materialName, PakFile pak, GameArchives archives, int maximumTextureSize)
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
            ViewerLog.Warn("assets", $"material materials/{materialName}.vmt was not found");

            return (null, null);
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

            return (null, null);
        }

        MapTexture? first = Decode(material.BaseTexture, material.IsTransparent);
        MapTexture? second = Decode(material.Value("$basetexture2"), material.IsTransparent);

        return (first, second);

        MapTexture? Decode(string? name, bool transparent)
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

                return new MapTexture(decoded.Width, decoded.Height, decoded.Pixels, transparent);
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
