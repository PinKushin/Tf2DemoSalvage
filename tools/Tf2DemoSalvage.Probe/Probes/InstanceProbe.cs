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
/// Where the renderer actually puts a model: the instance MATRIX, not the lighting origin.
/// </summary>
/// <remarks>
/// **Written because the log line everyone had been reading was the wrong number** (B241). The
/// viewer prints `door_grate003_top at (-0.1, -0, 0) reflects cubemap 19 of 45`, and that "at" is
/// the ILLUMINATION point — taken from the prop's local pose to choose a cubemap. It would read
/// (0,0,0) for a parented prop whether the placement worked or not, and three rounds of this bug
/// were argued from it.
///
/// This builds the real <see cref="EntityModelSet"/> against the real map and reports
/// <c>ModelInstance.Matrix</c>'s translation, which is what the shader uses.
///
/// <code>
///   instance tf2-2026-pub-pov-clean 870 grate
/// </code>
/// </remarks>
public sealed class InstanceProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "instance";

    /// <inheritdoc/>
    public string Summary =>
        "where the renderer puts a model: instance <demo> <tick> <model substring>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 3)
        {
            output.WriteLine("instance <demo> <tick> <model substring>");
            return;
        }

        string? demo = DemoCorpus.Find(arguments[0], output);
        if (demo is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int tick = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        string filter = arguments[2];

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(demo));

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        // The map from the demo's HEADER, which is where a demo names it — the timeline carries the
        // CRC for identity (a demo names a map VERSION) but not the name.
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(File.ReadAllBytes(demo)).MapName;

        if (mapName.Length == 0
            || locator.Find(mapName) is not { } mapPath
            || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        EntityModelSet models = new() { Geometry = assets.Geometry };

        models.Add(props, assets.Geometry);

        List<ModelInstance> instances = [];
        models.Instances(props, instances, seconds: tick * timeline.IntervalPerTick);

        output.WriteLine(
            $"{Path.GetFileName(demo)} on {mapName} tick "
            + $"{tick.ToString(CultureInfo.InvariantCulture)}: "
            + $"{props.Count.ToString(CultureInfo.InvariantCulture)} props, "
            + $"{instances.Count.ToString(CultureInfo.InvariantCulture)} instances");

        foreach (ModelInstance instance in instances
            .Where(instance => instance.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            // Translation lives in the last row of the shader's row-vector matrix.
            output.WriteLine(
                $"  at ({instance.Matrix[12]:0.#} {instance.Matrix[13]:0.#} "
                + $"{instance.Matrix[14]:0.#})  {instance.ModelPath}");
        }
    }
}
