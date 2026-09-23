using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A map texdata's material, as the production VMT reader resolves it, and its two surfaceprops.</summary>
/// <remarks>The control for "terrain reads as default": if the material names no `$surfaceprop`, surface zero is right.</remarks>
public sealed class TexdataProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "texdata";

    /// <inheritdoc/>
    public string Summary => "a map texdata's material and its $surfaceprop/$surfaceprop2: texdata <map name> <index>";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("usage: texdata <map name> <index>");
            return;
        }

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        string path = $"maps/{arguments[0]}.bsp";

        byte[]? bytes = game.Archives.Read(path);

        if (bytes is null && locator.Find(arguments[0]) is { } file)
        {
            bytes = File.ReadAllBytes(file);
        }

        if (bytes is null)
        {
            output.WriteLine($"{path}: not found.");
            return;
        }

        int index = int.Parse(arguments[1], CultureInfo.InvariantCulture);
        IReadOnlyList<BspMaterial> texdata = BspMaterials.Read(bytes);
        PakFile pak = PakFile.ReadFrom(bytes);
        string name = texdata[index].Name;
        string vmtPath = $"materials/{name}.vmt";

        // `MapAssets.ReadVmt`'s own two lines: the map's pak first, then the game, patches resolved by `Load`.
        VmtMaterial? vmt = (pak.ReadFile(vmtPath) ?? game.Archives.Read(vmtPath)) is { Length: > 0 } raw
            ? VmtMaterial.Load(raw, include => pak.ReadFile(include) ?? game.Archives.Read(include))
            : null;

        output.WriteLine($"texdata {index}: {name}");
        output.WriteLine($"  shader {vmt?.Shader ?? "(no vmt)"}");
        output.WriteLine($"  $surfaceprop  {vmt?.Value("$surfaceprop") ?? "(none)"}");
        output.WriteLine($"  $surfaceprop2 {vmt?.Value("$surfaceprop2") ?? "(none)"}");
    }
}
