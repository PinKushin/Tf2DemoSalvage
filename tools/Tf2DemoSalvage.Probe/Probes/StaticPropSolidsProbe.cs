using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How a map's static props declare they collide — the census that decides what the ported world must build (B369).</summary>
/// <remarks>
/// **`CStaticProp::CreateVPhysics` makes a different object per solid type** (`engine.dll` `FUN_180203060`): `SOLID_VPHYSICS` (6)
/// from the model's first solid, `SOLID_BBOX` (2) from `BBoxToCollide` of the model's bounds, and refuses anything else. Counting
/// them says whether the box path matters on a given map before it is ported.
/// <code>
///   static-prop-solids [map]
/// </code>
/// </remarks>
public sealed class StaticPropSolidsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "static-prop-solids";

    /// <inheritdoc/>
    public string Summary => "how many of a map's static props are each SOLID_ type: static-prop-solids [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string mapName = arguments.Count > 0 ? arguments[0] : "cp_process_f12";
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(File.ReadAllBytes(mapPath));

        output.WriteLine($"{mapName}: {props.Count} static props");

        foreach (IGrouping<int, BspStaticProp> group in props.GroupBy(prop => prop.Solid).OrderBy(group => group.Key))
        {
            string models = string.Join(", ", group.Select(prop => prop.Model).Distinct().Take(4));
            output.WriteLine($"  solid {group.Key}: {group.Count()}  ({models})");
        }
    }
}
