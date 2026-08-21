using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// Reads Valve's KeyValues as a stream of key/value events.
/// </summary>
/// <remarks>
/// **A stream rather than a tree, because the file this exists for is eight megabytes.**
/// <c>items_game.txt</c> carries every item TF2 has shipped, and what a viewer wants from it is a
/// model path off a few dozen weapons. Materialising the whole thing to read those would allocate
/// tens of megabytes and hold them for the session.
///
/// **Not a general KeyValues implementation and does not pretend to be.** There is no macro
/// expansion, no <c>#base</c> include, no conditional (<c>[$WIN32]</c>) handling. It reads the
/// syntax the shipped data actually uses, which is quoted and bare tokens, nested blocks, and
/// comments to end of line.
/// </remarks>
public static class KeyValuesReader
{
    /// <summary>Called for each key, with its value or <c>null</c> when a block opens.</summary>
    /// <param name="key">The key just read.</param>
    /// <param name="value">Its value, or <c>null</c> when the key opens a block.</param>
    /// <param name="depth">How many blocks enclose this key; zero at the top level.</param>
    /// <returns><c>true</c> to continue reading, <c>false</c> to stop.</returns>
    public delegate bool Visitor(string key, string? value, int depth);

    /// <summary>Reads a KeyValues document, reporting each key as it is met.</summary>
    /// <param name="text">The document's bytes, UTF-8.</param>
    /// <param name="visitor">Called for each key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is null.</exception>
    public static void Read(ReadOnlySpan<byte> text, Visitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        int at = 0;
        int depth = 0;

        while (true)
        {
            if (Token(text, ref at) is not { } key)
            {
                return;
            }

            // A closing brace where a key would be: the block ended.
            if (key == "}")
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            // An opening brace where a key would be belongs to the key before it, which has
            // already been reported — Valve's own files put it on the following line.
            if (key == "{")
            {
                depth++;
                continue;
            }

            int before = at;

            if (Token(text, ref at) is not { } next)
            {
                _ = visitor(key, null, depth);
                return;
            }

            if (next == "{")
            {
                if (!visitor(key, null, depth))
                {
                    return;
                }

                depth++;
                continue;
            }

            if (next == "}")
            {
                // A key with no value at the end of a block. Reported as a block-less key rather
                // than dropped, and the brace is put back so the depth still falls.
                at = before;

                if (!visitor(key, null, depth))
                {
                    return;
                }

                continue;
            }

            if (!visitor(key, next, depth))
            {
                return;
            }
        }
    }

    /// <summary>Reads one token: a quoted string, a brace, or a bare word.</summary>
    /// <remarks>
    /// **Braces are structure outside quotes and text inside them.** A scanner that looked for
    /// them without tracking quotes would lose its depth on the first value containing one.
    /// </remarks>
    private static string? Token(ReadOnlySpan<byte> text, ref int at)
    {
        while (true)
        {
            while (at < text.Length && (text[at] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
            {
                at++;
            }

            if (at >= text.Length)
            {
                return null;
            }

            // A comment runs to the end of the line. The shipped schema uses them heavily, and
            // commented-out keys are exactly the ones a reader must not report.
            if (text[at] == (byte)'/' && at + 1 < text.Length && text[at + 1] == (byte)'/')
            {
                while (at < text.Length && text[at] != (byte)'\n')
                {
                    at++;
                }

                continue;
            }

            break;
        }

        if (text[at] is (byte)'{' or (byte)'}')
        {
            at++;
            return text[at - 1] == (byte)'{' ? "{" : "}";
        }

        if (text[at] == (byte)'"')
        {
            at++;
            int start = at;

            while (at < text.Length && text[at] != (byte)'"')
            {
                at++;
            }

            string quoted = Encoding.UTF8.GetString(text[start..Math.Min(at, text.Length)]);

            if (at < text.Length)
            {
                at++;
            }

            return quoted;
        }

        int word = at;

        while (at < text.Length &&
               text[at] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'{' or (byte)'}' or (byte)'"'))
        {
            at++;
        }

        return Encoding.UTF8.GetString(text[word..at]);
    }
}
