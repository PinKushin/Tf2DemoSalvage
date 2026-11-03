using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a model draw pays under a live dlight, once the light cache is full (B669).</summary>
/// <remarks>
/// A demo cannot answer this on f12: the only allocator a demo can drive is the local player's Dragon's Fury fireball
/// (`MomentScene.ThinkDynamicLights`), and f12 carries no such projectile. So each static prop's lighting point stands in
/// for a model draw, with `DynamicLights.DragonsFury`'s own light 64 units above it, through the production calls.
/// **Three controls:** the dead-light pass must read no asked draw, the live pass's `DynamicMerges.Merged` must equal the
/// draws (the path ran), and the traces counted inside the live pass must be zero (B666: no dlight is traced).
/// <code>
///   dlight-cost &lt;map&gt;
/// </code>
/// </remarks>
public sealed class DlightCostProbe : IProbe
{
    private const int MaskOpaque = 0x4081;

    /// <inheritdoc/>
    public string Name => "dlight-cost";

    /// <inheritdoc/>
    public string Summary => "a model draw's cost under a live dlight on a full light cache, both routes: dlight-cost <map>";

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

        long traces = 0;

        float Traced((float X, float Y, float Z) from, (float X, float Y, float Z) to)
        {
            traces++;
            return level.TraceBrushOnly(from, to, 0f, MaskOpaque).Fraction;
        }

        DynamicLights dynamic = new();
        LevelLighting lighting = new(
            level.Leaves, level.Ambient, level.WorldLights, level.Sun, NullLogger.Instance, level.StrikesSky, null, level.Visibility, null, Traced)
        {
            Dynamic = dynamic,
        };

        // Fill both routes' caches with nothing alive; no pass below may build an entry.
        Pass(points, (index, point) =>
        {
            _ = lighting.ModelLightingAt(point.X, point.Y, point.Z);
            _ = lighting.StaticPlusDynamicLights(index, point.X, point.Y, point.Z);
        });

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{points.Count} points; {traces} traces filling the caches"));

        // What the live pass's `re-ranked` must reconcile with: the draws the gate lets through, from `CM_Vis`'s two inputs.
        int open = points.Count(point =>
            level.Leaves is { } tree &&
            level.Visibility is { } rows &&
            tree.ClusterAt(point.X, point.Y, point.Z) is >= 0 and var from &&
            tree.ClusterAt(point.X, point.Y, point.Z + 64f) is >= 0 and var cluster &&
            rows.Visible(from, cluster));

        output.WriteLine($"the gate should pass {open} of {points.Count}");

        long tracesFilled = traces;

        for (int pass = 0; pass < 5; pass++)
        {
            (long dead, _) = Pass(points, (_, point) => lighting.ModelLightingAt(point.X, point.Y, point.Z));
            (long _, long asked0, long merged0, long ticks0) = lighting.DynamicMerges;
            (long live, long liveBytes) = Pass(points, (_, point) =>
            {
                DynamicLights.DragonsFury(dynamic, 1, (point.X, point.Y, point.Z + 64f));
                lighting.ModelLightingAt(point.X, point.Y, point.Z);
            });
            (long _, long asked1, long merged1, long ticks1) = lighting.DynamicMerges;
            dynamic.Clear();

            int added = 0;
            PointLighting[] answers = new PointLighting[points.Count];
            (long propDead, _) = Pass(points, (index, point) => answers[index] = lighting.StaticPlusDynamicLights(index, point.X, point.Y, point.Z));
            (long propLive, long propBytes) = Pass(points, (index, point) =>
            {
                DynamicLights.DragonsFury(dynamic, 1, (point.X, point.Y, point.Z + 64f));
                // With no dlight added the route hands back the dead pass's answer, the same list, so a different one is the route having added one.
                added += lighting.StaticPlusDynamicLights(index, point.X, point.Y, point.Z).Equals(answers[index]) ? 0 : 1;
            });
            dynamic.Clear();

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"pass {pass}: model draw dead {Us(dead, points.Count):0.00} us, live {Us(live, points.Count):0.00} us, asked {asked1 - asked0}, re-ranked {merged1 - merged0} " +
                $"of {points.Count}, in the merge {Us(ticks1 - ticks0, points.Count):0.00} us, {liveBytes / points.Count} B; " +
                $"static prop dead {Us(propDead, points.Count):0.00} us, live {Us(propLive, points.Count):0.00} us, {propBytes / points.Count} B, added {added}; " +
                $"traces since the fill {traces - tracesFilled}"));
        }
    }

    private static (long Ticks, long Bytes) Pass(List<(float X, float Y, float Z)> points, Action<int, (float X, float Y, float Z)> draw)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();

        for (int index = 0; index < points.Count; index++)
        {
            draw(index, points[index]);
        }

        long ticks = Stopwatch.GetTimestamp() - started;

        return (ticks, GC.GetAllocatedBytesForCurrentThread() - bytes);
    }

    private static double Us(long ticks, int draws) => ticks * 1000000d / Stopwatch.Frequency / draws;
}
