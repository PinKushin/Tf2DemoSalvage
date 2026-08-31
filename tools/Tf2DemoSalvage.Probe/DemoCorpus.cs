using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tf2DemoSalvage.Probe;

/// <summary>
/// Locates the reference demos. The one implementation, shared by the probes and the suite.
/// </summary>
/// <remarks>
/// **This lives in the probe tool and is consumed by <c>Tf2DemoSalvage.Corpus.Tests</c>, not the
/// other way round.** The dependency runs that way because the tool must not drag NUnit, the test
/// adapter and Shouldly into a program whose whole point is to build quickly.
///
/// **One implementation, because two would drift silently.** The corpus is two directories with a
/// switch between them (<c>TF2DEMOSALVAGE_GCOR_ONLY</c>) and a size floor that rejects Git LFS
/// pointer stubs. A probe that resolved a name differently from the test that motivated it would
/// answer a question about a different file and say nothing about the discrepancy — the failure
/// <c>docs/memory/one-place-or-it-drifts.md</c> is about.
/// </remarks>
public static class DemoCorpus
{
    /// <summary>Anything smaller than this is a Git LFS pointer stub, not a demo.</summary>
    public const int SmallestPlausibleDemo = 4096;

    /// <summary>
    /// Extra demos, present only on a developer's machine and never committed.
    /// </summary>
    /// <remarks>
    /// The committed corpus is deliberately one specimen per category — era and point of view —
    /// because GitHub's free Git LFS tier is 1 GiB of bandwidth a month and every CI job that
    /// fetches it pays. A seventh protocol-24 SourceTV demo costs real budget to test nothing new.
    ///
    /// **Locally there is no such constraint**, and more real files is strictly better coverage.
    /// Anything dropped in <c>tools/corpus/local/</c> joins the run automatically. The directory
    /// is already git-ignored, and for a second reason worth keeping in mind: self-recorded demos
    /// carry the recorder's screen name and SteamID.
    ///
    /// This makes a local run a superset of CI rather than a different thing, so a local pass
    /// cannot hide a CI failure — only the reverse, which is the useful direction.
    /// </remarks>
    private const string LocalDirectoryName = "local";

    /// <summary>Whether this run is restricted to the committed corpus.</summary>
    /// <returns><c>true</c> when <c>tools/corpus/local</c> is to be skipped.</returns>
    /// <remarks>
    /// Anything other than unset or "0" counts as on, so a typo errs towards the smaller, faster
    /// run rather than towards silently including 774 MB of demos.
    /// </remarks>
    public static bool GcorOnly() =>
        Environment.GetEnvironmentVariable("TF2DEMOSALVAGE_GCOR_ONLY") is { } value &&
        value.Length > 0 &&
        value != "0";

    /// <summary>
    /// Walks up from the running binary looking for the corpus, rather than hard-coding a
    /// relative depth that breaks whenever the output path changes.
    /// </summary>
    /// <returns>The committed corpus directory, or <c>null</c> when there is none.</returns>
    public static string? Directory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "tools", "corpus", "demos");
            if (System.IO.Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>Every usable demo in the corpus, in a stable order.</summary>
    /// <param name="log">Where to announce a restricted run, or <c>null</c> to stay quiet.</param>
    /// <returns>Full paths, ordinal-sorted.</returns>
    /// <remarks>
    /// **The gcor announcement is written, not silent.** A run that quietly halved its corpus would
    /// report a smaller total that reads as a passing run — the failure *"Passed! is not the
    /// result, the COUNT is"* is about.
    /// </remarks>
    public static IReadOnlyList<string> Files(TextWriter? log)
    {
        string? directory = Directory();
        if (directory is null)
        {
            return [];
        }

        // Sibling of demos/, not a child: tools/corpus/local. Combining it onto `directory` gave
        // tools/corpus/demos/local, which does not exist — so the extra files were silently
        // ignored. A path that does not exist is not an error here, it is a no-op, which is
        // exactly how that mistake hid.
        string local = Path.Combine(
            Path.GetDirectoryName(directory) ?? directory, LocalDirectoryName);

        IEnumerable<string> paths = System.IO.Directory.EnumerateFiles(directory, "*.dem");

        if (GcorOnly())
        {
            log?.WriteLine(
                "CORPUS gcor only: the local corpus is excluded by TF2DEMOSALVAGE_GCOR_ONLY");
        }
        else if (System.IO.Directory.Exists(local))
        {
            paths = paths.Concat(System.IO.Directory.EnumerateFiles(local, "*.dem"));
        }

        return
        [
            .. paths
                .Where(path => new FileInfo(path).Length >= SmallestPlausibleDemo)
                .OrderBy(path => path, StringComparer.Ordinal),
        ];
    }

    /// <summary>The one demo whose file name contains a fragment.</summary>
    /// <param name="fragment">Part of the file name, such as a map.</param>
    /// <param name="log">Where to announce a restricted run, or <c>null</c>.</param>
    /// <returns>The path, or <c>null</c> when no such demo is present.</returns>
    /// <remarks>
    /// **Returns null rather than falling back to another demo.** The specimens differ enormously —
    /// the committed 2013 badlands POV carries 11 props and no wearables at all — so a probe
    /// quietly redirected there would report numbers about nothing.
    /// </remarks>
    public static string? Find(string fragment, TextWriter? log)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        return Files(log).FirstOrDefault(
            file => Path.GetFileName(file).Contains(fragment, StringComparison.Ordinal));
    }
}
