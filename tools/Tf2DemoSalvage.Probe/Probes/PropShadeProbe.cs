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
    /// <summary>`MASK_OPAQUE`, as the light cache's trace passes it (`engine.dll` `0x1801b9570`).</summary>
    private const int LightTraceMask = 0x4081;

    private static readonly string[] FogKeys =
        ["scale", "fogenable", "fogblend", "fogcolor", "fogcolor2", "fogdir", "fogstart", "fogend", "fogmaxdensity"];

    /// <inheritdoc/>
    public string Name => "prop-shade";

    /// <inheritdoc/>
    public string Summary =>
        "each placement of a static prop model: skin, material, shader, modulation, $nofog and the light its draw is given: prop-shade <map> <model substring> [radius: also list the style-0 lights within it of each lighting origin, with reach, the light cache's visibility trace, what it struck, and each light's share of the four faces] [x y z: list them at this point instead]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        float? radius = null;

        if (arguments.Count >= 3)
        {
            if (!float.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed) || parsed < 0f)
            {
                output.WriteLine($"'{arguments[2]}' is not a radius. {Summary}");
                return;
            }

            radius = parsed;
        }

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
        LevelLighting lighting = LevelLighting.From(level, NullLogger.Instance, pak.AheadOf(archives.Read));

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

            // The six faces as the draw holds them, carried on the instance rather than asked for again.
            output.WriteLine(
                $"  draw is given: baked colours {colours}; cube {(instance.Light is { } cube ? $"+x {cube.PositiveX} -x {cube.NegativeX} +y {cube.PositiveY} -y {cube.NegativeY} +z {cube.PositiveZ} -z {cube.NegativeZ}" : "none")}; " +
                $"sun {(instance.Sun is null ? "none" : "yes")}; {instance.Locals?.Count ?? 0} lamps");

            // The lighting origin the draw was lit at, through the set's own route.
            (float X, float Y, float Z) origin = models.IlluminationPoint(prop, prop.Pose);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  lit at ({origin.X:0.0} {origin.Y:0.0} {origin.Z:0.0}), angles ({prop.Pose.Pitch:0.#} {prop.Pose.Yaw:0.#} {prop.Pose.Roll:0.#}), lump lighting origin {prop.LightingOrigin is not null}"));

            // A question about another point: `prop-shade <map> <model> <radius> <x> <y> <z>` lists the lights there instead.
            if (arguments.Count >= 6)
            {
                origin = (float.Parse(arguments[3], CultureInfo.InvariantCulture), float.Parse(arguments[4], CultureInfo.InvariantCulture), float.Parse(arguments[5], CultureInfo.InvariantCulture));
            }

            if (radius is { } within)
            {
                WriteNearbyLights(output, level, lighting, origin, within);
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
                    $"attenuation {lamp.Constant:0.###}/{lamp.Linear:0.###}/{lamp.Quadratic:0.###} {kind}, {Seen(lighting, origin, level.WorldLights.First(light => light.Origin == (lamp.X, lamp.Y, lamp.Z)))}"));
            }
        }
    }

    /// <summary>
    /// The production visibility trace from the lighting origin to a light (<see cref="LevelLighting.Occlusion"/>, B666):
    /// how far short of it the ray stops, whether the PVS lets it through, and whether the light cache keeps it — or, loudly, that no
    /// trace ran.
    /// </summary>
    private static string Seen(LevelLighting lighting, (float X, float Y, float Z) from, BspWorldLight light) =>
        lighting.Occlusion(light, from.X, from.Y, from.Z) is { } traced
            ? string.Create(CultureInfo.InvariantCulture, $"blocked {traced.Blocked:0.#}, in pvs {traced.InPvs}, trace keeps {traced.Kept}")
            : "NOT TRACED: no visibility trace is wired, so whether the light is behind a wall is unknown";

    /// <summary>
    /// Every style-0 world light within a radius of the draw's lighting origin, with whether the production
    /// <see cref="LocalLights.Reaches"/> (the point's cell against the radius and cone, and `r_worldlightmin`) admits it there and whether the visibility trace keeps
    /// it: the candidates the four lamps were picked from, so a lamp that reaches and was not picked shows beside the four
    /// that were.
    /// </summary>
    private static void WriteNearbyLights(
        TextWriter output, MapLevel level, LevelLighting lighting, (float X, float Y, float Z) origin, float radius)
    {
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"    lighting origin ({origin.X:0.#} {origin.Y:0.#} {origin.Z:0.#}); style-0 lights within {radius:0}:"));

        float keptNegativeY = 0f;
        float keptPositiveY = 0f;
        float keptPositiveZ = 0f;
        float keptNegativeZ = 0f;

        foreach ((BspWorldLight light, float distance, float strength) in level.WorldLights
                     .Select(light => (Light: light, Distance: Distance(light, origin), Strength: LocalLights.StrengthAt(light, origin.X, origin.Y, origin.Z)))
                     .Where(entry => entry.Light.Style == 0 && entry.Distance <= radius)
                     .OrderByDescending(entry => entry.Strength))
        {
            // What this light's fold adds to each face, from the production fold (`FUN_1801b5db0`): carried, not recomputed (B243).
            AmbientCube fold = LocalLights.Fold(default, light, origin.X, origin.Y, origin.Z);
            bool reaches = LocalLights.Reaches(light, origin.X, origin.Y, origin.Z);
            bool kept = reaches && lighting.Occlusion(light, origin.X, origin.Y, origin.Z) is { Kept: true };

            if (kept)
            {
                keptNegativeY += fold.NegativeY.Green;
                keptPositiveY += fold.PositiveY.Green;
                keptPositiveZ += fold.PositiveZ.Green;
                keptNegativeZ += fold.NegativeZ.Green;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"      {light.Kind} at ({light.Origin.X:0} {light.Origin.Y:0} {light.Origin.Z:0}) {distance:0} away, normal {light.Normal}, " +
                $"colour ({light.Intensity.Red / MathF.Max(light.Intensity.Green, 1e-6f):0.00} 1 {light.Intensity.Blue / MathF.Max(light.Intensity.Green, 1e-6f):0.00}), strength here {strength:0.####} -y {fold.NegativeY.Green:0.####} +y {fold.PositiveY.Green:0.####} +z {fold.PositiveZ.Green:0.####} -z {fold.NegativeZ.Green:0.####}, " +
                $"reaches {reaches}, {Seen(lighting, origin, light)}{Struck(level, origin, light)}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    every kept light folded:-y {keptNegativeY:0.####} +y {keptPositiveY:0.####} +z {keptPositiveZ:0.####} -z {keptNegativeZ:0.####}"));
    }

    /// <summary>What the production trace struck on the way to a light: where, on which material, and the contents of the leaf just inside it.</summary>
    private static string Struck(MapLevel level, (float X, float Y, float Z) from, BspWorldLight light)
    {
        BspTrace hit = level.TraceBrushOnly(from, light.Origin, 0f, LightTraceMask);

        if (hit.Fraction >= 1f)
        {
            return string.Empty;
        }

        float length = Distance(light, from);
        float past = hit.Fraction + (0.5f / MathF.Max(length, 1f));
        (float X, float Y, float Z) inside = (
            from.X + ((light.Origin.X - from.X) * past),
            from.Y + ((light.Origin.Y - from.Y) * past),
            from.Z + ((light.Origin.Z - from.Z) * past));

        string surface = hit.DisplacementTexdata >= 0 ? "terrain" : "no surface";

        if (hit.Texinfo >= 0 && hit.Texinfo < level.Texinfo.Count)
        {
            int texdata = level.Texinfo[hit.Texinfo].Texdata;

            surface = string.Create(
                CultureInfo.InvariantCulture,
                $"'{(texdata >= 0 && texdata < level.Materials.Count ? level.Materials[texdata].Name : "?")}' flags 0x{level.Texinfo[hit.Texinfo].Flags:X}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $", struck at ({from.X + ((light.Origin.X - from.X) * hit.Fraction):0} {from.Y + ((light.Origin.Y - from.Y) * hit.Fraction):0} {from.Z + ((light.Origin.Z - from.Z) * hit.Fraction):0}) {surface}, leaf inside 0x{level.Leaves?.ContentsAt(inside.X, inside.Y, inside.Z):X}");
    }

    private static float Distance(BspWorldLight light, (float X, float Y, float Z) point)
    {
        float dx = light.Origin.X - point.X;
        float dy = light.Origin.Y - point.Y;
        float dz = light.Origin.Z - point.Z;

        return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
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
