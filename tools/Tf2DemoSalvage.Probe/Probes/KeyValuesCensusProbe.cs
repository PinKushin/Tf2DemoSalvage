using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Every shipped KeyValues text file through `KeyValuesTokens`, and what each of this project's readers makes of it
/// beside what `KeyValuesTree` — the port of `KeyValues::LoadFromBuffer` — makes of it (B524).
/// </summary>
/// <remarks>
/// **Written before the readers were changed, to count what each wrong reading cost.** Three parts:
///
/// <list type="bullet">
/// <item>the DENOMINATOR: over every `.txt`, `.res`, `.vdf` and `.vmt` in the game's archives, how many carry a
/// conditional tag, a `#base` or `#include`, a quoted brace, or a line that comments out a quoted pair;</item>
/// <item>`KeyValuesReader` (the stream) against the tree, file by file, over `scripts/`, `particles/` and `maps/`;</item>
/// <item>`VmtMaterial` against the tree, key by key, over every material that is not a patch, with the `name?$key`
/// conditions it strips counted by name.</item>
/// </list>
///
/// **The control is in each count**: files read and files agreeing are printed beside the files differing, so a zero
/// is a zero out of a stated number.
///
/// <code>
///   kv-census            every part
///   kv-census stream     only the stream reader's files, each difference named
/// </code>
/// </remarks>
public sealed class KeyValuesCensusProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "kv-census";

    /// <inheritdoc/>
    public string Summary =>
        "shipped KeyValues files: conditionals, includes, quoted braces, commented pairs, and each reader against the tree: kv-census [stream|vmt]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } game)
        {
            output.WriteLine("No TF2 install found, so there is nothing to read.");
            return;
        }

        GameArchives archives = GameArchives.Open(game);
        string part = arguments.Count > 0 ? arguments[0] : string.Empty;

        List<string> paths = [.. archives.Paths()
            .Where(path => Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{paths.Count:N0} text files in the archives"));

        if (part.Length == 0)
        {
            Denominator(output, archives, paths);
        }

        if (part.Length == 0 || part.Equals("stream", StringComparison.OrdinalIgnoreCase))
        {
            Stream(output, archives, paths, arguments.Count > 1 ? arguments[1] : null);
        }

        if (part.Length == 0 || part.Equals("vmt", StringComparison.OrdinalIgnoreCase))
        {
            Materials(output, archives, paths);
        }
    }

    private static readonly string[] Extensions = [".txt", ".res", ".vdf", ".vmt"];

    private static void Denominator(TextWriter output, GameArchives archives, List<string> paths)
    {
        Dictionary<string, int> conditionals = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> byKind = new(StringComparer.Ordinal);
        Dictionary<string, string> example = new(StringComparer.Ordinal);

        void Count(string kind, string path)
        {
            byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
            example.TryAdd(kind, path);
        }

        foreach (string path in paths)
        {
            if (archives.Read(path) is not { } bytes)
            {
                continue;
            }

            string text = KeyValuesTokens.Decode(bytes);
            KeyValuesTokens tokens = new(text);
            bool conditional = false, include = false, quotedBrace = false;

            while (tokens.Next() is { } token)
            {
                if (token.Conditional)
                {
                    conditional = true;
                    conditionals[token.Text] = conditionals.GetValueOrDefault(token.Text) + 1;
                }

                include |= !token.Quoted && token.Text is "#base" or "#include";
                quotedBrace |= token.Quoted && token.Text is "{" or "}";
            }

            if (conditional)
            {
                Count("a conditional tag", path);
            }

            if (include)
            {
                Count("#base or #include", path);
            }

            if (quotedBrace)
            {
                Count("a quoted brace", path);
            }

            if (text.Split('\n').Any(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                    line.Count(character => character == '"') >= 4))
            {
                Count("a commented-out quoted pair", path);
            }
        }

        foreach ((string kind, int count) in byKind.OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0} files with {kind}, e.g. {example[kind]}"));
        }

        output.WriteLine("  conditional tags by text: " + string.Join(
            ", ", conditionals.OrderByDescending(pair => pair.Value).Take(14).Select(pair => $"{pair.Key} x{pair.Value}")));
    }

    /// <summary>The stream reader's events beside the tree's, for the folders the stream reader is pointed at.</summary>
    private static void Stream(TextWriter output, GameArchives archives, List<string> paths, string? window)
    {
        int read = 0, differing = 0;

        // The item schema ships loose beside the archives, so it is named: the listing is of archives alone.
        foreach (string path in paths.Where(path =>
            !path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("particles/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))).Append("scripts/items/items_game.txt"))
        {
            if (archives.Read(path) is not { } bytes)
            {
                continue;
            }

            read++;

            List<string> stream = [];

            KeyValuesReader.Read(bytes, (key, value, depth) =>
            {
                stream.Add(Event(depth, key, value));
                return true;
            });

            List<string> tree = [];

            // No file is opened beside this one: the comparison is of the tokens, and the stream cannot include either.
            foreach (KeyValuesTree root in KeyValuesTree.LoadAll(bytes, path, _ => null))
            {
                Flatten(root, 0, tree);
            }

            if (stream.SequenceEqual(tree, StringComparer.Ordinal))
            {
                continue;
            }

            differing++;

            int at = 0;

            while (at < stream.Count && at < tree.Count && string.Equals(stream[at], tree[at], StringComparison.Ordinal))
            {
                at++;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  DIFFERS {path}: stream {stream.Count:N0} events, tree {tree.Count:N0}; first at {at:N0}: " +
                $"stream '{(at < stream.Count ? stream[at] : "(end)")}' tree '{(at < tree.Count ? tree[at] : "(end)")}'"));

            // `kv-census stream <part of a path>`: the events either side of the first difference, both readings.
            if (window is not null && path.Contains(window, StringComparison.OrdinalIgnoreCase))
            {
                int from = Math.Max(0, at - 4);

                output.WriteLine("    stream: " + string.Join("  ", stream.Skip(from).Take(22)));
                output.WriteLine("    tree:   " + string.Join("  ", tree.Skip(from).Take(22)));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"stream reader: {read:N0} files read, {differing:N0} differ from the tree"));
    }

    private static string Event(int depth, string key, string? value) =>
        string.Create(CultureInfo.InvariantCulture, $"{depth}|{key}|{value ?? "{"}");

    private static void Flatten(KeyValuesTree node, int depth, List<string> into)
    {
        bool block = node.Value is null;

        into.Add(Event(depth, node.Name, block ? null : node.Value));

        foreach (KeyValuesTree child in node.Children)
        {
            Flatten(child, depth + 1, into);
        }
    }

    /// <summary>Each material's own keys as `VmtMaterial` answers them beside the tree's last statement of each.</summary>
    private static void Materials(TextWriter output, GameArchives archives, List<string> paths)
    {
        int read = 0, patches = 0, differing = 0, keys = 0, emptied = 0, lostAfterEmpty = 0;
        List<string> emptiedExamples = [];
        Dictionary<string, int> conditions = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> kinds = new(StringComparer.Ordinal);
        List<string> examples = [];

        foreach (string path in paths.Where(path => path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase)))
        {
            if (archives.Read(path) is not { } bytes)
            {
                continue;
            }

            KeyValuesTree root = KeyValuesTree.Load(bytes, path, _ => null);

            if (root.Name.Equals("patch", StringComparison.OrdinalIgnoreCase))
            {
                patches++;
                continue;
            }

            read++;

            VmtMaterial material = VmtMaterial.Parse(bytes);
            Dictionary<string, string> stated = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuesTree child in root.Children.Where(child => child.Value is not null))
            {
                int question = child.Name.IndexOf('?', StringComparison.Ordinal);

                if (question > 0)
                {
                    string condition = child.Name[..question];

                    conditions[condition] = conditions.GetValueOrDefault(condition) + 1;
                }

                stated[child.Name] = child.Value!;
            }

            // What the old scanner lost: it ended a material's parse at the first empty quoted value (B524).
            int firstEmpty = root.Children.ToList().FindIndex(child => child.Value is { Length: 0 });

            if (firstEmpty >= 0)
            {
                int after = root.Children.Count - firstEmpty - 1;

                emptied++;
                lostAfterEmpty += after;

                if (after > 0 && emptiedExamples.Count < 12)
                {
                    emptiedExamples.Add($"{path} ({after} after '{root.Children[firstEmpty].Name}')");
                }
            }

            bool differs = false;

            if (!string.Equals(material.Shader, root.Name, StringComparison.Ordinal))
            {
                differs = true;
                Kind(kinds, examples, "the shader name", path, material.Shader, root.Name);
            }

            foreach ((string key, string value) in stated)
            {
                keys++;

                int question = key.IndexOf('?', StringComparison.Ordinal);
                string plain = question > 0 ? key[(question + 1)..] : key;
                string? answered = material.Value(plain);

                if (string.Equals(answered, value, StringComparison.Ordinal))
                {
                    continue;
                }

                // A plain key the tree states and a conditioned twin overwrote, or a fallback block's merge, is counted apart.
                string kind = "a plain key read differently";

                if (question > 0)
                {
                    kind = "a conditioned key not the last word";
                }
                else if (stated.Keys.Any(other => other.EndsWith("?" + key, StringComparison.OrdinalIgnoreCase)))
                {
                    kind = "a plain key overwritten by a conditioned twin";
                }
                else if (root.Children.Any(block => block.Value is null && block.Find(key) is not null))
                {
                    kind = "a plain key overwritten from a block";
                }

                differs = true;
                Kind(kinds, examples, kind, path, $"{key} = {answered ?? "(none)"}", value);
            }

            if (differs)
            {
                differing++;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"materials: {read:N0} read ({patches:N0} patches left out), {keys:N0} own keys, {differing:N0} materials differ from the tree"));

        foreach ((string kind, int count) in kinds.OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {count,7:N0} x {kind}"));
        }

        foreach (string example in examples)
        {
            output.WriteLine("    " + example);
        }

        output.WriteLine("  conditions on keys: " + string.Join(
            ", ", conditions.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}? x{pair.Value}")));

        string emptiedCount = string.Create(
            CultureInfo.InvariantCulture,
            $"  {emptied:N0} materials state an empty quoted value, with {lostAfterEmpty:N0} blocks and keys after the first: ");

        output.WriteLine(emptiedCount + string.Join("; ", emptiedExamples));
    }

    private static void Kind(
        Dictionary<string, int> kinds, List<string> examples, string kind, string path, string ours, string tree)
    {
        int seen = kinds.GetValueOrDefault(kind);

        kinds[kind] = seen + 1;

        if (seen < 4)
        {
            examples.Add($"{kind}: {path}: ours '{ours}' tree '{tree}'");
        }
    }
}
