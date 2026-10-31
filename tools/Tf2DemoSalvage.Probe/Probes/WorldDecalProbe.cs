using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>The decal temp entities a demo carries in a tick window, by name, with a camera that frames each.</summary>
/// <remarks>
/// **Through `DemoTimeline.Decals`, the feed the viewer's `DecalReplay` shoots** (B243): the origin is the event's own
/// `m_vecOrigin`, the name the <c>decalprecache</c> entry it indexes. The camera is placed straight up from the origin by
/// the given height and pulled back along a fixed yaw, looking at the origin, for decals on a floor.
/// </remarks>
public sealed class WorldDecalProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "world-decals";

    /// <inheritdoc/>
    public string Summary =>
        "decal temp entities in a tick window, by name, with a camera framing each: world-decals <demo> <from> <to> [name substring] [height=120] [back=80] [yaw=0]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3)
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        string filter = arguments.Count > 3 ? arguments[3] : string.Empty;

        if (!int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int from) ||
            !int.TryParse(arguments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int to) ||
            !TryFloat(arguments, 4, 120f, out float height) ||
            !TryFloat(arguments, 5, 80f, out float back) ||
            !TryFloat(arguments, 6, 0f, out float yaw))
        {
            output.WriteLine("from and to are ticks, height, back and yaw are numbers. Usage: " + Summary);
            return;
        }

        if (from > to)
        {
            output.WriteLine($"from {from} is after to {to}. Usage: " + Summary);
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: " + Summary);
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));
        int shown = 0;

        // Control: the whole feed's size, so an empty window is told apart from an empty feed.
        output.WriteLine($"{timeline.Decals.All.Count} decal events in the demo");

        foreach (SceneDecal decal in timeline.Decals.All)
        {
            if (decal.Tick < from || decal.Tick > to)
            {
                continue;
            }

            string name = timeline.Decals.Names.Name(decal.Index) ?? "?";

            if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            shown++;

            float radians = yaw * MathF.PI / 180f;
            float x = decal.Origin.X - (MathF.Cos(radians) * back);
            float y = decal.Origin.Y - (MathF.Sin(radians) * back);
            float z = decal.Origin.Z + height;
            float pitch = MathF.Atan2(height, back) * 180f / MathF.PI;

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"tick {decal.Tick} {decal.Kind} '{name}' (index {decal.Index}) entity {decal.Entity} hitbox {decal.Hitbox} at ({decal.Origin.X:0.0} {decal.Origin.Y:0.0} {decal.Origin.Z:0.0})  TF2VIEW_CAMERA=\"{x:0} {y:0} {z:0} {pitch:0} {yaw:0}\""));
        }

        output.WriteLine($"{shown} shown");
    }

    private static bool TryFloat(IReadOnlyList<string> arguments, int index, float otherwise, out float value)
    {
        if (arguments.Count <= index)
        {
            value = otherwise;
            return true;
        }

        return float.TryParse(arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
    }
}
