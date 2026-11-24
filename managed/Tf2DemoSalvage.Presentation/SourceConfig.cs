using System;
using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Reads the parts of a Source <c>.cfg</c> this viewer understands, and ignores the rest.
/// </summary>
/// <remarks>
/// **The goal is that a TF2 player pastes their own config in and their controls work** — including
/// a mastercomfig pack, which ships as `.cfg` files inside a `.vpk`. This project has read one of
/// those before: `docs/findings/24-reference-capture.md` pins the reference capture state against
/// `mastercomfig-base.vpk`, so the VPK route is known ground rather than a new problem.
///
/// **Ignoring is the primary feature, not an afterthought.** A real config is hundreds of lines of
/// `mat_*`, `cl_*`, `snd_*`, `alias`, `exec` and `incrementvar`, and this viewer implements almost
/// none of it. A parser that objected to anything it did not know would reject every real file it
/// was pointed at. So unknown lines are skipped in silence and only `bind` is acted on.
///
/// **What that costs, stated plainly:** a typo in a binding is indistinguishable from a command this
/// viewer does not implement, and both do nothing. <see cref="ReadBinds"/> therefore returns every
/// bind it saw rather than only the ones that mapped, so a caller can report the difference.
/// </remarks>
public static class SourceConfig
{
    /// <summary>Every <c>bind</c> in a config, in file order.</summary>
    /// <param name="text">The config's text.</param>
    /// <returns>Key and command pairs, later ones last.</returns>
    /// <remarks>
    /// **Later wins, and the order is why this returns a list rather than a dictionary.** A Source
    /// config is executed top to bottom, so a file that binds a key twice means the second one — and
    /// `exec`'d files layer the same way. Collapsing to a dictionary here would pick arbitrarily.
    ///
    /// **`unbindall` is honoured**, because `config_default.cfg` opens with it and mastercomfig
    /// packs use it too. Ignoring it would leave defaults in place that the file went out of its way
    /// to clear.
    /// </remarks>
    public static IReadOnlyList<(string Key, string Command)> ReadBinds(string? text)
    {
        List<(string Key, string Command)> binds = [];

        foreach (List<string> tokens in Commands(text))
        {
            if (tokens.Count == 0)
            {
                continue;
            }

            if (tokens[0].Equals("unbindall", StringComparison.OrdinalIgnoreCase))
            {
                // Everything before this is discarded, exactly as the engine would.
                binds.Clear();
                continue;
            }

            if (tokens[0].Equals("unbind", StringComparison.OrdinalIgnoreCase))
            {
                if (Unbinds(tokens))
                {
                    binds.Add((tokens[1], string.Empty));
                }

                continue;
            }

            if (tokens[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && Binds(tokens))
            {
                binds.Add((tokens[1], Binding(tokens[1], tokens[2])));
            }

            // Everything else — cvars, alias, exec, incrementvar — is somebody else's business.
        }

        return binds;
    }

    /// <summary>Whether a <c>bind</c> sets its key: exactly three arguments (`Key_Bind_f`, `engine.dll` x64 FUN_1801b4e60).</summary>
    /// <param name="tokens">The command's arguments, `bind` first.</param>
    /// <returns>True at three; two show the binding, and anything else prints the usage line.</returns>
    internal static bool Binds(IReadOnlyList<string> tokens) => tokens.Count == 3;

    /// <summary>Whether an <c>unbind</c> clears its key: exactly two arguments, and not `ESCAPE` (`Key_Unbind_f`).</summary>
    /// <param name="tokens">The command's arguments, `unbind` first.</param>
    /// <returns>True when the key's binding becomes the empty string.</returns>
    /// <remarks>`engine.dll` x64 build 1007 FUN_1801b6a30: `argc != 2` prints the usage line, key 0x46 prints `Can't unbind
    /// ESCAPE key`; otherwise the binding is set to "" — not null, so `bind k` then prints `"k" = ""` (B823 review 1).</remarks>
    internal static bool Unbinds(IReadOnlyList<string> tokens) => tokens.Count == 2 && !IsEscape(tokens[1]);

    /// <summary>What <c>bind key command</c> stores: `cancelselect` for `ESCAPE`, whatever the line says (FUN_1801b4560).</summary>
    /// <param name="key">The key's name.</param>
    /// <param name="command">The command the line gives.</param>
    /// <returns>The binding.</returns>
    internal static string Binding(string key, string command) => IsEscape(key) ? EscapeBinding : command;

    /// <summary>Whether a key name is `KEY_ESCAPE` (0x46), the key `bind` and `unbind` treat apart.</summary>
    /// <remarks>*Interpolated:* matched without case — the engine's key-name lookup (an input system call, vtable +0xe8) is
    /// not read. `KeyNames` in the view also takes `ESC` for this key, which TF2's name table may not (B826).</remarks>
    internal static bool IsEscape(string key) => key.Equals(EscapeKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>`KEY_ESCAPE`'s name.</summary>
    private const string EscapeKey = "ESCAPE";

    /// <summary>What `bind ESCAPE …` binds.</summary>
    private const string EscapeBinding = "cancelselect";

    /// <summary>Every <c>alias</c> in a config, by name.</summary>
    /// <param name="text">The config's text.</param>
    /// <returns>Alias name to body, later definitions winning.</returns>
    /// <remarks>
    /// **Competitive configs bind movement to aliases, not to engine commands, and this was found
    /// on a real one.** The owner's `config.cfg` binds `w` to `+mfwd`, and `autoexec.cfg` defines:
    ///
    /// <code>
    /// alias +mfwd   "-back;      +forward;   alias checkfwd   +forward"
    /// alias -mfwd   "-forward;   checkback;  alias checkfwd   none"
    /// </code>
    ///
    /// That is a null-cancelling movement script — the pattern that makes opposite keys behave when
    /// both are held — and it is ordinary in comp play. A reader that only understood engine
    /// commands would ignore every movement bind in such a config and report success.
    ///
    /// **Fifteen synthetic tests passed before this was noticed.** They were built from
    /// `config_default.cfg`, which binds movement directly, so none of them could contain an alias.
    /// The real file is what exposed it.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ReadAliases(string? text)
    {
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (List<string> tokens in Commands(text))
        {
            if (tokens.Count >= 3 && tokens[0].Equals("alias", StringComparison.OrdinalIgnoreCase))
            {
                // Later wins, as with binds: a config is executed top to bottom.
                aliases[tokens[1]] = string.Join(' ', tokens.GetRange(2, tokens.Count - 2));
            }
        }

        return aliases;
    }

    /// <summary>The commands an alias body runs, in order.</summary>
    /// <param name="body">The body, such as <c>-back; +forward; alias checkfwd +forward</c>.</param>
    /// <returns>Each semicolon-separated command's leading token.</returns>
    /// <remarks>
    /// **Only the first token of each command matters here.** `alias checkfwd +forward` is a
    /// nested definition rather than an invocation of `+forward`, so taking every word would find
    /// `+forward` in a clause that does not run it. Taking the leading token yields `alias`, which
    /// this viewer does not implement and therefore ignores — which is the correct outcome.
    /// </remarks>
    public static IReadOnlyList<string> Body(string? body)
    {
        List<string> commands = [];

        foreach (string clause in Clauses(body))
        {
            IReadOnlyList<string> tokens = Tokens(clause);

            if (tokens.Count > 0)
            {
                commands.Add(tokens[0]);
            }
        }

        return commands;
    }

    /// <summary>Splits a line or an alias body into its semicolon-separated commands.</summary>
    /// <param name="text">The line or body.</param>
    /// <returns>Each command, comments removed, in order.</returns>
    /// <remarks>
    /// **A semicolon inside quotes is not a separator**, and this is the case that matters rather
    /// than a hypothetical: the null-cancel script writes
    ///
    /// <code>
    /// alias +mfwd "-back; +forward; alias checkfwd +forward"
    /// </code>
    ///
    /// on one line. Splitting that on every semicolon yields three fragments, none of which parses,
    /// and the alias is silently lost. Once the quotes come off the body, the same semicolons
    /// *are* separators — which is why this is called again on the body when the alias runs.
    /// </remarks>
    public static IReadOnlyList<string> Clauses(string? text)
    {
        List<string> clauses = [];
        StringBuilder current = new();
        bool quoted = false;

        string line = text ?? string.Empty;

        for (int at = 0; at < line.Length; at++)
        {
            char c = line[at];

            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '/' && at + 1 < line.Length && line[at + 1] == '/')
            {
                break;
            }
            else if (!quoted && c == ';')
            {
                Add(clauses, current);
                continue;
            }

            current.Append(c);
        }

        Add(clauses, current);
        return clauses;

        static void Add(List<string> into, StringBuilder from)
        {
            // C's `isspace` alone (B823 review 1, Fable): .NET's `Trim` would take U+00A0 and U+2003, which the engine reads as
            // the bytes of a word.
            if (from.ToString().Trim(CSpace) is { Length: > 0 } clause)
            {
                into.Add(clause);
            }

            from.Clear();
        }
    }

    /// <summary>Splits one command into its tokens, honouring quotes and comments.</summary>
    /// <param name="clause">The command.</param>
    /// <returns>The tokens, quotes removed.</returns>
    /// <remarks>
    /// **TF2 tokenizes BYTES, so this does too** (B823): the clause is encoded to UTF-8 first, and a word ends at any byte of
    /// 0x80 or more (see <see cref="Tokenise"/>), so an unquoted `café` is `caf`, `C3`, `A9`.
    ///
    /// **A lone byte is spelled as the lone surrogate U+DC00 + byte** — Python's `surrogateescape` (PEP 383). Chosen because
    /// it is the one spelling that round-trips: an alias body is its arguments joined by spaces (`Cmd_Alias_f`, `engine.dll`
    /// FUN_180166a00) and runs through this tokenizer again, so `C3` must come back as the byte `C3`. UTF-8 decoding with
    /// replacement makes every lone byte U+FFFD, losing which byte it was; Latin-1 keeps them distinct but re-encodes `C3`
    /// as `C3 83` on the second pass. A surrogate cannot come out of valid UTF-8, so a spelled byte never collides with a
    /// character, and a quoted string, valid UTF-8 between two ASCII quotes, decodes as its text.
    /// </remarks>
    public static IReadOnlyList<string> Tokens(string? clause) => Tokenise(clause ?? string.Empty, out _);

    /// <summary>The value a console ConVar set takes from a command: its `ArgS`, trimmed as the engine trims it (B823).</summary>
    /// <param name="clause">The command, its ConVar name first.</param>
    /// <returns>The value; empty when the command has no argument.</returns>
    /// <remarks>
    /// `engine.dll` FUN_180176c50 (x64, the function holding "Can't use cheat cvar %s in multiplayer"), read in the
    /// disassembly: it copies `ArgS()` — the command's text from argument 1 on, its opening quote included
    /// (`CCommand::Tokenize`'s `m_nArgv0Size`, tier1/convar.cpp:432-451; convar.h:232-235) — drops a leading `"`, clears
    /// trailing bytes while `CMP byte ptr [RCX],0x20; JG` does not jump (a SIGNED compare, so every byte of 0x80 or more is
    /// cleared with the spaces), then clears one trailing `"` if it began with one. So `name a b` sets `a b`, `name café`
    /// sets `caf`, and `name éa` sets `éa`. *Not implemented*: the 1024-byte copy cap, past the 511-byte command limit.
    /// </remarks>
    public static string ConVarValue(string? clause)
    {
        _ = Tokenise(clause ?? string.Empty, out byte[] argS);

        bool quoted = argS.Length > 0 && argS[0] == '"';
        ReadOnlySpan<byte> value = quoted ? argS.AsSpan(1) : argS;
        int end = value.Length;

        while (end > 0 && (sbyte)value[end - 1] <= ' ')
        {
            end--;
        }

        if (quoted && end > 0 && value[end - 1] == '"')
        {
            end--;
        }

        return Utf8Escaped.GetString(value[..end]);
    }

    /// <summary>`CCommand::Tokenize`'s break set (tier1/convar.cpp:326): each is an argument of its own.</summary>
    private const string BreakSet = "{}()':";

    /// <summary>`CCommand::Tokenize` (tier1/convar.cpp:389): one `CUtlBuffer::ParseToken` per argument (B532).</summary>
    /// <remarks>
    /// <code>
    /// // tier1/utlbuffer.cpp:1242-1326
    /// EatWhiteSpace (isspace); a "//" here eats to the end of the line
    /// '"' → everything to the next '"' or the end of the buffer, the quotes dropped — an EMPTY string is an argument
    /// a break character → itself, alone
    /// else a word: up to a break character, a '"' or anything &lt;= ' ' — `c` a signed char, so any byte &gt;= 0x80 too
    /// </code>
    ///
    /// **This replaced a tokenizer of this project's own**, which dropped `""` — so `alias noop ""` and `bind k ""` were
    /// two arguments and ignored — glued a quoted run to the word beside it, and did not know the break set. A config
    /// is not KeyValues and has no escapes at all.
    ///
    /// **It reads the clause's UTF-8 bytes** (B823): `ParseToken` stores a word's first `char` unchecked and ends the word at
    /// `c &lt;= ' '` (utlbuffer.cpp:1303-1320), and `EatWhiteSpace` tests `isspace` on the unsigned byte (:404-415), so a byte
    /// of 0x80 or more ends a word and starts the next. A word therefore holds at most one such byte, its first. The text is
    /// cut at a NUL, as `Q_strlen` cuts the copy (convar.cpp:404-411).
    /// </remarks>
    /// <param name="text">The command.</param>
    /// <param name="argS">`ArgS()`: the bytes from argument 1's start, its opening quote included, or none.</param>
    private static List<string> Tokenise(string text, out byte[] argS)
    {
        byte[] line = Bytes(text);
        List<string> tokens = [];
        int at = 0;
        argS = [];

        while (true)
        {
            // Whitespace, then a comment where an argument would start, then whitespace again.
            while (at < line.Length && (IsSpace(line[at]) || IsComment(line, at)))
            {
                at = IsComment(line, at) ? EndOfLine(line, at) : at + 1;
            }

            if (at >= line.Length)
            {
                return tokens;
            }

            if (tokens.Count == 1)
            {
                // `m_nArgv0Size` (convar.cpp:432-451): where argument 1 starts, an opening quote included.
                argS = line[at..];
            }

            byte first = line[at++];

            if (first == '"')
            {
                int close = Array.IndexOf(line, (byte)'"', at);
                int end = close < 0 ? line.Length : close;

                tokens.Add(Utf8Escaped.GetString(line.AsSpan(at, end - at)));
                at = end + 1;
            }
            else if (IsBreak(first))
            {
                tokens.Add(((char)first).ToString());
            }
            else
            {
                int start = at - 1;

                while (at < line.Length && (sbyte)line[at] > ' ' && line[at] != '"' && !IsBreak(line[at]))
                {
                    at++;
                }

                tokens.Add(Utf8Escaped.GetString(line.AsSpan(start, at - start)));
            }
        }
    }

    /// <summary>The bytes TF2 would hold for a clause (<see cref="Utf8Escaped.GetBytes"/>), cut at a NUL.</summary>
    private static byte[] Bytes(string text)
    {
        byte[] bytes = Utf8Escaped.GetBytes(text);
        int nul = Array.IndexOf(bytes, (byte)0);

        return nul < 0 ? bytes : bytes[..nul];
    }

    private static bool IsBreak(byte b) => b < 0x80 && BreakSet.Contains((char)b, StringComparison.Ordinal);

    /// <summary>C's `isspace` on the unsigned byte in the "C" locale: space, and tab through carriage return.</summary>
    private static bool IsSpace(byte b) => b == ' ' || (b >= '\t' && b <= '\r');

    /// <summary>The characters <see cref="IsSpace"/> is true of, for trimming text.</summary>
    private static readonly char[] CSpace = [' ', '\t', '\n', '\v', '\f', '\r'];

    /// <summary>Every command of a config as its arguments: lines, then `;` clauses, then <see cref="Tokens"/>.</summary>
    private static IEnumerable<List<string>> Commands(string? text)
    {
        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            foreach (string clause in Clauses(line))
            {
                yield return Tokenise(clause, out _);
            }
        }
    }

    private static bool IsComment(byte[] line, int at) => line[at] == '/' && at + 1 < line.Length && line[at + 1] == '/';

    /// <summary>Past the line break that ends a comment, or the end of the text.</summary>
    private static int EndOfLine(byte[] line, int at)
    {
        int end = Array.IndexOf(line, (byte)'\n', at);

        return end < 0 ? line.Length : end + 1;
    }
}
