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

/// <summary>Where a static prop is lit from, and what the sky light's trace from there strikes (B514).</summary>
/// <remarks>
/// The point is the set's own (<see cref="EntityModelSet.IlluminationPoint"/>); the trace is the level's own
/// (<see cref="MapLevel.TraceBrushOnly(ValueTuple{float, float, float}, ValueTuple{float, float, float}, float, int)"/>), run under
/// `MASK_OPAQUE` (the light cache's, `engine.dll` `0x1801b9570`) and under `MASK_SOLID` beside it, because the two
/// differ by exactly the brushes light passes through. **The control is the extra point**: give one in open air and it
/// must strike sky, or the instrument is broken rather than the prop.
/// </remarks>
public sealed class PropLightProbe : IProbe
{
    private const int MaskOpaque = 0x4081;
    private const float SkyTraceLength = 57016.32f;

    /// <inheritdoc/>
    public string Name => "prop-light";

    /// <inheritdoc/>
    public string Summary =>
        "a static prop's lighting point and what the sun's trace from it strikes: prop-light <map> <model substring> [x y z control point]";

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
        MapLevel level = MapLevel.Read(bytes, NullLogger.Instance);
        MapAssets assets = MapAssets.Load(bytes, GameArchives.Open(folder), 64);
        IReadOnlyList<string> materials = [.. BspMaterials.Read(bytes).Select(material => material.Name)];
        IReadOnlyList<BspStaticProp> lump = BspStaticProps.Read(bytes);

        EntityModelSet models = new() { Geometry = assets.Geometry };
        models.Add(assets.StaticModels);

        if (level.Sun is not { } sun)
        {
            output.WriteLine("The map has no sky light.");
            return;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"sun: intensity {sun.Intensity.Red:0.###} {sun.Intensity.Green:0.###} {sun.Intensity.Blue:0.###}, travelling {sun.Normal.X:0.###} {sun.Normal.Y:0.###} {sun.Normal.Z:0.###}"));

        foreach (SceneProp prop in assets.StaticModels.Where(
                     prop => prop.ModelPath.Contains(arguments[1], StringComparison.OrdinalIgnoreCase)))
        {
            int index = prop.EntityIndex - PropModels.FirstStaticPropEntityIndex;
            BspStaticProp placed = lump[index];
            (float X, float Y, float Z) illumination = assets.Geometry(prop.ModelPath)?.Illumination ?? default;
            (float X, float Y, float Z) point = models.IlluminationPoint(prop, prop.Pose);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{prop.ModelPath} #{index}: origin ({placed.X:0.#} {placed.Y:0.#} {placed.Z:0.#}) angles ({prop.Pose.Pitch:0.#} {prop.Pose.Yaw:0.#} {prop.Pose.Roll:0.#}), " +
                $"lump lighting origin {(placed.UsesLightingOrigin ? "USED" : "not used")} ({placed.LightingOrigin.X:0.#} {placed.LightingOrigin.Y:0.#} {placed.LightingOrigin.Z:0.#}), " +
                $"model illumposition ({illumination.X:0.#} {illumination.Y:0.#} {illumination.Z:0.#})"));

            Trace(output, level, materials, sun, "  lit from", point);
        }

        if (arguments.Count >= 5)
        {
            Trace(
                output, level, materials, sun, "control",
                (Number(arguments[2]), Number(arguments[3]), Number(arguments[4])));
        }
    }

    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    private static void Trace(
        TextWriter output, MapLevel level, IReadOnlyList<string> materials, BspWorldLight sun, string label, (float X, float Y, float Z) from)
    {
        (float X, float Y, float Z) to = (
            from.X - (sun.Normal.X * SkyTraceLength), from.Y - (sun.Normal.Y * SkyTraceLength), from.Z - (sun.Normal.Z * SkyTraceLength));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{label} ({from.X:0.#} {from.Y:0.#} {from.Z:0.#}): leaf {level.Leaves?.LeafAt(from.X, from.Y, from.Z)}, contents 0x{level.Leaves?.ContentsAt(from.X, from.Y, from.Z):X}, " +
            $"leaf sky flag {level.Leaves?.SkyboxVisibleFrom(from.X, from.Y, from.Z)}; StrikesSky {level.StrikesSky(from, to)}"));

        foreach ((string name, int mask) in new[] { ("MASK_OPAQUE", MaskOpaque), ("MASK_SOLID", BspLeafTree.MaskSolid) })
        {
            BspTrace hit = level.TraceBrushOnly(from, to, 0f, mask);
            string surface = hit.DisplacementTexdata >= 0 ? $"terrain texdata {hit.DisplacementTexdata}" : "no surface";

            if (hit.Texinfo >= 0 && hit.Texinfo < level.Texinfo.Count)
            {
                int texdata = level.Texinfo[hit.Texinfo].Texdata;
                string material = texdata >= 0 && texdata < materials.Count ? materials[texdata] : "?";

                surface = $"texinfo {hit.Texinfo} flags {level.Texinfo[hit.Texinfo].Flags} '{material}'";
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {name}: fraction {hit.Fraction:0.######} = {hit.Fraction * SkyTraceLength:0.#} units, at ({from.X + ((to.X - from.X) * hit.Fraction):0.#} {from.Y + ((to.Y - from.Y) * hit.Fraction):0.#} {from.Z + ((to.Z - from.Z) * hit.Fraction):0.#}), " +
                $"start solid {hit.StartSolid}, all solid {hit.AllSolid}, {surface}"));
        }
    }
}
