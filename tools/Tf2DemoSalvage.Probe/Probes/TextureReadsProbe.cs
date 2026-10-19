using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>How many texture file reads one map load repeats, counted at the read (finding 80).</summary>
/// <remarks>
/// Loads the map by the production path, <see cref="LoadedMap.Read"/>, with the demo's timeline so its models' materials
/// resolve too, as in the viewer. The tally is <see cref="MapAssets.TextureReads"/>, recorded inside the resolve's own
/// read; nothing here recomputes it. The control: a real map must read hundreds of textures, so an empty tally is a broken
/// hook, not a fact about the map.
/// </remarks>
public sealed class TextureReadsProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "texture-reads";

    /// <inheritdoc/>
    public string Summary => "texture files a map load reads, and how many repeat: texture-reads <map> [demo] [maxTextureSize=0] [rows=15]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine(Summary);
            return;
        }

        int size = arguments.Count > 2 ? int.Parse(arguments[2], CultureInfo.InvariantCulture) : 0;
        int rows = arguments.Count > 3 ? int.Parse(arguments[3], CultureInfo.InvariantCulture) : 15;
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.Find(arguments[0]) is not { } path || locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine($"No map '{arguments[0]}' or no game folder.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);
        DemoTimeline? timeline = arguments.Count > 1 && arguments[1] != "-" ? DemoTimeline.Build(File.ReadAllBytes(arguments[1])) : null;
        TextureReadTally tally = new();
        MapAssets.TextureReads = tally;

        byte[] mapBytes = File.ReadAllBytes(path);
        double bakeBefore = PropModels.BakeSeconds;
        System.Diagnostics.Stopwatch load = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _ = LoadedMap.Read(mapBytes, game, timeline, size, NullLoggerFactory.Instance);
        }
        finally
        {
            MapAssets.TextureReads = null;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"map load {load.Elapsed.TotalSeconds:0.00} s, of which model posing and baking {PropModels.BakeSeconds - bakeBefore:0.00} s"));

        (long all, long repeats, long laterFrames) = tally.Totals();
        int reads = tally.ByPath.Values.Sum(entry => entry.Reads);
        output.WriteLine(
            $"{tally.ByPath.Count} distinct texture files, {reads} reads, {Mb(all)} MB read; " +
            $"repeats of a path already read {Mb(repeats)} MB, of which later animation frames {Mb(laterFrames)} MB");

        foreach ((string file, TextureReadTally.Entry entry) in tally.ByPath
            .Where(pair => pair.Value.Reads > 1)
            .OrderByDescending(pair => pair.Value.Bytes * (pair.Value.Reads - 1))
            .Take(rows))
        {
            output.WriteLine($"  {Mb(entry.Bytes * (entry.Reads - 1)),8} MB repeated  {entry.Reads,4} reads ({entry.FrameReads} frames)  {file}");
        }
    }

    private static string Mb(long bytes) => (bytes / 1048576d).ToString("0.0", CultureInfo.InvariantCulture);
}
