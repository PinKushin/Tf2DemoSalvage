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
/// What a model's <c>.phy</c> file holds, and how much of it is readable without Havok.
/// </summary>
/// <remarks>
/// **The route measurement for B58**, taken before any solver exists — `measure-the-route-before-
/// building-on-it`. A corpse stands upright because nothing simulates it, and the question that
/// decides whether that is even approachable is what a `.phy` actually contains:
///
/// <code>
/// typedef struct phyheader_s
/// {
///     int    size;
///     int    id;
///     int    solidCount;
///     int32  checkSum;   // checksum of source .mdl file
/// } phyheader_t;
/// </code>
///
/// `phyfile.h:14-21`. Sixteen bytes, then `solidCount` collision solids in Havok's closed `IVPS`
/// format, and then — the part that matters — **a plain-text KeyValues block** carrying the ragdoll
/// joints: which bone hangs off which, and the limits of each axis.
///
/// **So the two halves have completely different prospects.** The collision hulls would need a
/// closed format reverse-engineered; the constraint graph is text at the end of the file. This
/// probe reports both so the answer is a measurement rather than an expectation.
/// </remarks>
public sealed class RagdollConstraintProbe : IProbe
{
    /// <inheritdoc />
    public string Name => "ragdoll-constraints";

    /// <inheritdoc />
    public string Summary =>
        "what a model's .phy holds — solids, and the text ragdoll joints: " +
        "ragdoll-constraints [model substring]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder)
                .FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game folder could not be found.");
            return;
        }

        string archivePath = Path.Combine(folder, "tf2_misc_dir.vpk");

        if (!File.Exists(archivePath))
        {
            output.WriteLine($"No archive at {archivePath}.");
            return;
        }

        string wanted = arguments.Count > 0 ? arguments[0] : "models/player/";

        VpkArchive archive = VpkArchive.Open(archivePath);

        List<string> paths = [.. archive.Paths
            .Where(entry => entry.EndsWith(".phy", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry, StringComparer.Ordinal)];

        output.WriteLine(
            $"{paths.Count} .phy files matching '{wanted}' in {Path.GetFileName(archivePath)}");

        int shown = 0;

        foreach (string path in paths)
        {
            if (archive.ReadFile(path) is not { Length: >= HeaderSize } bytes)
            {
                continue;
            }

            int size = BitConverter.ToInt32(bytes, 0);
            int solids = BitConverter.ToInt32(bytes, 8);

            // **The text block is found by looking for it, not by arithmetic**, because the solids
            // between the header and it are Havok's format and this project cannot walk them. A
            // `.phy`'s KeyValues section is plain ASCII and always contains `ragdollconstraint` when
            // the model has joints, so searching the tail is both simpler and honest about what is
            // understood.
            string tail = Encoding.ASCII.GetString(bytes);

            int text = tail.IndexOf("solid {", StringComparison.Ordinal);

            int joints = Count(tail, "ragdollconstraint");
            int solidBlocks = Count(tail, "solid {");

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(path)}: header size {size}, {solids} solids; " +
                $"text at {(text < 0 ? "NOT FOUND" : text.ToString(CultureInfo.InvariantCulture))}, " +
                $"{solidBlocks} solid blocks, {joints} ragdoll constraints"));

            if (shown < 1 && text >= 0)
            {
                shown++;

                // **One constraint verbatim, because a count says nothing about whether the fields
                // are the ones a solver needs.** A joint that names its two bones and its three
                // axis limits is usable; a count of 20 is compatible with the block being
                // unparseable.
                int joint = tail.IndexOf("ragdollconstraint", StringComparison.Ordinal);

                if (joint >= 0)
                {
                    int end = Math.Min(joint + 260, tail.Length);

                    output.WriteLine("  --- the first constraint, verbatim ---");
                    output.WriteLine(tail[joint..end].ReplaceLineEndings("\n    "));
                    output.WriteLine("  --- ends ---");
                }

                // **A solid block too, because the constraints name bones by INDEX and an index
                // without a name is not usable.** `"parent" "0"` means nothing until something says
                // which bone solid 0 is. If the solids carry a `name`, the whole joint graph maps
                // onto the skeleton; if they do not, the constraint text is a set of numbers about
                // an unknown ordering.
                output.WriteLine("  --- the first solid, verbatim ---");
                output.WriteLine(tail[text..Math.Min(text + 260, tail.Length)]
                    .ReplaceLineEndings("\n    "));
                output.WriteLine("  --- ends ---");
            }
        }
    }

    /// <summary>Bytes of <c>phyheader_t</c>.</summary>
    private const int HeaderSize = 16;

    /// <summary>How many times a marker appears.</summary>
    /// <param name="text">The file, read as ASCII.</param>
    /// <param name="marker">What to count.</param>
    /// <returns>The count.</returns>
    private static int Count(string text, string marker)
    {
        int found = 0;
        int at = 0;

        while ((at = text.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
        {
            found++;
            at += marker.Length;
        }

        return found;
    }
}
