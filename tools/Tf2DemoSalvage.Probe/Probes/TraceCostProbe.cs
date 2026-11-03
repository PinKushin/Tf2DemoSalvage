using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Where the light visibility trace's map-load cost goes: brush tree against displacement sweep (B667).</summary>
/// <remarks>
/// Builds every static prop's static state (<see cref="LevelLighting.StaticPropLightingAt"/>) twice, untraced and traced,
/// reading the production counter <see cref="LevelLighting.StaticStateBuildTicks"/>; the traced run's delegate is
/// `LevelLighting.From`'s own (`TraceBrushOnly`, `MASK_OPAQUE`, a line), wrapped to record each ray and its time. Then it
/// replays the recorded rays split into the brush tree and the displacement sweep bounded by the brush fraction, exactly
/// as `TraceBrushOnly` composes them. **The control**, printed with its gap and flagged MISMATCH past 25%, is that the
/// wrapped total comes to the counter's delta; the replayed whole beside its two parts is printed for the reader to compare.
/// Last, the triangles the sweep actually tested, counted inside it (`SweepCounts`), beside the travel-box census
/// from before the group cull.
/// <code>
///   trace-cost &lt;map&gt;
/// </code>
/// </remarks>
public sealed class TraceCostProbe : IProbe
{
    private const int MaskOpaque = 0x4081;

    /// <inheritdoc/>
    public string Name => "trace-cost";

    /// <inheritdoc/>
    public string Summary => "static states' light visibility traces timed and split into brush tree vs displacement sweep: trace-cost <map>";

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

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map named '{arguments[0]}', or no game folder.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        MapAssets assets = MapAssets.Load(bytes, GameArchives.Open(folder), 64, mapName: path);
        EntityModelSet models = new() { Geometry = assets.Geometry };
        List<(float X, float Y, float Z)> points = [.. assets.StaticModels.Select(prop => models.IlluminationPoint(prop, prop.Pose)).Distinct()];

        List<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> rays = [];
        long wrapped = 0;

        float Traced((float X, float Y, float Z) from, (float X, float Y, float Z) to)
        {
            long at = Stopwatch.GetTimestamp();
            float fraction = level.TraceBrushOnly(from, to, 0f, MaskOpaque).Fraction;
            wrapped += Stopwatch.GetTimestamp() - at;
            rays.Add((from, to));
            return fraction;
        }

        // Warm the JIT on both routes before either is timed.
        Build(level, points.Take(20), null);
        Build(level, points.Take(20), (from, to) => level.TraceBrushOnly(from, to, 0f, MaskOpaque).Fraction);

        (int untracedCount, long untraced) = Build(level, points, null);
        (int tracedCount, long traced) = Build(level, points, Traced);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{points.Count} points; static states untraced {untracedCount} {Ms(untraced):0.0} ms, traced {tracedCount} {Ms(traced):0.0} ms, " +
            $"delta {Ms(traced - untraced):0.0} ms; {rays.Count} traces inside it, {Ms(wrapped):0.0} ms, {Ms(wrapped) * 1000d / Math.Max(1, rays.Count):0.0} us each"));

        // The control: the wrapped traces must account for the counter's delta, or the split below measures something else.
        double gap = Ms(wrapped) - Ms(traced - untraced);
        bool agrees = Math.Abs(gap) <= ControlTolerance * Ms(traced - untraced);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"control: wrapped total minus counter delta {gap:+0.0;-0.0} ms, {(agrees ? "agrees" : "MISMATCH")} within {ControlTolerance:P0}"));

        // The production build, computed ambient and all, as the viewer's "static-prop lighting at load" counter reads it.
        LevelLighting production = LevelLighting.From(level, NullLogger.Instance);

        foreach ((float x, float y, float z) in points)
        {
            _ = production.StaticPropLightingAt(x, y, z);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"LevelLighting.From: static states {production.StaticStateBuilds} {Ms(production.StaticStateBuildTicks):0.0} ms"));

        BspLeafTree? tree = level.Leaves;
        long brushes = long.MaxValue;
        long terrain = long.MaxValue;
        long whole = long.MaxValue;
        int terrainWon = 0;
        ulong checksum = 0;

        for (int pass = 0; pass < 3; pass++)
        {
            long brushTicks = 0;
            long terrainTicks = 0;
            terrainWon = 0;

            foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
            {
                long at = Stopwatch.GetTimestamp();
                BspTrace brush = tree is not null
                    ? tree.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), 0, MaskOpaque)
                    : new BspTrace(1f, -1, default, false);
                long mid = Stopwatch.GetTimestamp();
                (float hit, _, _, _, _) = level.Displacements.SweepSurface(
                    from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), MaskOpaque, brush.Fraction);
                long end = Stopwatch.GetTimestamp();

                brushTicks += mid - at;
                terrainTicks += end - mid;
                terrainWon += hit < brush.Fraction ? 1 : 0;
            }

            long wholeAt = Stopwatch.GetTimestamp();

            foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
            {
                _ = level.TraceBrushOnly(from, to, 0f, MaskOpaque);
            }

            long wholeTicks = Stopwatch.GetTimestamp() - wholeAt;

            // All thirteen values of `BspTrace` (the normal is three), FNV over the bits: `HashCode` is seeded per process, so
            // it would not compare across two runs, one with a change and one without.
            ulong hash = 14695981039346656037ul;

            foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
            {
                BspTrace trace = level.TraceBrushOnly(from, to, 0f, MaskOpaque);

                foreach (int bits in (ReadOnlySpan<int>)[
                    BitConverter.SingleToInt32Bits(trace.Fraction), trace.Texinfo, BitConverter.SingleToInt32Bits(trace.Normal.X),
                    BitConverter.SingleToInt32Bits(trace.Normal.Y), BitConverter.SingleToInt32Bits(trace.Normal.Z),
                    trace.AllSolid ? 1 : 0, BitConverter.SingleToInt32Bits(trace.Distance), trace.DisplacementTexdata,
                    trace.SurfaceProp2 ? 1 : 0, trace.StudioSurfaceProp, trace.BrushEntity, trace.StaticProp, trace.StartSolid ? 1 : 0])
                {
                    hash = (hash ^ (uint)bits) * 1099511628211ul;
                }
            }

            checksum = hash;
            brushes = Math.Min(brushes, brushTicks);
            terrain = Math.Min(terrain, terrainTicks);
            whole = Math.Min(whole, wholeTicks);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"replay of {rays.Count} rays (best of 3): TraceBrushOnly {Ms(whole):0.0} ms; brush tree {Ms(brushes):0.0} ms, " +
            $"displacement sweep {Ms(terrain):0.0} ms; terrain won {terrainWon}; {level.Displacements.Count} displacements, " +
            $"{level.Displacements.TriangleCount} triangles; checksum of every trace {checksum:x16}"));

        (long overlapped, long candidates) = Census(level, rays);
        double perRay = Math.Max(1, rays.Count);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"before the group cull (travel-box overlap, recomputed here): {overlapped / perRay:0.00} displacements a ray, {candidates / perRay:0.0} triangles a ray"));

        // What the sweep itself did, carried out of it through its counting seam.
        SweepCounts counts = default;

        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
        {
            float bound = tree?.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), 0, MaskOpaque).Fraction ?? 1f;
            _ = level.Displacements.SweepSurface(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), MaskOpaque, bound, ref counts);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"swept (counted in the sweep): {counts.TrianglesSwept / perRay:0.0} triangles a ray; groups culled {counts.GroupsCulled / perRay:0.0}, swept {counts.GroupsSwept / perRay:0.0} a ray"));
    }

    /// <summary>How far the wrapped trace total may stand from the counter's delta before the control calls a mismatch.</summary>
    private const double ControlTolerance = 0.25;

    /// <summary>
    /// How much of the terrain a ray's travel box (bounded by the brush fraction, as `SweepSurface` builds it) overlaps:
    /// displacements and their triangles, each displacement's box taken from the map's own triangles.
    /// </summary>
    private static (long Displacements, long Triangles) Census(
        MapLevel level, List<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> rays)
    {
        if (level.Terrain is not { } terrainLumps)
        {
            return (0, 0);
        }

        List<(float[] Low, float[] High, int Triangles)> boxes = [];

        foreach (BspSurface surface in level.Surfaces.Where(each => each.IsDisplacement))
        {
            IReadOnlyList<SurfaceVertex> corners = terrainLumps.ReadTriangles(surface);

            if (corners.Count >= 3)
            {
                boxes.Add((
                    [corners.Min(c => c.X), corners.Min(c => c.Y), corners.Min(c => c.Z)],
                    [corners.Max(c => c.X), corners.Max(c => c.Y), corners.Max(c => c.Z)],
                    corners.Count / 3));
            }
        }

        long displacements = 0;
        long triangles = 0;

        foreach (((float X, float Y, float Z) from, (float X, float Y, float Z) to) in rays)
        {
            float limit = level.Leaves?.Trace(from.X, from.Y, from.Z, to.X, to.Y, to.Z, (0f, 0f, 0f), 0, MaskOpaque).Fraction ?? 1f;
            float pad = limit < 1f ? 1f : 0f;
            float[] start = [from.X, from.Y, from.Z];
            float[] end = [from.X + ((to.X - from.X) * limit), from.Y + ((to.Y - from.Y) * limit), from.Z + ((to.Z - from.Z) * limit)];

            foreach ((float[] low, float[] high, int count) in boxes)
            {
                bool overlaps = true;

                for (int axis = 0; axis < 3 && overlaps; axis++)
                {
                    overlaps = low[axis] <= MathF.Max(start[axis], end[axis]) + pad && high[axis] >= MathF.Min(start[axis], end[axis]) - pad;
                }

                if (overlaps)
                {
                    displacements++;
                    triangles += count;
                }
            }
        }

        return (displacements, triangles);
    }

    private static (int Count, long Ticks) Build(
        MapLevel level, IEnumerable<(float X, float Y, float Z)> points, Func<(float X, float Y, float Z), (float X, float Y, float Z), float>? trace)
    {
        LevelLighting lighting = new(
            level.Leaves, level.Ambient, level.WorldLights, level.Sun, NullLogger.Instance, level.StrikesSky, null, level.Visibility, null, trace);

        foreach ((float x, float y, float z) in points)
        {
            _ = lighting.StaticPropLightingAt(x, y, z);
        }

        return (lighting.StaticStateBuilds, lighting.StaticStateBuildTicks);
    }

    private static double Ms(long ticks) => ticks * 1000d / Stopwatch.Frequency;
}
