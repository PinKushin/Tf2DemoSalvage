using System.Collections.Generic;
using System.Text;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for <c>SayText2</c>, the user message carrying chat.
/// </summary>
/// <remarks>
/// Two shapes share one message, and which one is present is decided by looking at a byte
/// rather than by a flag: a body whose third byte is a control character in 1..8 is the
/// simplified form, carrying coloured text and nothing else. Anything else begins a localisation
/// key like <c>TF_Chat_All</c>.
///
/// Chat text also carries inline colour codes — control characters, and <c>\x07</c> followed by
/// six hex digits. They have to come out, or every line of a chat log is full of stray bytes.
/// </remarks>
public sealed class ChatMessageTests
{
    /// <summary>Builds a <c>SayText2</c> body: client, flag, then NUL-terminated strings.</summary>
    private static byte[] Body(byte client, byte raw, params string[] strings)
    {
        List<byte> body = [client, raw];
        foreach (string value in strings)
        {
            body.AddRange(Encoding.UTF8.GetBytes(value));
            body.Add(0);
        }

        return [.. body];
    }

    [Fact]
    public void Parse_FullForm_ReadsKindSenderAndText()
    {
        ChatMessage chat = ChatMessage.Parse(
            Body(3, 1, "TF_Chat_All", "Sassy", "gg")).ShouldNotBeNull();

        chat.ClientEntityIndex.ShouldBe(3);
        chat.Kind.ShouldBe("TF_Chat_All");
        chat.From.ShouldBe("Sassy");
        chat.Text.ShouldBe("gg");
    }

    [Fact]
    public void Parse_SimplifiedForm_HasNoSenderAndKeepsTheText()
    {
        // A body whose first string starts with a colour code carries only text. Reading it as
        // the full form would take the message itself as the localisation key and lose it.
        byte[] body = [5, 0, 0x01, .. Encoding.UTF8.GetBytes("server says hello"), 0];

        ChatMessage chat = ChatMessage.Parse(body).ShouldNotBeNull();

        chat.From.ShouldBeNull();
        chat.Text.ShouldBe("server says hello");
    }

    [Fact]
    public void Parse_StripsInlineControlCharacters()
    {
        // Codes 1-6 select colours and are not part of the message.
        byte[] body = [3, 1, .. Encoding.UTF8.GetBytes("TF_Chat_Team"), 0,
            .. Encoding.UTF8.GetBytes("Sassy"), 0,
            0x03, .. Encoding.UTF8.GetBytes("nice"), 0x01, .. Encoding.UTF8.GetBytes(" shot"), 0];

        ChatMessage.Parse(body).ShouldNotBeNull().Text.ShouldBe("nice shot");
    }

    [Fact]
    public void Parse_StripsHexColourCodesAndTheirSixDigits()
    {
        // 0x07 introduces a six-character hex colour. Removing only the marker leaves six
        // stray characters in the middle of the line, which reads as corruption.
        byte[] body = [3, 1, .. Encoding.UTF8.GetBytes("TF_Chat_All"), 0,
            .. Encoding.UTF8.GetBytes("Sassy"), 0,
            0x07, .. Encoding.UTF8.GetBytes("FF3F3FI won"), 0];

        ChatMessage.Parse(body).ShouldNotBeNull().Text.ShouldBe("I won");
    }

    [Fact]
    public void Parse_TextWithNoColourCodes_IsUnchanged()
    {
        // The control. Stripping must not eat ordinary characters.
        ChatMessage.Parse(Body(1, 0, "TF_Chat_All", "a", "hello world"))
            .ShouldNotBeNull().Text.ShouldBe("hello world");
    }

    [Fact]
    public void Parse_BodyTooShort_IsRejectedRatherThanGuessed()
    {
        ChatMessage.Parse([1]).ShouldBeNull();
        ChatMessage.Parse([]).ShouldBeNull();
    }

    [Fact]
    public void Parse_MissingText_IsRejected()
    {
        // Kind and sender but no message. Returning a chat line with empty text would put a
        // blank entry in the log rather than showing something went wrong.
        ChatMessage.Parse(Body(3, 1, "TF_Chat_All", "Sassy")).ShouldBeNull();
    }
}
