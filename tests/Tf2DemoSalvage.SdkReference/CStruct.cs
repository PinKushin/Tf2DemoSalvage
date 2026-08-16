using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>One member of a C structure, with the byte offset the compiler gives it.</summary>
/// <param name="Name">The member's name, as the header spells it.</param>
/// <param name="Offset">Bytes from the start of the structure.</param>
/// <param name="Size">Bytes the member occupies, counting every element of an array.</param>
/// <param name="Elements">How many elements, 1 for a scalar.</param>
public sealed record CMember(string Name, int Offset, int Size, int Elements);

/// <summary>A C structure's layout: its members, their offsets, and its total size.</summary>
/// <param name="Name">The structure's name.</param>
/// <param name="Members">Its members in declaration order.</param>
/// <param name="Size">Bytes one instance occupies, including trailing padding.</param>
public sealed record CLayout(string Name, IReadOnlyList<CMember> Members, int Size)
{
    /// <summary>The byte offset of one member.</summary>
    /// <param name="member">The member's name.</param>
    /// <returns>Bytes from the start of the structure.</returns>
    /// <exception cref="KeyNotFoundException">No member by that name.</exception>
    public int Offset(string member) =>
        Members.FirstOrDefault(field => string.Equals(field.Name, member, StringComparison.Ordinal))
            ?.Offset
        ?? throw new KeyNotFoundException($"{Name} has no member named {member}");
}

/// <summary>A type's size and alignment, in bytes.</summary>
/// <param name="Size">Bytes one value occupies.</param>
/// <param name="Alignment">The boundary the compiler places it on.</param>
public sealed record CTypeSize(int Size, int Alignment);

/// <summary>
/// Works out what a C structure's layout is, by reading the header that declares it.
/// </summary>
/// <remarks>
/// **This exists because a file reader is mostly offsets, and a wrong one never throws.** Reading a
/// face's texinfo index from byte 12 instead of byte 10 returns a number, that number indexes a real
/// texinfo, and the map draws with the wrong material on some surfaces. The engine's header states
/// every one of these; a test that reads it turns a remembered number into a checked one.
///
/// **It computes offsets rather than only sizes**, which is the half that matters. A stride can be
/// right while the members inside it are read from the wrong places — the total is the sum either
/// way — so <c>BspStructTests</c> asserts both.
///
/// **It refuses rather than guesses.** An unknown type name, a pointer, a nested brace, or a
/// declarator it cannot parse returns null instead of a plausible number. That polarity is
/// deliberate: this is the reference the other tests are measured against, so a value it invents
/// would fail a correct reader, or worse, pass a wrong one. Composite types are supplied by the
/// caller with their size stated at the call site, where the reasoning is visible.
///
/// **Padding is modelled the way C specifies it** — each member aligned to its own type, the whole
/// padded to its widest member. For every structure in <c>bspfile.h</c> this happens to equal tight
/// packing, but assuming that would be an assumption rather than a rule, and it is exactly the kind
/// that holds until one structure ends on an odd byte.
/// </remarks>
public static class CStruct
{
    /// <summary>How long a pattern may run before it is treated as a defect in the pattern.</summary>
    private static readonly TimeSpan PatternLimit = TimeSpan.FromSeconds(10);

    /// <summary>The built-in types a Source header uses in a file structure.</summary>
    /// <remarks>
    /// Sizes are the Win32/x86 ones the BSP and MDL formats were written against, which is what the
    /// files on disk contain regardless of what compiles this project.
    /// </remarks>
    private static readonly Dictionary<string, CTypeSize> BuiltIn = new(StringComparer.Ordinal)
    {
        ["char"] = new(1, 1),
        ["signed char"] = new(1, 1),
        ["unsigned char"] = new(1, 1),
        ["byte"] = new(1, 1),
        ["bool"] = new(1, 1),
        ["short"] = new(2, 2),
        ["unsigned short"] = new(2, 2),
        ["int"] = new(4, 4),
        ["unsigned int"] = new(4, 4),
        ["unsigned"] = new(4, 4),
        ["long"] = new(4, 4),
        ["unsigned long"] = new(4, 4),
        ["float"] = new(4, 4),
        ["double"] = new(8, 8),
    };

    /// <summary>Reads one structure's layout out of a header.</summary>
    /// <param name="header">The header's full text.</param>
    /// <param name="name">The structure's name as declared, such as <c>dface_t</c>.</param>
    /// <param name="constants">Named integers, for array bounds written as macros.</param>
    /// <param name="composites">Sizes for types this does not know, such as <c>Vector</c>.</param>
    /// <returns>The layout, or null when anything about it could not be determined.</returns>
    public static CLayout? Layout(
        string header,
        string name,
        IReadOnlyDictionary<string, int>? constants = null,
        IReadOnlyDictionary<string, CTypeSize>? composites = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(name);

        // **Comments come out before anything else looks at a brace**, and that ordering is not
        // cosmetic. dface_t carries a commented-out union — `// union` / `// {` / `// };` — so a
        // nested-brace check run on the raw text refuses a structure that has no nested brace at
        // all, and brace matching run on it is only correct because those two happen to balance.
        string source = Uncommented(header);

        if (Body(source, name) is not { } body)
        {
            return null;
        }

        List<CMember> members = [];
        int offset = 0;
        int widest = 1;

        // Consecutive bitfields of one base type share a storage unit: dleaf_t's `short area:9`
        // and `short flags:7` are sixteen bits of one short, not two shorts.
        string? bitfieldType = null;
        int bitfieldBits = 0;

        foreach (string statement in Statements(body))
        {
            if (Declaration(statement, constants, composites) is not { } declared)
            {
                return null;
            }

            if (declared.Declarators.Count == 0)
            {
                continue;
            }

            foreach ((string member, int elements, int bits) in declared.Declarators)
            {
                if (bits > 0)
                {
                    bool sameUnit =
                        string.Equals(bitfieldType, declared.TypeName, StringComparison.Ordinal) &&
                        bitfieldBits + bits <= declared.Type.Size * 8;

                    if (!sameUnit)
                    {
                        offset = Aligned(offset, declared.Type.Alignment);
                        bitfieldType = declared.TypeName;
                        bitfieldBits = 0;

                        members.Add(new CMember(member, offset, declared.Type.Size, 1));
                        offset += declared.Type.Size;
                        widest = Math.Max(widest, declared.Type.Alignment);
                    }
                    else
                    {
                        // Shares the unit already accounted for; its offset is that unit's.
                        members.Add(
                            new CMember(member, offset - declared.Type.Size, declared.Type.Size, 1));
                    }

                    bitfieldBits += bits;
                    continue;
                }

                bitfieldType = null;
                bitfieldBits = 0;

                offset = Aligned(offset, declared.Type.Alignment);
                int size = declared.Type.Size * elements;

                members.Add(new CMember(member, offset, size, elements));

                offset += size;
                widest = Math.Max(widest, declared.Type.Alignment);
            }
        }

        return members.Count == 0 ? null : new CLayout(name, members, Aligned(offset, widest));
    }

    /// <summary>The text between a structure's braces, or null when it cannot be isolated.</summary>
    private static string? Body(string header, string name)
    {
        Match declaration = Regex.Match(
            header,
            @"(?:^|\n)\s*(?:typedef\s+)?struct\s+" + Regex.Escape(name) + @"\s*(?://[^\n]*)?\s*\n?\s*\{",
            RegexOptions.None,
            PatternLimit);

        if (!declaration.Success)
        {
            return null;
        }

        int start = declaration.Index + declaration.Length;
        int depth = 1;

        for (int at = start; at < header.Length; at++)
        {
            depth += header[at] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };

            if (depth == 0)
            {
                string body = header[start..at];

                // A nested brace means an inner struct or union whose packing this does not model.
                // Refusing is the point: a wrong reference is worse than an absent one.
                return body.Contains('{', StringComparison.Ordinal) ? null : body;
            }
        }

        return null;
    }

    /// <summary>Removes comments and preprocessor directives, leaving the code.</summary>
    /// <remarks>
    /// Run on the whole header before a brace is counted or a structure is found, because a comment
    /// can contain either — <c>dface_t</c> comments out a union, braces and all.
    /// </remarks>
    private static string Uncommented(string header)
    {
        string stripped = Regex.Replace(
            header, @"/\*.*?\*/", " ", RegexOptions.Singleline, PatternLimit);
        stripped = Regex.Replace(stripped, @"//[^\n]*", " ", RegexOptions.None, PatternLimit);

        return Regex.Replace(stripped, @"(?m)^\s*#[^\n]*", " ", RegexOptions.None, PatternLimit);
    }

    /// <summary>Splits an already-uncommented body into declarations.</summary>
    private static IEnumerable<string> Statements(string body)
    {
        foreach (string statement in body.Split(';'))
        {
            string trimmed = Regex.Replace(
                statement, @"\s+", " ", RegexOptions.None, PatternLimit).Trim();

            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>A parsed declaration: one type, and the names declared with it.</summary>
    private readonly record struct Declared(
        string TypeName,
        CTypeSize Type,
        IReadOnlyList<(string Name, int Elements, int Bits)> Declarators);

    /// <summary>Parses one statement, or returns null when it is not something this understands.</summary>
    /// <remarks>
    /// Returns an empty declarator list for statements that are legitimately not members — a macro
    /// such as <c>DECLARE_BYTESWAP_DATADESC()</c>, a method, an access specifier — and null for
    /// anything that looks like a member and could not be parsed. The two must not be confused: the
    /// first is nothing to account for, the second is bytes unaccounted for.
    /// </remarks>
    private static Declared? Declaration(
        string statement,
        IReadOnlyDictionary<string, int>? constants,
        IReadOnlyDictionary<string, CTypeSize>? composites)
    {
        string text = statement;

        // Access specifiers attach to whatever follows them, because the split is on semicolons.
        text = Regex.Replace(
            text,
            @"^\s*(?:public|private|protected)\s*:\s*",
            string.Empty,
            RegexOptions.None,
            PatternLimit);

        if (text.Length == 0 || text is "public" or "private" or "protected")
        {
            return new Declared(string.Empty, new CTypeSize(0, 1), []);
        }

        // Anything with parentheses is a method or a macro, never a member.
        if (text.Contains('(', StringComparison.Ordinal))
        {
            return new Declared(string.Empty, new CTypeSize(0, 1), []);
        }

        // Pointers and nested type declarations are outside what a file structure contains, and a
        // guess about either would be a number rather than a refusal.
        if (text.Contains('*', StringComparison.Ordinal) ||
            text.StartsWith("typedef", StringComparison.Ordinal) ||
            text.StartsWith("union", StringComparison.Ordinal) ||
            text.StartsWith("struct", StringComparison.Ordinal) ||
            text.StartsWith("class", StringComparison.Ordinal))
        {
            return null;
        }

        string[] tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The type is the longest leading run of tokens that names one. Longest first, so
        // "unsigned short" wins over "unsigned".
        for (int length = Math.Min(3, tokens.Length - 1); length >= 1; length--)
        {
            string candidate = string.Join(' ', tokens[..length]);

            if (!TypeOf(candidate, composites, out CTypeSize type))
            {
                continue;
            }

            List<(string, int, int)> declarators = [];

            foreach (string part in string.Join(' ', tokens[length..]).Split(','))
            {
                if (Declarator(part.Trim(), constants) is not { } one)
                {
                    return null;
                }

                declarators.Add(one);
            }

            return new Declared(candidate, type, declarators);
        }

        return null;
    }

    /// <summary>Looks up a type, preferring the caller's composites over the built-ins.</summary>
    private static bool TypeOf(
        string name, IReadOnlyDictionary<string, CTypeSize>? composites, out CTypeSize type)
    {
        if (composites is not null && composites.TryGetValue(name, out CTypeSize? supplied))
        {
            type = supplied;
            return true;
        }

        if (BuiltIn.TryGetValue(name, out CTypeSize? built))
        {
            type = built;
            return true;
        }

        type = new CTypeSize(0, 1);
        return false;
    }

    /// <summary>Parses one declarator: a name, its array bounds, and any bitfield width.</summary>
    private static (string Name, int Elements, int Bits)? Declarator(
        string text, IReadOnlyDictionary<string, int>? constants)
    {
        Match parsed = Regex.Match(
            text,
            @"^([A-Za-z_][A-Za-z0-9_]*)((?:\[[^\]]+\])*)(?:\s*:\s*(\d+))?$",
            RegexOptions.None,
            PatternLimit);

        if (!parsed.Success)
        {
            return null;
        }

        int elements = 1;

        foreach (Match bound in Regex.Matches(
            parsed.Groups[2].Value, @"\[([^\]]+)\]", RegexOptions.None, PatternLimit))
        {
            string dimension = bound.Groups[1].Value.Trim();

            if (int.TryParse(dimension, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                elements *= count;
            }
            else if (constants is not null && constants.TryGetValue(dimension, out int named))
            {
                elements *= named;
            }
            else
            {
                // An array bound that cannot be resolved is the one case where guessing is worst:
                // the structure's size would be wrong by a multiple.
                return null;
            }
        }

        int bits = parsed.Groups[3].Success
            ? int.Parse(parsed.Groups[3].Value, CultureInfo.InvariantCulture)
            : 0;

        return (parsed.Groups[1].Value, elements, bits);
    }

    /// <summary>Rounds an offset up to an alignment boundary.</summary>
    private static int Aligned(int offset, int alignment) =>
        alignment <= 1 ? offset : ((offset + alignment - 1) / alignment) * alignment;
}
