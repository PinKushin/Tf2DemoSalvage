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

    [Fact]
    public void Parse_NonAsciiSenderAndText_SurviveAsUtf8()
    {
        // Chat is the worst place to get this wrong: the sender field is a player name, and TF2
        // names are arbitrary client-chosen bytes. Decoding these as ASCII replaces every byte
        // above 0x7F with a question mark and nothing fails - the line still parses, still has a
        // sender, and still reads as chat.
        //
        // Found by flipping this decoder to ASCII and watching the whole suite stay green, after
        // the same bug was found for real in the header.
        //
        // The sender is a Steam display name, so the Cyrillic and CJK are ordinary; the emoji is
        // not, since Steam rejects those in names. It stays because chat TEXT has no such
        // restriction and because the decoder must not corrupt valid UTF-8 whatever its source.
        ChatMessage chat = ChatMessage.Parse(
            Body(3, 1, "TF_Chat_All", "Пётр・大将🚀", "gg 🎉 wp")).ShouldNotBeNull();

        chat.From.ShouldBe("Пётр・大将🚀");
        chat.Text.ShouldBe("gg 🎉 wp");
    }

    [Fact]
    public void Parse_EmptyKind_IsTheFullFormWithAnEmptyString()
    {
        // Zero is not a colour code. The deciding test is `> 0 && <= 8`, and every other case
        // here supplies a byte that is either comfortably inside that range or comfortably a
        // letter — so widening the lower bound to `>= 0` changed nothing any of them could see.
        //
        // The same fixture covers the string reader's own boundary. A NUL at offset zero is an
        // empty string, not a missing one: reading it as missing abandons the message and loses
        // the sender and text that follow it perfectly well.
        ChatMessage chat = ChatMessage.Parse(Body(3, 1, "", "Sassy", "gg")).ShouldNotBeNull();

        chat.Kind.ShouldBe("");
        chat.From.ShouldBe("Sassy");
        chat.Text.ShouldBe("gg");
    }

    [Fact]
    public void Parse_HighestColourCode_IsStillTheSimplifiedForm()
    {
        // 8 is the last code, and the existing simplified-form case uses 1 — which cannot tell
        // `<= 8` from `< 8`. Reading this as the full form takes the message itself as the
        // localisation key and then finds no sender, losing the line entirely.
        byte[] body = [5, 0, 0x08, .. Encoding.UTF8.GetBytes("headshot"), 0];

        ChatMessage chat = ChatMessage.Parse(body).ShouldNotBeNull();

        chat.From.ShouldBeNull();
        chat.Kind.ShouldBeNull();
        chat.Text.ShouldBe("headshot");
    }

    [Fact]
    public void Parse_SimplifiedFormWithNoTerminator_IsRejected()
    {
        // The simplified path has its own unterminated-string case, separate from the full form's.
        // Without this, dropping its null check is invisible.
        byte[] body = [5, 0, 0x01, .. Encoding.UTF8.GetBytes("truncated")];

        ChatMessage.Parse(body).ShouldBeNull();
    }

    [Fact]
    public void Parse_StripsTheHighestColourCode()
    {
        // Code 8 is inside the stripped range, and the strip test uses 1 and 3 — neither of which
        // can tell `> 8` from `>= 8`. Leaving it in puts a backspace character in a chat log.
        byte[] body = [3, 1, .. Encoding.UTF8.GetBytes("TF_Chat_All"), 0,
            .. Encoding.UTF8.GetBytes("Sassy"), 0,
            .. Encoding.UTF8.GetBytes("well"), 0x08, .. Encoding.UTF8.GetBytes("played"), 0];

        ChatMessage.Parse(body).ShouldNotBeNull().Text.ShouldBe("wellplayed");
    }
}
