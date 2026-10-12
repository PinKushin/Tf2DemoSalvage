using System;

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
/// **The tokens and the conditionals are `KeyValues`' own** (B524): <see cref="KeyValuesTokens"/> is `ReadToken`, and a
/// key whose tag fails `EvaluateConditional` is not reported — nor is anything in its block — as
/// `RecursiveLoadFromBuffer` reads and discards it. This had its own tokenizer, which took a `[$WIN32]` tag for the
/// next key (`scripts/game_sounds.txt`, six sounds) and a quoted brace for structure.
///
/// **What a stream cannot do is include.** `#base` and `#include` at the top are consumed with the file they name and
/// reported as nothing; none of the 273 files this is pointed at uses either (`kv-census`). A file layered that way
/// goes through <see cref="KeyValuesTree"/>.
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
    /// <param name="text">The document's bytes: UTF-8, or UTF-16 behind its byte order mark.</param>
    /// <param name="visitor">Called for each key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is null.</exception>
    public static void Read(ReadOnlySpan<byte> text, Visitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        KeyValuesTokens tokens = new(KeyValuesTokens.Decode(text));
        int depth = 0;

        while (tokens.Next() is { } key)
        {
            // A closing brace where a key would be: the block ended.
            if (key.Closes)
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            // An opening brace where a key would be belongs to the key before it, which has
            // already been reported — Valve's own files put it on the following line.
            if (key.Opens)
            {
                depth++;
                continue;
            }

            if (depth == 0 && !key.Quoted &&
                (string.Equals(key.Text, "#base", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(key.Text, "#include", StringComparison.OrdinalIgnoreCase)))
            {
                _ = tokens.Next();
                continue;
            }

            bool accepted = true;
            int before = tokens.Position;

            if (tokens.Next() is not { } next)
            {
                _ = visitor(key.Text, null, depth);
                return;
            }

            // A tag between a key and what follows it decides the key.
            if (next.Conditional)
            {
                accepted = KeyValuesTree.EvaluateConditional(next.Text);
                before = tokens.Position;

                if (tokens.Next() is not { } real)
                {
                    return;
                }

                next = real;
            }

            if (next.Opens)
            {
                if (!accepted)
                {
                    Skip(tokens);
                    continue;
                }

                if (!visitor(key.Text, null, depth))
                {
                    return;
                }

                depth++;
                continue;
            }

            if (next.Closes)
            {
                // A key with no value at the end of a block. Reported as a block-less key rather
                // than dropped, and the brace is put back so the depth still falls.
                tokens.Position = before;

                if (accepted && !visitor(key.Text, null, depth))
                {
                    return;
                }

                continue;
            }

            // A tag straight after a value decides the key it follows.
            int after = tokens.Position;

            if (tokens.Next() is { Conditional: true } tag)
            {
                accepted = KeyValuesTree.EvaluateConditional(tag.Text);
            }
            else
            {
                tokens.Position = after;
            }

            if (accepted && !visitor(key.Text, next.Text, depth))
            {
                return;
            }
        }
    }

    /// <summary>Reads past a block whose opening brace has just been read, to the brace that closes it.</summary>
    private static void Skip(KeyValuesTokens tokens)
    {
        int open = 1;

        while (open > 0 && tokens.Next() is { } token)
        {
            if (token.Opens)
            {
                open++;
            }
            else if (token.Closes)
            {
                open--;
            }
        }
    }
}
