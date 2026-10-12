using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>One KeyValues token and how it was written.</summary>
/// <param name="Text">The token, without its quotes.</param>
/// <param name="Quoted">`wasQuoted`: a quoted `{` is text, a bare one is structure.</param>
/// <param name="Conditional">`wasConditional`: a bare token holding `[` and then `]`, such as `[$WIN32]`.</param>
public readonly record struct KeyValuesToken(string Text, bool Quoted, bool Conditional)
{
    /// <summary>Whether this is the bare brace that opens a block.</summary>
    public bool Opens => !Quoted && Text == "{";

    /// <summary>Whether this is the bare brace that closes a block.</summary>
    public bool Closes => !Quoted && Text == "}";
}

/// <summary>
/// `KeyValues::ReadToken` over a whole file (`tier1/KeyValues.cpp:538-618`) — THE tokenizer, for every reader of Valve's
/// KeyValues text in this project (B524).
/// </summary>
/// <remarks>
/// <code>
/// loop: EatWhiteSpace; EatCPPComment — "//" to the end of the line, only where a token would start
/// '"'        : a quoted token, read to the next '"' with no escapes (GetNoEscCharConversion, the default)
/// '{' or '}' : that one character
/// otherwise  : a bare token, ended by white space, '"', '{', '}' or a NUL; conditional when it held '[' and then ']'
/// </code>
///
/// **One, because four readers each had their own and each was wrong differently.** A substring search found keys
/// inside comments (B523: every soldier jumped in the reference pose); the stream reader took a quoted brace for
/// structure and a `[$X360]` tag for the next key; the material reader ran a bare token through a quote. The census is
/// in RISKS B524.
///
/// *Not built:* `UsesEscapeSequences( true )` — the C-string conversion a few loaders ask for. No file this project
/// reads through here is loaded that way.
/// </remarks>
/// <param name="text">The file, decoded.</param>
public sealed class KeyValuesTokens(string text)
{
    /// <summary>`KEYVALUES_TOKEN_SIZE`: a longer token is cut.</summary>
    private const int TokenSize = 1024 * 4;

    /// <summary>Where the next token is read from; settable, to look one token ahead and step back.</summary>
    public int Position { get; set; }

    /// <summary>Decodes a file as `LoadFromBuffer` does: UTF-16 behind its byte order mark, anything else UTF-8.</summary>
    /// <param name="bytes">The file.</param>
    /// <returns>Its text.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
            ? Encoding.Unicode.GetString(bytes[2..])
            : Encoding.UTF8.GetString(bytes);

    /// <summary>The next token, or null at the end of the file.</summary>
    /// <returns>The token.</returns>
    public KeyValuesToken? Next()
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

            return new KeyValuesToken(Cut(quoted), Quoted: true, Conditional: false);
        }

        if (first is '{' or '}')
        {
            Position++;
            return new KeyValuesToken(first.ToString(), Quoted: false, Conditional: false);
        }

        int start = Position;
        bool opened = false;
        bool conditional = false;

        while (Position < text.Length)
        {
            char c = text[Position];

            if (c is '"' or '{' or '}' or '\0' || char.IsWhiteSpace(c))
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

        // A NUL ends the token and the buffer: `if ( *c == 0 ) break;` with nothing after it ever read.
        if (Position < text.Length && text[Position] == '\0')
        {
            string last = text[start..Position];

            Position = text.Length;

            return last.Length > 0 ? new KeyValuesToken(Cut(last), Quoted: false, conditional) : null;
        }

        return new KeyValuesToken(Cut(text[start..Position]), Quoted: false, conditional);
    }

    private static string Cut(string token) => token.Length < TokenSize ? token : token[..(TokenSize - 1)];
}
