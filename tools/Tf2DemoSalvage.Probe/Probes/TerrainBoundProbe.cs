using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// On a real map, `MapLevel.TraceBrushOnly` with the terrain sweep bounded by the brush fraction against the same compose
/// with the whole ray swept — every field of every trace (2026-10-26, B594).
/// </summary>
/// <remarks>
/// Rays: seeded short and long ones with three box sizes, and the light cache's sky trace (57,016 units against the sun's
/// normal) from seeded points. **Control:** how many traces the terrain won must be above zero, or the comparison never
/// exercised the half that changed.
/// <code>
///   terrain-bound &lt;bsp path&gt; [count]
/// </code>
/// </remarks>
public sealed class TerrainBoundProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "terrain-bound";

    /// <inheritdoc/>
    public string Summary => "bounded vs whole terrain sweep in TraceBrushOnly, field for field: terrain-bound <bsp path> [count]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine(Summary);
            return;
        }

        byte[] bytes = File.ReadAllBytes(arguments[0]);
        int count = arguments.Count > 1 ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : 50000;
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        (float X, float Y, float Z) sun = level.Sun is { } light ? (light.Normal.X, light.Normal.Y, light.Normal.Z) : (0f, 0f, -1f);
        Lcg random = new();
        float[] sizes = [0f, 2f, 16f];
        int traces = 0;
        int differ = 0;
        int terrainWon = 0;

        for (int index = 0; index < count; index++)
        {
            (float X, float Y, float Z) from = (Coordinate(random, 4096f), Coordinate(random, 4096f), Coordinate(random, 1024f));
            float reach = index % 2 == 0 ? 512f : 8192f;
            (float X, float Y, float Z) to = index % 4 == 3
                ? (from.X - (sun.X * 57016.32f), from.Y - (sun.Y * 57016.32f), from.Z - (sun.Z * 57016.32f))
                : (from.X + Coordinate(random, reach), from.Y + Coordinate(random, reach), from.Z + Coordinate(random, reach));
            float size = sizes[index % sizes.Length];
            int mask = index % 4 == 3 ? 0x4081 : BspLeafTree.MaskSolid;

            BspTrace bounded = level.TraceBrushOnly(from, to, size, mask);
            BspTrace whole = Whole(level, from, to, (size, size, size), mask);

            traces++;
            differ += bounded == whole ? 0 : 1;
            terrainWon += whole.DisplacementTexdata >= 0 && whole.Fraction < 1f ? 1 : 0;
        }

        output.WriteLine($"{traces} traces, {differ} differ, terrain won {terrainWon}");
    }

    /// <summary>`TraceBrushOnly`'s compose with the terrain swept over the whole ray, as before the bound.</summary>
    private static BspTrace Whole(
        MapLevel level, (float X, float Y, float Z) from, (float X, float Y, float Z) to, (float X, float Y, float Z) extents, int mask)
    {
        BspTrace brushes = level.Leaves is { } tree
            ? tree.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, 0, mask)
            : new BspTrace(1f, -1, default, false);

        (float terrain, int texdata, bool second, (float X, float Y, float Z) normal, float distance) =
            level.Displacements.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, extents, mask);

        return terrain < brushes.Fraction
            ? new BspTrace(terrain, -1, normal, false, distance, DisplacementTexdata: texdata, SurfaceProp2: second)
            : brushes;
    }

    private static float Coordinate(Lcg random, float half) => (random.Next() * 2f * half) - half;

    /// <summary>A fixed sequence, so a difference names a reproducible ray; not randomness anyone relies on.</summary>
    private sealed class Lcg
    {
        private uint _state = 594;

        public float Next()
        {
            _state = (_state * 1664525u) + 1013904223u;
            return (_state >> 8) / 16777216f;
        }
    }
}
