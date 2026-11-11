using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many shipped particle systems start a child after a delay (B564).</summary>
/// <remarks>
/// Reads every `.pcf` the install's `particles/particles_manifest.txt` names, through the production `DmxFile`, and
/// lists each `DmeParticleChild` whose `delay` is not zero. The control is the count of children read at all: a
/// manifest with hundreds of systems and no children means the reader, not TF2, came back empty.
/// </remarks>
public sealed partial class ChildDelayProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "child-delays";

    /// <inheritdoc/>
    public string Summary => "particle children that start after a delay, across the manifest's .pcf files: child-delays";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (game.Archives.Read("particles/particles_manifest.txt") is not { } manifest)
        {
            output.WriteLine("particles/particles_manifest.txt: not found.");
            return;
        }

        string[] files = [.. PcfName().Matches(Encoding.UTF8.GetString(manifest)).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase)];
        int systems = 0;
        int children = 0;
        List<string> delayed = [];

        foreach (string file in files)
        {
            if (game.Archives.Read(file) is not { } bytes)
            {
                output.WriteLine($"{file}: not found.");
                continue;
            }

            IReadOnlyList<DmxElement> elements = DmxFile.Read(bytes);

            foreach (DmxElement element in elements)
            {
                if (element.Type == "DmeParticleSystemDefinition")
                {
                    systems++;

                    if (element.Attributes.TryGetValue("children", out DmxValue list) && list.Elements is { } references)
                    {
                        foreach (int reference in references.Where(r => r >= 0 && r < elements.Count))
                        {
                            DmxElement child = elements[reference];
                            children++;

                            if (child.Attributes.TryGetValue("delay", out DmxValue delay) && delay.Number != 0d)
                            {
                                delayed.Add(string.Create(CultureInfo.InvariantCulture, $"{file}: {element.Name} -> {child.Name} delay {delay.Number:0.###}"));
                            }
                        }
                    }
                }
            }
        }

        delayed.ForEach(output.WriteLine);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{files.Length} files, {systems} systems, {children} children read, {delayed.Count} with a nonzero delay"));
    }

    // A manifest entry may carry a leading `!` (precache now), which is not part of the path.
    [GeneratedRegex("\"!?(particles/[^\"]+\\.pcf)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PcfName();
}
