using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which shipped models carry <c>STUDIOHDR_FLAGS_CONSTANT_DIRECTIONAL_LIGHT_DOT</c> (`0x2000`), and their dot (B429).
/// </summary>
/// <remarks>
/// Every `.mdl` in `tf2_misc_dir.vpk` and `tf2_textures_dir.vpk`, read through <see cref="StudioModel.Read"/>. The
/// control is the `STATIC_PROP` count beside it, which must be large, so a zero on the `0x2000` row is about the game.
/// </remarks>
public sealed class ConstantDirectionalProbe : IProbe
{
    private static readonly string[] Archives = ["tf2_misc_dir.vpk", "tf2_textures_dir.vpk"];

    /// <inheritdoc/>
    public string Name => "const-directional";

    /// <inheritdoc/>
    public string Summary => "which shipped models are $constantdirectionallight, with their dot: const-directional";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        List<VpkArchive> archives = [.. Archives
            .Select(name => Path.Combine(folder, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)];

        List<string> paths = [.. archives
            .SelectMany(archive => archive.Paths)
            .Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.Ordinal)];

        int read = 0;
        int refused = 0;
        int staticProps = 0;
        List<string> constant = [];

        foreach (string path in paths)
        {
            byte[]? bytes = null;

            foreach (VpkArchive archive in archives)
            {
                bytes ??= archive.ReadFile(path);
            }

            if (bytes is null)
            {
                continue;
            }

            StudioModelInfo model;

            try
            {
                model = StudioModel.Read(bytes);
            }
            catch (InvalidDataException)
            {
                refused++;
                continue;
            }

            read++;
            staticProps += model.IsStaticProp ? 1 : 0;

            if ((model.Flags & StudioModelFlags.ConstantDirectionalLightDot) != 0)
            {
                constant.Add(
                    $"  {path}  flags 0x{model.Flags:X}  dot {model.ConstantDirectionalLightDot} " +
                    $"({StaticPropVertexLighting.ConstantDot(model.Flags, model.ConstantDirectionalLightDot):0.###})");
            }
        }

        output.WriteLine($"{read:N0} models read of {paths.Count:N0} paths ({refused} refused)");
        output.WriteLine($"  STATIC_PROP (control)            {staticProps,6:N0}");
        output.WriteLine($"  CONSTANT_DIRECTIONAL_LIGHT_DOT   {constant.Count,6:N0}");
        constant.ForEach(output.WriteLine);
    }
}
