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
    public string Summary => "every shipped Refract material and the combos it selects: refract-census [list]";

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

            foreach (string proxy in material.Proxies.Select(proxy => proxy.Name))
            {
                proxies[proxy] = proxies.GetValueOrDefault(proxy) + 1;
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
    }
}
