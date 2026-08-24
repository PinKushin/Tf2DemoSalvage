using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>Everything read from a map, before a camera is chosen.</summary>
/// <param name="Bytes">The map file.</param>
/// <param name="Outline">Its overhead outline and play-area bounds.</param>
/// <param name="Assets">Its textures, materials, lighting and placed models.</param>
/// <param name="Surfaces">Its brush faces.</param>
/// <param name="Terrain">Its displacements, or null when it has none.</param>
/// <param name="Overlays">Its decals.</param>
internal readonly record struct MapScene(
    ReadOnlyMemory<byte> Bytes,
    MapOutline Outline,
    MapAssets Assets,
    IReadOnlyList<BspSurface> Surfaces,
    BspTerrain? Terrain,
    IReadOnlyList<BspOverlay> Overlays)
{
    /// <summary>Builds the world for a camera, exactly as the viewer does.</summary>
    /// <param name="camera">Where the view is.</param>
    /// <param name="categoryColours">Draw flat category colours instead of textures.</param>
    /// <returns>The world, decals included.</returns>
    /// <remarks>
    /// **One call, so a test and the window cannot draw different maps.** They had drifted:
    /// the picture test passed no overlays and asked for half-size textures, so it rendered a
    /// scene with no decals and different mip levels and was then read as evidence about the
    /// viewer. Every argument that could differ is now decided here.
    /// </remarks>
    public MapWorld Build(TopDownCamera camera, bool categoryColours = false) =>
        MapWorldBuilder.Build(
            Terrain,
            Surfaces,
            Assets.Materials,
            Assets.Lightmaps,
            Assets.Props,
            camera,
            Outline.MainBounds,
            categoryColours,
            Overlays);
}

/// <summary>
/// Reads a map once, the way the viewer reads it.
/// </summary>
/// <remarks>
/// **The point is that there is one of these.** A renderer defect is only visible in a picture, and
/// a picture is only evidence if it was produced by the same code path the window uses. Before
/// this, the two agreed by hand and stopped agreeing the moment either gained an argument — decals
/// were added to the viewer and never to the test, so the test kept passing on a map that had
/// none.
/// </remarks>
internal static class MapSceneReader
{
    /// <summary>Reads everything a map needs to be drawn.</summary>
    /// <param name="mapPath">Path to the <c>.bsp</c>.</param>
    /// <param name="gameFolder">The game's <c>tf</c> folder, for its content.</param>
    /// <param name="maximumTextureSize">Largest texture edge; zero for full size, as the viewer uses.</param>
    /// <returns>The scene.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mapPath"/> is null.</exception>
    /// <param name="loggers">
    /// Where reading reports what it could not use. **A factory rather than a logger, because the
    /// work below spans areas (D83)**: this method's own refusals are `assets`, and everything it
    /// calls into — the material set, the props, the terrain — reports under its own name.
    /// Optional, so a test that wants a scene rather than a log passes nothing.
    /// </param>
    public static MapScene Read(
        string mapPath,
        string? gameFolder,
        int maximumTextureSize = 0,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(mapPath);

        ILoggerFactory factory = loggers ?? NullLoggerFactory.Instance;
        ILogger assets = factory.CreateLogger("assets");

        ReadOnlyMemory<byte> bytes = System.IO.File.ReadAllBytes(mapPath);

        BspTerrain? terrain = null;

        try
        {
            terrain = BspTerrain.Create(bytes);
        }
        catch (System.IO.InvalidDataException failure)
        {
            assets.LogWarning(failure, "reading the map's terrain");
        }

        IReadOnlyList<BspOverlay> overlays = [];

        try
        {
            overlays = BspOverlays.Read(bytes);
        }
        catch (System.IO.InvalidDataException failure)
        {
            assets.LogWarning(failure, "reading the map's decals");
        }

        return new MapScene(
            bytes,
            MapOutline.FromFaces(BspGeometry.Read(bytes).Faces),
            MapAssets.Load(
                bytes,
                GameArchives.Open(gameFolder, factory.LogTo()),
                maximumTextureSize,
                loggers: factory),
            BspSurfaces.Read(bytes),
            terrain,
            overlays);
    }
}
