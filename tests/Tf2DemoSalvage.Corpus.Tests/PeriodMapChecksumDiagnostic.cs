using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// Every period map's checksum against every era demo's, to see which pair up.
/// </summary>
/// <remarks>
/// **Written to separate two explanations of one failure.** The 2007 granary demo carries
/// `0x534EEB7C`; the 2007 client's own `cp_granary.bsp` computes `0x0E9C7A43`. Either the CRC is
/// implemented wrongly, or the extracted build is not the exact revision the demo was recorded on.
///
/// **The lump selection is already proven**, which is what makes the second explanation likely: the
/// MODERN hash — MD5 over the same walk — matches `cp_process_f12` exactly. If the selection were
/// wrong, that could not have matched either. But "likely" is not measured, so this walks every
/// period map and looks for the number.
///
/// Explicit: it reports rather than asserts.
/// </remarks>
[Explicit("Diagnostic: matches period map checksums against era demo checksums.")]
public sealed class PeriodMapChecksumDiagnostic
{
    private const string Builds = @"F:\tf2-builds";

    [Test]
    public void ReportWhichPeriodMapsMatchWhichDemos()
    {
        Dictionary<uint, string> wanted = [];

        foreach (string path in Corpus.Files())
        {
            uint? crc;

            try
            {
                crc = TimelineCache.For(path).MapCrc;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (crc is { } value && value != 0xFFFFFFFF)
            {
                wanted[value] = Path.GetFileName(path);
            }
        }

        Console.WriteLine($"{wanted.Count} era demos carry a real checksum:");

        foreach (KeyValuePair<uint, string> each in wanted)
        {
            Console.WriteLine($"  {each.Key:X8}  {each.Value}");
        }

        Console.WriteLine();

        if (!Directory.Exists(Builds))
        {
            Assert.Ignore("no period builds on this machine.");
            return;
        }

        int matched = 0;

        foreach (string map in Directory.EnumerateFiles(Builds, "*.bsp", SearchOption.AllDirectories))
        {
            uint crc;

            try
            {
                crc = BspMapChecksum.OfMap(File.ReadAllBytes(map)).Crc;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!wanted.TryGetValue(crc, out string? demo))
            {
                continue;
            }

            matched++;

            Console.WriteLine(
                $"MATCH {crc.ToString("X8", CultureInfo.InvariantCulture)}  {demo}");
            Console.WriteLine($"      {map}");
        }

        Console.WriteLine();
        Console.WriteLine(
            matched > 0
                ? $"{matched} period map(s) matched a demo — the CRC implementation is correct."
                : "NO period map matched any demo checksum.");

        wanted.ShouldNotBeEmpty();
    }
}
