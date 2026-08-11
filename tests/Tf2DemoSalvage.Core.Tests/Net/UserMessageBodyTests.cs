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

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8, ModernProtocol);

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

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8, ModernProtocol);

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

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8, ModernProtocol);

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

        UserMessage message = UserMessageBody.Decode(3, "SayText", [.. body], body.Count * 8, ModernProtocol);

        Field(message, "client").ShouldBe("7");
        Field(message, "text").ShouldBe("gg wp");
    }

    [Fact]
    public void NonAsciiText_SurvivesTheBody()
    {
        // Same requirement as everywhere else in this parser: names are arbitrary client bytes.
        byte[] body = TextMsgBody(1, "#TF_Chat", "Пётр");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8, ModernProtocol);

        Field(message, "param1").ShouldBe("Пётр");
    }

    [Fact]
    public void BodyThatDoesNotConsumeExactly_IsLeftUndecoded()
    {
        // The guard that makes a guessed layout safe. Here the stated length is longer than the
        // content, which is what a wrong layout looks like from the inside - and reporting fields
        // anyway would present a misparse as data.
        byte[] body = TextMsgBody(3, "#TF_Name_Change");

        UserMessage message = UserMessageBody.Decode(5, "TextMsg", body, (body.Length * 8) + 32, ModernProtocol);

        message.Fields.ShouldBeNull();
        message.BodyBits.ShouldBe((body.Length * 8) + 32);

        // The name goes with the fields. A layout that refuses is evidence the id is not the
        // message this table claims, so asserting the name over it would state something the
        // bytes contradict - see UserMessageLayoutTests for the case that motivated it.
        message.Name.ShouldBeNull();
    }

    [Fact]
    public void UnknownMessage_KeepsItsNameAndLength()
    {
        // Types with no decoder must stay reported by name. Withholding a name is evidence-driven
        // - it fires only where a layout exists to contradict it - so a message this project has
        // never decoded says nothing either way, and dropping its name would discard information
        // rather than avoid a false claim.
        //
        // This used to use CheapBreakModel as the example of "will never be decoded". It is
        // decoded now, which is the better outcome and a reminder that the set shrinks.
        UserMessage message = UserMessageBody.Decode(2, "HudText", [1, 2, 3], 24, ModernProtocol);

        message.Fields.ShouldBeNull();
        message.Name.ShouldBe("HudText");
        message.UserMessageType.ShouldBe(2);
        message.BodyBits.ShouldBe(24);
    }

    [Fact]
    public void UnterminatedString_IsLeftUndecoded()
    {
        // A body claiming a string that never terminates. Reading to the end and calling it a
        // value would invent one.
        byte[] body = [3, .. Encoding.UTF8.GetBytes("no terminator")];

        UserMessageBody.Decode(5, "TextMsg", body, body.Length * 8, ModernProtocol).Fields.ShouldBeNull();
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
            26, "Damage", whole.Build(), whole.BitCount, ModernProtocol);

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
            26, "Damage", writer.Build(), writer.BitCount, ModernProtocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "damage").ShouldBe(120);
        message.Fields!.Any(field => field.Key == "x").ShouldBeFalse();
    }

    [Fact]
    public void Damage_WhenTheLayoutStopsShortOfTheStatedLength_ReportsNothing()
    {
        // The stated length is exact, not padded: the 77-bit bodies in the protocol-14 corpus
        // demo prove a body is free to end mid-byte. So a layout that finishes early has not
        // fitted the body, it has read a prefix of it - and the leftover bits are the evidence.
        //
        // This is the check that let a whole era through. Accepting "consumed no more than
        // stated" passes any layout short enough, and the modern one is short enough for a
        // protocol-14 body, so 20 of that demo's 24 damage messages reported invented numbers.
        Tf2DemoSalvage.Core.Primitives.BitWriter writer = new();
        writer.Write(120, 16).Write(0, 32).WriteBit(false);

        UserMessage message = UserMessageBody.Decode(
            26, "Damage", writer.Build(), writer.BitCount + 8, ModernProtocol);

        message.Fields.ShouldBeNull();
    }

    [Fact]
    public void Damage_BeforeProtocolFifteen_IsOneByteAndAVector()
    {
        // A different message, not a variant of the same one: no damage-type long, no bit saying
        // whether a position follows, and a single byte of damage where the modern layout sends a
        // short. The vector is the same BitVec3Coord and is always present.
        Tf2DemoSalvage.Core.Primitives.BitWriter coords = new();
        SendPropEncoder.WriteCoord(coords, -1061.5f, CoordFlag);
        SendPropEncoder.WriteCoord(coords, 928.25f, CoordFlag);
        SendPropEncoder.WriteCoord(coords, 64f, CoordFlag);

        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.Write(36, 8)
            .WriteBit(true).WriteBit(true).WriteBit(true)
            .AppendBits(coords.Build(), coords.BitCount);

        UserMessage message = UserMessageBody.Decode(
            18, "Damage", whole.Build(), whole.BitCount, OldProtocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "damage").ShouldBe(36);
        Value(message, "x").ShouldBe(-1061.5f);
        Value(message, "y").ShouldBe(928.25f);
        Value(message, "z").ShouldBe(64f);

        // No "bits" field, because that era does not send one. Reporting a zero would claim the
        // damage had no type flags rather than that the era never said.
        message.Fields!.Any(field => field.Key == "bits").ShouldBeFalse();
    }

    [Fact]
    public void Damage_FromTheProtocolFourteenDemo_RefusesTheModernLayout()
    {
        // Captured from tf2-2008-build3420-pov-cp_granary.dem, where the modern layout read it as
        // damage=16164 with a plausible-looking position. TF2 damage does not reach five figures.
        byte[] body = [0x24, 0x3F, 0x09, 0x01, 0xD7, 0x7E, 0xFD, 0x8B, 0x05, 0x01];

        UserMessageBody.Decode(18, "Damage", body, 77, ModernProtocol).Fields.ShouldBeNull();

        UserMessage message = UserMessageBody.Decode(18, "Damage", body, 77, OldProtocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "damage").ShouldBe(36);
    }

    /// <summary>Protocol 24, where the damage layout is the one Valve's published client reads.</summary>
    private const int ModernProtocol = 24;

    /// <summary>Protocol 14, the corpus's March 2008 build.</summary>
    private const int OldProtocol = 14;
}
