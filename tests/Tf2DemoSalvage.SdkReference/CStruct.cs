using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

/// <summary>The result of reading a structure: a layout, or what stopped it.</summary>
/// <param name="Layout">The layout, or null when it could not be determined.</param>
/// <param name="Refused">The declaration that could not be resolved, or null on success.</param>
public sealed record CLayoutAttempt(CLayout? Layout, string? Refused);

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

    /// <summary>No preprocessor symbols defined: the build a PC file format was written by.</summary>
    private static readonly HashSet<string> Nothing = new(StringComparer.Ordinal);

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
    /// <param name="pointerBytes">
    /// How many bytes a pointer member occupies, or null to refuse a structure containing one.
    /// </param>
    /// <param name="defined">Preprocessor symbols to treat as defined; null means none.</param>
    /// <param name="pack">The <c>#pragma pack</c> in force; null means natural alignment.</param>
    /// <returns>The layout, or null when anything about it could not be determined.</returns>
    public static CLayout? Layout(
        string header,
        string name,
        IReadOnlyDictionary<string, int>? constants = null,
        IReadOnlyDictionary<string, CTypeSize>? composites = null,
        int? pointerBytes = null,
        IReadOnlySet<string>? defined = null,
        int? pack = null) =>
        Attempt(header, name, constants, composites, pointerBytes, defined, pack).Layout;

    /// <summary>Reads one structure's layout, and says what stopped it when it could not.</summary>
    /// <param name="header">The header's full text.</param>
    /// <param name="name">The structure's name as declared.</param>
    /// <param name="constants">Named integers, for array bounds written as macros.</param>
    /// <param name="composites">Sizes for types this does not know.</param>
    /// <param name="pointerBytes">Bytes per pointer member, or null to refuse one.</param>
    /// <param name="defined">
    /// Preprocessor symbols to treat as defined. Null means none, which is what a PC file written
    /// by a 32-bit tool was compiled with.
    /// </param>
    /// <param name="pack">
    /// The <c>#pragma pack</c> in force, capping every member's alignment. Null means natural
    /// alignment.
    /// </param>
    /// <returns>The layout, or the declaration that could not be resolved.</returns>
    /// <remarks>
    /// **A refusal without a reason costs more than it saves.** Three studio structures came back
    /// null at once and the only way to find out why was to guess at the declarations — which is the
    /// exact move this whole reference exists to remove. Naming the statement turns "could not
    /// parse" into "could not parse <c>mutable void *virtualModel</c>", and that sentence answers
    /// itself.
    /// </remarks>
    public static CLayoutAttempt Attempt(
        string header,
        string name,
        IReadOnlyDictionary<string, int>? constants = null,
        IReadOnlyDictionary<string, CTypeSize>? composites = null,
        int? pointerBytes = null,
        IReadOnlySet<string>? defined = null,
        int? pack = null)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(name);

        // **Comments come out before anything else looks at a brace**, and that ordering is not
        // cosmetic. dface_t carries a commented-out union — `// union` / `// {` / `// };` — so a
        // nested-brace check run on the raw text refuses a structure that has no nested brace at
        // all, and brace matching run on it is only correct because those two happen to balance.
        // Conditionals are then resolved rather than deleted; see Conditioned.
        if (Conditioned(Uncommented(header), defined, constants, out string? unhandled)
            is not { } source)
        {
            return new CLayoutAttempt(null, unhandled);
        }

        if (Body(source, name) is not { } body)
        {
            return new CLayoutAttempt(null, $"no declaration of {name} was found");
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
            if (Declaration(statement, constants, composites, pointerBytes) is not { } declared)
            {
                return new CLayoutAttempt(null, statement);
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
                        offset = Aligned(offset, Capped(declared.Type.Alignment, pack));
                        bitfieldType = declared.TypeName;
                        bitfieldBits = 0;

                        members.Add(new CMember(member, offset, declared.Type.Size, 1));
                        offset += declared.Type.Size;
                        widest = Math.Max(widest, Capped(declared.Type.Alignment, pack));
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

                offset = Aligned(offset, Capped(declared.Type.Alignment, pack));
                int size = declared.Type.Size * elements;

                members.Add(new CMember(member, offset, size, elements));

                offset += size;
                widest = Math.Max(widest, Capped(declared.Type.Alignment, pack));
            }
        }

        return members.Count == 0
            ? new CLayoutAttempt(null, $"{name} was found but declared no members")
            : new CLayoutAttempt(
                new CLayout(name, members, Aligned(offset, Capped(widest, pack))), null);
    }

    /// <summary>The text between a structure's braces, or null when it cannot be isolated.</summary>
    private static string? Body(string header, string name)
    {
        // **`class` as well as `struct`, because C++ makes them the same thing for layout.** The
        // only difference is default access, which changes nothing about where a member sits.
        // Refusing classes cost real coverage: ddispinfo_t was written off as underivable purely
        // because CDispNeighbor and CDispCornerNeighbors are declared with the other keyword.
        Match declaration = Regex.Match(
            header,
            @"(?:^|\n)\s*(?:typedef\s+)?(?:struct|class)\s+" + Regex.Escape(name) +
                @"\s*(?://[^\n]*)?\s*\n?\s*\{",
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
                return WithoutBlocks(header[start..at]);
            }
        }

        return null;
    }

    /// <summary>Replaces each braced block inside a structure body with a statement terminator.</summary>
    /// <remarks>
    /// **Studio headers declare members and inline methods in the same list**, and a method carries
    /// a body: <c>inline char * const pszName( void ) const { return … }</c> sits between
    /// <c>sznameindex</c> and <c>parent</c> in <c>mstudiobone_t</c>. Deleting the body outright would
    /// weld the method's leftover tokens onto the next member's declaration, and since the method has
    /// parentheses the whole run would then be skipped — losing a real member and shifting every
    /// offset after it. A semicolon terminates the method instead, so the member behind it is still
    /// its own statement.
    ///
    /// **A nested struct or union survives this as a refusal rather than a wrong number.** Its block
    /// collapses the same way, which leaves the bare keyword <c>struct</c> or <c>union</c> as a
    /// statement, and <see cref="Declaration"/> returns null for those. That is the intended
    /// outcome: this models no packing rule for an inner type, so it must not produce a size for one.
    /// </remarks>
    private static string WithoutBlocks(string body)
    {
        StringBuilder kept = new(body.Length);
        int depth = 0;

        foreach (char character in body)
        {
            switch (character)
            {
                case '{':
                    depth++;
                    break;

                case '}':
                    depth--;

                    if (depth == 0)
                    {
                        kept.Append(';');
                    }

                    break;

                default:
                    if (depth == 0)
                    {
                        kept.Append(character);
                    }

                    break;
            }
        }

        return kept.ToString();
    }

    /// <summary>Removes comments from a header or a source file.</summary>
    /// <remarks>
    /// Run on the whole header before a brace is counted or a structure is found, because a comment
    /// can contain either — <c>dface_t</c> comments out a union, braces and all.
    /// </remarks>
    public static string Uncommented(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        string stripped = Regex.Replace(
            header, @"/\*.*?\*/", " ", RegexOptions.Singleline, PatternLimit);

        return Regex.Replace(stripped, @"//[^\n]*", " ", RegexOptions.None, PatternLimit);
    }

    /// <summary>Resolves preprocessor conditionals, keeping only the branch that compiles.</summary>
    /// <param name="text">Header text with comments already removed.</param>
    /// <param name="defined">The symbols to treat as defined.</param>
    /// <param name="constants">Named integers, for a bare macro used as a truth value.</param>
    /// <param name="unhandled">Set to the directive that could not be resolved, or null.</param>
    /// <returns>The text with unselected branches and all directives removed.</returns>
    /// <remarks>
    /// **Deleting directive lines instead of resolving them counts both branches**, and the result
    /// is a plausible number rather than an error. <c>mstudiotexture_t</c> ends with
    /// <c>#ifdef PLATFORM_64BITS int unused[8]; #else int unused[10]; #endif</c>; stripping the three
    /// directives leaves both arrays, which makes the structure 96 bytes instead of 64. It failed a
    /// correct constant, which is the worst direction for a reference to be wrong in.
    ///
    /// **The default is that nothing is defined**, and for these files that is the right model
    /// rather than a convenience: an MDL was written by 32-bit studiomdl on a PC, so
    /// <c>PLATFORM_64BITS</c> and <c>_X360</c> are exactly the branches the file does NOT contain.
    ///
    /// **An expression this cannot evaluate is reported, not assumed.** Guessing a branch would
    /// silently include or drop members.
    /// </remarks>
    public static string? Conditioned(
        string text,
        IReadOnlySet<string>? defined,
        IReadOnlyDictionary<string, int>? constants,
        out string? unhandled)
    {
        ArgumentNullException.ThrowIfNull(text);

        defined ??= Nothing;

        unhandled = null;

        StringBuilder kept = new(text.Length);
        List<bool> regions = [];

        foreach (string line in text.Split('\n'))
        {
            string directive = line.TrimStart();

            if (!directive.StartsWith('#'))
            {
                if (regions.TrueForAll(taken => taken))
                {
                    kept.Append(line).Append('\n');
                }

                continue;
            }

            Match conditional = Regex.Match(
                directive,
                @"^#\s*(ifdef|ifndef|if|else|endif)\b\s*(!?)\s*(?:defined\s*\(?\s*)?([A-Za-z_][A-Za-z0-9_]*|0|1)?",
                RegexOptions.None,
                PatternLimit);

            switch (conditional.Success ? conditional.Groups[1].Value : string.Empty)
            {
                case "ifdef":
                    regions.Add(defined.Contains(conditional.Groups[3].Value));
                    break;

                case "ifndef":
                    regions.Add(!defined.Contains(conditional.Groups[3].Value));
                    break;

                case "if":
                    if (conditional.Groups[3].Value is "0" or "1")
                    {
                        regions.Add(conditional.Groups[3].Value == "1");
                    }
                    else if (conditional.Groups[3].Success && directive.Contains("defined", StringComparison.Ordinal))
                    {
                        bool present = defined.Contains(conditional.Groups[3].Value);
                        regions.Add(conditional.Groups[2].Value == "!" ? !present : present);
                    }
                    else if (conditional.Groups[3].Success &&
                        defined.Contains(conditional.Groups[3].Value))
                    {
                        // A bare macro the caller has stated is set, such as VALVE_LITTLE_ENDIAN.
                        // Membership of that set means "defined and non-zero" for this purpose.
                        regions.Add(conditional.Groups[2].Value != "!");
                    }
                    else if (conditional.Groups[3].Success &&
                        constants is not null &&
                        constants.TryGetValue(conditional.Groups[3].Value, out int value))
                    {
                        // A bare macro used as a truth value, such as
                        // `#if STUDIO_SEQUENCE_ACTIVITY_LAZY_INITIALIZE`, which the same header
                        // defines as 1. Its own declaration decides the branch.
                        regions.Add(conditional.Groups[2].Value == "!" ? value == 0 : value != 0);
                    }
                    else
                    {
                        unhandled = directive.Trim();
                        return null;
                    }

                    break;

                case "else":
                    if (regions.Count == 0)
                    {
                        unhandled = directive.Trim();
                        return null;
                    }

                    regions[^1] = !regions[^1];
                    break;

                case "endif":
                    if (regions.Count == 0)
                    {
                        unhandled = directive.Trim();
                        return null;
                    }

                    regions.RemoveAt(regions.Count - 1);
                    break;

                default:
                    // #pragma, #define, #include, #undef: nothing to select, nothing to keep.
                    break;
            }
        }

        return kept.ToString();
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
        IReadOnlyDictionary<string, CTypeSize>? composites,
        int? pointerBytes)
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

        // Anything with parentheses is a method or a macro, never a member. A `friend` declaration
        // grants access and occupies no bytes; studiohdr_t has one.
        if (text.Contains('(', StringComparison.Ordinal) ||
            text.StartsWith("friend ", StringComparison.Ordinal))
        {
            return new Declared(string.Empty, new CTypeSize(0, 1), []);
        }

        // Qualifiers that change nothing about a member's size or position. `mutable` appears on
        // mstudiobone_t::physicsbone and would otherwise read as an unknown type name.
        foreach (string qualifier in new[] { "mutable ", "volatile ", "const " })
        {
            text = text.StartsWith(qualifier, StringComparison.Ordinal)
                ? text[qualifier.Length..]
                : text;
        }

        // **A static member occupies no bytes in an instance, so treating it as one would be
        // silently wrong in the direction that matters.** Refuse instead.
        if (text.StartsWith("static ", StringComparison.Ordinal))
        {
            return null;
        }

        // **A pointer member is real and occupies bytes, but how many is not in the header.** MDL
        // headers carry several — `mutable void *virtualModel` in studiohdr_t, `void *pVertexData`
        // in mstudiomodel_t — as runtime scratch that studiomdl still writes space for. The size is
        // the one the FILE was authored with, not the one this process runs at, so the caller states
        // it and the parser refuses when nobody has.
        if (text.Contains('*', StringComparison.Ordinal))
        {
            if (pointerBytes is not { } bytes)
            {
                return null;
            }

            string pointee = text[(text.LastIndexOf('*') + 1)..].Trim();

            return Declarator(pointee, constants) is { } declared
                ? new Declared("pointer", new CTypeSize(bytes, bytes), [declared])
                : null;
        }

        // A nested type DECLARATION is still outside what this models — only a nested member of an
        // already-sized type is handled, through the composites the caller supplies.
        if (text.StartsWith("typedef", StringComparison.Ordinal) ||
            text.StartsWith("union", StringComparison.Ordinal) ||
            text.StartsWith("struct", StringComparison.Ordinal) ||
            text.StartsWith("class", StringComparison.Ordinal))
        {
            return null;
        }

        // **A nested enum declares constants, not storage.** ddispinfo_t carries
        // `enum unnamed { ALLOWEDVERTS_SIZE = ... }` purely to name an array bound, and its braces
        // have already collapsed by the time this sees it — leaving the bare keyword, which occupies
        // no bytes. Skipped rather than refused, because refusing would lose the whole structure
        // over a member that is not one.
        if (text.StartsWith("enum", StringComparison.Ordinal))
        {
            return new Declared(string.Empty, new CTypeSize(0, 1), []);
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

    /// <summary>An alignment, capped by whatever <c>#pragma pack</c> is in force.</summary>
    /// <remarks>
    /// **VTX is byte-packed and that is the whole reason its numbers look wrong.** optimize.h
    /// wraps its declarations in <c>#pragma pack(1)</c>, so <c>StripHeader_t</c> is 27 bytes —
    /// four ints, a short, a byte, two more ints — where natural alignment would pad it to 28.
    /// Every strip after the first would then be read one byte late, and the indices that come
    /// back are real numbers pointing at the wrong vertices.
    /// </remarks>
    private static int Capped(int alignment, int? pack) =>
        pack is { } limit ? Math.Min(alignment, limit) : alignment;

    /// <summary>Rounds an offset up to an alignment boundary.</summary>
    private static int Aligned(int offset, int alignment) =>
        alignment <= 1 ? offset : ((offset + alignment - 1) / alignment) * alignment;
}
