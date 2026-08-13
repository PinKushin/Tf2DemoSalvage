using System;
using System.Collections.Generic;
using System.IO;

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
    private readonly List<VpkArchive> _archives = [];
    private readonly List<string> _folders = [];

    private GameArchives(IEnumerable<VpkArchive> archives, IEnumerable<string> folders)
    {
        _archives.AddRange(archives);
        _folders.AddRange(folders);
    }

    /// <summary>Whether nothing at all was found.</summary>
    public bool IsEmpty => _archives.Count == 0 && _folders.Count == 0;

    /// <summary>How many loose content folders are being searched.</summary>
    public int FolderCount => _folders.Count;

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
        List<VpkArchive> archives = [];
        List<string> folders = [];

        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
        {
            return new GameArchives(archives, folders);
        }

        try
        {
            // Custom content first, because that is what it is for: a file under tf/custom
            // replaces the game's own copy.
            string custom = Path.Combine(gameFolder, "custom");

            if (Directory.Exists(custom))
            {
                foreach (string folder in Directory.GetDirectories(custom))
                {
                    folders.Add(folder);
                }
            }

            // Then the game folder itself, which is where loose files sit - including anything
            // extracted from a pre-VPK install.
            folders.Add(gameFolder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // An unreadable custom folder costs its overrides, not the viewer.
        }

        foreach (string name in new[] { "tf2_textures_dir.vpk", "tf2_misc_dir.vpk" })
        {
            string path = Path.Combine(gameFolder, name);

            try
            {
                if (File.Exists(path))
                {
                    archives.Add(VpkArchive.Open(path));
                }
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                // A damaged archive costs its textures, not the viewer.
            }
        }

        return new GameArchives(archives, folders);
    }

    /// <summary>Finds a file, searching loose folders before the archives.</summary>
    /// <param name="path">Path such as <c>materials/concrete/x.vmt</c>.</param>
    /// <returns>The bytes, or null.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    public byte[]? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        foreach (string folder in _folders)
        {
            // **The path is joined and then checked to be inside the folder.** It comes from a
            // material name in a map written by a stranger, and ".." in one would otherwise read
            // any file on the machine (D32).
            string candidate = Path.GetFullPath(Path.Combine(folder, path));

            if (!candidate.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                if (File.Exists(candidate))
                {
                    return File.ReadAllBytes(candidate);
                }
            }
            catch (Exception failure) when (
                failure is IOException or UnauthorizedAccessException)
            {
                // One unreadable file must not stop the search.
            }
        }

        foreach (VpkArchive archive in _archives)
        {
            try
            {
                if (archive.ReadFile(path) is { } bytes)
                {
                    return bytes;
                }
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                // One unreadable entry must not stop the search.
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
                return null;
            }
        }

        if (Find("materials/" + materialName + ".vmt") is not { } vmt)
        {
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
        catch (InvalidDataException)
        {
            return (null, null);
        }

        MapTexture? first = Decode(material.BaseTexture, material.IsTransparent);
        MapTexture? second = Decode(material.Value("$basetexture2"), material.IsTransparent);

        return (first, second);

        MapTexture? Decode(string? name, bool transparent)
        {
            if (name is null || Find("materials/" + name + ".vtf") is not { } vtf)
            {
                return null;
            }

            try
            {
                VtfTexture decoded = VtfTexture.Decode(vtf, maximumTextureSize);

                return new MapTexture(decoded.Width, decoded.Height, decoded.Pixels, transparent);
            }
            catch (InvalidDataException)
            {
                // A format this project does not read, or a truncated file. The face falls back to
                // the material's reflectivity.
                return null;
            }
        }
    }
}
