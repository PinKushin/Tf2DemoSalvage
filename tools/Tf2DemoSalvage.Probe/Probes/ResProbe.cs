using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>A `.res` file as the engine's KeyValues loader builds it — `#base` merged, conditionals applied.</summary>
/// <remarks>
/// **The HUD's instrument.** Every VGUI layout is one of these, layered by `#base`, and a question about where a panel
/// is starts with what the merged tree says. Stock by default — `tf/custom` left out, because the owner's install carries
/// his own HUD (`docs/memory/modern-tf2-is-not-a-stock-reference.md`); `custom` as a second argument reads what the viewer
/// would, custom first.
/// </remarks>
public sealed class ResProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "res";

    /// <inheritdoc/>
    public string Summary => "a .res/.txt KeyValues file, #base merged and conditionals applied, stock unless asked: res <path> [custom]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("usage: res <path, e.g. scripts/hudlayout.res> [custom]");
            return;
        }

        GameArchives all = GameArchives.Open(
            new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder());
        GameArchives archives = arguments.Count > 1 && arguments[1] == "custom" ? all : all.WithoutCustom();
        string path = arguments[0].Replace('\\', '/');

        if (archives.Read(path) is not { } bytes)
        {
            output.WriteLine($"{path}: not found");
            return;
        }

        foreach (KeyValuesTree root in KeyValuesTree.LoadAll(bytes, path, archives.Read))
        {
            Print(output, root, 0);
        }
    }

    private static void Print(TextWriter output, KeyValuesTree node, int depth)
    {
        string indent = new(' ', depth * 2);

        if (node.Value is { } value)
        {
            output.WriteLine($"{indent}\"{node.Name}\" \"{value}\"");
            return;
        }

        output.WriteLine($"{indent}\"{node.Name}\"");
        output.WriteLine($"{indent}{{");

        foreach (KeyValuesTree child in node.Children)
        {
            Print(output, child, depth + 1);
        }

        output.WriteLine($"{indent}}}");
    }
}
