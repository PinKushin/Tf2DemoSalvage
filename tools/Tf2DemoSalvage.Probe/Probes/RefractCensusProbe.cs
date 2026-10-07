using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every <c>Refract</c> material TF2 ships, and which of the shader's combos each selects (B476).
/// </summary>
/// <remarks>
/// **The denominator for porting <c>refract_ps2x.fxc</c>**: a combo no shipped material selects is not a divergence,
/// one that some do is. Read through <see cref="VmtMaterial"/> like <c>vmt-param</c>, so a parameter inside a
/// conditional block counts exactly when production would see it. The control is the material count itself.
/// </remarks>
public sealed class RefractCensusProbe : IProbe
{
    private static readonly string[] Parameters =
    [
        "$basetexture", "$envmap", "$normalmap2", "$masked", "$fadeoutonsilhouette", "$bluramount",
        "$refracttinttexture", "$vertexcolormodulate", "$bumptransform", "$bumptransform2", "$nowritez", "$nofog",
        "$forcealphawrite", "$model", "$bumpframe", "$alphatest", "$additive",
    ];

    /// <inheritdoc/>
    public string Name => "refract-census";

    /// <inheritdoc/>
    public string Summary => "every shipped Refract material and the combos it selects: refract-census [list|reach]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        bool list = arguments.Count > 0 && arguments[0] == "list";
        int materials = 0;
        int refract = 0;
        Dictionary<string, Dictionary<string, int>> values = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> proxies = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                continue;
            }

            VmtMaterial material;

            try
            {
                material = VmtMaterial.Parse(bytes);
            }
            catch (InvalidDataException failure)
            {
                output.WriteLine($"unreadable {path}: {failure.Message}");
                continue;
            }

            materials++;

            if (!material.Shader.StartsWith("Refract", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            refract++;

            List<string> stated = [];

            foreach (string key in Parameters)
            {
                if (material.Value(key) is { } value)
                {
                    if (!values.TryGetValue(key, out Dictionary<string, int>? counts))
                    {
                        counts = new(StringComparer.OrdinalIgnoreCase);
                        values[key] = counts;
                    }

                    counts[value] = counts.GetValueOrDefault(value) + 1;
                    stated.Add($"{key}={value}");
                }
            }

            foreach (MaterialProxy proxy in material.Proxies)
            {
                // The proxy AND the parameter it writes: a TextureScroll into $bumptransform is the scrolling the
                // model path does not apply (B506), one into anything else is not.
                string key = $"{proxy.Name}->{proxy.Argument("resultVar") ?? proxy.Argument("texturescrollvar") ?? "?"}";
                proxies[key] = proxies.GetValueOrDefault(key) + 1;
                stated.Add($"proxy={key}");
            }

            if (list)
            {
                output.WriteLine($"{path} [{material.Shader}] {string.Join(' ', stated)}");
            }
        }

        output.WriteLine($"{materials} materials read; {refract} use a Refract shader");

        foreach ((string key, Dictionary<string, int> counts) in values.OrderBy(entry => entry.Key))
        {
            output.WriteLine(
                $"  {key}: {counts.Values.Sum()} — " +
                string.Join(", ", counts.OrderByDescending(entry => entry.Value).Take(8).Select(entry => $"{entry.Key} ×{entry.Value}")));
        }

        output.WriteLine("proxies: " + string.Join(", ", proxies.Select(entry => $"{entry.Key} ×{entry.Value}")));

        if (arguments.Count > 0 && arguments[0] == "reach")
        {
            Reach(output, game, folder);
        }
    }

    /// <summary>
    /// The B506 leftovers' reach: which shipped models name one of the 13 refused materials, and which installed
    /// maps place one of those models or a <c>point_camera</c>.
    /// </summary>
    /// <remarks>
    /// A byte search of each <c>.mdl</c> for the material's base name, case-insensitive — a studio model stores its
    /// texture names as plain strings. **The control is the Bazaar lens**, which must be found in its own model.
    /// It does not read map pakfiles.
    /// </remarks>
    private static void Reach(TextWriter output, GameContent game, string folder)
    {
        string[] names =
        [
            "shader5", "shader4", "shader3", "point_camera", "tprings_globe", "stasisshield_sheet", "com_shield001b",
            "com_shield001a", "portalrift_sheet", "frostedglass_01a", "lighthouse_fresnel_light", "xencrystal_sheet",
            "tp_refract", "tank_glass001", "screenwarp", "c_bazaar_sniper_lens",
        ];

        HashSet<string> models = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in game.Archives.Paths()
            .Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (game.Archives.Read(path) is not { } bytes)
            {
                continue;
            }

            string text = System.Text.Encoding.Latin1.GetString(bytes);

            foreach (string name in names.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase)))
            {
                output.WriteLine($"MODEL {path} names {name}");
                models.Add(path);
            }
        }

        string maps = Path.Combine(folder, "maps");
        int read = 0;

        foreach (string map in new[] { maps, MapProvider.OwnMapsFolder }
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.bsp")))
        {
            byte[] bytes = File.ReadAllBytes(map);
            read++;

            foreach (string model in Content.Bsp.BspStaticProps.Read(bytes).Select(prop => prop.Model)
                .Where(models.Contains).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                output.WriteLine($"MAP {Path.GetFileName(map)} static {model}");
            }

            foreach (Content.Bsp.BspEntity entity in Content.Bsp.BspEntities.ReadFrom(bytes))
            {
                string model = entity.TryGetValue("model", out string value) ? value : "";

                if (entity.ClassName.Equals("point_camera", StringComparison.OrdinalIgnoreCase) || models.Contains(model))
                {
                    output.WriteLine($"MAP {Path.GetFileName(map)} entity {entity.ClassName} {model}");
                }
            }
        }

        output.WriteLine($"{models.Count} models name one; {read} maps read from {maps} and {MapProvider.OwnMapsFolder}");
    }
}
