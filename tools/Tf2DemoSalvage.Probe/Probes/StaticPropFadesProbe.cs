using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many of a map's static props earn a fade entry, and of which kind (B430).</summary>
/// <remarks>
/// Through <see cref="StaticPropFade.For"/>, the production rule `UnserializeModels` (`engine.dll`
/// `0x180206590`) follows. The control is the total: every prop is counted in exactly one row.
/// <code>
///   static-prop-fades [map]
/// </code>
/// </remarks>
public sealed class StaticPropFadesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "static-prop-fades";

    /// <inheritdoc/>
    public string Summary => "how many of a map's static props fade, by distance or screen space: static-prop-fades [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string mapName = arguments.Count > 0 ? arguments[0] : "cp_process_final";
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        IReadOnlyList<BspStaticProp> props = BspStaticProps.Read(File.ReadAllBytes(mapPath));
        List<(BspStaticProp Prop, StaticPropFade Fade)> fading =
        [
            .. props
                .Select(prop => (prop, fade: StaticPropFade.For(prop.Flags, prop.FadeMinimum, prop.FadeMaximum)))
                .Where(pair => pair.fade is not null)
                .Select(pair => (pair.prop, pair.fade!.Value)),
        ];

        int screen = fading.Count(pair => pair.Fade.ScreenSpace);

        output.WriteLine(
            $"{mapName}: {props.Count} static props, {props.Count - fading.Count} without a fade entry, "
            + $"{fading.Count - screen} distance, {screen} screen-space");
        output.WriteLine(
            $"  nonzero max: {fading.Count(pair => pair.Prop.FadeMaximum > 0f)}; "
            + $"max range {fading.Select(pair => pair.Prop.FadeMaximum).DefaultIfEmpty().Min():0}"
            + $"..{fading.Select(pair => pair.Prop.FadeMaximum).DefaultIfEmpty().Max():0}");

        foreach ((BspStaticProp prop, _) in fading.Take(5))
        {
            output.WriteLine(
                $"  {prop.Model} at ({prop.X:0} {prop.Y:0} {prop.Z:0}) fades {prop.FadeMinimum:0}..{prop.FadeMaximum:0}");
        }
    }
}
