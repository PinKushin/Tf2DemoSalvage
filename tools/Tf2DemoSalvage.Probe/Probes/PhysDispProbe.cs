using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a map's <c>LUMP_PHYSDISP</c> actually holds — the displacement collision vbsp compiled (B369).
/// </summary>
/// <remarks>
/// **The lump nobody has read.** vbsp builds each displacement's collision as a virtual mesh with
/// `params.buildOuterHull = true` and writes it with `CollideWrite` (`utils/vbsp/disp_ivp.cpp:314-350`);
/// `bspfile.h:459` calls the lump *"the binary blob for each displacement surface's virtual hull"*.
/// This project instead rebuilds terrain from the RENDER displacement data and stands in for the
/// outer hull with a 512-unit slab, and `corpse-drop` measured limbs resting eighty units under that
/// terrain on corpses that sleep.
///
/// **The layout is published, so it is checked rather than assumed** (`disp_ivp.cpp:320-357`):
///
/// <code>
///   unsigned short numDisplacements;          // dphysdisp_t
///   short          dataSize[numDisplacements]; // -1 when that displacement has no mesh
///   byte           blob[...];                  // each CollideWrite output, back to back
/// </code>
///
/// **Two controls, because an instrument that reads a lump it has never read needs something that
/// must be true.** The count must equal the dispinfo count — vbsp sets it from
/// `g_CoreDispInfos.Count()` — and the declared sizes must sum to EXACTLY the bytes after the table.
/// Either failing means the layout reading is wrong, not the map.
/// </remarks>
public sealed class PhysDispProbe : IProbe
{
    /// <summary>How many blobs to dump the head of.</summary>
    private const int Shown = 4;

    /// <summary>How many leading bytes of each shown blob to print.</summary>
    private const int HeadBytes = 32;

    /// <inheritdoc/>
    public string Name => "phys-disp";

    /// <inheritdoc/>
    public string Summary =>
        "what LUMP_PHYSDISP (28) holds — sizes, controls, blob heads: phys-disp [map]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        string mapName = arguments.Count > 0 ? arguments[0] : "koth_harvest_final";

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        byte[] file = File.ReadAllBytes(mapPath);

        BspHeader header = BspHeader.Parse(file);

        BspLump directory = header.Lump(PhysDispLump);

        ReadOnlySpan<byte> lump = BspLumpData.Read(file, directory).Span;

        int dispinfo = BspTerrain.Create(file).Count;

        output.WriteLine(
            $"{mapName}: lump 28 is {directory.Length} bytes on disk, {lump.Length} read, " +
            $"version {directory.Version}; dispinfo count {dispinfo}");

        if (lump.Length < 2)
        {
            output.WriteLine("  EMPTY — this map carries no compiled displacement collision.");
            return;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(lump);

        output.WriteLine(
            $"  numDisplacements {count} — control against dispinfo: " +
            (count == dispinfo ? "MATCH" : "MISMATCH, the layout reading is wrong"));

        int table = 2 + (count * 2);

        if (table > lump.Length)
        {
            output.WriteLine($"  the size table needs {table} bytes and the lump has {lump.Length}.");
            return;
        }

        long declared = 0;
        int present = 0;
        int absent = 0;
        int smallest = int.MaxValue;
        int largest = 0;

        int[] sizes = new int[count];

        for (int index = 0; index < count; index++)
        {
            short size = BinaryPrimitives.ReadInt16LittleEndian(lump[(2 + (index * 2))..]);

            sizes[index] = size;

            if (size < 0)
            {
                absent++;
                continue;
            }

            present++;
            declared += size;
            smallest = Math.Min(smallest, size);
            largest = Math.Max(largest, size);
        }

        long remaining = lump.Length - table;

        output.WriteLine(
            $"  {present} with a mesh, {absent} without; sizes {(present > 0 ? smallest : 0)}" +
            $"..{largest}, declared total {declared} against {remaining} after the table — " +
            (declared == remaining ? "EXACT" : "NOT EXACT, the layout reading is wrong"));

        // **Every blob against the size vphysics itself computes** — `FUN_180025e40`, the virtual
        // mesh's `CollideSize`: `4 + 5·hulls + Σ(4·triangles + 2·edges)`, with the per-hull header
        // `[0]` triangles and `[2]` edges (writer `FUN_180004110`). Four samples matched by hand;
        // this is the whole lump, so one mismatch anywhere says the reading is wrong.
        int exact = 0;
        int oneHull = 0;
        int twoHulls = 0;
        int otherHulls = 0;
        int mostTriangles = 0;
        int mostEdges = 0;
        int offset = table;

        for (int index = 0; index < count; index++)
        {
            int size = sizes[index];

            if (size < 0)
            {
                continue;
            }

            if (offset + size > lump.Length || size < 4)
            {
                output.WriteLine($"  blob {index} does not fit: {offset}+{size} of {lump.Length}.");
                return;
            }

            ReadOnlySpan<byte> blob = lump.Slice(offset, size);
            uint hulls = BinaryPrimitives.ReadUInt32LittleEndian(blob);

            switch (hulls)
            {
                case 1: oneHull++; break;
                case 2: twoHulls++; break;
                default: otherHulls++; break;
            }

            long expected = 4 + (5L * hulls);

            for (int hull = 0; hull < hulls && 4 + (5 * (hull + 1)) <= blob.Length; hull++)
            {
                int triangles = blob[4 + (5 * hull)];
                int edges = blob[4 + (5 * hull) + 2];

                mostTriangles = Math.Max(mostTriangles, triangles);
                mostEdges = Math.Max(mostEdges, edges);
                expected += (4L * triangles) + (2L * edges);
            }

            if (expected == size)
            {
                exact++;
            }

            offset += size;
        }

        output.WriteLine(
            $"  size formula exact on {exact} of {present} blobs; hulls: {oneHull} with one, " +
            $"{twoHulls} with two, {otherHulls} other; at most {mostTriangles} triangles and " +
            $"{mostEdges} edges in one hull");

        int at = table;
        int shown = 0;

        for (int index = 0; index < count && shown < Shown; index++)
        {
            if (sizes[index] < 0)
            {
                continue;
            }

            if (at + sizes[index] > lump.Length)
            {
                output.WriteLine($"  blob {index} runs past the lump at {at}+{sizes[index]}.");
                return;
            }

            ReadOnlySpan<byte> blob = lump.Slice(at, sizes[index]);
            ReadOnlySpan<byte> head = blob[..Math.Min(HeadBytes, blob.Length)];

            output.WriteLine(
                $"  blob {index,4} {sizes[index],6} bytes  {Convert.ToHexString(head)}  |{Printable(head)}|");

            at += sizes[index];
            shown++;
        }
    }

    /// <summary><c>LUMP_PHYSDISP</c>, <c>public/bspfile.h:309</c>.</summary>
    private const int PhysDispLump = 28;

    private static string Printable(ReadOnlySpan<byte> bytes)
    {
        StringBuilder text = new(bytes.Length);

        foreach (byte value in bytes)
        {
            text.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
        }

        return text.ToString();
    }
}
