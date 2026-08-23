using System;
using System.Collections.Generic;
using System.Text;

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

        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            List<string> tokens = Tokenise(line);

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

            if (tokens[0].Equals("unbind", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 2)
            {
                binds.Add((tokens[1], string.Empty));
                continue;
            }

            if (tokens[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 3)
            {
                binds.Add((tokens[1], tokens[2]));
            }

            // Everything else — cvars, alias, exec, incrementvar — is somebody else's business.
        }

        return binds;
    }

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

        foreach (string line in (text ?? string.Empty).Split('\n'))
        {
            List<string> tokens = Tokenise(line);

            if (tokens.Count >= 3 && tokens[0].Equals("alias", StringComparison.OrdinalIgnoreCase))
            {
                // Later wins, as with binds: a config is executed top to bottom.
                aliases[tokens[1]] = tokens[2];
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
            if (from.ToString().Trim() is { Length: > 0 } clause)
            {
                into.Add(clause);
            }

            from.Clear();
        }
    }

    /// <summary>Splits one command into its tokens, honouring quotes and comments.</summary>
    /// <param name="clause">The command.</param>
    /// <returns>The tokens, quotes removed.</returns>
    public static IReadOnlyList<string> Tokens(string? clause) => Tokenise(clause ?? string.Empty);

    /// <summary>Splits one config line into tokens, honouring quotes and comments.</summary>
    /// <remarks>
    /// **Quotes are optional in Source and both forms appear in the same file.**
    /// `config_default.cfg` writes `bind "w" "+forward"` while a hand-edited `autoexec.cfg` is as
    /// likely to say `bind w +forward`. A parser that required quotes would silently read half a
    /// real config.
    ///
    /// **A `//` inside quotes is not a comment.** No binding needs one, but a cvar value can — and
    /// stripping to the first `//` regardless would truncate the line and turn a value into a
    /// different value rather than into an error.
    /// </remarks>
    private static List<string> Tokenise(string line)
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
