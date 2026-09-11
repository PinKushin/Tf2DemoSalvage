using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Which models an older TF2 install orders its sequences differently from today's (B380, D160).
/// </summary>
/// <remarks>
/// **A networked <c>m_nSequence</c> is an index into the model the recording client loaded**, so a
/// model whose sequence list changed since plays the wrong animation on an old demo. The 2008 sticky
/// launcher is the case that found it: today's file inserted <c>ref</c> at index 0, and the demo's
/// <c>draw</c> became <c>fire</c>. This counts how many models moved, per install, which is what sizes
/// the translation table D160 calls for.
///
/// <code>
///   sequence-eras F:/tf2-builds/tf2-2008/tf
///   sequence-eras F:/tf2-builds/tf2-2013/TF2/tf/tf2_misc_dir.vpk 100
/// </code>
///
/// **A loose folder or a VPK**, because the period installs store their content both ways: 2007 and
/// 2008 loose, 2013 in VPKs. 2011 is in `.gcf` archives, which nothing here reads.
///
/// **Compared by label, case-insensitively**, because the question is which NAME an index meant. A model
/// whose labels match but whose animation data changed counts as identical here, and that is the half
/// D160 says it does not recover.
/// </remarks>
public sealed class SequenceEraProbe : IProbe
{
    private const int DefaultExamples = 40;

    private const int MaximumIncludeDepth = 8;

    /// <inheritdoc/>
    public string Name => "sequence-eras";

    /// <inheritdoc/>
    public string Summary =>
        "models an older install orders sequences differently from today's: sequence-eras <tf folder or _dir.vpk> [examples]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            output.WriteLine("sequence-eras <tf folder or _dir.vpk> [examples] — for example: sequence-eras F:/tf2-builds/tf2-2008/tf");
            return;
        }

        string source = arguments[0];
        int examples = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultExamples;

        string? folder = new MapLocator(
            MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder();

        if (folder is null)
        {
            output.WriteLine("The game is not installed, so there is nothing to compare against.");
            return;
        }

        GameContent today = GameContent.Open(folder, NullLoggerFactory.Instance);
        (IEnumerable<string> paths, Func<string, byte[]?> read) = Open(source);

        int compared = 0;
        int identical = 0;
        int absentToday = 0;
        int unreadable = 0;
        List<string> differing = [];

        foreach (string path in paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (today.Archives.Read(path) is not { } now)
            {
                absentToday++;
                continue;
            }

            if (read(path) is not { } then)
            {
                unreadable++;
                continue;
            }

            List<string> old;
            List<string> current;

            try
            {
                old = Labels(then, read);
                current = Labels(now, today.Archives.Read);
            }
            catch (InvalidDataException failure)
            {
                // Counted and named rather than skipped: an unreadable old model is an absence in the
                // census, and the total has to say how much of the install it did not see.
                unreadable++;
                output.WriteLine($"  unreadable {path}: {failure.Message}");
                continue;
            }

            compared++;

            if (old.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            {
                identical++;
                continue;
            }

            differing.Add(Describe(path, old, current));
        }

        output.WriteLine(
            $"{source}: {compared} models compared with today's, {identical} identical, "
            + $"{differing.Count} differ; {absentToday} absent today, {unreadable} unreadable");

        foreach (string line in differing.Take(examples))
        {
            output.WriteLine(line);
        }

        if (differing.Count > examples)
        {
            output.WriteLine($"  … {differing.Count - examples} more not shown");
        }
    }

    /// <summary>The model paths a source holds, and how to read one.</summary>
    private static (IEnumerable<string> Paths, Func<string, byte[]?> Read) Open(string source)
    {
        if (source.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            VpkArchive archive = VpkArchive.Open(source);

            return (
                archive.Paths.Where(path => path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)),
                archive.ReadFile);
        }

        string root = Path.GetFullPath(source);

        return (
            Directory.EnumerateFiles(Path.Combine(root, "models"), "*.mdl", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')),
            path => File.ReadAllBytes(Path.Combine(root, path)));
    }

    /// <summary>
    /// Each sequence's label in the order a demo's <c>m_nSequence</c> indexes: the model's own, then
    /// every model it includes, merged by label.
    /// </summary>
    /// <remarks>
    /// **The merged list, not the file's own**, because the arms models declare one sequence and take
    /// the rest from `c_*_animations` — comparing the files alone measured a table no demo indexes.
    /// Included models are gathered the way <c>virtualmodel_t::AppendModels</c> walks them
    /// (`studio_virtualmodel.cpp:81-137`): the model's own sequences, then each include in turn, each
    /// one's own includes before the next sibling. An include the source does not hold is skipped, as
    /// the engine skips one `FindModel` cannot find (`:111-124`). The merge is `StudioSequenceTable`'s.
    /// </remarks>
    private static List<string> Labels(byte[] bytes, Func<string, byte[]?> read)
    {
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups = [];

        Gather(bytes, read, groups, depth: 0);

        StudioSequenceTable table = StudioSequenceTable.Merge(groups);
        List<string> labels = new(table.Count);

        for (int index = 0; index < table.Count; index++)
        {
            (int group, int local) = table.At(index)!.Value;
            labels.Add(groups[group].Sequences[local].Label);
        }

        return labels;
    }

    /// <summary>A model's own sequences as the next group, then its includes', depth first.</summary>
    private static void Gather(
        byte[] bytes,
        Func<string, byte[]?> read,
        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups,
        int depth)
    {
        groups.Add((groups.Count, StudioSequences.Read(bytes)));

        // A guard against a model that includes itself, which a real one does not.
        if (depth >= MaximumIncludeDepth)
        {
            return;
        }

        foreach (string include in StudioModelGroups.Read(bytes))
        {
            if (read(include) is { } included)
            {
                Gather(included, read, groups, depth + 1);
            }
        }
    }

    /// <summary>One line naming where two lists first part.</summary>
    private static string Describe(string path, List<string> old, List<string> current)
    {
        int at = 0;

        while (at < old.Count && at < current.Count &&
            string.Equals(old[at], current[at], StringComparison.OrdinalIgnoreCase))
        {
            at++;
        }

        string then = at < old.Count ? old[at] : "(end)";
        string now = at < current.Count ? current[at] : "(end)";

        return $"  {path}: {old.Count} then, {current.Count} now; first differs at {at}: '{then}' then, '{now}' now";
    }
}
