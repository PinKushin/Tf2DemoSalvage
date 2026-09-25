using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>What a map's own pakfile carries, with each VTF's pixel format.</summary>
/// <remarks>
/// **Written to answer which cubemap bakes a map ships**: `c&lt;x&gt;_&lt;y&gt;_&lt;z&gt;.vtf` is the LDR one and `.hdr.vtf` the one
/// TF2 binds at its default `mat_hdr_level 2`. The format is `vtfheader_t.highResImageFormat`, the int at byte 52, as
/// `ImageFormat` numbers it (`imageformat.h`: 0 RGBA8888, 12 BGRA8888, 13 DXT1, 15 DXT5, 24 RGBA16161616F).
/// </remarks>
public sealed class PakProbe : IProbe
{
    /// <summary>`vtfheader_t.highResImageFormat`.</summary>
    private const int FormatOffset = 52;

    /// <inheritdoc/>
    public string Name => "pak";

    /// <inheritdoc/>
    public string Summary => "a map's pakfile entries matching a filter, with each VTF's format: pak <map> [substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments.Count > 0 ? arguments[0] : "koth_harvest_final") is not { } map)
        {
            output.WriteLine("Map not found.");
            return;
        }

        PakFile pak = PakFile.ReadFrom(File.ReadAllBytes(map));
        string filter = arguments.Count > 1 ? arguments[1] : string.Empty;
        List<string> matching = [.. pak.Paths.Where(path => path.Contains(filter, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal)];

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(map)}: {pak.Count} entries, {matching.Count} match '{filter}'"));

        foreach (string path in matching.Take(60))
        {
            string format = path.EndsWith(".vtf", StringComparison.OrdinalIgnoreCase) &&
                            pak.ReadFile(path) is { Length: > FormatOffset + 4 } bytes
                ? string.Create(CultureInfo.InvariantCulture, $"  format {BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(FormatOffset))}")
                : string.Empty;

            output.WriteLine($"  {path}{format}");
        }
    }
}
