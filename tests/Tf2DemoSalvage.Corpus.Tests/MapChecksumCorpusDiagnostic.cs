using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// What map checksum each demo in the corpus actually carries.
/// </summary>
/// <remarks>
/// **Run before building anything on the checksum, and it is why.** The first real comparison —
/// an f12 demo against the f12 map — reported the demo's `mapCRC` as `0xFFFFFFFF`, which is the
/// CRC32 INIT value rather than a checksum. If that is what TF2 demos generally carry then the
/// field cannot detect a mismatched map and D113's plan needs a different instrument.
///
/// Explicit, because it reports numbers rather than asserting one.
/// </remarks>
[Explicit("Diagnostic: reports the map checksum every corpus demo carries.")]
public sealed class MapChecksumCorpusDiagnostic
{
    [Test]
    public void ReportMapCrcAcrossTheCorpus()
    {
        Dictionary<string, int> byValue = [];

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

            string described = crc switch
            {
                null => "no svc_ServerInfo",
                0xFFFFFFFF => "0xFFFFFFFF (the CRC32 init value - never computed)",
                0 => "zero",
                _ => "a real-looking checksum",
            };

            byValue[described] = byValue.GetValueOrDefault(described) + 1;

            // **And the hash beside it**, because the protocol carries both and the CRC turned out
            // to be dead from 2013 onward. If `MapHash` is populated where the CRC is not, it is
            // the instrument for modern demos.
            string hash = MapHashOf(path);

            Console.WriteLine(
                $"{Path.GetFileName(path),-56} " +
                $"{crc?.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) ?? "-",-10} " +
                $"{hash,-36} {described}");
        }

        Console.WriteLine();

        foreach (KeyValuePair<string, int> group in byValue.OrderByDescending(each => each.Value))
        {
            Console.WriteLine($"{group.Value,4} x {group.Key}");
        }

        byValue.ShouldNotBeEmpty();
    }

    /// <summary>The <c>svc_ServerInfo</c> map hash, as hex, or why there is none.</summary>
    private static string MapHashOf(string path)
    {
        if (TimelineCache.For(path).MapHash is not { Count: > 0 } hash)
        {
            return "(none)";
        }

        return Convert.ToHexString([.. hash]) +
            (hash.All(each => each == 0) ? " ALL ZERO" : string.Empty);
    }
}
