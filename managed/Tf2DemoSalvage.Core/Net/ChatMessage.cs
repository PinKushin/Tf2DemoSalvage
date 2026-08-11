using System;
using System.Collections.Generic;
using System.Text;

namespace Tf2DemoSalvage.Core.Net;

/// <summary>
/// One chat line, decoded from a <c>SayText2</c> user message.
/// </summary>
/// <param name="ClientEntityIndex">Entity index of the sender, or 0 for a server message.</param>
/// <param name="Kind">
/// Localisation key describing the channel — <c>TF_Chat_All</c>, <c>TF_Chat_Team</c> and so on.
/// Null on the simplified form.
/// </param>
/// <param name="From">Sender's name as sent, or null on the simplified form.</param>
/// <param name="Text">The message, with inline colour codes removed.</param>
/// <param name="BodyBits">How many bits the user message body occupies.</param>
/// <param name="Body">
/// The body's bits, kept verbatim so the message can be written back to the demo it came from.
/// </param>
public sealed record ChatMessage(
    int ClientEntityIndex,
    string? Kind,
    string? From,
    string Text,
    int BodyBits = 0,
    System.ReadOnlyMemory<byte> Body = default) : INetMessage
{
    /// <inheritdoc />
    /// <remarks>
    /// Reported as the user message it arrived in, because that is what the wire carried. Chat
    /// has no message id of its own — it is one of forty-odd payloads sharing
    /// <c>svc_UserMessage</c>.
    /// </remarks>
    public NetMessageType Type => NetMessageType.UserMessage;

    /// <summary>The user message type carrying chat.</summary>
    public const int SayText2Type = 4;

    /// <summary>Bytes before the first string: sender entity index and a flag.</summary>
    private const int HeaderBytes = 2;

    /// <summary>Highest control character used as a colour selector.</summary>
    private const byte HighestColourCode = 8;

    /// <summary>Introduces a six-digit hex colour.</summary>
    private const byte HexColourCode = 7;

    /// <summary>Digits following <see cref="HexColourCode"/>.</summary>
    private const int HexColourDigits = 6;

    /// <summary>Reads a chat line, or <c>null</c> if the body is not one.</summary>
    /// <param name="body">The user message payload.</param>
    /// <returns>The chat line, or <c>null</c> when the body cannot be read as one.</returns>
    /// <remarks>
    /// Returns null rather than throwing: this sits inside a length-prefixed message, so a body
    /// that makes no sense costs one chat line and cannot desynchronise the packet. A blank line
    /// in the log would be worse than an absent one — it would look like someone said nothing.
    ///
    /// **Two shapes share this message and nothing flags which.** A third byte in 1..8 is a
    /// colour code, meaning the simplified form: coloured text and no sender. Anything else
    /// begins a localisation key. Reading the simplified form as the full one takes the message
    /// itself as the key and loses it.
    /// </remarks>
    public static ChatMessage? Parse(ReadOnlySpan<byte> body)
    {
        if (body.Length <= HeaderBytes)
        {
            return null;
        }

        int client = body[0];
        ReadOnlySpan<byte> rest = body[HeaderBytes..];

        // The deciding byte. A colour code here means there is no kind and no sender.
        if (rest[0] > 0 && rest[0] <= HighestColourCode)
        {
            string? only = ReadString(ref rest);
            return only is null ? null : new ChatMessage(client, null, null, Strip(only));
        }

        string? kind = ReadString(ref rest);
        string? from = ReadString(ref rest);
        string? text = ReadString(ref rest);

        return text is null ? null : new ChatMessage(client, kind, from, Strip(text));
    }

    /// <summary>Takes the next NUL-terminated string, or <c>null</c> if there is not one.</summary>
    private static string? ReadString(ref ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        if (end < 0)
        {
            return null;
        }

        string value = Encoding.UTF8.GetString(data[..end]);
        data = data[(end + 1)..];
        return value;
    }

    /// <summary>
    /// Removes the colour codes the game embeds in chat text.
    /// </summary>
    /// <remarks>
    /// Two forms. Control characters up to 8 select a preset colour and stand alone. A
    /// <c>\x07</c> introduces a six-digit hex colour, and dropping only the marker leaves six
    /// stray characters mid-sentence — which reads as corruption rather than as a colour.
    /// </remarks>
    private static string Strip(string text)
    {
        StringBuilder plain = new(text.Length);
        int index = 0;

        // A while loop rather than a for: a hex colour consumes seven characters, so the step
        // is not uniform and the counter has to move by more than one.
        while (index < text.Length)
        {
            char current = text[index];

            if (current == HexColourCode)
            {
                index += HexColourDigits + 1;
                continue;
            }

            if (current > HighestColourCode)
            {
                plain.Append(current);
            }

            index++;
        }

        return plain.ToString();
    }
}
