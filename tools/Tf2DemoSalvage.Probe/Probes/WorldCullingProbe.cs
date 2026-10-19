using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which of a map's drawn faces are reachable through its leaves at all.
/// </summary>
/// <remarks>
/// **Written because the owner looked at the viewer and saw the ground missing, while every
/// automated check passed.** The coverage test asks whether each face named by a visible leaf
/// reaches a run — which is vacuously true for any face NO leaf ever names. That is the empty-search
/// trap: the denominator was the leaves, so anything outside them could not be counted as lost.
///
/// **A probe since D213**; it was the `[Explicit]` `WorldCullingDiagnostic` in `Rendering.Tests`. It reports numbers
/// instead of a verdict. The control is the span count: a map that loads builds face spans.
/// </remarks>
public sealed class WorldCullingProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "world-culling";

    /// <inheritdoc/>
    public string Summary => "which of a map's face spans a leaf names at all, by surface category: world-culling [map]";

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

        if (level.Leaves is not { } tree || level.LeafFaces is not { } faces)
        {
            output.WriteLine("The map has no leaf tree.");
            return;
        }

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

        output.WriteLine($"leaves {tree.LeafCount}, leaf-face entries {faces.Count}");
        output.WriteLine($"distinct faces named by leaves: {named.Count}");
        output.WriteLine($"face spans built: {world.FaceSpans.Count}");

        foreach (IGrouping<SurfaceCategory, WorldFaceSpan> group in
            world.FaceSpans.GroupBy(span => span.Category))
        {
            int reachable = group.Count(span => named.Contains(span.Face));
            int corners = group.Sum(span => span.VertexCount);
            int lost = group.Where(span => !named.Contains(span.Face)).Sum(span => span.VertexCount);

            output.WriteLine(
                $"  {group.Key}: {group.Count()} spans, {reachable} reachable, "
                + $"{corners} corners of which {lost} unreachable");
        }

        int highestSpan = world.FaceSpans.Count > 0 ? world.FaceSpans.Max(span => span.Face) : -1;
        int highestNamed = named.Count > 0 ? named.Max() : -1;

        output.WriteLine($"highest face in a span {highestSpan}, highest named by a leaf {highestNamed}");
    }
}
