using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Every shipped sky material through <see cref="SkySurface.Choose"/> under integer HDR, by shader and format (B461).</summary>
/// <remarks>
/// **The denominator for the sky's HDR branches**: which of `Sky_HDR_DX9`'s three pixel shaders TF2's own skies reach, and
/// how many half-float faces take the ×16. Runs the production choice and the production VTF reader. The control is
/// the count of `sky` materials at all, which must not be zero.
/// </remarks>
public sealed class SkyHdrProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "sky-hdr";

    /// <inheritdoc/>
    public string Summary => "every shipped sky material's HDR shader and texture format, as the viewer chooses them: sky-hdr";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        Dictionary<string, int> tally = new(StringComparer.Ordinal);
        Dictionary<string, string> example = new(StringComparer.Ordinal);
        int skies = 0;

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
            catch (InvalidDataException)
            {
                continue;
            }

            if (!material.Shader.Equals("sky", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            skies++;

            string key;

            if (SkySurface.Choose(material, HdrType.IntegerHdr) is not { } choice)
            {
                key = "no texture";
            }
            else
            {
                string format;

                try
                {
                    format = game.Archives.Read($"materials/{choice.Texture}.vtf") is { } vtf
                        ? VtfTexture.Read(vtf, 16).Format.ToString()
                        : "missing vtf";
                }
                catch (InvalidDataException failure)
                {
                    format = $"unreadable ({failure.Message})";
                }

                key = $"{choice.Shader} {format}";
            }

            tally[key] = tally.GetValueOrDefault(key) + 1;
            example.TryAdd(key, path);
        }

        output.WriteLine($"{skies} sky materials — the control");

        foreach ((string key, int count) in tally.OrderByDescending(entry => entry.Value))
        {
            output.WriteLine($"  {count,5}  {key}   e.g. {example[key]}");
        }
    }
}
