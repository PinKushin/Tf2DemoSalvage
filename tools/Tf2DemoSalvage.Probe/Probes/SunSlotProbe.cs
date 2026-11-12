using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Where on a real map the sun holds one of a model's four slots beside three lamps, and where it loses one (B170).</summary>
/// <remarks>
/// Asks the production light cache (<see cref="LevelLighting.ModelLightingAt"/>, <see cref="LevelLighting.ModelSunAt"/>) at points round
/// every lamp: 64 units each way across and 64 below it, where a model under a lamp stands. A point counts once per cell it lands in.
/// **The control** is the census itself: points with the sun and with lamps at all, which a broken instrument would report as zero.
/// "Seen but lost" is the direct sky test (<see cref="LevelLighting.SunAt"/>) passing where the state holds no sun: before B170's fix
/// each of those drew the sun beside four lamps.
/// </remarks>
public sealed class SunSlotProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sun-slot";

    /// <inheritdoc/>
    public string Summary => "how often the sun takes, or loses, a model's fourth light slot round a map's lamps: sun-slot <map.bsp>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string path in arguments)
        {
            MapLevel level = MapLevel.Read(File.ReadAllBytes(path), NullLogger.Instance);
            LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance, find: null);

            // The ranking as it was before B170's fix: the same level with the sky light out of the ranked list, the sun drawn beside.
            LevelLighting lampsOnly = LevelLighting.From(
                level with { WorldLights = [.. level.WorldLights.Where(static light => light.Kind != WorldLightKind.SkyLight)] },
                NullLogger.Instance,
                find: null);
            HashSet<(int, int, int)> cells = [];
            (int Points, int SunSeen, int SunSlotted, int SunAndThree, int WasFive, int SunLost, int WithLamps) count = default;
            (float X, float Y, float Z)? example = null;

            foreach ((float X, float Y, float Z) origin in level.WorldLights
                         .Where(static light => light.Kind is WorldLightKind.Point or WorldLightKind.Spotlight)
                         .Select(static light => light.Origin))
            {
                foreach ((float dx, float dy) in new[] { (0f, 0f), (64f, 0f), (-64f, 0f), (0f, 64f), (0f, -64f) })
                {
                    (float x, float y, float z) = (origin.X + dx, origin.Y + dy, origin.Z - 64f);

                    if (!cells.Add(((int)MathF.Floor(x / 32f), (int)MathF.Floor(y / 32f), (int)MathF.Floor(z / 32f))))
                    {
                        continue;
                    }

                    lighting.Frame++;
                    lampsOnly.Frame++;

                    PointLighting lit = lighting.ModelLightingAt(x, y, z);
                    bool slotted = lighting.ModelSunAt(x, y, z) is not null;
                    bool seen = lighting.SunAt(x, y, z) is not null;

                    count.Points++;
                    count.WithLamps += lit.Locals.Count > 0 ? 1 : 0;
                    count.SunSeen += seen ? 1 : 0;
                    count.SunSlotted += slotted ? 1 : 0;
                    count.SunLost += seen && !slotted ? 1 : 0;

                    if (slotted && lit.Locals.Count == LocalLights.MaximumLocalLights - 1)
                    {
                        count.SunAndThree++;

                        if (lampsOnly.ModelLightingAt(x, y, z).Locals.Count == LocalLights.MaximumLocalLights)
                        {
                            count.WasFive++;
                            example ??= (x, y, z);
                        }
                    }
                }
            }

            output.WriteLine(
                $"{Path.GetFileNameWithoutExtension(path)}: {count.Points} points round {level.WorldLights.Count} lights; with lamps {count.WithLamps}, " +
                $"sun seen {count.SunSeen}, in a slot {count.SunSlotted}, of which beside three lamps {count.SunAndThree} " +
                $"({count.WasFive} drew four lamps and the sun before), seen but lost its slot {count.SunLost}" +
                (example is { } at ? $"; first that drew five at ({at.X:0} {at.Y:0} {at.Z:0})" : string.Empty));
        }
    }
}
