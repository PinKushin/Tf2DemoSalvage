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

        // The brush entities' outlines and face planes (reviews 2-4): packed by the model set the viewer uses
        // (EntityModelSet.Precache), each model's run of its vertices sliced from the lowest to the highest vertex its
        // batches name, as Device3D.UploadModels slices it, and the planes FaceFan.Faces makes of that run, which is what
        // WorldRenderer.UploadModels hands it. The slice arithmetic is the upload's, repeated here (Device3D's Slice is
        // private to the Render assembly), and is the one step of this not carried from the code that runs.
        IReadOnlyDictionary<string, PropModels.ModelFrames> brush =
            BrushModels.Build(level.BrushModels ?? [], level.Surfaces, assets.Lightmaps);
        EntityModelSet set = new() { Geometry = model => brush.GetValueOrDefault(model) };

        set.Precache(brush.Keys);

        // Review 6: the planes are the stored ones the upload hands over (EntityModelSet.PackedFacePlanes, as Device3D
        // passes them), and beside them what the triangles alone would have said (the largest one's plane, FaceFan.Faces
        // without them), to count where the two disagree: the cases a derived plane got wrong.
        long brushVertices = 0;
        long faces = 0;
        int submodels = 0;
        (int Stored, int PlaneBack, int Disagree, int FirstSliver) count = (0, 0, 0, 0);

        foreach (string model in brush.Keys)
        {
            List<WorldVertex> run = Uploaded(set, model);
            IReadOnlyList<StoredPlane>? planes = set.PackedFacePlanes(model);
            FacePlane[] used = FaceFan.Faces(run, planes);
            FacePlane[] derived = FaceFan.Faces(run);

            submodels++;
            brushVertices += run.Count;
            faces += used.Length;

            for (int face = 0; planes is not null && face < used.Length; face++)
            {
                FacePlane plane = used[face];
                float agreement = (plane.NormalX * derived[face].NormalX) + (plane.NormalY * derived[face].NormalY) + (plane.NormalZ * derived[face].NormalZ);
                float sine = Sine(run[plane.First], run[plane.First + 1], run[plane.First + 2]);

                count = (
                    count.Stored + 1,
                    count.PlaneBack + (plane.PlaneBack ? 1 : 0),
                    count.Disagree + (agreement < 0.9999f ? 1 : 0),
                    count.FirstSliver + (plane.Count > 3 && sine < 1e-2f ? 1 : 0));
            }
        }

        int planeSize = Unsafe.SizeOf<FacePlane>();

        output.WriteLine(
            $"{submodels} brush entities, {brushVertices:N0} vertices: outlines {brushVertices * 2 * sizeof(uint):N0} bytes on the GPU; "
            + $"{faces:N0} face planes of {planeSize} bytes, {faces * planeSize:N0} bytes managed (plus an array header each)");
        output.WriteLine(
            $"stored planes: {count.Stored} (the control: one per face), {count.PlaneBack} plane-back; "
            + $"{count.Disagree} where the largest triangle's plane is more than 0.8° off the stored one; "
            + $"{count.FirstSliver} faces of more than one triangle whose first triangle's sine is under 1e-2");
    }

    /// <summary>A model's vertices as the upload slices them from the set (Device3D.UploadModels).</summary>
    private static List<WorldVertex> Uploaded(EntityModelSet set, string path)
    {
        int lowest = int.MaxValue;
        int highest = 0;

        foreach (IReadOnlyList<WorldBatch> frame in set.AllFrames(path))
        {
            foreach (WorldBatch batch in frame)
            {
                lowest = Math.Min(lowest, batch.FirstVertex);
                highest = Math.Max(highest, batch.FirstVertex + batch.VertexCount);
            }
        }

        return lowest == int.MaxValue || highest <= lowest
            ? []
            : [.. set.Vertices.Skip(lowest - set.VertexBase).Take(highest - lowest)];
    }

    /// <summary>The sine of a triangle's angle at its first corner: its cross product over its two edges' lengths.</summary>
    private static float Sine(WorldVertex a, WorldVertex b, WorldVertex c)
    {
        System.Numerics.Vector3 u = new(b.X - a.X, b.Y - a.Y, b.Depth - a.Depth);
        System.Numerics.Vector3 v = new(c.X - a.X, c.Y - a.Y, c.Depth - a.Depth);
        float lengths = u.Length() * v.Length();

        return lengths == 0f ? 0f : System.Numerics.Vector3.Cross(u, v).Length() / lengths;
    }
}
