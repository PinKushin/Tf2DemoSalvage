using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What each entity's pose parameters are set to at a tick, and where the values came from.
/// </summary>
/// <remarks>
/// **Written for B269, where the values were all present, all plausible, and all wrong.** A sentry
/// gun networks <c>aim_pitch</c> and <c>aim_yaw</c>; this project ignored the array and filled every
/// uncomputed parameter with a raw zero, which normalises to the MIDDLE of a symmetric range. So
/// every building drew level and straight ahead and nothing looked broken.
///
/// <code>
///   poseparams tf2-2013-build1729296-stv-cp_foundry 12000
///   poseparams tf2-2013-build1729296-stv-cp_foundry 12000 sentry
/// </code>
///
/// **Both numbers per parameter, deliberately.** The value in force is what the blend received,
/// asked of the model set that posed it (`PoseValuesOf`) rather than recomputed here — and beside
/// it the DENORMALISED angle, which is the only form a person can judge. A normalised 0.5 says
/// nothing; "0 degrees, dead centre" says the value never arrived.
///
/// The two lines it prints per entity are the control for each other: an entity whose wire array is
/// empty and whose values are all mid-range is a player or an entity the send table excluded, and
/// one with a populated array whose values are still mid-range is this defect returning.
/// </remarks>
public sealed class PoseParameterProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "poseparams";

    /// <inheritdoc/>
    public string Summary =>
        "pose parameters in force at a tick: poseparams <demo> [tick] [model substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("poseparams <demo> [tick] [model substring]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        // The map is named in the demo's HEADER, and it is needed because the geometry loader —
        // the production one, which is the only one worth asking — comes off the loaded map.
        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(File.ReadAllBytes(path)).MapName;

        if (mapName.Length == 0
            || locator.Find(mapName) is not { } mapPath
            || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The demo's map or the game could not be found.");
            return;
        }

        int tick = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        string filter = arguments.Count > 2 ? arguments[2] : string.Empty;

        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);

        List<ScenePlayer> players = [];
        timeline.PlayersAt(tick, players);

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        // The production resolution step, for the same reason `props` runs it: a model named only
        // by an item is absent until this happens (B263).
        new WeaponPropModels().Resolve(props, players, game.Weapons.For);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine("The map loaded with no assets.");
            return;
        }

        EntityModelSet models = new() { Geometry = assets.Geometry };

        models.Add(props, assets.Geometry);

        // **`Instances` and not a shortcut**, because that is the call that runs `Simulate`, which
        // is where a pose parameter is chosen. Asking anything else would report a value this
        // project never used.
        List<ModelInstance> instances = [];
        models.Instances(props, instances);

        output.WriteLine(
            $"{Path.GetFileName(path)} tick {tick.ToString(CultureInfo.InvariantCulture)}, "
            + $"{props.Count.ToString(CultureInfo.InvariantCulture)} props, filter '{filter}'");

        int reported = 0;

        foreach (SceneProp prop in props
            .Where(prop => prop.ModelPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(prop => prop.EntityIndex))
        {
            IReadOnlyList<StudioPoseParameter> parameters =
                assets.Geometry(prop.ModelPath)?.Skinned?.PoseParameters ?? [];

            if (parameters.Count == 0)
            {
                continue;
            }

            reported++;

            IReadOnlyList<float> inForce = models.PoseValuesOf(prop.EntityIndex);
            IReadOnlyList<float> sent = prop.Pose.PoseParameters;

            output.WriteLine(
                $"ENTITY {prop.EntityIndex.ToString(CultureInfo.InvariantCulture),5} "
                + $"'{prop.ModelPath}' class '{prop.ClassName}' "
                + $"wire sent {sent.Count.ToString(CultureInfo.InvariantCulture)} of "
                + $"{parameters.Count.ToString(CultureInfo.InvariantCulture)}");

            for (int index = 0; index < parameters.Count; index++)
            {
                StudioPoseParameter parameter = parameters[index];
                float normalised = index < inForce.Count ? inForce[index] : 0f;

                output.WriteLine(
                    $"    {parameter.Name,-14} {normalised:0.###} normalised = "
                    + $"{Denormalised(parameter, normalised):0.##} "
                    + $"(range {parameter.Start:0.##} to {parameter.End:0.##}"
                    + (parameter.Loop != 0f ? ", loops" : string.Empty) + ")");
            }
        }

        output.WriteLine(
            $"{reported.ToString(CultureInfo.InvariantCulture)} entities carry pose parameters.");
    }

    /// <summary>A normalised value put back into the units a person reads.</summary>
    /// <remarks>
    /// The inverse of <c>StudioBlendGrid.Normalize</c>, and the reason this probe is legible: the
    /// difference between "aiming 40 degrees left" and "in the middle of its range because nothing
    /// arrived" is invisible while both read 0.28 and 0.5.
    /// </remarks>
    private static float Denormalised(StudioPoseParameter parameter, float normalised) =>
        parameter.Start + (normalised * (parameter.End - parameter.Start));
}
