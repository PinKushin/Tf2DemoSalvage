using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Colour names resolved through a scheme file as `vgui2.dll`'s `CScheme` resolves them.</summary>
/// <remarks>Stock `resource/clientscheme.res` by default, `tf/custom` left out; `custom` as the last argument reads what the viewer would.</remarks>
public sealed class SchemeProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "scheme";

    /// <inheritdoc/>
    public string Summary => "colour names resolved through clientscheme.res as CScheme resolves them, stock unless asked: scheme <name>... [custom]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        bool custom = arguments.Count > 0 && arguments[^1] == "custom";
        GameArchives all = GameArchives.Open(new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder());
        GameArchives archives = custom ? all : all.WithoutCustom();
        const string Path = "resource/clientscheme.res";

        if (archives.Read(Path) is not { } bytes)
        {
            output.WriteLine($"{Path}: not found");
            return;
        }

        VguiScheme scheme = VguiScheme.Load(KeyValuesTree.Load(bytes, Path, archives.Read));

        foreach (string name in arguments)
        {
            if (name == "custom")
            {
                continue;
            }

            (byte red, byte green, byte blue, byte alpha) = scheme.GetColor(name, (0, 0, 0, 0));

            output.WriteLine($"{name}: lookup \"{scheme.Lookup(name)}\" -> {red} {green} {blue} {alpha}");
        }
    }
}
