using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using static System.FormattableString;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>One file the census found, and what each stage made of it: one CSV row.</summary>
/// <remarks>
/// Cells are strings keyed by column, because a row is written once to CSV and read back twice — by a
/// resumed run, to know what is done, and by the summary. <see cref="Columns"/> is the one order both use.
///
/// **A stage is `pass`, `fail` or `skip`, and a skip always says which kind** — `none` (the demo holds
/// nothing for this stage, such as no voice), `blocked` (a stage it depends on failed), `budget` (the
/// memory it would take is over the run's bound), `unavailable` (a native decoder is not on this
/// machine), `not asked` (left out by `--stages`). Only `none` counts toward "passes every stage": the
/// others are a stage that did not measure the demo, and a census that counted them as passes would be
/// the silent drop B440 was.
/// </remarks>
internal sealed class CensusRow
{
    /// <summary>Each stage and the counts that show it did work, in the order the stages run.</summary>
    private static readonly (string Stage, string[] Counts)[] StageCounts =
    [
        ("container", ["commands", "packets", "signons", "usercmds", "datatables", "stop", "truncated"]),
        ("schema", ["tables", "classes"]),
        ("messages", ["messages", "messages_exact", "message_bits", "unwritable_bits", "reader_stops", "mismatches", "game_events"]),
        ("entities", ["snapshots", "snapshots_exact", "entities", "snapshot_throws", "slack_bits"]),
        ("trace", ["trace_lines", "trace_chars", "trace_packets", "trace_stopped", "trace_undecoded", "trace_anonymous"]),
        ("assembly", ["assembly_bytes", "rebuilt_bytes", "first_difference"]),
        ("timeline", ["timeline_frames", "timeline_props", "timeline_players", "timeline_events", "timeline_mb", "timeline_ratio"]),
        ("voice", ["voice_codec", "voice_packets", "voice_frames", "voice_silent", "voice_speakers", "voice_bad"]),
    ];

    /// <summary>The stages, in the order they run and the order a demo's first failure is taken in.</summary>
    public static readonly IReadOnlyList<string> Stages = [.. StageCounts.Select(entry => entry.Stage)];

    /// <summary>Every column, in file order.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "path", "root", "status", "reason", "sha256", "bytes", "duplicate_of",
        "demo_protocol", "network_protocol", "view", "server", "map", "game",
        "header_ticks", "header_frames", "header_seconds",
        .. StageCounts.SelectMany(entry => (string[])
        [
            entry.Stage + "_status", entry.Stage + "_skip", entry.Stage + "_shape",
            entry.Stage + "_detail", entry.Stage + "_where", entry.Stage + "_seconds",
            .. entry.Counts,
        ]),
        "first_stage", "first_shape", "first_detail", "first_where",
        "seconds", "working_set_peak_mb", "commit_peak_mb", "finished",
    ];

    private readonly Dictionary<string, string> _cells = new(StringComparer.Ordinal);

    /// <summary>A cell, or the empty string when it was never set.</summary>
    public string this[string column]
    {
        get => _cells.TryGetValue(column, out string? value) ? value : string.Empty;
        set => _cells[column] = value;
    }

    /// <summary>Sets a count.</summary>
    public void Set(string column, long value) => _cells[column] = value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Sets a measurement, to three places.</summary>
    public void Set(string column, double value) =>
        _cells[column] = value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The stage did its work and every check held; the detail names the control count.</summary>
    public void Pass(string stage, string detail)
    {
        this[stage + "_status"] = "pass";
        this[stage + "_detail"] = detail;
    }

    /// <summary>The stage found a defect.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="shape">What the failure IS, without its numbers — the key failures are grouped by.</param>
    /// <param name="detail">The failure with its numbers, as the production code reported it.</param>
    /// <param name="where">Tick, command, byte or bit — as much as the stage knows.</param>
    public void Fail(string stage, string shape, string detail, string where)
    {
        this[stage + "_status"] = "fail";
        this[stage + "_shape"] = Shape(shape);
        this[stage + "_detail"] = detail;
        this[stage + "_where"] = where;
    }

    /// <summary>The stage did not measure this demo, and why.</summary>
    public void Skip(string stage, string kind, string reason)
    {
        this[stage + "_status"] = "skip";
        this[stage + "_skip"] = kind;
        this[stage + "_detail"] = reason;
    }

    /// <summary>A stage's status: pass, fail, skip, or empty when it never ran.</summary>
    public string Status(string stage) => this[stage + "_status"];

    /// <summary>Copies one stage's cells from another row, for merging a later pass over the same demo.</summary>
    public void TakeStage(CensusRow from, string stage)
    {
        ArgumentNullException.ThrowIfNull(from);

        foreach (string column in StageColumns(stage))
        {
            this[column] = from[column];
        }
    }

    /// <summary>Fills the first-failure cells from the stages, in stage order.</summary>
    public void TakeFirstFailure()
    {
        string? first = Stages.FirstOrDefault(stage => Status(stage) == "fail");

        this["first_stage"] = first ?? string.Empty;
        this["first_shape"] = first is null ? string.Empty : this[first + "_shape"];
        this["first_detail"] = first is null ? string.Empty : this[first + "_detail"];
        this["first_where"] = first is null ? string.Empty : this[first + "_where"];
    }

    /// <summary>A failure's text with its numbers taken out, so two demos failing the same way share a key.</summary>
    /// <remarks>
    /// **Digits become `#` and nothing else changes.** A reader stop at bit 1610 and one at bit 1712 are the
    /// same finding; a stop and a length mismatch are not. Capped, because a shape is a key and not a report.
    /// </remarks>
    public static string Shape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder shape = new(Math.Min(text.Length, 160));
        bool inNumber = false;

        foreach (char character in text)
        {
            if (shape.Length >= 160)
            {
                break;
            }

            bool digit = char.IsAsciiDigit(character);

            if (!digit || !inNumber)
            {
                shape.Append(digit ? '#' : character);
            }

            inNumber = digit;
        }

        return shape.ToString();
    }

    /// <summary>One stage's columns: its outcome and its counts.</summary>
    private static IEnumerable<string> StageColumns(string stage)
    {
        string prefix = stage + "_";

        return Columns.Where(column => column.StartsWith(prefix, StringComparison.Ordinal))
            .Concat(StageCounts.First(entry => entry.Stage == stage).Counts);
    }
}

/// <summary>Reads and appends the census CSV.</summary>
/// <remarks>
/// **Appended a row at a time and flushed, so a run that dies loses one demo, not the census.** A resumed
/// run reads what is there and carries on; the rows are keyed by path, and a later row for the same path
/// replaces an earlier one.
/// </remarks>
internal static class CensusCsv
{
    /// <summary>Every row in the file, keyed by path; empty when there is no file.</summary>
    public static Dictionary<string, CensusRow> Read(string path)
    {
        Dictionary<string, CensusRow> rows = new(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return rows;
        }

        using StreamReader reader = new(path);
        string? headerLine = reader.ReadLine();

        if (headerLine is null)
        {
            return rows;
        }

        List<string> header = Split(headerLine);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            List<string> cells = Split(line);
            CensusRow row = new();

            for (int index = 0; index < header.Count && index < cells.Count; index++)
            {
                row[header[index]] = cells[index];
            }

            rows[row["path"]] = row;
        }

        return rows;
    }

    /// <summary>Appends one row, writing the header first if the file is new.</summary>
    public static void Append(string path, CensusRow row)
    {
        bool fresh = !File.Exists(path) || new FileInfo(path).Length == 0;
        StringBuilder text = new();

        if (fresh)
        {
            text.AppendJoin(',', CensusRow.Columns).Append('\n');
        }

        text.AppendJoin(',', CensusRow.Columns.Select(column => Escape(row[column]))).Append('\n');
        File.AppendAllText(path, text.ToString());
    }

    /// <summary>Quotes a cell when it holds a comma or a quote; control characters become spaces, so a row is a line.</summary>
    /// <remarks>
    /// Line breaks for the row, and every other control character too: a string read from a misaligned stream (B440's
    /// voice codec) carries them, and neither a CSV nor the summary's Markdown can show one.
    /// </remarks>
    private static string Escape(string value)
    {
        string flat = string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character));

        return flat.Contains(',', StringComparison.Ordinal) || flat.Contains('"', StringComparison.Ordinal)
            ? "\"" + flat.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : flat;
    }

    /// <summary>Splits one CSV line, honouring quotes.</summary>
    private static List<string> Split(string line)
    {
        List<string> cells = [];
        StringBuilder cell = new();
        bool quoted = false;
        int index = 0;

        while (index < line.Length)
        {
            char character = line[index];
            index++;

            if (quoted && character == '"' && index < line.Length && line[index] == '"')
            {
                cell.Append('"');
                index++;
            }
            else if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(character);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}

/// <summary>The census, summarised: membership, the headline, and the failures grouped by class.</summary>
internal static class CensusSummary
{
    private const int NamesShown = 12;

    /// <summary>The statuses listed file by file at the end, with their headings.</summary>
    private static readonly (string Status, string Title)[] ListedStatuses =
    [
        ("excluded", "Excluded"),
        ("duplicate", "Duplicates (decoded once, as the first path)"),
        ("unreached", "Not reached"),
    ];

    /// <summary>Merges the CSVs — a later file's pass or fail replaces an earlier file's skip — and summarises.</summary>
    /// <param name="csvPaths">CSVs from one or more runs over the same pool, earliest first.</param>
    /// <returns>The summary as Markdown.</returns>
    public static string Build(IReadOnlyList<string> csvPaths)
    {
        Dictionary<string, CensusRow> merged = new(StringComparer.Ordinal);

        foreach (string csv in csvPaths)
        {
            foreach ((string path, CensusRow row) in CensusCsv.Read(csv))
            {
                if (!merged.TryGetValue(path, out CensusRow? existing))
                {
                    merged[path] = row;
                    continue;
                }

                foreach (string stage in CensusRow.Stages.Where(stage => row.Status(stage) is "pass" or "fail"))
                {
                    existing.TakeStage(row, stage);
                }

                existing.TakeFirstFailure();
            }
        }

        StringBuilder text = new();
        List<CensusRow> rows = [.. merged.Values.OrderBy(row => row["path"], StringComparer.Ordinal)];
        List<CensusRow> demos = [.. rows.Where(row => row["status"] == "demo")];

        text.Append(Invariant($"# Decode census\n\nFrom {string.Join(", ", csvPaths)}.\n\n"));
        Membership(text, rows, demos);
        Headline(text, demos);
        Protocols(text, demos);
        PerStage(text, demos);
        Classes(text, demos);
        EveryFailure(text, demos);
        Measurements(text, demos);
        Lists(text, rows);

        return text.ToString();
    }

    private static void Membership(StringBuilder text, List<CensusRow> rows, List<CensusRow> demos)
    {
        text.Append("## Membership\n\n| root | files | TF2 demos (unique) | duplicates | excluded |\n|---|---|---|---|---|\n");

        foreach (IGrouping<string, CensusRow> root in rows.Where(row => row["status"] != "unreached")
                     .GroupBy(row => row["root"], StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"| `{root.Key}` | {root.Count()} | {root.Count(row => row["status"] == "demo")} | " +
                $"{root.Count(row => row["status"] == "duplicate")} | {root.Count(row => row["status"] == "excluded")} |\n");
        }

        long bytes = demos.Sum(row => Long(row, "bytes"));
        text.Append(
            CultureInfo.InvariantCulture,
            $"\n**M = {demos.Count} unique TF2 demos** ({bytes / 1048576.0:N0} MB), from {rows.Count(row => row["status"] != "unreached")} files; " +
            $"{rows.Count(row => row["status"] == "duplicate")} duplicates by SHA-256, " +
            $"{rows.Count(row => row["status"] == "excluded")} excluded, " +
            $"{rows.Count(row => row["status"] == "unreached")} paths not reached (listed at the end).\n\n");
    }

    private static void Headline(StringBuilder text, List<CensusRow> demos)
    {
        int every = demos.Count(row => CensusRow.Stages.All(stage => Passed(row, stage)));
        int everyButTail = demos.Count(row => CensusRow.Stages.All(stage =>
            Passed(row, stage) ||
            (stage == "assembly" && row["assembly_shape"].StartsWith(DemoCensus.CutTailShape, StringComparison.Ordinal))));
        int unmeasured = demos.Count(row =>
            !CensusRow.Stages.Any(stage => row.Status(stage) == "fail") &&
            !CensusRow.Stages.All(stage => Passed(row, stage)));

        text.Append("## Headline\n\n");
        text.Append(Invariant($"**{every} of {demos.Count} demos pass every stage**, the round trip byte for byte.\n\n"));
        text.Append(
            CultureInfo.InvariantCulture,
            $"{everyButTail} of {demos.Count} pass if a round trip that rebuilds all but a final command cut off mid-write " +
            $"counts, which is how `EveryDemo_CompilesBackToItsOwnBytes` reads it (a prefix of the file).\n\n");
        text.Append(Invariant(
            $"{unmeasured} fail nothing but were not measured by every stage (a budget, unavailable or not-asked skip).\n\n"));
    }

    /// <summary>The pool by network protocol and point of view — the era axis `docs/TIMELINE.md` keeps.</summary>
    private static void Protocols(StringBuilder text, List<CensusRow> demos)
    {
        text.Append("## Protocols\n\n| network protocol | demos | SourceTV | POV | pass every stage | MB |\n|---|---|---|---|---|---|\n");

        foreach (IGrouping<long, CensusRow> protocol in demos.GroupBy(row => Long(row, "network_protocol")).OrderBy(group => group.Key))
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"| {protocol.Key} | {protocol.Count()} | {protocol.Count(row => row["view"] == "SourceTV")} | " +
                $"{protocol.Count(row => row["view"] == "POV")} | " +
                $"{protocol.Count(row => CensusRow.Stages.All(stage => Passed(row, stage)))} | " +
                $"{protocol.Sum(row => Long(row, "bytes")) / 1048576.0:N0} |\n");
        }

        text.Append('\n');
    }

    private static void PerStage(StringBuilder text, List<CensusRow> demos)
    {
        text.Append("## Per stage\n\n| stage | pass | fail | nothing to do | blocked | budget | unavailable | not asked | seconds |\n");
        text.Append("|---|---|---|---|---|---|---|---|---|\n");

        foreach (string stage in CensusRow.Stages)
        {
            text.Append(
                CultureInfo.InvariantCulture,
                $"| {stage} | {demos.Count(row => row.Status(stage) == "pass")} | {demos.Count(row => row.Status(stage) == "fail")} | " +
                $"{Skips(demos, stage, "none")} | {Skips(demos, stage, "blocked")} | {Skips(demos, stage, "budget")} | " +
                $"{Skips(demos, stage, "unavailable")} | {Skips(demos, stage, "not asked")} | " +
                $"{demos.Sum(row => Double(row, stage + "_seconds")):N0} |\n");
        }

        text.Append('\n');
    }

    private static void Classes(StringBuilder text, List<CensusRow> demos)
    {
        text.Append("## Failure classes, by each demo's FIRST failure\n\n");
        text.Append("| class | demos | protocols | views | smallest specimen | where it first fails |\n|---|---|---|---|---|---|\n");

        foreach (IGrouping<string, CensusRow> group in demos.Where(row => row["first_stage"].Length > 0)
                     .GroupBy(row => row["first_stage"] + ": " + row["first_shape"], StringComparer.Ordinal)
                     .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            CensusRow smallest = group.MinBy(row => Long(row, "bytes"))!;

            text.Append(
                CultureInfo.InvariantCulture,
                $"| {Cell(group.Key)} | {group.Count()} | {Distinct(group, "network_protocol")} | {Distinct(group, "view")} | " +
                $"`{Path.GetFileName(smallest["path"])}` ({Long(smallest, "bytes") / 1048576.0:N1} MB) | {Cell(smallest["first_where"])} |\n");
        }

        text.Append("\nThe demos in each class:\n\n");

        foreach (IGrouping<string, CensusRow> group in demos.Where(row => row["first_stage"].Length > 0)
                     .GroupBy(row => row["first_stage"] + ": " + row["first_shape"], StringComparer.Ordinal)
                     .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            text.Append(Invariant($"- **{group.Key}** — {Names(group)}\n"));
        }

        text.Append('\n');
    }

    private static void EveryFailure(StringBuilder text, List<CensusRow> demos)
    {
        text.Append("## Every failure, by stage\n\nA demo's later failures, which grouping by the first one hides.\n\n");
        text.Append("| stage | shape | demos | names |\n|---|---|---|---|\n");

        foreach (string stage in CensusRow.Stages)
        {
            foreach (IGrouping<string, CensusRow> group in demos.Where(row => row.Status(stage) == "fail")
                         .GroupBy(row => row[stage + "_shape"], StringComparer.Ordinal).OrderByDescending(group => group.Count()))
            {
                text.Append(Invariant($"| {stage} | {Cell(group.Key)} | {group.Count()} | {Names(group)} |\n"));
            }
        }

        text.Append('\n');
    }

    private static void Measurements(StringBuilder text, List<CensusRow> demos)
    {
        text.Append("## Measurements\n\n");

        List<CensusRow> timed = [.. demos.Where(row => row["finished"].Length > 0)];

        if (timed.Count > 0)
        {
            DateTime last = timed.Max(row => DateTime.Parse(row["finished"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
            text.Append(Invariant(
                $"- Decoding time, summed over demos: {demos.Sum(row => Double(row, "seconds")) / 3600:N2} h; last demo finished {last:u}.\n"));
        }

        text.Append(
            CultureInfo.InvariantCulture,
            $"- Census process peak: working set {demos.Select(row => Long(row, "working_set_peak_mb")).DefaultIfEmpty().Max():N0} MB, " +
            $"commit {demos.Select(row => Long(row, "commit_peak_mb")).DefaultIfEmpty().Max():N0} MB (the OS's own counters, per process).\n");

        List<CensusRow> timelines = [.. demos.Where(row => row["timeline_ratio"].Length > 0).OrderBy(row => Double(row, "timeline_ratio"))];

        if (timelines.Count > 0)
        {
            CensusRow low = timelines[0];
            CensusRow median = timelines[timelines.Count / 2];
            CensusRow high = timelines[^1];

            text.Append(
                CultureInfo.InvariantCulture,
                $"- Timeline live heap over demo size, {timelines.Count} demos: {Double(low, "timeline_ratio"):N1}x " +
                $"(`{Path.GetFileName(low["path"])}`), median {Double(median, "timeline_ratio"):N1}x, " +
                $"{Double(high, "timeline_ratio"):N1}x (`{Path.GetFileName(high["path"])}`, {Double(high, "timeline_mb"):N0} MB).\n");
        }

        text.Append('\n');
    }

    private static void Lists(StringBuilder text, List<CensusRow> rows)
    {
        foreach ((string status, string title) in ListedStatuses)
        {
            List<CensusRow> listed = [.. rows.Where(row => row["status"] == status)];

            if (listed.Count == 0)
            {
                continue;
            }

            text.Append(Invariant($"## {title}\n\n"));

            foreach (CensusRow row in listed)
            {
                string why = status == "duplicate" ? "same bytes as `" + row["duplicate_of"] + "`" : row["reason"];
                text.Append(Invariant($"- `{row["path"]}` — {why}\n"));
            }

            text.Append('\n');
        }
    }

    /// <summary>Whether a stage passed, or had nothing to do — the only two outcomes "passes every stage" allows.</summary>
    private static bool Passed(CensusRow row, string stage) =>
        row.Status(stage) == "pass" || (row.Status(stage) == "skip" && row[stage + "_skip"] == "none");

    private static int Skips(List<CensusRow> demos, string stage, string kind) =>
        demos.Count(row => row.Status(stage) == "skip" && row[stage + "_skip"] == kind);

    private static string Distinct(IEnumerable<CensusRow> rows, string column) =>
        string.Join(" ", rows.Select(row => row[column]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    private static string Names(IEnumerable<CensusRow> rows)
    {
        List<string> names = [.. rows.OrderBy(row => Long(row, "bytes")).Select(row => "`" + Path.GetFileName(row["path"]) + "`")];

        return names.Count <= NamesShown
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(NamesShown)) + Invariant($" and {names.Count - NamesShown} more");
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static long Long(CensusRow row, string column) =>
        long.TryParse(row[column], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

    private static double Double(CensusRow row, string column) =>
        double.TryParse(row[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}
