using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>Bytes as text and back with every byte kept: UTF-8 where valid, each other byte as U+DC00 + byte (B823).</summary>
/// <remarks>
/// **TF2 reads a config's BYTES** — `CUtlBuffer::ParseToken` (`src/tier1/utlbuffer.cpp:1242-1326`) ends a word at any byte of
/// 0x80 or more — so a config, a token, an alias body or a ConVar value that holds a byte no UTF-8 character accounts for
/// (a Latin-1 `autoexec.cfg`, or the lone `C3` of an unquoted `é`) must come back as that byte. Python's PEP 383
/// `surrogateescape`: a byte from 0x80 to 0xFF that does not decode is the lone surrogate U+DC80 to U+DCFF. A surrogate never
/// comes out of valid UTF-8, so a spelled byte cannot collide with a character. Replacement decoding makes every such
/// byte U+FFFD, and Latin-1 re-encodes `C3` as `C3 83`; neither round-trips.
/// </remarks>
public static class Utf8Escaped
{
    /// <summary>The surrogate the byte 0x00 would be spelled as; only 0x80 to 0xFF ever are.</summary>
    private const char LoneByte = '\uDC00';

    /// <summary>The spelling of the lone byte 0x80; no lower byte is ever left over by UTF-8.</summary>
    private const char LowestLoneByte = '\uDC80';

    /// <summary>The spelling of the lone byte 0xFF.</summary>
    private const char HighestLoneByte = '\uDCFF';

    /// <summary>Bytes as text: UTF-8 where it is valid, each byte that is not as U+DC00 + byte.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text, from which <see cref="GetBytes"/> gives back the same bytes.</returns>
    public static string GetString(ReadOnlySpan<byte> bytes)
    {
        StringBuilder text = new(bytes.Length);

        while (!bytes.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(bytes, out Rune rune, out int used) == System.Buffers.OperationStatus.Done)
            {
                _ = text.Append(rune.ToString());
            }
            else
            {
                _ = text.Append((char)(LoneByte + bytes[0]));
                used = 1;
            }

            bytes = bytes[used..];
        }

        return text.ToString();
    }

    /// <summary>Text as bytes: UTF-8, a spelled lone byte as its byte, and any other unpaired surrogate as U+FFFD.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] GetBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<byte> bytes = new(text.Length);
        Span<byte> encoded = stackalloc byte[4];

        for (int at = 0; at < text.Length;)
        {
            if (text[at] is >= LowestLoneByte and <= HighestLoneByte)
            {
                bytes.Add((byte)(text[at++] - LoneByte));
                continue;
            }

            // An unpaired surrogate outside the spelled range is U+FFFD, as any UTF-8 encoder writes it.
            _ = Rune.DecodeFromUtf16(text.AsSpan(at), out Rune rune, out int used);
            bytes.AddRange(encoded[..rune.EncodeToUtf8(encoded)]);
            at += used;
        }

        return [.. bytes];
    }

    /// <summary>How many of <see cref="GetBytes"/>' bytes the character at <paramref name="at"/> is, so a count by character agrees.</summary>
    /// <param name="text">The text.</param>
    /// <param name="at">The character's index.</param>
    /// <returns>
    /// A spelled lone byte 1 (B823 review 1, Sonnet); a surrogate pair 4 on its first half and 0 on its second; an unpaired
    /// surrogate 3, the U+FFFD it is encoded as; any other character its UTF-8 length.
    /// </returns>
    public static int ByteCount(string text, int at)
    {
        ArgumentNullException.ThrowIfNull(text);
        char c = text[at];

        if (c < 0x80)
        {
            return 1;
        }

        if (c < 0x800)
        {
            return 2;
        }

        if (char.IsHighSurrogate(c))
        {
            return at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]) ? 4 : 3;
        }

        if (char.IsLowSurrogate(c) && at > 0 && char.IsHighSurrogate(text[at - 1]))
        {
            return 0;
        }

        return c is >= LowestLoneByte and <= HighestLoneByte ? 1 : 3;
    }
}
