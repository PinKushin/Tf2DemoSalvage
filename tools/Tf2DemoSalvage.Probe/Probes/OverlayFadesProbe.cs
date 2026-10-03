using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Which installed maps' overlays carry a lump-60 fade, through the production reader.</summary>
/// <remarks>
/// The control is the overlay total per map: a map whose overlays read as zero says nothing about
/// fades. Prints the nearest-reaching fade per map, which is where a screenshot can show one vanish.
/// <code>
///   overlay-fades [map]
/// </code>
/// </remarks>
public sealed class OverlayFadesProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "overlay-fades";

    /// <inheritdoc/>
    public string Summary => "overlays with a lump-60 distance fade, per installed map or one map: overlay-fades [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "cp_process_final") is not { } anyMap)
        {
            output.WriteLine("No map found.");
            return;
        }

        IEnumerable<string> maps = arguments.Count > 0
            ? [anyMap]
            : Directory.EnumerateFiles(Path.GetDirectoryName(anyMap)!, "*.bsp").Order(StringComparer.Ordinal);

        int total = 0, fading = 0, withAny = 0;

        foreach (string path in maps)
        {
            IReadOnlyList<BspOverlay> overlays;

            try
            {
                overlays = BspOverlays.Read(File.ReadAllBytes(path));
            }
            catch (InvalidDataException exception)
            {
                output.WriteLine($"{Path.GetFileNameWithoutExtension(path)}: unreadable, {exception.Message}");
                continue;
            }

            List<BspOverlay> faded = [.. overlays.Where(overlay => overlay.FadeMaxSquared > 0f)];
            total += overlays.Count;
            fading += faded.Count;

            if (faded.Count == 0 && arguments.Count == 0)
            {
                continue;
            }

            withAny++;

            BspOverlay? nearest = faded.MinBy(overlay => overlay.FadeMaxSquared);

            output.WriteLine(
                $"{Path.GetFileNameWithoutExtension(path)}: {faded.Count} of {overlays.Count} overlays fade"
                + (nearest is null
                    ? string.Empty
                    : $"; nearest max {MathF.Sqrt(nearest.FadeMaxSquared):0} (min {MathF.Sqrt(Math.Max(0f, nearest.FadeMinSquared)):0})"
                      + $" overlay {nearest.Id} at ({nearest.Origin.X:0} {nearest.Origin.Y:0} {nearest.Origin.Z:0})"
                      + $" facing ({nearest.BasisNormal.X:0.##} {nearest.BasisNormal.Y:0.##} {nearest.BasisNormal.Z:0.##})"));
        }

        output.WriteLine($"total: {fading} of {total} overlays fade, on {withAny} maps");
    }
}
