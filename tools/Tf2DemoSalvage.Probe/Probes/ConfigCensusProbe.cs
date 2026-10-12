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
/// Every command of every `.cfg` the game ships or the install holds, tokenized by `CCommand::Tokenize`'s port beside
/// the tokenizer this project had before it (B532).
/// </summary>
/// <remarks>
/// **The old tokenizer is kept HERE, frozen, as the thing being measured** — production has only the port. The control
/// is in the count: commands read and commands agreeing are printed beside the ones differing, each kind named.
///
/// <code>
///   cfg-census             the shipped archives' cfg/ and the install's loose cfg/ and custom/*/cfg/
///   cfg-census &lt;folder&gt;    every .cfg under a folder instead
/// </code>
/// </remarks>
public sealed class ConfigCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "cfg-census";

    /// <inheritdoc/>
    public string Summary =>
        "every .cfg command through CCommand::Tokenize's port beside the old tokenizer, differences by kind: cfg-census [folder]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        List<(string Path, string Text)> files = [];

        if (arguments.Count > 0)
        {
            files.AddRange(Loose(arguments[0]));
        }
        else if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is { } game)
        {
            GameArchives archives = GameArchives.Open(game);

            foreach (string path in archives.Paths()
                         .Where(static path => path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                if (archives.Read(path) is { } bytes)
                {
                    files.Add((path, Encoding.UTF8.GetString(bytes)));
                }
            }

            files.AddRange(Loose(Path.Combine(game, "cfg")));
            files.AddRange(Loose(Path.Combine(game, "custom")));
        }
        else
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        int commands = 0;
        Dictionary<string, int> kinds = new(StringComparer.Ordinal);
        Dictionary<string, int> verbs = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> examples = new(StringComparer.Ordinal);

        foreach ((string path, string text) in files)
        {
            foreach (string line in text.Split('\n'))
            {
                foreach (string clause in SourceConfig.Clauses(line))
                {
                    IReadOnlyList<string> now = SourceConfig.Tokens(clause);
                    List<string> before = Before(clause);

                    commands++;

                    if (now.SequenceEqual(before, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    string kind = Kind(clause);
                    string verb = now.Count > 0 ? now[0] : "(none)";

                    kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                    verbs[verb] = verbs.GetValueOrDefault(verb) + 1;
                    examples.TryAdd(kind, string.Create(CultureInfo.InvariantCulture, $"{path}: {clause}"));
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{files.Count:N0} .cfg files, {commands:N0} commands, {kinds.Values.Sum():N0} tokenized differently"));

        foreach ((string kind, int count) in kinds.OrderByDescending(static pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0}  {kind}   e.g. {examples[kind]}"));
        }

        output.WriteLine("by command:");

        foreach ((string verb, int count) in verbs.OrderByDescending(static pair => pair.Value).Take(25))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0}  {verb}"));
        }
    }

    /// <summary>Why a command's two readings differ, by what its text holds.</summary>
    private static string Kind(string clause)
    {
        if (clause.Contains("\"\"", StringComparison.Ordinal))
        {
            return "an empty quoted argument";
        }

        string unquoted = string.Concat(clause.Split('"').Where(static (_, index) => index % 2 == 0));

        return unquoted.IndexOfAny(['{', '}', '(', ')', '\'', ':']) >= 0
            ? "a break character outside quotes"
            : "a quote against a word, or other";
    }

    private static IEnumerable<(string Path, string Text)> Loose(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.cfg", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(static path => (path, File.ReadAllText(path)))
            : [];

    /// <summary>`SourceConfig.Tokenise` as it stood before B532: split on space, tab and carriage return, quotes toggled.</summary>
    private static List<string> Before(string line)
    {
        List<string> tokens = [];
        StringBuilder current = new();
        bool quoted = false;

        for (int at = 0; at < line.Length; at++)
        {
            char c = line[at];

            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && c == '/' && at + 1 < line.Length && line[at + 1] == '/')
            {
                break;
            }

            if (!quoted && (c == ' ' || c == '\t' || c == '\r'))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
