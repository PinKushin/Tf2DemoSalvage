using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a map's baked physics collision holds — the thing a corpse would land on (B58).
/// </summary>
/// <remarks>
/// **The last unread input a ragdoll needs, measured before anything is built on it**
/// (`docs/memory/measure-the-route-before-building-on-it.md`). A TF2 corpse is simulated by the
/// client against the map's own collision, which the compiler bakes into `LUMP_PHYSCOLLIDE`
/// (lump 29, `bspfile.h:310`) and the engine hands to `CreatePolyObjectStatic`. This project reads
/// 34 of the 64 lumps and that is not one of them.
///
/// **The layout is fully specified by Valve's own loader** (`bsplib.cpp:1577-1625`), including its
/// terminator, which is the part a guess would get wrong:
///
/// <code>
/// // physics data is variable length.  The last physmodel is a NULL pointer
/// // with modelIndex -1, dataSize -1
/// struct dphysmodel_t { int modelIndex; int dataSize; int keydataSize; int solidCount; };
/// </code>
///
/// **Each entry is one brush model** — index 0 is the world, the rest are `func_` brush entities —
/// followed by `dataSize` bytes of solids (each a length-prefixed blob) and then `keydataSize`
/// bytes of KeyValues TEXT.
///
/// **So it splits exactly the way a `.phy` does**, which is the finding worth having: the hulls are
/// the closed `IVPS` format this project already skips in a model's `.phy`, and the text beside
/// them is plain KeyValues carrying the surface properties. One format, needed twice.
///
/// <code>
///   map-collision [map]
/// </code>
/// </remarks>
public sealed class MapCollisionProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "map-collision";

    /// <inheritdoc/>
    public string Summary =>
        "the map's baked physics collision, which a corpse lands on: map-collision [map]";

    /// <summary><c>LUMP_PHYSCOLLIDE</c>, <c>bspfile.h:310</c>.</summary>
    private const int PhysCollideLump = 29;

    /// <summary>Bytes of <c>dphysmodel_t</c> — four ints.</summary>
    private const int ModelHeaderSize = 16;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count > 0)
        {
            if (locator.Find(arguments[0]) is not { } named)
            {
                output.WriteLine($"No map named '{arguments[0]}'.");
                return;
            }

            Report(output, named, verbose: true);
            return;
        }

        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        Report(output, anyMap, verbose: true);

        string folder = Path.GetDirectoryName(anyMap) ?? string.Empty;
        string[] maps = [.. Directory.EnumerateFiles(folder, "*.bsp").Order(StringComparer.Ordinal)];

        int withCollision = 0;

        foreach (string map in maps)
        {
            if (Report(output, map, verbose: false).Models > 0)
            {
                withCollision++;
            }
        }

        output.WriteLine();

        // **The control, and it must equal the map count.** Every compiled map has a world brush
        // model with collision; a number below the total means the walk failed rather than that
        // some maps ship without physics.
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{withCollision} of {maps.Length} installed maps yielded at least one physics model " +
            $"(the control; must match)."));
    }

    /// <summary>Walks one map's collision lump.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="path">The map file.</param>
    /// <param name="verbose">Whether to print the per-map detail.</param>
    /// <returns>How many models and solids were found.</returns>
    private static (int Models, int Solids) Report(TextWriter output, string path, bool verbose)
    {
        ReadOnlyMemory<byte> file;

        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (IOException error)
        {
            output.WriteLine($"{Path.GetFileName(path)}: unreadable — {error.Message}");
            return (0, 0);
        }

        BspHeader header;
        ReadOnlyMemory<byte> lump;

        try
        {
            header = BspHeader.Parse(file.Span);
            lump = BspLumpData.Read(file, header.Lump(PhysCollideLump));
        }
        catch (InvalidDataException error)
        {
            output.WriteLine($"{Path.GetFileName(path)}: lump unreadable — {error.Message}");
            return (0, 0);
        }

        if (lump.Length == 0)
        {
            if (verbose)
            {
                output.WriteLine($"{Path.GetFileName(path)}: no physics collision lump.");
            }

            return (0, 0);
        }

        return Walk(output, Path.GetFileName(path), lump.Span, verbose);
    }

    /// <summary>Walks the lump's chain of models.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="name">The map's file name.</param>
    /// <param name="lump">The lump's bytes.</param>
    /// <param name="verbose">Whether to print the per-map detail.</param>
    /// <returns>How many models and solids were found.</returns>
    /// <remarks>
    /// **Every read is bounds-checked, because a `.bsp` is a stranger's file** (D32). Maps arrive
    /// from fastdl, supplied by whoever runs the server and reviewed by nobody, and this walk is
    /// driven entirely by lengths the file itself declares — which is the exact shape of the
    /// allocate-before-validate defects already fixed elsewhere here.
    /// </remarks>
    private static (int Models, int Solids) Walk(
        TextWriter output, string name, ReadOnlySpan<byte> lump, bool verbose)
    {
        int at = 0;
        int models = 0;
        int solids = 0;
        int textBytes = 0;
        string firstText = string.Empty;

        while (at + ModelHeaderSize <= lump.Length)
        {
            int modelIndex = BinaryPrimitives.ReadInt32LittleEndian(lump[at..]);
            int dataSize = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 4)..]);
            int keydataSize = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 8)..]);
            int solidCount = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 12)..]);

            at += ModelHeaderSize;

            // Valve's own terminator: "The last physmodel is a NULL pointer with modelIndex -1,
            // dataSize -1" (`bsplib.cpp:1575`). Tested before the sizes are trusted for anything.
            if (modelIndex == -1 && dataSize == -1)
            {
                break;
            }

            if (dataSize < 0 || keydataSize < 0 || solidCount < 0 ||
                (long)at + dataSize + keydataSize > lump.Length)
            {
                output.WriteLine(
                    $"{name}: a physics model declares sizes past the end of the lump — stopping.");

                break;
            }

            models++;
            solids += solidCount;

            if (firstText.Length == 0 && keydataSize > 0)
            {
                firstText = Encoding.ASCII
                    .GetString(lump.Slice(at + dataSize, Math.Min(keydataSize, 200)))
                    .ReplaceLineEndings("\n      ");
            }

            textBytes += keydataSize;
            at += dataSize + keydataSize;
        }

        if (verbose)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {models} physics models, {solids} solids, " +
                $"{textBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes of KeyValues text"));

            if (firstText.Length > 0)
            {
                output.WriteLine("  --- the first model's text, verbatim ---");
                output.WriteLine($"      {firstText}");
                output.WriteLine("  --- ends ---");
            }
        }

        return (models, solids);
    }
}
