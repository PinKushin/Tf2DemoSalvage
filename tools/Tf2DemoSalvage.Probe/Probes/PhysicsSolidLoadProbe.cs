using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which end of vphysics' per-solid loader every solid TF2 ships reaches — built, NULL, or refused (B404).
/// </summary>
/// <remarks>
/// **The census behind `PhysicsHull.Load`.** `FUN_18000a100` tells a solid's two kinds apart by a `VPHY` tag, reads a
/// tagged solid's type from `+6` and copies its `+8` data size, and reads a magic only for an untagged one
/// (`docs/findings/51`, *What the loader does with a solid it cannot use*). The disassembly says what each branch
/// does; this says which branches shipped content takes — every `.phy` in `tf2_misc_dir.vpk` and every solid in every
/// installed map's `LUMP_PHYSCOLLIDE`.
///
/// **The outcome column is the production call**; the header words beside it are the file's own bytes, printed so a
/// reading of the layout can be checked against them. **Two controls:** the size-prefix walk here must find as many
/// solids as `PhysicsModel` and `BspPhysicsCollision` do, and the magic a TAGGED solid's surface carries — which the
/// loader never reads — must come back `IVPS`, or the offsets printed beside it are wrong.
///
/// <code>
///   phy-solids [phy|maps|all]
/// </code>
/// </remarks>
public sealed class PhysicsSolidLoadProbe : IProbe
{
    /// <summary><c>LUMP_PHYSCOLLIDE</c>, <c>bspfile.h:310</c>.</summary>
    private const int PhysCollideLump = 29;

    /// <summary>Bytes of <c>phyheader_t</c>, <c>phyfile.h:14-21</c>.</summary>
    private const int HeaderSize = 16;

    /// <inheritdoc/>
    public string Name => "phy-solids";

    /// <inheritdoc/>
    public string Summary =>
        "which end of vphysics' solid loader each shipped solid reaches — tag, type, magic: phy-solids [phy|maps|all]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        string mode = arguments.Count > 0 ? arguments[0] : "all";

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        if (mode is "phy" or "all")
        {
            Models(output, folder);
        }

        if (mode is "maps" or "all")
        {
            Maps(output, locator);
        }
    }

    /// <summary>Every solid of every <c>.phy</c> in <c>tf2_misc_dir.vpk</c>.</summary>
    private static void Models(TextWriter output, string folder)
    {
        string archivePath = Path.Combine(folder, "tf2_misc_dir.vpk");

        if (!File.Exists(archivePath))
        {
            output.WriteLine($"No archive at {archivePath}.");
            return;
        }

        VpkArchive archive = VpkArchive.Open(archivePath);

        Census census = new();

        int files = 0;
        int declared = 0;
        int agreed = 0;
        int refused = 0;

        foreach (string path in archive.Paths
            .Where(entry => entry.EndsWith(".phy", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            if (archive.ReadFile(path) is not { Length: >= HeaderSize } bytes)
            {
                continue;
            }

            files++;

            int solidCount = BitConverter.ToInt32(bytes, 8);
            int walked = 0;
            int at = HeaderSize;

            declared += solidCount;

            for (int solid = 0; solid < solidCount && at + 4 <= bytes.Length; solid++)
            {
                int size = BitConverter.ToInt32(bytes, at);

                if (size < 0 || (long)at + 4 + size > bytes.Length)
                {
                    break;
                }

                census.Add(bytes.AsSpan(at + 4, size));
                walked++;
                at += 4 + size;
            }

            // **The control on the walk above**: the production reader's own walk, which must agree.
            try
            {
                if (PhysicsModel.Read(bytes).Hulls.Count == walked)
                {
                    agreed++;
                }
            }
            catch (InvalidDataException failure)
            {
                refused++;
                output.WriteLine($"  {path}: refused — {failure.Message}");
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"tf2_misc_dir.vpk: {files} .phy files declaring {declared} solids, {census.Total} walked; " +
            $"PhysicsModel walked the same number in {agreed} and refused {refused} " +
            $"(the control; the two must sum to {files})"));

        census.Report(output);
    }

    /// <summary>Every solid in every installed map's collision lump.</summary>
    private static void Maps(TextWriter output, MapLocator locator)
    {
        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        string folder = Path.GetDirectoryName(anyMap) ?? string.Empty;

        Census census = new();

        int maps = 0;
        int declared = 0;
        int extents = 0;

        foreach (string map in Directory.EnumerateFiles(folder, "*.bsp").Order(StringComparer.Ordinal))
        {
            ReadOnlyMemory<byte> lump;

            try
            {
                ReadOnlyMemory<byte> file = File.ReadAllBytes(map);

                lump = BspLumpData.Read(file, BspHeader.Parse(file.Span).Lump(PhysCollideLump));
            }
            catch (Exception failure) when (failure is IOException or InvalidDataException)
            {
                output.WriteLine($"  {Path.GetFileName(map)}: unreadable — {failure.Message}");
                continue;
            }

            maps++;

            foreach (MapPhysicsModel model in BspPhysicsCollision.Read(lump))
            {
                declared += model.SolidCount;

                foreach (PhysicsBrushSolid solid in model.Solids)
                {
                    census.Add(lump.Span.Slice(solid.Offset, solid.Length));
                    extents++;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{maps} maps: their models declare {declared} solids and the walk found {extents} " +
            $"(the control; must match)"));

        census.Report(output);
    }

    /// <summary>Tallies what one population of solids carries, and what the loader makes of each.</summary>
    private sealed class Census
    {
        private readonly Dictionary<PhysicsSolidLoad, int> loads = [];

        private readonly Dictionary<short, int> unreadWords = [];

        private readonly Dictionary<short, int> types = [];

        private readonly Dictionary<string, int> taggedMagics = new(StringComparer.Ordinal);

        private readonly Dictionary<string, int> untaggedMagics = new(StringComparer.Ordinal);

        private int tagged;

        private int taggedShort;

        private int sizeAgrees;

        private int untagged;

        private int untaggedShort;

        private int collideWithoutMass;

        private int collideWithoutLedges;

        /// <summary>How many solids were added.</summary>
        public int Total { get; private set; }

        /// <summary>Counts one solid.</summary>
        /// <param name="solid">Its bytes, after the size prefix.</param>
        public void Add(ReadOnlySpan<byte> solid)
        {
            Total++;

            PhysicsSolidLoad load = PhysicsHull.Load(solid);

            Bump(loads, load);

            if (load == PhysicsSolidLoad.Collide)
            {
                collideWithoutMass += PhysicsHull.MassProperties(solid) is null ? 1 : 0;
                collideWithoutLedges += PhysicsHull.Read(solid).Count == 0 ? 1 : 0;
            }

            if (solid.Length >= 4 && solid[..4].SequenceEqual("VPHY"u8))
            {
                Tagged(solid);
                return;
            }

            untagged++;

            if (solid.Length < 0x30)
            {
                untaggedShort++;
                return;
            }

            Bump(untaggedMagics, Magic(solid.Slice(0x2C, 4)));
        }

        /// <summary>Prints the tallies.</summary>
        /// <param name="output">Where to.</param>
        public void Report(TextWriter output)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  tagged VPHY: {tagged} ({taggedShort} too short for a header and a surface)"));
            output.WriteLine($"    word +4, which the loader does not read: {List(unreadWords, Hex)}");
            output.WriteLine($"    type word +6: {List(types, Hex)}");
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    data size +8 equal to the size prefix less 0x1C: {sizeAgrees} of {tagged - taggedShort}"));
            output.WriteLine(
                $"    surface magic +0x1C+0x2C, not read on this branch (the control): {List(taggedMagics, Text)}");
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  untagged: {untagged} ({untaggedShort} under 0x30 bytes)"));
            output.WriteLine($"    magic +0x2C: {List(untaggedMagics, Text)}");
            output.WriteLine($"  PhysicsHull.Load: {List(loads, Name)}");
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  a Collide with no mass properties: {collideWithoutMass}; with no ledges: {collideWithoutLedges}"));
        }

        private void Tagged(ReadOnlySpan<byte> solid)
        {
            tagged++;

            if (solid.Length < 0x1C + 0x30)
            {
                taggedShort++;
                return;
            }

            Bump(unreadWords, BitConverter.ToInt16(solid[4..]));
            Bump(types, BitConverter.ToInt16(solid[6..]));
            sizeAgrees += BitConverter.ToInt32(solid[8..]) == solid.Length - 0x1C ? 1 : 0;
            Bump(taggedMagics, Magic(solid.Slice(0x1C + 0x2C, 4)));
        }

        private static void Bump<T>(Dictionary<T, int> counts, T key)
            where T : notnull =>
            counts[key] = counts.GetValueOrDefault(key) + 1;

        private static string Hex(short word) => string.Create(CultureInfo.InvariantCulture, $"0x{word:X4}");

        private static string Text(string magic) => magic;

        private static string Name(PhysicsSolidLoad load) => load.ToString();

        /// <summary>Four bytes as text when every one is printable, else as a little-endian hex word.</summary>
        private static string Magic(ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                if (value is < 0x20 or > 0x7E)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"0x{BitConverter.ToInt32(bytes):X8}");
                }
            }

            return Encoding.ASCII.GetString(bytes);
        }

        private static string List<T>(Dictionary<T, int> counts, Func<T, string> name)
            where T : notnull =>
            counts.Count == 0
                ? "none"
                : string.Join(
                    ", ",
                    counts
                        .OrderByDescending(pair => pair.Value)
                        .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{name(pair.Key)} x{pair.Value}")));
    }
}
