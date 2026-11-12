using System;
using System.Buffers.Text;
using System.Text;

namespace Tf2DemoSalvage.Audio;

/// <summary>
/// A wave's <c>VDAT</c> sentence, read only for whether its <c>OPTIONS</c> say <c>voice_duck</c> (B719): what makes the
/// wave a voice source to the speech ducker (<c>CAudioSourceWave::IsVoiceSource</c>, engine.dll <c>0x180053f10</c>).
/// </summary>
/// <remarks>
/// **A port of the published parser, section by section** (<c>public/sentence.cpp</c>): <c>InitFromBuffer</c>
/// (<c>:1049-1072</c>), <c>ParseDataVersionOnePointZero</c> (<c>:672-716</c>), <c>ParsePlaintext</c> (<c>:450</c>),
/// <c>ParseWords</c> (<c>:468</c>), <c>ParseEmphasis</c> (<c>:536</c>), <c>ParseCloseCaption</c> (<c>:565</c>, whose raw
/// byte reads mean a brace inside a phrase is not a brace) and <c>ParseOptions</c> (<c>:637</c>). Tokens are
/// <c>CUtlBuffer::GetString</c>'s in a text buffer (<c>tier1/utlbuffer.cpp:467-516</c>, <c>:625-673</c>).
///
/// **Read in place, with no allocation and no limit**: the engine reads the whole chunk into one buffer
/// (<c>InitFromDataChunk</c>, <c>:1035</c>) and so does the caller here, so a scan that copies nothing costs no more than
/// the chunk already did. **Where the engine would loop forever** — <c>ParsePlaintext</c>, <c>ParseEmphasis</c> and the
/// phoneme loop of <c>ParseWords</c> never test for the end of the buffer — this stops and answers what it has read.
/// </remarks>
internal static class VdatSentence
{
    /// <summary>Whether the sentence's <c>OPTIONS</c> leave <c>voice_duck</c> nonzero.</summary>
    /// <param name="text">The chunk's bytes.</param>
    /// <returns>The flag; false when the chunk is not a <c>VERSION 1.0</c> sentence.</returns>
    public static bool VoiceDuck(ReadOnlySpan<byte> text)
    {
        Tokens tokens = new(text);

        if (!Ascii.EqualsIgnoreCase(tokens.Next(), "VERSION"u8) || Atof(tokens.Next()) is not 1.0)
        {
            return false;
        }

        bool duck = false;

        while (true)
        {
            ReadOnlySpan<byte> section = tokens.Next();

            if (section.IsEmpty || section.SequenceEqual("}"u8) || !Ascii.EqualsIgnoreCase(tokens.Next(), "{"u8))
            {
                return duck;
            }

            if (Section(section, ref tokens, ref duck))
            {
                return duck;
            }
        }
    }

    /// <summary>One section's own parser, by name; true when it reached the end. An unknown name parses nothing (<c>:693-714</c>).</summary>
    private static bool Section(ReadOnlySpan<byte> name, ref Tokens tokens, ref bool duck)
    {
        if (Ascii.EqualsIgnoreCase(name, "PLAINTEXT"u8))
        {
            return !SkipToClose(ref tokens, pairs: false);
        }

        if (Ascii.EqualsIgnoreCase(name, "WORDS"u8))
        {
            return !SkipWords(ref tokens);
        }

        if (Ascii.EqualsIgnoreCase(name, "EMPHASIS"u8))
        {
            return !SkipToClose(ref tokens, pairs: true);
        }

        if (Ascii.EqualsIgnoreCase(name, "CLOSECAPTION"u8))
        {
            return SkipCloseCaption(ref tokens);
        }

        return Ascii.EqualsIgnoreCase(name, "OPTIONS"u8) && Options(ref tokens, ref duck);
    }

    /// <summary><c>ParsePlaintext</c>, or with pairs <c>ParseEmphasis</c>: to a <c>}</c> token; false at the end, where the engine would loop.</summary>
    private static bool SkipToClose(ref Tokens tokens, bool pairs)
    {
        while (true)
        {
            ReadOnlySpan<byte> token = tokens.Next();

            if (token.SequenceEqual("}"u8))
            {
                return true;
            }

            if (token.IsEmpty)
            {
                return false;
            }

            if (pairs)
            {
                _ = tokens.Next();
            }
        }
    }

    /// <summary><c>ParseWords</c>; false at the end inside a word's phonemes, where the engine would loop.</summary>
    private static bool SkipWords(ref Tokens tokens)
    {
        while (true)
        {
            ReadOnlySpan<byte> token = tokens.Next();

            if (token.SequenceEqual("}"u8) || !Ascii.EqualsIgnoreCase(token, "WORD"u8))
            {
                return true;
            }

            // The word, its start and end, then its phonemes' brace.
            _ = tokens.Next();
            _ = tokens.Next();
            _ = tokens.Next();

            if (!Ascii.EqualsIgnoreCase(tokens.Next(), "{"u8))
            {
                return true;
            }

            while (true)
            {
                token = tokens.Next();

                if (token.SequenceEqual("}"u8))
                {
                    break;
                }

                if (token.IsEmpty)
                {
                    return false;
                }

                // Code, name, start, end, volume: the code was this token.
                for (int field = 0; field < 4; field++)
                {
                    _ = tokens.Next();
                }
            }
        }
    }

    /// <summary><c>ParseCloseCaption</c>; true when its raw reads ran out, which ends every later read too.</summary>
    private static bool SkipCloseCaption(ref Tokens tokens)
    {
        const int Stream = 4096;

        while (true)
        {
            ReadOnlySpan<byte> token = tokens.Next();

            if (token.SequenceEqual("}"u8))
            {
                return false;
            }

            if (!Ascii.EqualsIgnoreCase(tokens.Next(), "{"u8))
            {
                return tokens.Failed;
            }

            token = tokens.Next();

            while (!token.SequenceEqual("}"u8) && Ascii.EqualsIgnoreCase(token, "PHRASE"u8))
            {
                _ = tokens.Next();
                int length = Atoi(tokens.Next());

                if (length is < 0 or >= Stream)
                {
                    break;
                }

                // A space, the phrase's bytes, a space: raw, so a brace in a phrase is text.
                tokens.Skip(1);
                tokens.Skip(length);
                tokens.Skip(1);
                _ = tokens.Next();
                _ = tokens.Next();
                token = tokens.Next();
            }
        }
    }

    /// <summary><c>ParseOptions</c>: key and value to a <c>}</c> or the end; true at the end.</summary>
    private static bool Options(ref Tokens tokens, ref bool duck)
    {
        while (true)
        {
            ReadOnlySpan<byte> key = tokens.Next();

            if (key.SequenceEqual("}"u8))
            {
                return false;
            }

            if (key.IsEmpty)
            {
                return true;
            }

            ReadOnlySpan<byte> value = tokens.Next();

            if (Ascii.EqualsIgnoreCase(key, "voice_duck"u8))
            {
                duck = Atoi(value) != 0;
            }
        }
    }

    /// <summary><c>atof</c>: the longest leading number, 0 when there is none.</summary>
    private static double Atof(ReadOnlySpan<byte> text) =>
        Utf8Parser.TryParse(text, out double value, out _) ? value : 0;

    /// <summary><c>atoi</c>: an optional sign and the leading digits, saturating as the CRT's does, 0 when there are none.</summary>
    private static int Atoi(ReadOnlySpan<byte> text)
    {
        bool negative = !text.IsEmpty && text[0] == (byte)'-';
        int at = !text.IsEmpty && (text[0] == (byte)'-' || text[0] == (byte)'+') ? 1 : 0;
        long value = 0;

        for (; at < text.Length && char.IsAsciiDigit((char)text[at]); at++)
        {
            value = Math.Min((value * 10) + (text[at] - '0'), (long)int.MaxValue + 1);
        }

        return (int)Math.Clamp(negative ? -value : value, int.MinValue, int.MaxValue);
    }

    /// <summary><c>CUtlBuffer</c> in text mode over the chunk: whitespace-separated tokens, read in place.</summary>
    private ref struct Tokens(ReadOnlySpan<byte> text)
    {
        private readonly ReadOnlySpan<byte> _text = text;
        private int _at;

        /// <summary>Whether a read ran past the end, which sets <c>GET_OVERFLOW</c> and empties every later read.</summary>
        public bool Failed { get; private set; }

        /// <summary><c>GetString</c>: past <c>isspace</c>, then to the next space or NUL.</summary>
        /// <remarks>
        /// At the end it fails (<c>PeekStringLength</c> 0). At a NUL it answers empty and consumes nothing, so every later
        /// token is empty too (<c>:509</c>, <c>:658-661</c>).
        /// </remarks>
        public ReadOnlySpan<byte> Next()
        {
            if (Failed)
            {
                return [];
            }

            while (_at < _text.Length && IsSpace(_text[_at]))
            {
                _at++;
            }

            if (_at >= _text.Length)
            {
                Failed = true;
                return [];
            }

            int start = _at;

            while (_at < _text.Length && _text[_at] != 0 && !IsSpace(_text[_at]))
            {
                _at++;
            }

            return _text[start.._at];
        }

        /// <summary><c>GetChar</c> or <c>Get</c>: all of <paramref name="count"/> bytes or none, failing past the end.</summary>
        public void Skip(int count)
        {
            if (Failed || count <= 0)
            {
                return;
            }

            if (_text.Length - _at < count)
            {
                Failed = true;
                return;
            }

            _at += count;
        }

        /// <summary>The C locale's <c>isspace</c>.</summary>
        private static bool IsSpace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\v' or (byte)'\f' or (byte)'\r';
    }
}
