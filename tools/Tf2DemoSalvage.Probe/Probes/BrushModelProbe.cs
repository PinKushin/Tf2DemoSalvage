using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a brush model's faces are painted with, and whether any of them would draw.
/// </summary>
/// <remarks>
/// **Written to check a claim rather than to make one.** A `func_respawnroomvisualizer` appearing in
/// the draw list says the ENTITY is there; it says nothing about whether its brushwork produces
/// pixels. A brush entity whose faces all carry a tool material draws nothing at all, and
/// `BrushModels.Build` skips it with the comment *"No geometry is a real answer for a submodel whose
/// faces are all tool textures — a trigger volume is a brush entity too"*.
///
/// The owner was looking at the screen and said there was no wall. That is the instrument that
/// outranks a draw-list listing, and this is the one that can agree or disagree with it in numbers.
///
/// <code>
///   brush-model cp_fulgur 109
/// </code>
/// </remarks>
public sealed class BrushModelProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "brush-model";

    /// <inheritdoc/>
    public string Summary => "a brush model's faces and materials: brush-model <map> <index>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("brush-model <map> <index> — for example: brush-model cp_fulgur 109");
            return;
        }

        string map = arguments[0];
        int index = int.Parse(arguments[1], CultureInfo.InvariantCulture);

        string? path = new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
            .Find(map.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) ? map[..^4] : map);

        if (path is null)
        {
            output.WriteLine($"No map named '{map}'.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);

        IReadOnlyList<BspModel> models = BspModels.Read(bytes);

        if (index < 0 || index >= models.Count)
        {
            output.WriteLine(
                $"*{index.ToString(CultureInfo.InvariantCulture)} is outside the map's "
                + $"{models.Count.ToString(CultureInfo.InvariantCulture)} models.");
            return;
        }

        BspModel model = models[index];
        IReadOnlyList<BspSurface> surfaces = BspSurfaces.Read(bytes);
        string[] materials = BspMaterials.ReadNames(bytes);

        output.WriteLine(
            $"*{index.ToString(CultureInfo.InvariantCulture)}: "
            + $"{model.FaceCount.ToString(CultureInfo.InvariantCulture)} faces from "
            + $"{model.FirstFace.ToString(CultureInfo.InvariantCulture)}, "
            + $"box ({model.Minimum.X:0} {model.Minimum.Y:0} {model.Minimum.Z:0}) "
            + $"to ({model.Maximum.X:0} {model.Maximum.Y:0} {model.Maximum.Z:0})");

        Dictionary<string, int> painted = new(StringComparer.OrdinalIgnoreCase);

        foreach (int material in surfaces
            .Where(surface => surface.FaceIndex >= model.FirstFace
                && surface.FaceIndex < model.FirstFace + model.FaceCount)
            .Select(surface => surface.MaterialIndex))
        {
            string name = material >= 0 && material < materials.Length
                ? materials[material]
                : $"#{material.ToString(CultureInfo.InvariantCulture)}";

            painted[name] = painted.TryGetValue(name, out int seen) ? seen + 1 : 1;
        }

        foreach ((string name, int count) in painted.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"{count,4} faces  '{name}'  "
                + $"{(name.Contains("tools/", StringComparison.OrdinalIgnoreCase)
                    ? "a TOOL material"
                    : "an ordinary material")}");
        }

        if (painted.Count == 0)
        {
            output.WriteLine("no faces at all — nothing to draw");
        }
    }
}
