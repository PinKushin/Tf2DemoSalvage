using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What <c>mat_wireframe 3</c>'s face outline costs on a real map, whether or not it is ever set (B803 review 1).</summary>
/// <remarks>
/// Builds the world as the viewer does (<see cref="MapWorldBuilder.Build"/>) and its outline as the renderer uploads it
/// (<see cref="FaceFan.Outline"/>), then reports the index buffer's bytes and what the <see cref="WorldVertex.FaceEdge"/>
/// mark adds to each managed vertex. The control is the vertex count: a map that loads builds vertices.
/// </remarks>
public sealed class OutlineMemoryProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "outline-memory";

    /// <inheritdoc/>
    public string Summary => "bytes the mat_wireframe 3 outline and the FaceEdge mark cost on a map's world: outline-memory [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);
        string name = arguments.Count > 0 ? arguments[0] : "cp_process_final";

        if (locator.Find(name) is not { } path || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map named '{name}', or no game.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        MapAssets assets = MapAssets.Load(bytes, GameArchives.Open(folder), maximumTextureSize: 64, mapName: path);
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        MapWorld world = MapWorldBuilder.Build(
            level.Terrain,
            level.Surfaces,
            assets.Materials,
            assets.Lightmaps,
            area: null,
            level.Overlays,
            level.BrushModels,
            NullLoggerFactory.Instance);

        int vertices = world.Vertices.Count;
        long outline = (long)FaceFan.Outline(world.Vertices).Length * sizeof(uint);
        int vertexSize = Unsafe.SizeOf<WorldVertex>();

        // The record's 31 floats are what it held before the mark; the rest is the bool and its padding.
        const int Before = 31 * sizeof(float);

        output.WriteLine($"{name}: {vertices:N0} world vertices (the control: nonzero)");
        output.WriteLine($"outline index buffer: {outline:N0} bytes ({outline / (1024.0 * 1024.0):0.00} MiB) on the GPU");
        output.WriteLine($"WorldVertex: {vertexSize} bytes, {Before} before the mark; {(long)(vertexSize - Before) * vertices:N0} bytes more for this list");

        // The brush entities' outlines and face planes (review 2): each model's corners packed as the upload packs them
        // (EntityModelSet.Packed) and its planes the array FaceFan.Faces returns, the one the renderer keeps (review 3, B243).
        long brushVertices = 0;
        long faces = 0;
        int submodels = 0;

        foreach (PropModels.ModelFrames model in BrushModels.Build(level.BrushModels ?? [], level.Surfaces, assets.Lightmaps).Values)
        {
            IReadOnlyList<PropVertex> corners = model.Geometry[0];

            submodels++;
            brushVertices += corners.Count;
            faces += FaceFan.Faces([.. corners.Select(corner => EntityModelSet.Packed(corner, corner))]).Length;
        }

        int planeSize = Unsafe.SizeOf<FacePlane>();

        output.WriteLine(
            $"{submodels} brush entities, {brushVertices:N0} vertices: outlines {brushVertices * 2 * sizeof(uint):N0} bytes on the GPU; "
            + $"{faces:N0} face planes of {planeSize} bytes, {faces * planeSize:N0} bytes managed (plus an array header each)");
    }
}
