using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Tf2DemoSalvage.Core.Container;

using static System.FormattableString;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// D200's measure: every demo under the given paths, through every stage of the production decode, one at a time.
/// </summary>
/// <remarks>
/// <code>
///   decode-census &lt;dir-or-file&gt;... --csv out.csv [--budget-mb 4096] [--stages messages,entities,...]
///                 [--temp &lt;dir&gt;] [--summary out.md]
///   decode-census --summarize a.csv [b.csv ...] [--summary out.md]
/// </code>
///
/// The voice stage needs speex.dll and celt.dll beside the probe: build it with
/// `-p:NativeAudioDirectory=&lt;a folder that has them&gt;` (the csproj says why), or the Speex and CELT demos are
/// `unavailable` skips that name the missing library.
///
/// **Nothing it finds is dropped silently** (B440: `FilesWithSchema` dropped an undecodable demo from every
/// sweep). Every file is a row: a `demo` with each stage's outcome, a `duplicate` (same SHA-256 as a demo
/// already decoded — decoded once), `excluded` with its reason (not a Source demo, or a game directory other than
/// `tf`), or `unreached` with its reason (a compressed archive, an unreadable directory, a link not followed).
/// A file with any extension is a candidate if it starts with the `HL2DEMO` stamp.
///
/// **One demo at a time, and its memory is released before the next**: its bytes, commands, text and timeline
/// never outlive its row, and a full compacting collection runs between demos. Anything whose predicted peak
/// passes `--budget-mb` is skipped as `budget`, never attempted (`DemoCensus` has the multiples).
///
/// **It resumes.** Rows are appended as each demo finishes; a rerun with the same `--csv` skips paths already
/// there. Each stage is logged to `&lt;csv&gt;.log` before it starts, so a process that dies — a native voice decoder
/// can take it down — leaves the demo and the stage named, and the next run records that demo as a failure of
/// that stage rather than dying on it again.
///
/// It asserts nothing — a measurement is not a test (D38); the rows are the result.
/// </remarks>
public sealed class DecodeCensusProbe : IProbe
{
    private const long Megabyte = 1 << 20;

    /// <summary>Extensions of archives a demo may sit inside, which the census reports rather than opens.</summary>
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z", ".gz", ".bz2", ".xz", ".tar", ".tgz"];

    /// <inheritdoc/>
    public string Name => "decode-census";

    /// <inheritdoc/>
    public string Summary =>
        "every demo under the paths through every decode stage, one at a time, to CSV (D200): " +
        "decode-census <dir-or-file>... --csv <out.csv> [--budget-mb N] [--stages a,b] | " +
        "decode-census --summarize <a.csv>...";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        CensusOptions? options = CensusOptions.Parse(arguments, output);

        if (options is null)
        {
            return;
        }

        if (!options.SummarizeOnly)
        {
            Census(options, output);
        }

        string summary = CensusSummary.Build(options.SummarizeOnly ? options.Roots : [options.CsvPath]);
        File.WriteAllText(options.SummaryPath, summary);
        output.Write(summary);
        output.WriteLine(Invariant($"summary written to {options.SummaryPath}"));
    }

    private static void Census(CensusOptions options, TextWriter output)
    {
        string logPath = options.CsvPath + ".log";
        Dictionary<string, CensusRow> done = CensusCsv.Read(options.CsvPath);
        RecordADeath(logPath, done, options, output);

        Dictionary<string, string> seen = new(StringComparer.Ordinal);

        foreach (CensusRow row in done.Values.Where(row => row["status"] == "demo" && row["sha256"].Length > 0))
        {
            seen.TryAdd(row["sha256"], row["path"]);
        }

        List<(string Path, string Root, string Reason)> unreached = [];
        List<Candidate> candidates = Enumerate(options.Roots, unreached);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{candidates.Count} candidate files ({candidates.Sum(candidate => candidate.Length) / Megabyte:N0} MB), " +
            $"{unreached.Count} paths not reached, {done.Count} rows already in {options.CsvPath}"));

        foreach ((string path, string root, string reason) in unreached.Where(entry => !done.ContainsKey(entry.Path)))
        {
            CensusRow row = new();
            row["path"] = path;
            row["root"] = root;
            row["status"] = "unreached";
            row["reason"] = reason;
            CensusCsv.Append(options.CsvPath, row);
        }

        using StreamWriter log = new(logPath, append: true);
        log.AutoFlush = true;
        log.NewLine = "\n";

        for (int index = 0; index < candidates.Count; index++)
        {
            Candidate candidate = candidates[index];

            if (done.ContainsKey(candidate.Path))
            {
                continue;
            }

            log.WriteLine(Invariant($"{DateTime.UtcNow:O} BEGIN {candidate.Path}"));
            CensusRow row = Examine(candidate, options, seen, log);
            CensusCsv.Append(options.CsvPath, row);
            log.WriteLine(Invariant($"{DateTime.UtcNow:O} END {candidate.Path}"));
            output.WriteLine(Line(index + 1, candidates.Count, row));

            // Released before the next demo, and handed back to the OS: the next may be the largest.
            _ = DemoCensus.LiveHeap();
        }
    }

    /// <summary>One file: excluded, duplicate, or a demo through every stage.</summary>
    private static CensusRow Examine(Candidate candidate, CensusOptions options, Dictionary<string, string> seen, TextWriter log)
    {
        long started = Stopwatch.GetTimestamp();
        CensusRow row = new();
        row["path"] = candidate.Path;
        row["root"] = candidate.Root;
        row.Set("bytes", candidate.Length);

        try
        {
            if (Admit(candidate, row) is { } header)
            {
                Decode(candidate, header, row, options, seen, log);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            row["status"] = "unreached";
            row["reason"] = "could not be read: " + error.Message;
        }

        row.TakeFirstFailure();
        row.Set("seconds", Stopwatch.GetElapsedTime(started).TotalSeconds);

        using Process self = Process.GetCurrentProcess();
        row.Set("working_set_peak_mb", self.PeakWorkingSet64 / Megabyte);
        row.Set("commit_peak_mb", self.PeakPagedMemorySize64 / Megabyte);
        row["finished"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return row;
    }

    /// <summary>The demo's header when the file is a TF2 demo, else null with the row marked excluded and why.</summary>
    private static DemoHeader? Admit(Candidate candidate, CensusRow row)
    {
        byte[] start = new byte[Math.Min(candidate.Length, DemoHeader.SizeBytes)];

        using (FileStream stream = File.OpenRead(candidate.Path))
        {
            stream.ReadExactly(start);
        }

        string? excluded = null;
        DemoHeader? header = null;

        if (start.Length < DemoHeader.SizeBytes)
        {
            excluded = Encoding.ASCII.GetString(start).StartsWith("version https://git-lfs", StringComparison.Ordinal)
                ? "a Git LFS pointer, not the demo it names"
                : Invariant($"{start.Length} bytes, shorter than the {DemoHeader.SizeBytes}-byte header");
        }
        else
        {
            try
            {
                header = DemoHeader.Parse(start);
            }
            catch (InvalidDataException error)
            {
                excluded = "not a Source demo: " + error.Message;
            }
        }

        if (header is not null && !string.Equals(header.GameDirectory, "tf", StringComparison.OrdinalIgnoreCase))
        {
            excluded = "game directory '" + header.GameDirectory + "', not tf";
        }

        if (excluded is not null)
        {
            row["status"] = "excluded";
            row["reason"] = excluded;
            return null;
        }

        return header;
    }

    /// <summary>Reads the demo, sets aside a duplicate, and runs every stage over the rest.</summary>
    private static void Decode(
        Candidate candidate, DemoHeader header, CensusRow row, CensusOptions options, Dictionary<string, string> seen, TextWriter log)
    {
        row["status"] = "demo";
        row.Set("demo_protocol", header.DemoProtocol);
        row.Set("network_protocol", header.NetworkProtocol);
        row["view"] = header.ClientName == "SourceTV Demo" ? "SourceTV" : "POV";
        row["server"] = header.ServerName;
        row["map"] = header.MapName;
        row["game"] = header.GameDirectory;
        row.Set("header_ticks", header.PlaybackTicks);
        row.Set("header_frames", header.PlaybackFrames);
        row.Set("header_seconds", header.PlaybackTimeSeconds);

        if (candidate.Length > Array.MaxLength)
        {
            // Not a budget question: every reader here takes the whole file as one array, so no budget reads it.
            row.Fail("container", "larger than one .NET array", Invariant($"{candidate.Length} bytes; File.ReadAllBytes holds at most {Array.MaxLength}"), string.Empty);
            return;
        }

        if (!DemoCensus.Fits(candidate.Length, options.BudgetBytes))
        {
            foreach (string stage in CensusRow.Stages)
            {
                row.Skip(stage, "budget", Invariant($"holding a {candidate.Length / Megabyte:N0} MB demo passes the {options.BudgetBytes / Megabyte:N0} MB budget"));
            }

            return;
        }

        byte[] bytes = File.ReadAllBytes(candidate.Path);
        string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        row["sha256"] = sha;

        if (seen.TryGetValue(sha, out string? original))
        {
            row["status"] = "duplicate";
            row["duplicate_of"] = original;
            return;
        }

        seen[sha] = candidate.Path;
        new DemoCensus(row, bytes, header, options, log).Run();
    }

    /// <summary>
    /// Every candidate under the roots, smallest first, and every path that could not be looked into.
    /// </summary>
    /// <remarks>
    /// Smallest first so an interrupted run has covered the most demos, and the largest — which can take the most
    /// memory — come last. Directories are walked by hand rather than with `EnumerateFiles`, whose
    /// `IgnoreInaccessible` would drop an unreadable folder without a word.
    /// </remarks>
    private static List<Candidate> Enumerate(IReadOnlyList<string> roots, List<(string Path, string Root, string Reason)> unreached)
    {
        List<Candidate> found = [];

        foreach (string root in roots)
        {
            if (File.Exists(root))
            {
                Consider(new FileInfo(root), root, found, unreached);
                continue;
            }

            if (!Directory.Exists(root))
            {
                unreached.Add((root, root, "no such file or directory"));
                continue;
            }

            Stack<DirectoryInfo> pending = new();
            pending.Push(new DirectoryInfo(root));

            while (pending.Count > 0)
            {
                Walk(pending.Pop(), root, pending, found, unreached);
            }
        }

        return [.. found.OrderBy(candidate => candidate.Length).ThenBy(candidate => candidate.Path, StringComparer.Ordinal)];
    }

    private static void Walk(
        DirectoryInfo directory, string root, Stack<DirectoryInfo> pending, List<Candidate> found, List<(string Path, string Root, string Reason)> unreached)
    {
        FileSystemInfo[] entries;

        try
        {
            entries = directory.GetFileSystemInfos();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            unreached.Add((directory.FullName, root, "an unreadable directory: " + error.Message));
            return;
        }

        foreach (FileSystemInfo entry in entries)
        {
            if (entry is FileInfo file)
            {
                Consider(file, root, found, unreached);
            }
            else if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                unreached.Add((entry.FullName, root, "a junction or link, not followed"));
            }
            else
            {
                pending.Push((DirectoryInfo)entry);
            }
        }
    }

    private static void Consider(FileInfo file, string root, List<Candidate> found, List<(string Path, string Root, string Reason)> unreached)
    {
        bool demo = string.Equals(file.Extension, ".dem", StringComparison.OrdinalIgnoreCase);

        if (!demo && ArchiveExtensions.Any(extension => string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase)))
        {
            unreached.Add((file.FullName, root, Invariant($"a compressed archive ({file.Length / (double)Megabyte:N1} MB), not opened: any demo inside it is not counted")));
            return;
        }

        if (!demo && file.Length >= DemoHeader.SizeBytes)
        {
            try
            {
                demo = HasDemoStamp(file);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                unreached.Add((file.FullName, root, "could not be opened to look for a demo stamp: " + error.Message));
                return;
            }
        }

        if (demo)
        {
            found.Add(new Candidate(file.FullName, file.Length, root));
        }
    }

    /// <summary>Whether a file starts with `HL2DEMO` and its terminator, the stamp every Source demo opens with.</summary>
    private static bool HasDemoStamp(FileInfo file)
    {
        Span<byte> stamp = stackalloc byte[8];

        using FileStream stream = file.OpenRead();
        stream.ReadExactly(stamp);

        return stamp.SequenceEqual("HL2DEMO\0"u8);
    }

    /// <summary>
    /// Records the demo a previous run died on, as a failure of the stage it died in, so the next run moves past it.
    /// </summary>
    private static void RecordADeath(string logPath, Dictionary<string, CensusRow> done, CensusOptions options, TextWriter output)
    {
        if (!File.Exists(logPath))
        {
            return;
        }

        string? current = null;
        string? stage = null;

        foreach (string line in File.ReadLines(logPath))
        {
            int space = line.IndexOf(' ', StringComparison.Ordinal);
            string entry = space < 0 ? line : line[(space + 1)..];

            if (entry.StartsWith("BEGIN ", StringComparison.Ordinal))
            {
                current = entry["BEGIN ".Length..];
                stage = null;
            }
            else if (entry.StartsWith("STAGE ", StringComparison.Ordinal))
            {
                stage = entry["STAGE ".Length..];
            }
            else if (entry.StartsWith("END ", StringComparison.Ordinal))
            {
                current = null;
            }
        }

        if (current is null || done.ContainsKey(current))
        {
            return;
        }

        CensusRow row = new();
        row["path"] = current;
        row["root"] = options.Roots.Where(root => current.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(root => root.Length).FirstOrDefault() ?? string.Empty;
        row["status"] = "demo";
        row["reason"] = "the census process ended while decoding this demo";
        row.Set("bytes", File.Exists(current) ? new FileInfo(current).Length : 0);
        row.Fail(stage ?? "container", "the census process ended in this stage",
            "no result was written: the process died here (a native crash, a stack overflow) or was stopped", string.Empty);
        row.TakeFirstFailure();

        CensusCsv.Append(options.CsvPath, row);
        done[current] = row;
        output.WriteLine("a previous run ended during " + (stage ?? "the start") + " of " + current + "; recorded as a failure there");
    }

    /// <summary>The progress line for one finished file.</summary>
    private static string Line(int index, int total, CensusRow row)
    {
        StringBuilder line = new(Invariant($"[{index}/{total}] {Path.GetFileName(row["path"])} {row["bytes"]} bytes: {row["status"]}"));

        if (row["status"] == "demo")
        {
            foreach (string stage in CensusRow.Stages)
            {
                string status = row.Status(stage);
                line.Append(' ').Append(stage).Append('=').Append(status == "skip" ? "skip(" + row[stage + "_skip"] + ")" : status);
            }

            line.Append(Invariant($" {row["seconds"]} s, peak {row["working_set_peak_mb"]} MB"));
        }
        else
        {
            line.Append(' ').Append(row["status"] == "duplicate" ? "of " + row["duplicate_of"] : row["reason"]);
        }

        return line.ToString();
    }

    /// <summary>A file the census will look at, and which root it was found under.</summary>
    private readonly record struct Candidate(string Path, long Length, string Root);
}

/// <summary>The census's command line.</summary>
internal sealed record CensusOptions
{
    private const long Megabyte = 1 << 20;

    /// <summary>Directories and files to census, or with <see cref="SummarizeOnly"/> the CSVs to summarise.</summary>
    public required IReadOnlyList<string> Roots { get; init; }

    public required string CsvPath { get; init; }

    public required string SummaryPath { get; init; }

    public required IReadOnlySet<string> Stages { get; init; }

    public required string TempDirectory { get; init; }

    /// <summary>The most any one stage may be predicted to hold; above it the stage is a budget skip.</summary>
    public long BudgetBytes { get; init; }

    public bool SummarizeOnly { get; init; }

    /// <summary>Parses the arguments, or prints why not and returns null.</summary>
    public static CensusOptions? Parse(IReadOnlyList<string> arguments, TextWriter output)
    {
        List<string> roots = [];
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool summarize = false;
        int index = 0;

        while (index < arguments.Count)
        {
            string argument = arguments[index];
            index++;

            if (argument == "--summarize")
            {
                summarize = true;
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal) && index < arguments.Count)
            {
                values[argument] = arguments[index];
                index++;
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                return Refuse(output, argument + " needs a value");
            }
            else
            {
                roots.Add(argument);
            }
        }

        return Build(roots, values, summarize, output);
    }

    private static CensusOptions? Build(List<string> roots, Dictionary<string, string> values, bool summarize, TextWriter output)
    {
        string[] known = ["--csv", "--summary", "--budget-mb", "--stages", "--temp"];

        if (values.Keys.FirstOrDefault(key => !known.Contains(key, StringComparer.Ordinal)) is { } unknown)
        {
            return Refuse(output, "unknown option " + unknown);
        }

        if (roots.Count == 0)
        {
            return Refuse(output, summarize ? "no CSV to summarise" : "no directory or file to census");
        }

        string? csv = values.GetValueOrDefault("--csv");

        if (!summarize && csv is null)
        {
            return Refuse(output, "--csv is required");
        }

        long budgetMegabytes = 4096;

        if (values.TryGetValue("--budget-mb", out string? budget) &&
            (!long.TryParse(budget, NumberStyles.Integer, CultureInfo.InvariantCulture, out budgetMegabytes) || budgetMegabytes <= 0))
        {
            return Refuse(output, "--budget-mb needs a positive number of megabytes");
        }

        HashSet<string> stages = new(CensusRow.Stages, StringComparer.Ordinal);

        if (values.TryGetValue("--stages", out string? asked))
        {
            stages = new HashSet<string>(asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);

            if (stages.FirstOrDefault(stage => !CensusRow.Stages.Contains(stage)) is { } wrong)
            {
                return Refuse(output, "no stage called " + wrong + "; the stages are " + string.Join(",", CensusRow.Stages));
            }
        }

        string first = csv ?? roots[0];

        return new CensusOptions
        {
            Roots = summarize ? roots : [.. roots.Select(Path.GetFullPath)],
            CsvPath = Path.GetFullPath(first),
            SummaryPath = Path.GetFullPath(values.GetValueOrDefault("--summary") ?? Path.ChangeExtension(first, ".md")),
            Stages = stages,
            TempDirectory = values.GetValueOrDefault("--temp") ?? Path.GetTempPath(),
            BudgetBytes = budgetMegabytes * Megabyte,
            SummarizeOnly = summarize,
        };
    }

    private static CensusOptions? Refuse(TextWriter output, string why)
    {
        output.WriteLine("decode-census: " + why);
        output.WriteLine("usage: decode-census <dir-or-file>... --csv <out.csv> [--budget-mb 4096] [--stages a,b] " +
            "[--temp <dir>] [--summary <out.md>]");
        output.WriteLine("       decode-census --summarize <a.csv> [b.csv ...] [--summary <out.md>]");
        return null;
    }
}
