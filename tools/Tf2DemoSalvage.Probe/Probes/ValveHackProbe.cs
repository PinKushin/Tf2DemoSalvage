using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Where Valve says their own code is a hack, ranked by whether we have implemented it (D144).
/// </summary>
/// <remarks>
/// **The owner's rule:** *"if valve notes that something they did is a hack, or not optimal, [see]
/// if we can fix those and still have parity, those are places valve itself says we should change
/// the code if we can basically, because its places valve would do stuff differently if they could
/// redo it."*
///
/// **Ranked by what we CITE, not by what exists.** The SDK is full of these and most are in code
/// this project will never touch. A comment in a file we already quote is a comment about behaviour
/// we already reproduce, which is the same ordering rule `docs/PARITY-AUDIT.md` opens with — pick a
/// subject that is on screen now.
///
/// **The output is a candidate list and nothing more.** D144 splits these two ways and only one is
/// actionable: a hack whose output is identical either way can be written the better way freely,
/// and a hack that IS the behaviour stays exactly as it is however apologetic the comment.
/// `useClockwiseRotations` is the second kind — Valve calls it a HACKHACK, and it decides which way
/// a joint's limits point.
///
/// <code>
///   valve-hacks             — every self-flagged comment in a file this project cites
///   valve-hacks all         — every one in the SDK, cited or not
///   valve-hacks detailobj   — only files whose name contains this
/// </code>
/// </remarks>
public sealed class ValveHackProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "valve-hacks";

    /// <inheritdoc/>
    public string Summary =>
        "where Valve calls their own code a hack, in files we cite: valve-hacks [all|filter]";

    /// <summary>Where the published SDK is checked out.</summary>
    private const string SdkRoot = @"F:\src\source-sdk-2013\src";

    /// <summary>
    /// What counts as Valve flagging their own code.
    /// </summary>
    /// <remarks>
    /// **Chosen from what the tree actually says**, not from a generic list — see the array below.
    /// Two are Valve's deliberate markers, one is their own older spelling of the same thing, and
    /// the rest are prose forms that catch the ones written as sentences. Those are often the most
    /// informative: *"Did this wrong in version one"* carries more than any marker in front of it.
    ///
    /// **The not-done marker is deliberately absent.** It flags work never started rather than work
    /// done badly, and including it buries the signal — it is far more common than every marker here
    /// together, and almost none of it is about a compromise Valve regrets.
    ///
    /// **The markers are not spelled out in this comment on purpose**: the analysers treat two of
    /// them as a standing instruction to the reader, so a doc block naming them fails the build.
    /// They live in the array, where they are data.
    /// </remarks>
    private static readonly string[] Markers =
    [
        "HACKHACK",
        "HACK:",
        "FIXME",
        "BUGBUG",
        "this is wrong",
        "did this wrong",
        "not optimal",
        "should really",
        "would be better",
        "ugly",
        "yuck",
    ];

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!Directory.Exists(SdkRoot))
        {
            output.WriteLine($"The SDK is not at {SdkRoot}, so there is nothing to read.");
            return;
        }

        string filter = arguments.Count > 0 ? arguments[0] : string.Empty;
        bool everything = string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase);

        HashSet<string> cited = Cited(output);

        int files = 0;
        int hits = 0;

        foreach (string path in Directory
            .EnumerateFiles(SdkRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);

            if (!everything && !cited.Contains(name))
            {
                continue;
            }

            if (filter.Length > 0 && !everything &&
                !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(path);
            bool named = false;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];

                if (!Markers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (!named)
                {
                    output.WriteLine(
                        $"{name}{(cited.Contains(name) ? "  (CITED by this project)" : string.Empty)}");
                    named = true;
                    files++;
                }

                output.WriteLine(
                    $"    {name}:{(index + 1).ToString(CultureInfo.InvariantCulture)}  " +
                    line.Trim());

                hits++;
            }
        }

        output.WriteLine(
            $"{hits.ToString(CultureInfo.InvariantCulture)} self-flagged comments across " +
            $"{files.ToString(CultureInfo.InvariantCulture)} files" +
            $"{(everything ? " (the whole SDK)" : " this project cites")}.");
    }

    /// <summary>Which SDK file names this project quotes anywhere.</summary>
    /// <remarks>
    /// **By NAME rather than by line**, unlike the `parity` probe: the question here is whether we
    /// have looked at a file at all, not whether we cited the exact line a comment sits on. A hack
    /// forty lines from a line we quote is still a hack in code we have implemented.
    /// </remarks>
    private static HashSet<string> Cited(TextWriter output)
    {
        HashSet<string> cited = new(StringComparer.OrdinalIgnoreCase);

        if (Managed() is not { } managed)
        {
            output.WriteLine("Could not find the managed tree; every file will read as uncited.");
            return cited;
        }

        Regex citation = new(
            @"\b([a-z_0-9]+\.(?:cpp|h)):\d+\b", RegexOptions.None, TimeSpan.FromSeconds(5));

        foreach (string source in Directory.EnumerateFiles(managed, "*.cs", SearchOption.AllDirectories))
        {
            if (source.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in citation.Matches(File.ReadAllText(source)))
            {
                cited.Add(match.Groups[1].Value);
            }
        }

        output.WriteLine(
            $"{cited.Count.ToString(CultureInfo.InvariantCulture)} distinct SDK files cited by this project");

        return cited;
    }

    private static string? Managed()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "managed");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
