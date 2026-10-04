using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The beams a moment holds, as the timeline hands them to the viewer, each with a camera that can see it.
/// </summary>
/// <remarks>
/// **The camera comes from the data** (`docs/memory/point-the-camera-from-the-data.md`): two blind captures of a
/// <c>cp_process</c> spotlight landed against walls before this existed. For each beam it walks a ring of stand-off
/// points around the shaft's middle and keeps the first from which the map's own trace reaches the middle unblocked —
/// the viewer's <c>LoadedMap</c>, so the trace is the one the halo gate asks.
///
/// <code>
///   effect-entities demostf-cp_process_f12-2026-08-08 cp_process_f12 48314
/// </code>
/// </remarks>
public sealed class EffectEntityProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "effect-entities";

    /// <inheritdoc/>
    public string Summary =>
        "beams at a tick as the viewer receives them, each with a TF2VIEW_CAMERA that sees it: " +
        "effect-entities <demo> <map> [tick] [n]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("Usage: effect-entities <demo> <map> [tick] [n]");
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (DemoCorpus.Find(arguments[0], output) is not { } path ||
            locator.FindGameFolder() is not { } folder ||
            locator.Find(arguments[1]) is not { } mapPath)
        {
            output.WriteLine("Demo, game or map not found.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        LoadedMap map = LoadedMap.Read(File.ReadAllBytes(mapPath), game, timeline, 256, NullLoggerFactory.Instance);

        double tick = arguments.Count > 2
            ? double.Parse(arguments[2], CultureInfo.InvariantCulture)
            : timeline.Frames[timeline.Frames.Count / 2].Tick;
        int shown = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 5;

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<SceneProp> beams = [.. props.Where(prop => prop.Pose.Beam is not null)];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} tick {tick:0}: {props.Count} props, {beams.Count} beams; " +
            $"{map.Assets?.SpriteMaterials.Count ?? 0} sprite materials loaded"));

        foreach (SceneProp prop in beams.Take(shown))
        {
            SceneBeam beam = prop.Pose.Beam!;
            (float ex, float ey, float ez) = beam.EndPosition;
            (float X, float Y, float Z) middle = (
                (prop.Pose.X + ex) / 2f, (prop.Pose.Y + ey) / 2f, (prop.Pose.Z + ez) / 2f);

            string camera = CameraFor(map, middle) ?? "no clear stand-off within 600 units";

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  entity {prop.EntityIndex} type {beam.Type} flags 0x{beam.Flags:x} '{prop.ModelPath}' halo '{beam.HaloPath}' " +
                $"({prop.Pose.X:0},{prop.Pose.Y:0},{prop.Pose.Z:0}) -> ({ex:0},{ey:0},{ez:0}) width {beam.Width} " +
                $"alpha {prop.Pose.RenderAlpha} mode {prop.Pose.RenderMode} loaded " +
                $"{map.Assets?.SpriteMaterials.ContainsKey(prop.ModelPath) == true}; {camera}"));
        }
    }

    /// <summary>The first stand-off on a ring around a point from which the map's trace reaches it, as a camera.</summary>
    private static string? CameraFor(LoadedMap map, (float X, float Y, float Z) target)
    {
        if (map.Level is not { } level)
        {
            return null;
        }

        foreach (float distance in (float[])[200f, 350f, 500f, 120f])
        {
            for (int step = 0; step < 16; step++)
            {
                float angle = step * MathF.PI / 8f;
                (float X, float Y, float Z) from = (
                    target.X + (MathF.Cos(angle) * distance), target.Y + (MathF.Sin(angle) * distance), target.Z);

                // Clear both ways: a point inside a solid traces out of it unobstructed in one direction.
                if (level.Trace(from, target, 0f).Fraction < 1f || level.Trace(target, from, 0f).Fraction < 1f)
                {
                    continue;
                }

                float yaw = float.RadiansToDegrees(MathF.Atan2(target.Y - from.Y, target.X - from.X));

                return string.Create(
                    CultureInfo.InvariantCulture, $"TF2VIEW_CAMERA=\"{from.X:0} {from.Y:0} {from.Z:0} 0 {yaw:0}\"");
            }
        }

        return null;
    }
}
