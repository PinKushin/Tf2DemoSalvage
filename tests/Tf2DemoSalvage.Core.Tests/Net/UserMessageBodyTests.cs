using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

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

    /// <summary>The field's value with its own type, for layouts that are not all strings.</summary>
    private static object? Value(UserMessage message, string name) =>
        message.Fields!.First(field => field.Key == name).Value;

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

    /// <summary>SPROP_COORD, the flag the coordinate encoder selects the plain form with.</summary>
    private const int CoordFlag = 1 << 1;

    [Fact]
    public void Damage_CarriesTheAmountAndWhereItCameFrom()
    {
        // The message behind a POV demo's damage numbers, and the layout is Valve's own client
        // rather than a guess - CHudDamageIndicator::MsgFunc_Damage reads a short, a long it
        // discards, a bit saying whether a position follows, and then a coordinate vector.
        //
        // The discarded long matters here even though the game ignores it: it is on the wire, so
        // a decoder that skips it reads the position from the wrong bits.
        Tf2DemoSalvage.Core.Primitives.BitWriter coords = new();
        SendPropEncoder.WriteCoord(coords, -1332.5f, CoordFlag);
        SendPropEncoder.WriteCoord(coords, 2976f, CoordFlag);
        SendPropEncoder.WriteCoord(coords, 64.25f, CoordFlag);

        // The coordinate bits have to land at the writer's own offset rather than being byte
        // aligned, so they are appended bit by bit.
        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.Write(45, 16).Write(0, 32).WriteBit(true)
            .WriteBit(true).WriteBit(true).WriteBit(true)
            .AppendBits(coords.Build(), coords.BitCount);

        UserMessage message = UserMessageBody.Decode(
            26, "Damage", whole.Build(), whole.BitCount);

        message.Fields.ShouldNotBeNull();
        Value(message, "damage").ShouldBe(45);
        Value(message, "x").ShouldBe(-1332.5f);
        Value(message, "y").ShouldBe(2976f);
        Value(message, "z").ShouldBe(64.25f);
    }

    [Fact]
    public void Damage_WithNoPosition_StopsAfterTheFlag()
    {
        // The game returns early when the bit is clear, so nothing follows it. Reading a vector
        // anyway would consume bits that are not there and reject a message that is fine.
        Tf2DemoSalvage.Core.Primitives.BitWriter writer = new();
        writer.Write(120, 16).Write(0, 32).WriteBit(false);

        UserMessage message = UserMessageBody.Decode(
            26, "Damage", writer.Build(), writer.BitCount);

        message.Fields.ShouldNotBeNull();
        Value(message, "damage").ShouldBe(120);
        message.Fields!.Any(field => field.Key == "x").ShouldBeFalse();
    }
}
