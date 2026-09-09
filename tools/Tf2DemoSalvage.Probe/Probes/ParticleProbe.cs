using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What TF2's particle data actually is, measured before anything is built against it (B373).
/// </summary>
/// <remarks>
/// **A rocket draws its model and none of its effects, and the question is how big that gap is.**
/// `C_TFProjectile_Rocket::CreateTrails` (`c_tf_projectile_rocket.cpp:48`) asks for named systems —
/// `rockettrail`, `rockettrail_underwater`, `rockettrail_airstrike`, `critical_rocket_red` — and
/// those names index `.pcf` files the game ships. This counts them rather than guessing, because
/// "a particle system is a big subsystem" is an assertion until somebody measures the denominator.
///
/// <code>
///   particles                 the files and how much they hold
///   particles rockettrail     which file declares one system
/// </code>
/// </remarks>
public sealed class ParticleProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "particles";

    /// <inheritdoc/>
    public string Summary =>
        "the particle files TF2 ships and the systems they declare: particles [system substring]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to measure.");
            return;
        }

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;

        // **Every archive, because a `.pcf` can live in any of them** — and a probe that opens one
        // by hand and reports "not found" is answering about itself
        // (`docs/memory/an-empty-search-needs-a-control.md`).
        List<string> archives =
        [
            .. Directory.EnumerateFiles(game, "*_dir.vpk").OrderBy(one => one, StringComparer.Ordinal),
        ];

        output.WriteLine($"{archives.Count} archives in {game}");

        long files = 0;
        long bytes = 0;
        List<(string Archive, string Path, long Size)> found = [];

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            foreach (string path in open.Paths)
            {
                if (!path.EndsWith(".pcf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                files++;

                long size = open.TryFind(path, out VpkEntry entry) ? entry.Size : 0;

                bytes += size;
                found.Add((Path.GetFileName(archive), path, size));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {files} .pcf files, {bytes / 1024d / 1024d:0.0} MB total"));

        // **The largest few, because a format's cost is carried by its biggest files** and a count
        // alone cannot say whether this is a weekend or a month.
        foreach ((string archive, string path, long size) in found
            .OrderByDescending(one => one.Size)
            .Take(8))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {size / 1024d:0} KB  {path}  ({archive})"));
        }

        if (found.Count == 0)
        {
            output.WriteLine("  none — which for a game that draws rocket trails means this probe " +
                "is looking in the wrong place, not that TF2 ships no particles.");
            return;
        }

        // **The format's own header, read rather than assumed.** A `.pcf` is a DMX file and its
        // first line is plain text naming the encoding and version, which decides how much of a
        // reader is needed: a binary DMX is a different job from a keyvalues one.
        (string _, string sample, long _) = found.OrderByDescending(one => one.Size).First();

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            if (open.ReadFile(sample) is not { Length: > 64 } bytesOf)
            {
                continue;
            }

            string header = Encoding.ASCII.GetString(bytesOf, 0, 64).Split('\0')[0];

            output.WriteLine($"  header of {Path.GetFileName(sample)}: {header}");
            break;
        }

        if (filter.Length == 0)
        {
            return;
        }

        // **Which file declares a named system**, since `CreateTrails` asks by name and the name is
        // all the engine carries. The names are stored as plain strings in the file, so a byte
        // search answers without a DMX reader — enough to say where to look, not what it means.
        byte[] wanted = Encoding.ASCII.GetBytes(filter);
        int declaring = 0;

        foreach (string archive in archives)
        {
            VpkArchive open = VpkArchive.Open(archive);

            foreach ((string _, string path, long _) in found.Where(
                one => one.Archive == Path.GetFileName(archive)))
            {
                if (open.ReadFile(path) is not { } content ||
                    content.AsSpan().IndexOf(wanted) < 0)
                {
                    continue;
                }

                declaring++;
                output.WriteLine($"    '{filter}' appears in {path}");
            }
        }

        output.WriteLine($"  '{filter}' named by {declaring} of {found.Count} files");
    }
}
