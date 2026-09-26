using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>A KeyValues node, built as `KeyValues::LoadFromBuffer` builds one — the loader every `.res` file goes through.</summary>
/// <remarks>
/// **A tree, where <see cref="KeyValuesReader"/> is a stream.** The reader exists for `items_game.txt`, eight megabytes read
/// for a few dozen values; a HUD is the opposite — many small files, layered by `#base`, where a customised one overrides
/// stock by the merge rules below. Those rules are the customisation, so they are ported, not approximated.
///
/// **From `src/tier1/KeyValues.cpp`:**
/// <list type="bullet">
/// <item>`ReadToken` (:538): `//` comments; a quoted token read without escapes (the `.res` default); a bare token ends at
/// whitespace, `"`, `{` or `}`, and is a conditional when it holds `[` then `]`.</item>
/// <item>`RecursiveLoadFromBuffer`: every key is created, duplicates included; a conditional between a key and its `{`, or
/// straight after a value, keeps the key only when it holds.</item>
/// <item>`EvaluateConditional` (:2218): the first of `$DECK`, `$X360`, `$WIN32`, `$WINDOWS`, `$OSX`, `$LINUX`, `$POSIX`
/// found, case-insensitively, answers; a leading `!` negates. This is a Windows PC.</item>
/// <item>`LoadFromBuffer` (:2260): `#include` appends a file's roots after this file's (`AppendIncludedKeys`); `#base`
/// merges a file's first root into this file's first root (`RecursiveMergeKeyValues`: children matched by EXACT name,
/// recursively, a missing one copied in). Both paths are the named file beside this one (`ParseIncludedKeys`).</item>
/// </list>
/// Lookup by name is case-insensitive, as the key symbol table is.
/// </remarks>
public sealed class KeyValuesTree
{
    /// <summary>`KEYVALUES_TOKEN_SIZE`: a longer token is cut.</summary>
    private const int TokenSize = 1024 * 4;

    /// <summary>`RecursiveLoadFromBuffer`'s recursion guard.</summary>
    private const int MaximumDepth = 100;

    private readonly List<KeyValuesTree> _children = [];

    private KeyValuesTree(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The key.</summary>
    public string Name { get; private set; }

    /// <summary>`KeyValues::ProcessResolutionKeys`: suffixed keys replace the plain ones, at every depth.</summary>
    /// <param name="suffix">The suffix, such as `_minmode`; null does nothing, as on PC for the resolution key.</param>
    /// <remarks>
    /// `KeyValues.cpp:2996`. Each child recurses first; then a child whose name ends in the suffix (compared without case,
    /// and only as the whole tail — `_lodef` must not match `_lodef_wide`) removes the first key with the plain name
    /// (`FindKey`, without case) and takes that name.
    /// </remarks>
    public void ProcessResolutionKeys(string? suffix)
    {
        if (suffix is null || _children.Count == 0)
        {
            return;
        }

        // `pSubKey = pSubKey->GetNextKey()`: the walk continues after the child just processed, wherever a removal left it.
        int next = 0;

        while (next < _children.Count)
        {
            KeyValuesTree child = _children[next];

            child.ProcessResolutionKeys(suffix);

            int at = child.Name.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);

            if (at >= 0 && string.Equals(child.Name[at..], suffix, StringComparison.OrdinalIgnoreCase))
            {
                string plain = child.Name[..at];

                if (Find(plain) is { } original)
                {
                    _children.Remove(original);
                }

                child.Name = plain;
            }

            next = _children.IndexOf(child) + 1;
        }
    }

    /// <summary>The value, or null for a block.</summary>
    public string? Value { get; private set; }

    /// <summary>The sub-keys, in file order, duplicates included.</summary>
    public IReadOnlyList<KeyValuesTree> Children => _children;

    /// <summary>The first sub-key with this name, compared without case, or null.</summary>
    /// <param name="name">The key.</param>
    /// <returns>The key, or null.</returns>
    public KeyValuesTree? Find(string name)
    {
        foreach (KeyValuesTree child in _children)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>`FindKey( name, true )`: the first sub-key with this name, compared without case, created when absent.</summary>
    /// <param name="name">The key.</param>
    /// <returns>The key.</returns>
    public KeyValuesTree FindOrCreate(string name)
    {
        if (Find(name) is { } found)
        {
            return found;
        }

        KeyValuesTree created = new(name);

        _children.Add(created);

        return created;
    }

    /// <summary>`SetString`: the named sub-key's value, the key created when absent.</summary>
    /// <param name="name">The key.</param>
    /// <param name="value">Its value.</param>
    public void SetString(string name, string? value) => FindOrCreate(name).Value = value ?? string.Empty;

    /// <summary>`SetStringValue`: this key's own value.</summary>
    /// <param name="value">The value.</param>
    public void SetValue(string value) => Value = value;

    /// <summary>`AddSubKey( other->MakeCopy() )`: a deep copy of another key, appended.</summary>
    /// <param name="other">The key to copy.</param>
    public void AddCopy(KeyValuesTree other)
    {
        ArgumentNullException.ThrowIfNull(other);

        _children.Add(Copy(other));
    }

    private static KeyValuesTree Copy(KeyValuesTree source)
    {
        KeyValuesTree copy = new(source.Name, source.Value);

        foreach (KeyValuesTree child in source._children)
        {
            copy._children.Add(Copy(child));
        }

        return copy;
    }

    /// <summary>Loads a file and returns its first root, empty when it has none.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="resourceName">Its path, which `#base` and `#include` are resolved beside.</param>
    /// <param name="open">Reads another file by path, or null when it is absent.</param>
    /// <returns>The first root.</returns>
    public static KeyValuesTree Load(ReadOnlySpan<byte> bytes, string resourceName, Func<string, byte[]?> open)
    {
        IReadOnlyList<KeyValuesTree> roots = LoadAll(bytes, resourceName, open);

        return roots.Count > 0 ? roots[0] : new KeyValuesTree(string.Empty);
    }

    /// <summary>Loads a file and returns every root, included files' after this file's own.</summary>
    /// <param name="bytes">The file.</param>
    /// <param name="resourceName">Its path, which `#base` and `#include` are resolved beside.</param>
    /// <param name="open">Reads another file by path, or null when it is absent.</param>
    /// <returns>The roots.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<KeyValuesTree> LoadAll(ReadOnlySpan<byte> bytes, string resourceName, Func<string, byte[]?> open)
    {
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(open);

        return LoadAll(bytes, resourceName, open, 0);
    }

    private static List<KeyValuesTree> LoadAll(
        ReadOnlySpan<byte> bytes, string resourceName, Func<string, byte[]?> open, int nesting)
    {
        Tokens tokens = new(Decode(bytes));
        List<KeyValuesTree> roots = [];
        List<KeyValuesTree> included = [];
        List<KeyValuesTree> bases = [];

        while (tokens.Next() is { } token)
        {
            if (!token.Quoted && string.Equals(token.Text, "#include", StringComparison.OrdinalIgnoreCase))
            {
                included.AddRange(Beside(tokens.Next(), resourceName, open, nesting));
                continue;
            }

            if (!token.Quoted && string.Equals(token.Text, "#base", StringComparison.OrdinalIgnoreCase))
            {
                List<KeyValuesTree> baseRoots = Beside(tokens.Next(), resourceName, open, nesting);

                if (baseRoots.Count > 0)
                {
                    bases.Add(baseRoots[0]);
                }

                continue;
            }

            if (token.Text.Length == 0)
            {
                break;
            }

            KeyValuesTree root = new(token.Text);
            bool accepted = true;
            Token? open2 = tokens.Next();

            if (open2 is { Conditional: true } condition)
            {
                accepted = Holds(condition.Text);
                open2 = tokens.Next();
            }

            if (open2 is { Text: "{", Quoted: false })
            {
                root.ReadBlock(tokens, 0);
            }

            if (accepted)
            {
                roots.Add(root);
            }
        }

        roots.AddRange(included);

        if (roots.Count > 0)
        {
            foreach (KeyValuesTree baseRoot in bases)
            {
                roots[0].Merge(baseRoot);
            }
        }

        return roots;
    }

    /// <summary>`ParseIncludedKeys`: the named file beside this one, loaded whole.</summary>
    private static List<KeyValuesTree> Beside(Token? name, string resourceName, Func<string, byte[]?> open, int nesting)
    {
        if (name is not { Text.Length: > 0 } file || nesting > MaximumDepth)
        {
            return [];
        }

        int slash = Math.Max(resourceName.LastIndexOf('/'), resourceName.LastIndexOf('\\'));
        string path = resourceName[..(slash + 1)] + file.Text;

        return open(path) is { } bytes ? LoadAll(bytes, path, open, nesting + 1) : [];
    }

    /// <summary>`RecursiveLoadFromBuffer`: reads keys until this block's closing brace.</summary>
    private void ReadBlock(Tokens tokens, int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        while (tokens.Next() is { Text.Length: > 0 } name)
        {
            if (name is { Text: "}", Quoted: false })
            {
                return;
            }

            KeyValuesTree key = new(name.Text);
            bool accepted = true;

            if (tokens.Next() is not { } value)
            {
                return;
            }

            if (value.Conditional)
            {
                accepted = Holds(value.Text);

                if (tokens.Next() is not { } real)
                {
                    return;
                }

                value = real;
            }

            if (value is { Text: "}", Quoted: false })
            {
                return;
            }

            if (value is { Text: "{", Quoted: false })
            {
                key.ReadBlock(tokens, depth + 1);
            }
            else
            {
                key.Value = value.Text;

                // Look ahead one token for a conditional tag.
                int before = tokens.Position;

                if (tokens.Next() is { Conditional: true } tag)
                {
                    accepted = Holds(tag.Text);
                }
                else
                {
                    tokens.Position = before;
                }
            }

            if (accepted)
            {
                _children.Add(key);
            }
        }
    }

    /// <summary>`RecursiveMergeKeyValues`: the base's children under this one's, matched by exact name.</summary>
    private void Merge(KeyValuesTree baseKey)
    {
        foreach (KeyValuesTree baseChild in baseKey._children)
        {
            KeyValuesTree? match = _children.Find(child => string.Equals(child.Name, baseChild.Name, StringComparison.Ordinal));

            if (match is not null)
            {
                match.Merge(baseChild);
            }
            else
            {
                _children.Add(baseChild.Copy());
            }
        }
    }

    private KeyValuesTree Copy()
    {
        KeyValuesTree copy = new(Name, Value);

        foreach (KeyValuesTree child in _children)
        {
            copy._children.Add(child.Copy());
        }

        return copy;
    }

    /// <summary>`EvaluateConditional`'s tests in its own order, and what each answers on a Windows PC.</summary>
    private static readonly (string Platform, bool Here)[] Platforms =
    [
        ("$DECK", false), ("$X360", false), ("$WIN32", true), ("$WINDOWS", true),
        ("$OSX", false), ("$LINUX", false), ("$POSIX", false),
    ];

    /// <summary>`EvaluateConditional`, on a Windows PC.</summary>
    private static bool Holds(string condition)
    {
        string text = condition.StartsWith('[') ? condition[1..] : condition;
        bool not = text.StartsWith('!');

        // The engine's order, first match answers: Steam Deck, 360, PC, Windows, OSX, Linux, POSIX.
        foreach ((string platform, bool here) in Platforms)
        {
            if (text.Contains(platform, StringComparison.OrdinalIgnoreCase))
            {
                return here ^ not;
            }
        }

        return false;
    }

    /// <summary>UTF-16 with its byte order mark is translated first, as `LoadFromBuffer` does; anything else is UTF-8.</summary>
    private static string Decode(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode.GetString(bytes[2..])
            : Encoding.UTF8.GetString(bytes);

    /// <summary>One token and how it was written.</summary>
    private readonly record struct Token(string Text, bool Quoted, bool Conditional);

    /// <summary>`ReadToken` over a whole file.</summary>
    private sealed class Tokens(string text)
    {
        public int Position { get; set; }

        public Token? Next()
        {
            while (true)
            {
                while (Position < text.Length && char.IsWhiteSpace(text[Position]))
                {
                    Position++;
                }

                if (Position >= text.Length)
                {
                    return null;
                }

                if (Position + 1 < text.Length && text[Position] == '/' && text[Position + 1] == '/')
                {
                    while (Position < text.Length && text[Position] != '\n')
                    {
                        Position++;
                    }

                    continue;
                }

                break;
            }

            char first = text[Position];

            if (first == '"')
            {
                int end = text.IndexOf('"', Position + 1);
                int stop = end < 0 ? text.Length : end;
                string quoted = text[(Position + 1)..stop];

                Position = end < 0 ? text.Length : end + 1;

                return new Token(Cut(quoted), Quoted: true, Conditional: false);
            }

            if (first is '{' or '}')
            {
                Position++;
                return new Token(first.ToString(), Quoted: false, Conditional: false);
            }

            int start = Position;
            bool opened = false;
            bool conditional = false;

            while (Position < text.Length)
            {
                char c = text[Position];

                if (c is '"' or '{' or '}' || char.IsWhiteSpace(c))
                {
                    break;
                }

                if (c == '[')
                {
                    opened = true;
                }

                if (c == ']' && opened)
                {
                    conditional = true;
                }

                Position++;
            }

            return new Token(Cut(text[start..Position]), Quoted: false, conditional);
        }

        private static string Cut(string token) => token.Length < TokenSize ? token : token[..(TokenSize - 1)];
    }
}
