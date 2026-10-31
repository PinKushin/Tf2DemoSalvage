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

        if (arguments.Count < 3 || DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine("Usage: world-decals <demo> <from> <to> [name substring] [height] [back] [yaw]");
            return;
        }

        int from = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        int to = int.Parse(arguments[2], CultureInfo.InvariantCulture);
        string filter = arguments.Count > 3 ? arguments[3] : string.Empty;
        float height = arguments.Count > 4 ? float.Parse(arguments[4], CultureInfo.InvariantCulture) : 120f;
        float back = arguments.Count > 5 ? float.Parse(arguments[5], CultureInfo.InvariantCulture) : 80f;
        float yaw = arguments.Count > 6 ? float.Parse(arguments[6], CultureInfo.InvariantCulture) : 0f;

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
}
