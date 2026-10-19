using System;
using System.Collections.Generic;
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

/// <summary>What a static prop's draw is handed: its skin's material and the light the model set gives it (B514).</summary>
/// <remarks>
/// For each placement of a model: the skin, the material that skin names as the map's table resolved it, the shader,
/// the modulation (<c>$color</c> times <c>$color2</c>), <c>$nofog</c>, and the lighting of the instance the production
/// <see cref="EntityModelSet"/> emits — baked colours, cube, sun, lamps. The sky camera's own fog keys are printed raw
/// beside it. **The control is a model whose answer is known**: a `VertexLitGeneric` prop on the same map must show
/// colours or a cube, or the instrument is reading nothing.
/// </remarks>
public sealed class PropShadeProbe : IProbe
{
    private static readonly string[] FogKeys =
        ["scale", "fogenable", "fogblend", "fogcolor", "fogcolor2", "fogdir", "fogstart", "fogend", "fogmaxdensity"];

    /// <inheritdoc/>
    public string Name => "prop-shade";

    /// <inheritdoc/>
    public string Summary =>
        "each placement of a static prop model: skin, material, shader, modulation, $nofog and the light its draw is given: prop-shade <map> <model substring>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
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
        GameArchives archives = GameArchives.Open(folder);
        PakFile pak = PakFile.ReadFrom(bytes);
        MapAssets assets = MapAssets.Load(bytes, archives, 64, mapName: path);
        LevelLighting lighting = LevelLighting.From(MapLevel.Read(bytes, NullLogger.Instance), NullLogger.Instance);

        BspEntity? sky = BspEntities.ReadFrom(bytes).FirstOrDefault(
            entity => entity.TryGetValue("classname", out string name) && name == BspEntities.SkyCameraClass);

        output.WriteLine(sky is null
            ? "no sky_camera"
            : "sky_camera: " + string.Join(", ", FogKeys.Select(key => $"{key} '{(sky.TryGetValue(key, out string value) ? value : "(unset)")}'")));

        EntityModelSet models = new()
        {
            Geometry = assets.Geometry,
            StaticPropColours = assets.StaticModelColours,
            StaticPlusDynamicLights = lighting.StaticPlusDynamicLights,
            StaticPropLighting = lighting.StaticPropLightingAt,
            StaticPropFullLighting = lighting.StaticPropFullLightingAt,
        };

        models.Add(assets.StaticModels);

        List<ModelInstance> drawn = [];
        models.Instances(assets.StaticModels, drawn, lighting.ModelLightingAt, lighting.ModelSunAt);

        Dictionary<int, ModelInstance> byEntity = drawn.ToDictionary(instance => instance.EntityIndex);

        foreach (SceneProp prop in assets.StaticModels.Where(
                     prop => prop.ModelPath.Contains(arguments[1], StringComparison.OrdinalIgnoreCase)))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{prop.ModelPath} #{prop.EntityIndex - PropModels.FirstStaticPropEntityIndex} at ({prop.Pose.X:0} {prop.Pose.Y:0} {prop.Pose.Z:0}) skin {prop.Pose.Skin}"));

            if (!byEntity.TryGetValue(prop.EntityIndex, out ModelInstance instance))
            {
                output.WriteLine("  NOT DRAWN by the model set");
                continue;
            }

            // The materials this skin paints its meshes with, as the model's own table swaps them.
            foreach (int material in (instance.SkinSwap?.Values ?? []).Distinct().Where(index => index >= 0 && index < assets.Materials.Count))
            {
                string name = assets.Materials[material].Name;
                VmtMaterial? vmt = (pak.ReadFile($"materials/{name}.vmt") ?? archives.Read($"materials/{name}.vmt")) is { } file
                    ? VmtMaterial.Parse(file)
                    : null;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  material [{material}] {name}: shader '{assets.Shaders[material]}', modulation {vmt?.Modulation}, $nofog '{vmt?.Value("$nofog") ?? "(unset)"}', " +
                    $"$translucent '{vmt?.Value("$translucent") ?? "(unset)"}', $alphatest '{vmt?.Value("$alphatest") ?? "(unset)"}', $vertexcolor '{vmt?.Value("$vertexcolor") ?? "(unset)"}'"));
            }

            float[]? baked = instance.BakedColours;
            string colours = baked is { Length: >= 3 }
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"mean ({Mean(baked, 0):0.000} {Mean(baked, 1):0.000} {Mean(baked, 2):0.000}) over {baked.Length / 3} corners")
                : "none";

            output.WriteLine(
                $"  draw is given: baked colours {colours}; cube {(instance.Light is { } cube ? $"up {cube.PositiveZ}" : "none")}; " +
                $"sun {(instance.Sun is null ? "none" : "yes")}; {instance.Locals?.Count ?? 0} lamps");
        }
    }

    private static float Mean(float[] colours, int channel)
    {
        double sum = 0;

        for (int at = channel; at < colours.Length; at += 3)
        {
            sum += colours[at];
        }

        return (float)(sum / (colours.Length / 3));
    }
}
