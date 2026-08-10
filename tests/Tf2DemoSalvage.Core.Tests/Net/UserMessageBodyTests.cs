using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for decoding the bodies of the user messages worth reading.
/// </summary>
/// <remarks>
/// **Every layout here is a hypothesis, and the length is what tests it.** A user message states
/// its body's size in bits, and a correct layout consumes exactly that many. A wrong one lands on
/// the boundary only by coincidence, so exact consumption is a real check on a guessed format
/// rather than a formality — and it is the only check available without the game DLL that defines
/// these.
///
/// A body that does not consume exactly is reported as undecoded rather than as fields. Wrong
/// values that look plausible are worse than no values, which is the lesson from RISKS B16: that
/// message was implemented from its struct's C types rather than its read function, and the wrong
/// widths desynchronised an entire packet while every individual number looked reasonable.
/// </remarks>
public sealed class UserMessageBodyTests
{
    /// <summary>Builds a body from a destination byte and NUL-terminated strings.</summary>
    private static byte[] TextMsgBody(byte destination, params string[] strings)
    {
        List<byte> body = [destination];
        foreach (string value in strings)
        {
            body.AddRange(Encoding.UTF8.GetBytes(value));
            body.Add(0);
        }

        return [.. body];
    }

    private static string? Field(UserMessage message, string name) =>
        message.Fields?.FirstOrDefault(f => f.Key == name).Value?.ToString();

    [Fact]
    public void TextMsg_ReportsItsDestinationAndStrings()
    {
        // The most-read non-chat user message: announcements, connection notices, round results.
        // 80 of them across the corpus.
        byte[] body = TextMsgBody(3, "#TF_Name_Change", "Sassy", "b4nny");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8);

        message.Fields.ShouldNotBeNull();
        Field(message, "destination").ShouldBe("3");
        Field(message, "text").ShouldBe("#TF_Name_Change");
        Field(message, "param1").ShouldBe("Sassy");
        Field(message, "param2").ShouldBe("b4nny");
    }

    [Fact]
    public void EmptySubstitutionSlots_AreNotListed()
    {
        // TextMsg always sends four substitution slots and real messages leave most of them
        // empty, so listing them unconditionally put `param1="" param2="" param3="" param4=""`
        // on the end of every announcement in the trace. Measured, not assumed: all 80 TextMsg
        // bodies in the corpus carry four slots and 76 of them use none.
        byte[] body = TextMsgBody(3, "#Game_connected", "asian zyzz", "", "", "");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8);

        Field(message, "param1").ShouldBe("asian zyzz");
        message.Fields.ShouldNotBeNull();
        message.Fields.Select(f => f.Key).ShouldBe(["destination", "text", "param1"]);
    }

    [Fact]
    public void EmptyKeyText_IsStillReported()
    {
        // The complement, and the reason the rule is not "drop empty strings": an empty key is a
        // fact about the message, where an unused substitution slot is padding.
        byte[] body = TextMsgBody(3, "");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8);

        message.Fields.ShouldNotBeNull();
        Field(message, "text").ShouldBe(string.Empty);
        message.Fields.Select(f => f.Key).ShouldBe(["destination", "text"]);
    }

    [Fact]
    public void SayText_ReportsTheClientAndTheLine()
    {
        List<byte> body = [7];
        body.AddRange(Encoding.UTF8.GetBytes("gg wp"));
        body.Add(0);
        body.Add(1);

        UserMessage message = UserMessageBody.Decode(3, "SayText", [.. body], body.Count * 8);

        Field(message, "client").ShouldBe("7");
        Field(message, "text").ShouldBe("gg wp");
    }

    [Fact]
    public void NonAsciiText_SurvivesTheBody()
    {
        // Same requirement as everywhere else in this parser: names are arbitrary client bytes.
        byte[] body = TextMsgBody(1, "#TF_Chat", "Пётр");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8);

        Field(message, "param1").ShouldBe("Пётр");
    }

    [Fact]
    public void BodyThatDoesNotConsumeExactly_IsLeftUndecoded()
    {
        // The guard that makes a guessed layout safe. Here the stated length is longer than the
        // content, which is what a wrong layout looks like from the inside - and reporting fields
        // anyway would present a misparse as data.
        byte[] body = TextMsgBody(3, "#TF_Name_Change");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, (body.Length * 8) + 32);

        message.Fields.ShouldBeNull();
        message.Name.ShouldBe("TextMsg");
        message.BodyBits.ShouldBe((body.Length * 8) + 32);
    }

    [Fact]
    public void UnknownMessage_KeepsItsNameAndLength()
    {
        // Most of the 79 types have no decoder and never will - CheapBreakModel is 259 of the
        // corpus's 756 user messages and says nothing a reader wants. They must stay reported.
        UserMessage message = UserMessageBody.Decode(45, "CheapBreakModel", [1, 2, 3], 24);

        message.Fields.ShouldBeNull();
        message.Name.ShouldBe("CheapBreakModel");
        message.UserMessageType.ShouldBe(45);
        message.BodyBits.ShouldBe(24);
    }

    [Fact]
    public void UnterminatedString_IsLeftUndecoded()
    {
        // A body claiming a string that never terminates. Reading to the end and calling it a
        // value would invent one.
        byte[] body = [3, .. Encoding.UTF8.GetBytes("no terminator")];

        UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8).Fields.ShouldBeNull();
    }
}
