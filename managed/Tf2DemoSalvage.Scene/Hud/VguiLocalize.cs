using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>`CLocalizedStringTable`, closed in vgui2.dll: the `#token` strings a `.res` names.</summary>
/// <remarks>
/// vgui2.dll (`tf2vgui2`):
/// <list type="bullet">
/// <item>`AddFile` (0x180008f80): a path holding `%language%` loads the `english` file, then the game's language over it
/// when that is not English. A file must start with the UTF-16 byte order mark — anything else is "Ignoring non-unicode
/// close caption file". Tokens are read by 0x1800192e0 and a key starting `//` skips its line (0x1800192a0). At the top,
/// `Language` names the file, `Tokens` enters the table and `}` ends the file; in the table `}` leaves it, a file whose name
/// lacks `_english.txt` skips `[english]` keys, and a following `[$…]` token decides whether the pair is added.</item>
/// <item>The conditional: only a token starting `[$` is one. A language name after it (`!` inverting) compares with the
/// game's language; anything else goes to `EvaluateConditional` (0x18001e920), where `$WIN32` and `$WINDOWS` hold and
/// the rest do not — its `!` is read only straight after the `[`, which a `[$` token never has, so `[$!X360]` is
/// false. The shipped `tf_english.txt` has none.</item>
/// <item>`AddString` (0x180009ed0): an existing key takes the new value, so the last file loaded wins. Keys compare
/// without case.</item>
/// </list>
/// Every file the game adds is read from the same search paths, so the first found is the one kept; only
/// `bIncludeFallbackSearchPaths` would stack them, and nothing the HUD reads sets it.
/// </remarks>
/// <param name="language">The game's language, such as `english`.</param>
public sealed class VguiLocalize(string language)
{
    private const int KeyLength = 0x80;
    private const int ValueLength = 0x1000;

    // The language list in `AddFile` a conditional is checked against.
    private static readonly string[] Languages =
    [
        "ENGLISH", "JAPANESE", "GERMAN", "FRENCH", "SPANISH", "ITALIAN", "KOREAN", "TCHINESE", "PORTUGUESE", "SCHINESE",
        "POLISH", "RUSSIAN",
    ];

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>`Find`, the `#` already stripped.</summary>
    /// <param name="key">The token.</param>
    /// <returns>The string, or null when no file named it.</returns>
    public string? Find(string key) => _strings.GetValueOrDefault(key);

    /// <summary>`ConstructString` with variables (0x180025320): `%name%` replaced, `[unknown]` for an unset one.</summary>
    /// <param name="format">The localised string.</param>
    /// <param name="variables">The variables, or null — then every name stays as written.</param>
    /// <returns>The string.</returns>
    /// <remarks>
    /// `%%` is one `%`; `%s` and a digit is copied as written; a `%` with no closing `%` is copied. A name is at most 31
    /// characters, and the result at most 4095 (the label's 4096-character buffer).
    /// </remarks>
    public static string ConstructString(string format, IReadOnlyDictionary<string, string>? variables)
    {
        ArgumentNullException.ThrowIfNull(format);

        System.Text.StringBuilder output = new();
        int index = 0;

        while (index < format.Length && output.Length < ValueLength - 1)
        {
            char character = format[index];

            if (character == '%' && index + 1 < format.Length && format[index + 1] == '%')
            {
                output.Append('%');
                index += 2;
                continue;
            }

            bool positional = character == '%' && index + 2 < format.Length && format[index + 1] == 's' && char.IsAsciiDigit(format[index + 2]);
            int close = character == '%' && !positional && variables is not null ? format.IndexOf('%', index + 1) : -1;

            if (close < 0)
            {
                output.Append(character);
                index++;
                continue;
            }

            string name = format[(index + 1)..close];
            string value = variables!.GetValueOrDefault(name[..Math.Min(name.Length, 31)]) ?? "[unknown]";

            output.Append(value.AsSpan(0, Math.Min(value.Length, ValueLength - 1 - output.Length)));
            index = close + 1;
        }

        return output.ToString();
    }

    /// <summary>`AddFile`.</summary>
    /// <param name="path">The file, which may hold `%language%`.</param>
    /// <param name="read">Reads a game path, or null when it is absent.</param>
    /// <returns>Whether every file it named loaded.</returns>
    public bool AddFile(string path, Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(read);

        if (!path.Contains("%language%", StringComparison.Ordinal))
        {
            return Load(path, read);
        }

        bool loaded = Load(path.Replace("%language%", "english", StringComparison.Ordinal), read);

        if (language.Length == 0 || string.Equals(language, "english", StringComparison.OrdinalIgnoreCase))
        {
            return loaded;
        }

        return Load(path.Replace("%language%", language, StringComparison.Ordinal), read) && loaded;
    }

    private bool Load(string path, Func<string, byte[]?> read)
    {
        if (read(path) is not { } bytes)
        {
            return false;
        }

        if (bytes.Length < 2 || bytes[0] != 0xFF || bytes[1] != 0xFE)
        {
            return false;
        }

        string text = System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2 - (bytes.Length % 2));
        bool english = path.Contains("_english.txt", StringComparison.Ordinal);
        int position = 0;
        bool inTokens = false;

        while (true)
        {
            (string key, _) = ReadToken(text, ref position, KeyLength);

            if (key.Length == 0)
            {
                break;
            }

            if (key.StartsWith("//", StringComparison.Ordinal))
            {
                SkipLine(text, ref position);
                continue;
            }

            (string value, bool quoted) = ReadToken(text, ref position, ValueLength);

            if (value.Length == 0 && !quoted)
            {
                break;
            }

            if (!inTokens)
            {
                if (string.Equals(key, "Tokens", StringComparison.OrdinalIgnoreCase))
                {
                    inTokens = true;
                }
                else if (key == "}")
                {
                    break;
                }

                continue;
            }

            if (key == "}")
            {
                inTokens = false;
                continue;
            }

            if (!english && key.StartsWith("[english]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int peek = position;
            (string condition, bool conditionQuoted) = ReadToken(text, ref peek, ValueLength);

            if (!conditionQuoted && condition.StartsWith("[$", StringComparison.Ordinal))
            {
                position = peek;

                if (!Evaluate(condition))
                {
                    continue;
                }
            }

            _strings[key] = value;
        }

        return true;
    }

    /// <summary>The conditional: a language against the game's, else the platform test (0x18001e920).</summary>
    private bool Evaluate(string condition)
    {
        bool negated = condition.Length > 2 && condition[2] == '!';
        string name = condition[(negated ? 3 : 2)..^1];

        if (Array.Exists(Languages, listed => string.Equals(listed, name, StringComparison.OrdinalIgnoreCase)))
        {
            string current = language.Length == 0 ? "english" : language;

            return string.Equals(name, current, StringComparison.OrdinalIgnoreCase) != negated;
        }

        // vgui2.dll's own copy (0x18001e920) is tier1's, test for test.
        return Content.Assets.KeyValuesTree.EvaluateConditional(condition);
    }

    /// <summary>0x1800192e0: whitespace skipped, then a quoted token with `\n` and `\"` escaped, or up to whitespace.</summary>
    private static (string Token, bool Quoted) ReadToken(string text, ref int position, int length)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        if (position >= text.Length || text[position] == '\0')
        {
            return (string.Empty, false);
        }

        System.Text.StringBuilder token = new();

        if (text[position] != '"')
        {
            while (position < text.Length && text[position] != '\0' && !char.IsWhiteSpace(text[position]) && token.Length < length - 1)
            {
                token.Append(text[position++]);
            }

            return (token.ToString(), false);
        }

        position++;

        while (position < text.Length && text[position] is not ('\0' or '"') && token.Length < length - 1)
        {
            char character = text[position];

            if (character == '\\' && position + 1 < text.Length && text[position + 1] is 'n' or '"')
            {
                character = text[++position] == 'n' ? '\n' : '"';
            }

            token.Append(character);
            position++;
        }

        if (position < text.Length && text[position] == '"')
        {
            position++;
        }

        return (token.ToString(), true);
    }

    /// <summary>0x1800192a0: to the end of the line, then past the line breaks.</summary>
    private static void SkipLine(string text, ref int position)
    {
        while (position < text.Length && text[position] is not ('\0' or '\r' or '\n'))
        {
            position++;
        }

        while (position < text.Length && text[position] is '\r' or '\n')
        {
            position++;
        }
    }
}
