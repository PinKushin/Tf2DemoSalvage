using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Which of a map's drawn faces are reachable through its leaves at all.
/// </summary>
/// <remarks>
/// **Written because the owner looked at the viewer and saw the ground missing, while every
/// automated check passed.** The coverage test asks whether each face named by a visible leaf
/// reaches a run — which is vacuously true for any face NO leaf ever names. That is the empty-search
/// trap: the denominator was the leaves, so anything outside them could not be counted as lost.
///
/// Explicit rather than an assertion, so it reports numbers instead of a verdict.
/// </remarks>
[Explicit("Diagnostic: reports which faces the leaf lists can reach.")]
public sealed class WorldCullingDiagnostic
{
    [Test]
    public void ReportFacesReachableThroughLeaves()
    {
        MapAssets assets = MapCache.Load();
        MapLevel level = MapLevel.Read(MapCache.Bytes(), NullLogger.Instance);

        MapWorld world = MapWorldBuilder.Build(
            level.Terrain,
            level.Surfaces,
            assets.Materials,
            assets.Lightmaps,
            assets.Props,
            area: null,
            level.Overlays,
            level.BrushModels,
            NullLoggerFactory.Instance);

        BspLeafTree tree = level.Leaves!;
        BspLeafFaces faces = level.LeafFaces!;

        HashSet<int> named = [];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = faces.Face(first + entry);

                if (face >= 0)
                {
                    named.Add(face);
                }
            }
        }

        Console.WriteLine($"leaves {tree.LeafCount}, leaf-face entries {faces.Count}");
        Console.WriteLine($"distinct faces named by leaves: {named.Count}");
        Console.WriteLine($"face spans built: {world.FaceSpans.Count}");

        foreach (IGrouping<SurfaceCategory, WorldFaceSpan> group in
            world.FaceSpans.GroupBy(span => span.Category))
        {
            int reachable = group.Count(span => named.Contains(span.Face));
            int corners = group.Sum(span => span.VertexCount);
            int lost = group.Where(span => !named.Contains(span.Face)).Sum(span => span.VertexCount);

            Console.WriteLine(
                $"  {group.Key}: {group.Count()} spans, {reachable} reachable, "
                + $"{corners} corners of which {lost} unreachable");
        }

        int highestSpan = world.FaceSpans.Max(span => span.Face);
        int highestNamed = named.Count > 0 ? named.Max() : -1;

        Console.WriteLine($"highest face in a span {highestSpan}, highest named by a leaf {highestNamed}");

        // The analyzers want an assertion; this one states the only thing that is certainly true of
        // a map that loaded at all, and leaves the report above as the point of the test.
        world.FaceSpans.ShouldNotBeEmpty();
    }
}
