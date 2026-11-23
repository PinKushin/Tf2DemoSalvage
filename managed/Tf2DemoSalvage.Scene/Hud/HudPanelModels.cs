using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>
/// The models the HUD's model panels name in their layouts, for the level's preload (B779 review 19, B812): read from the
/// same `.res` files and the same keys the panels read.
/// </summary>
/// <remarks>
/// **Why the preload needs them.** The engine's panels load their models from the install on demand
/// (`InitializeAsClientEntity`, basemodelpanel.cpp:407; `FindMDL`, mdlpanel.cpp:185). Ours read the level's preload
/// (`MapAssets.Geometry`, a lookup over `DemoModels.Needed`), read with the map so each model's materials join its texture
/// table. A model named only by a HUD layout — the round sign's `models/props_ui/banner.mdl` in `HudMatchStatus.res` — was
/// in no list, so it never loaded: measured with the `hud-model-loads` probe on `tf2-2026-pub-pov-clean.dem`, 1 of the 13
/// models the HUD asked for was missing, the banner. The faithful equivalent of the engine's on-demand load, in a
/// preload design, is every model a panel can ask for on the preload list.
///
/// **One source with the panels**: the layout paths are the elements' own constants, and the keys are the ones
/// `VguiModelPanel.ParseModelInfo` and `VguiBaseModelPanel.ParseModelResInfo` read. A layout's conditional blocks
/// (`if_match`) are all walked, so the list is a superset of what one HUD state shows.
/// </remarks>
public static class HudPanelModels
{
    /// <summary>A model panel's <c>model</c> block (basemodelpanel.cpp:88).</summary>
    public const string ModelBlock = "model";

    /// <summary>The model's path in a <c>model</c> or <c>attached_model</c> block (basemodelpanel.cpp:127, :149).</summary>
    public const string ModelName = "modelname";

    /// <summary>A model hung on the panel's model (basemodelpanel.cpp:143).</summary>
    public const string AttachedModel = "attached_model";

    /// <summary>The layouts of the HUD elements that hold model panels.</summary>
    public static IReadOnlyList<string> Layouts { get; } =
        [TfHudMatchStatus.ResFile, TfHudPlayerClass.ResFile, TfItemModelPanel.ResFile];

    /// <summary>Every model a <see cref="Layouts"/> file's model blocks name, its attached models included.</summary>
    /// <param name="read">The install's reader.</param>
    /// <returns>Distinct paths, in layout order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is null.</exception>
    public static IReadOnlyList<string> Named(Func<string, byte[]?> read) => Named(read, Layouts);

    /// <summary>Every model the given layouts' model blocks name — the `hud-model-loads` probe's census of every file the HUD read.</summary>
    /// <param name="read">The install's reader.</param>
    /// <param name="layouts">The `.res` files.</param>
    /// <returns>Distinct paths, in layout order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> or <paramref name="layouts"/> is null.</exception>
    public static IReadOnlyList<string> Named(Func<string, byte[]?> read, IEnumerable<string> layouts)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(layouts);

        List<string> named = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string layout in layouts)
        {
            if (read(layout) is not { Length: > 0 } bytes)
            {
                continue;
            }

            foreach (KeyValuesTree root in KeyValuesTree.LoadAll(bytes, layout, read))
            {
                Walk(root, named, seen);
            }
        }

        return named;
    }

    private static void Walk(KeyValuesTree node, List<string> named, HashSet<string> seen)
    {
        if (string.Equals(node.Name, ModelBlock, StringComparison.OrdinalIgnoreCase))
        {
            Add(node.Find(ModelName)?.Value, named, seen);

            foreach (KeyValuesTree child in node.Children)
            {
                if (string.Equals(child.Name, AttachedModel, StringComparison.OrdinalIgnoreCase))
                {
                    Add(child.Find(ModelName)?.Value, named, seen);
                }
            }
        }

        foreach (KeyValuesTree child in node.Children)
        {
            Walk(child, named, seen);
        }
    }

    private static void Add(string? path, List<string> named, HashSet<string> seen)
    {
        if (path is { Length: > 0 } && seen.Add(path))
        {
            named.Add(path);
        }
    }
}
