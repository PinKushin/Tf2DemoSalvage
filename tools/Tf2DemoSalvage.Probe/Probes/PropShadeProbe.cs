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
        "each placement of a static prop model: skin, material, shader, modulation, $nofog and the light its draw is given: prop-shade <map> <model substring> [x y z: a lighting origin from prop-light, to list the style-0 lights near it]";

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
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance);

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
                    $"$translucent '{vmt?.Value("$translucent") ?? "(unset)"}', $alphatest '{vmt?.Value("$alphatest") ?? "(unset)"}', $vertexcolor '{vmt?.Value("$vertexcolor") ?? "(unset)"}', " +
                    $"$bumpmap '{vmt?.Value("$bumpmap") ?? "(unset)"}', $phong '{vmt?.Value("$phong") ?? "(unset)"}', $envmap '{vmt?.Value("$envmap") ?? "(unset)"}'"));
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

            // The lighting origin, as `prop-light` prints it: given on the command line, since the draw does not carry it.
            if (arguments.Count >= 5)
            {
                WriteNearbyLights(output, level.WorldLights, (
                    float.Parse(arguments[2], CultureInfo.InvariantCulture),
                    float.Parse(arguments[3], CultureInfo.InvariantCulture),
                    float.Parse(arguments[4], CultureInfo.InvariantCulture)));
            }

            foreach (LocalLight lamp in instance.Locals ?? [])
            {
                string kind = lamp.Directional ? "directional" : "point";

                if (lamp.Spot)
                {
                    kind = string.Create(CultureInfo.InvariantCulture, $"spot dir {lamp.Direction} cone {lamp.SpotInner:0.###}..{lamp.SpotOuter:0.###}");
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    lamp at ({lamp.X:0} {lamp.Y:0} {lamp.Z:0}) colour ({lamp.Red:0.###} {lamp.Green:0.###} {lamp.Blue:0.###}) " +
                    $"attenuation {lamp.Constant:0.###}/{lamp.Linear:0.###}/{lamp.Quadratic:0.###} {kind}"));
            }
        }
    }

    /// <summary>
    /// Every style-0 world light within 600 units of the draw's lighting origin, with whether the production
    /// <see cref="LocalLights.Reaches"/> (distance, radius and cone) admits it there: the candidates the four lamps were
    /// picked from, so a lamp that reaches and was not picked is visible beside the four that were.
    /// </summary>
    private static void WriteNearbyLights(TextWriter output, IReadOnlyList<BspWorldLight> lights, (float X, float Y, float Z) origin)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    lighting origin ({origin.X:0.#} {origin.Y:0.#} {origin.Z:0.#}); style-0 lights within 600:"));

        foreach (BspWorldLight light in lights)
        {
            float dx = light.Origin.X - origin.X;
            float dy = light.Origin.Y - origin.Y;
            float dz = light.Origin.Z - origin.Z;
            float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (light.Style != 0 || distance > 600f)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"      {light.Kind} at ({light.Origin.X:0} {light.Origin.Y:0} {light.Origin.Z:0}) {distance:0} away, normal {light.Normal}, " +
                $"intensity ({light.Intensity.Red:0} {light.Intensity.Green:0} {light.Intensity.Blue:0}), reaches {LocalLights.Reaches(light, origin.X, origin.Y, origin.Z)}"));
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
