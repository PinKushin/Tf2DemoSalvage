using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// Decodes the bodies of the user messages worth reading, and refuses the rest.
/// </summary>
/// <remarks>
/// **Every layout here is a hypothesis, and the stated length is what tests it.** A user message
/// carries its body's size in bits; a correct layout consumes exactly that many. A wrong one lands
/// on the boundary only by coincidence, so exact consumption is a genuine check on a guessed
/// format — and it is the only one available without the game DLL that defines these.
///
/// **A body that does not consume exactly is reported as undecoded, not as fields.** Wrong values
/// that look plausible are worse than no values. That is RISKS B16 stated as a rule: a message
/// implemented from its struct's C types rather than its read function desynchronised a whole
/// packet while every individual number it produced looked reasonable.
///
/// Only a handful of the 79 types are here, chosen from what the corpus actually contains rather
/// than from what sounded useful. <c>CheapBreakModel</c> is 259 of 756 user messages and reports a
/// piece of scenery breaking; <c>Rumble</c> drives a controller. Neither will ever be decoded.
/// </remarks>
public static class UserMessageBody
{
    /// <summary>Builds a <see cref="UserMessage"/>, decoding the body when a layout is known.</summary>
    /// <param name="userMessageType">The game-defined message id.</param>
    /// <param name="name">The registered name, or <c>null</c> if the id is past the table.</param>
    /// <param name="body">The body bytes, as copied from the stream.</param>
    /// <param name="bodyBits">The body's stated length in bits.</param>
    /// <returns>
    /// The message, with <see cref="UserMessage.Fields"/> set only when a known layout consumed
    /// the body exactly.
    /// </returns>
    public static UserMessage Decode(
        int userMessageType, string? name, ReadOnlySpan<byte> body, int bodyBits)
    {
        List<KeyValuePair<string, object?>>? fields = name switch
        {
            "TextMsg" => TextMsg(body, bodyBits),
            "SayText" => SayText(body, bodyBits),
            "ItemPickup" => SingleString(body, bodyBits, "item"),
            "Geiger" => SingleByte(body, bodyBits, "range"),
            "Train" => SingleByte(body, bodyBits, "state"),
            "VoiceSubtitle" => VoiceSubtitle(body, bodyBits),
            _ => null,
        };

        return new UserMessage(userMessageType, name, bodyBits, fields);
    }

    /// <summary>
    /// <c>TextMsg</c> — a destination, a localisation key, and up to four substitutions.
    /// </summary>
    /// <remarks>
    /// The string count is not stated; the body simply ends. Reading until the bits run out is
    /// therefore the format, not a shortcut, and it is why the exact-consumption check matters
    /// more here than anywhere else: an over-read runs into whatever follows and still produces
    /// strings.
    /// </remarks>
    private static List<KeyValuePair<string, object?>>? TextMsg(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || body.Length < 1)
        {
            return null;
        }

        List<KeyValuePair<string, object?>> fields =
            [new("destination", (int)body[0])];

        int offset = 1;
        int index = 0;
        while (offset < ByteLength(bodyBits))
        {
            if (!ReadString(body, ByteLength(bodyBits), ref offset, out string value))
            {
                return null;
            }

            // The four substitution slots are always sent and are usually all empty, so listing
            // them unconditionally put `param1="" param2="" param3="" param4=""` on the end of
            // every announcement in the trace. The key text is kept even when empty, because an
            // empty message is a fact about the message; an unused slot is not.
            if (index == 0)
            {
                fields.Add(new("text", value));
            }
            else if (value.Length > 0)
            {
                fields.Add(new(Substitution(index), value));
            }

            index++;
        }

        return offset == ByteLength(bodyBits) ? fields : null;
    }

    /// <summary><c>SayText</c> — a client slot, the line, and whether it counts as chat.</summary>
    private static List<KeyValuePair<string, object?>>? SayText(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (!IsWholeBytes(bodyBits, body) || body.Length < 3)
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 1;
        if (!ReadString(body, end, ref offset, out string text) || offset + 1 != end)
        {
            return null;
        }

        return
        [
            new("client", (int)body[0]),
            new("text", text),
            new("chat", body[offset] != 0),
        ];
    }

    /// <summary><c>VoiceSubtitle</c> — which player, and which line of which menu.</summary>
    private static List<KeyValuePair<string, object?>>? VoiceSubtitle(
        ReadOnlySpan<byte> body, int bodyBits)
    {
        if (bodyBits != 24)
        {
            return null;
        }

        return
        [
            new("client", (int)body[0]),
            new("menu", (int)body[1]),
            new("item", (int)body[2]),
        ];
    }

    private static List<KeyValuePair<string, object?>>? SingleString(
        ReadOnlySpan<byte> body, int bodyBits, string field)
    {
        if (!IsWholeBytes(bodyBits, body))
        {
            return null;
        }

        int end = ByteLength(bodyBits);
        int offset = 0;
        if (!ReadString(body, end, ref offset, out string value) || offset != end)
        {
            return null;
        }

        return [new(field, value)];
    }

    private static List<KeyValuePair<string, object?>>? SingleByte(
        ReadOnlySpan<byte> body, int bodyBits, string field) =>
        bodyBits == 8 ? [new(field, (int)body[0])] : null;

    /// <summary>Reads a NUL-terminated string, refusing one that never terminates.</summary>
    private static bool ReadString(
        ReadOnlySpan<byte> body, int end, ref int offset, out string value)
    {
        int terminator = body[offset..end].IndexOf((byte)0);
        if (terminator < 0)
        {
            value = string.Empty;
            return false;
        }

        // UTF-8, like every other string in this parser. TF2 names carry Cyrillic and CJK
        // routinely, and ASCII turns each of those bytes into a question mark.
        value = Encoding.UTF8.GetString(body.Slice(offset, terminator));
        offset += terminator + 1;
        return true;
    }

    /// <summary>
    /// Whether the stated length is a whole number of bytes the body actually contains.
    /// </summary>
    /// <remarks>
    /// The length check is not paranoia about malformed files. A stated length longer than the
    /// bytes present is exactly what a wrong layout looks like from inside a decoder, and without
    /// this the slice throws instead of declining — turning "this format does not fit" into an
    /// exception that would abandon the packet.
    /// </remarks>
    private static bool IsWholeBytes(int bodyBits, ReadOnlySpan<byte> body) =>
        bodyBits > 0 && bodyBits % 8 == 0 && ByteLength(bodyBits) <= body.Length;

    private static int ByteLength(int bodyBits) => bodyBits / 8;

    private static string Substitution(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"param{index}");
}
