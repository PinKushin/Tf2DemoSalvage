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
            // **The matrix AND the bones, because for a GPU-posed model the bones are what
            // place it.** A skinned model's vertices are transformed by bone matrices in the vertex
            // shader; the instance matrix can be perfectly right while the model draws somewhere
            // else entirely. Reporting only one of the two is how B241 was declared fixed while the
            // gates were still empty.
            // **The bone's ROTATION as well as its translation.** A matrix3x4 whose rotation is all
            // zeros still carries a correct-looking position, and every vertex it skins collapses
            // onto that one point — a model that is placed, bounded, batched, textured and
            // invisible. Reporting only the translation cannot tell that from a working bone.
            string bones = instance.Bones is { Count: > 0 } skeleton
                ? $"bone0 pos ({skeleton[0][3]:0.#} {skeleton[0][7]:0.#} {skeleton[0][11]:0.#}) "
                  + $"diag ({skeleton[0][0]:0.###} {skeleton[0][5]:0.###} {skeleton[0][10]:0.###}) "
                  + $"of {skeleton.Count.ToString(CultureInfo.InvariantCulture)}"
                : "no bones (baked)";

            // Translation lives in the last row of the shader's row-vector matrix; a bone is
            // Valve's matrix3x4_t with translation in column 3, so indices 3, 7 and 11.
            // **And the WORLD BOUNDS, which is what the frustum culls on.** Everything else can be
            // right while these sit at the map origin, and a box the camera cannot see removes the
            // model with no other symptom at all.
            (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) =
                instance.WorldBounds;

            output.WriteLine(
                $"  matrix ({instance.Matrix[12]:0.#} {instance.Matrix[13]:0.#} "
                + $"{instance.Matrix[14]:0.#})  {bones}  "
                + $"bounds ({minX:0.#} {minY:0.#} {minZ:0.#})-({maxX:0.#} {maxY:0.#} {maxZ:0.#})  "
                + instance.ModelPath);
        }
    }
}
