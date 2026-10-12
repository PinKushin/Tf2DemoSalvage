using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// vphysics' own text tokenizer — the key parser (<c>FUN_18002e6c0</c>) and the tokenizer (<c>FUN_180003640</c>) under it,
/// which read both `surfaceproperties.txt` (B369) and a `.phy`'s text section (B532).
/// </summary>
/// <remarks>
/// **Not KeyValues.** No conditionals, no `#base`, no escapes; `{}()':` are tokens alone; both the key and the value are
/// lowercased; and every byte from <c>0x80</c> up is white space outside quotes. Moved here from the surface-properties
/// reader so the `.phy` reader, a layer below it, reads through the same one.
/// </remarks>
public static class VphysicsTokens
{
    /// <summary>The token length <c>FUN_18002e6c0</c> passes, <c>0x400</c>, less its terminator.</summary>
    private const int MostTokenBytes = 0x3ff;

    /// <summary>A key and its value, lowercased — <c>FUN_18002e6c0</c>. A key of exactly <c>}</c> reads no value.</summary>
    /// <param name="text">The text; its end reads as the engine's terminator.</param>
    /// <param name="at">Where to read from, moved past what was read; −1 once no token is found.</param>
    /// <returns>The pair.</returns>
    public static (string Key, string Value) ParseKeyValue(ReadOnlySpan<byte> text, ref int at)
    {
        string key = ParseFile(text, ref at);

        if (key == "}")
        {
            return (key, string.Empty);
        }

        string value = ParseFile(text, ref at);

        return (Lower(key), Lower(value));
    }

    /// <summary>One token — <c>FUN_180003640</c> with the break set <c>{}()':</c>; <paramref name="at"/> becomes −1 when none is found.</summary>
    /// <param name="text">The text.</param>
    /// <param name="at">Where to read from, moved past the token.</param>
    /// <returns>The token, empty when there is none.</returns>
    /// <remarks>
    /// **Bytes are compared signed**, so every byte from <c>0x80</c> up is whitespace outside quotes. <c>//</c> runs to the line's
    /// end and <c>/* */</c> to its close; a quoted token runs to its quote or the text's end; a break character is a token alone.
    /// </remarks>
    public static string ParseFile(ReadOnlySpan<byte> text, ref int at)
    {
        if (at < 0)
        {
            return string.Empty;
        }

        int c;

        // Stryker disable all : the Boolean Literal mutator flips this loop's 'true' to 'false',
        // so the loop — which holds the only assignment to 'c' — never runs and the reads of 'c'
        // below are unassigned (CS0165), and Safe Mode then drops every mutation in this method —
        // B410.
        while (true)
        {
            c = Signed(text, at);

            while (c <= 0x20)
            {
                if (c == 0)
                {
                    at = -1;
                    return string.Empty;
                }

                c = Signed(text, ++at);
            }

            if (c == '/' && Signed(text, at + 1) == '/')
            {
                while (Signed(text, at) is not (0 or '\n'))
                {
                    at++;
                }

                continue;
            }

            if (c == '/' && Signed(text, at + 1) == '*')
            {
                at += 2;

                while (Signed(text, at) != 0 && !(Signed(text, at) == '*' && Signed(text, at + 1) == '/'))
                {
                    at++;
                }

                if (Signed(text, at) != 0)
                {
                    at += 2;
                }

                continue;
            }

            break;
        }

        // Stryker restore all

        StringBuilder token = new();

        if (c == '"')
        {
            at++;

            while (true)
            {
                c = Signed(text, at++);

                if (c is '"' or 0)
                {
                    return token.ToString();
                }

                token.Append((char)(byte)c);

                if (token.Length == MostTokenBytes)
                {
                    return token.ToString();
                }
            }
        }

        if (IsBreak(c))
        {
            at++;
            return ((char)c).ToString();
        }

        do
        {
            token.Append((char)(byte)c);
            c = Signed(text, ++at);
        }
        while (token.Length < MostTokenBytes && !IsBreak(c) && c > 0x20);

        return token.ToString();
    }

    /// <summary><c>FUN_1800b9a10</c>: ASCII capitals lowered; the C locale leaves every other byte.</summary>
    private static string Lower(string token)
    {
        char[] lowered = token.ToCharArray();

        for (int index = 0; index < lowered.Length; index++)
        {
            if (lowered[index] is >= 'A' and <= 'Z')
            {
                lowered[index] = (char)(lowered[index] + 0x20);
            }
        }

        return new string(lowered);
    }

    private static bool IsBreak(int c) => c is '{' or '}' or '(' or ')' or '\'' or ':';

    private static int Signed(ReadOnlySpan<byte> text, int at) => at < text.Length ? (sbyte)text[at] : 0;
}
