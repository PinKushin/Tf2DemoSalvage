using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Named user-message payloads, built by hand and decoded into fields.
/// </summary>
/// <remarks>
/// **`svc_UserMessage` is forty-odd different formats sharing one message id**, and each named
/// layout is its own decoder. Until now every one of them was reachable only from a recording that
/// happened to contain that message, which is why <c>UserMessageBody</c> had 77 lines never
/// executed by <c>Core.Tests</c> — the layouts nobody in the corpus triggered.
///
/// **What anchors these is not the fixture.** A body built here encodes this project's reading of
/// the layout on both sides, so agreement proves consistency rather than correctness. The
/// semantics are anchored elsewhere and already: <c>UserMessageLayoutTests</c> and
/// <c>HapticMessageConformanceTests</c> check the widths and orders against Valve's own source,
/// and each decoder cites the client function it came from. What these add is that the decoder
/// runs at all, on values chosen to separate adjacent fields.
///
/// That distinction is why the values are deliberately distinct rather than convenient. Several of
/// these layouts are runs of same-width fields — Fade is three sixteen-bit numbers then four bytes
/// — and a body of zeroes decodes identically however the fields are transposed.
/// </remarks>
public sealed class UserMessageLayoutDemoTests
{
    [Test]
    public void Decode_Fade_SeparatesItsThreeShortsAndFourColourBytes()
    {
        // Three sixteen-bit fields then four bytes, ten in total. Distinct values throughout,
        // because a transposition of any two is invisible on a body of zeroes — and the colour
        // bytes are adjacent and interchangeable by inspection.
        byte[] body = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1500);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 250);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 0x0009);
        body[6] = 11;
        body[7] = 22;
        body[8] = 33;
        body[9] = 44;

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("Fade", body);

        Field(fields, "duration").ShouldBe(1500);
        Field(fields, "holdtime").ShouldBe(250);
        Field(fields, "flags").ShouldBe(0x0009);
        Field(fields, "r").ShouldBe(11);
        Field(fields, "g").ShouldBe(22);
        Field(fields, "b").ShouldBe(33);
        Field(fields, "a").ShouldBe(44);
    }

    [Test]
    public void Decode_Shake_ReadsAByteThenThreeFloats()
    {
        // Thirteen bytes: a command byte then amplitude, frequency and duration. The three floats
        // are the same width and adjacent, so they are given values that could not be mistaken for
        // one another.
        byte[] body = new byte[13];
        body[0] = 2;
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(1), 16.5f);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(5), 40f);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(9), 1.25f);

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("Shake", body);

        Field(fields, "command").ShouldBe(2);
        Field(fields, "amplitude").ShouldBe(16.5f);
        Field(fields, "frequency").ShouldBe(40f);
        Field(fields, "duration").ShouldBe(1.25f);
    }

    [Test]
    public void Decode_TextMsg_KeepsAnEmptyTextButDropsUnusedSubstitutions()
    {
        // **The asymmetry here is deliberate and worth pinning.** The four substitution slots are
        // always sent and usually empty, so listing them unconditionally put
        // param1="" param2="" param3="" param4="" on the end of every announcement in the trace.
        // An empty MESSAGE is a fact about the message; an unused slot is not.
        byte[] body = Bytes(3, "#Game_connected", "Heavy", string.Empty, string.Empty, string.Empty);

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("TextMsg", body);

        Field(fields, "destination").ShouldBe(3);
        Field(fields, "text").ShouldBe("#Game_connected");
        fields.Count(pair => pair.Key.StartsWith("param", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Test]
    public void Decode_TextMsg_WithAnEmptyMessage_StillReportsTheTextField()
    {
        // The other side of that rule, and the one a "drop empties" shortcut breaks: the message
        // itself is kept even when empty, so a blank announcement is distinguishable from none.
        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields(
            "TextMsg", Bytes(1, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));

        Field(fields, "text").ShouldBe(string.Empty);
    }

    [Test]
    public void Decode_ItemPickup_IsASingleNulTerminatedString()
    {
        Field(Fields("ItemPickup", Bytes("ammopack_medium")), "item")
            .ShouldBe("ammopack_medium");
    }

    [Test]
    public void Decode_Geiger_IsASingleByte()
    {
        // A one-byte layout is the narrowest thing here, and the case where a decoder that read
        // two bytes would still succeed on most bodies because the next byte is usually there.
        Field(Fields("Geiger", [42]), "range").ShouldBe(42);
    }

    [Test]
    public void Decode_PlayerShieldBlocked_IsTwoEntityIndices()
    {
        // Two adjacent single-byte entity indices, which is exactly the pair a transposition
        // survives when both are small. Different values separate them.
        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("PlayerShieldBlocked", [3, 9]);

        Field(fields, "attacker").ShouldBe(3);
        Field(fields, "victim").ShouldBe(9);
    }

    [Test]
    public void Decode_PlayerTauntSoundLoopStart_IsAnEntityThenAString()
    {
        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields(
            "PlayerTauntSoundLoopStart", [.. new byte[] { 7 }, .. Bytes("taunt/conga.wav")]);

        Field(fields, "entity").ShouldBe(7);
        Field(fields, "sound").ShouldBe("taunt/conga.wav");
    }

    [Test]
    public void Decode_ABodyOfTheWrongLength_IsRefusedRatherThanGuessed()
    {
        // **Every fixed-width layout checks its own length, and that is what keeps a wrong guess
        // from becoming a plausible field list.** A Fade body one byte short would otherwise read
        // its colour from whatever followed.
        //
        // Refusal shows as the message keeping its raw body and gaining no fields, rather than as
        // an exception: this sits inside a length-prefixed message, so a body that makes no sense
        // costs one message and cannot desynchronise the packet.
        UserMessage read = Read("Fade", new byte[9]);

        read.Fields.ShouldBeNull();
    }

    /// <summary>The decoded fields of a named user message carried through a demo.</summary>
    private static IReadOnlyList<KeyValuePair<string, object?>> Fields(string name, byte[] body) =>
        Read(name, body).Fields.ShouldNotBeNull($"{name} did not decode");

    /// <summary>Puts a user message through a synthetic demo and reads it back.</summary>
    /// <remarks>
    /// Through a demo rather than straight into the decoder, because that is what says the message
    /// id resolves to this NAME at this protocol — the layout is chosen by name, and the name comes
    /// from a per-era table. A direct call would test the layout and skip the lookup.
    /// </remarks>
    private static UserMessage Read(string name, byte[] body)
    {
        int type = IdOf(name);

        return SyntheticDemo.MessagesIn(SyntheticDemo.Containing(
                new UserMessage(type, null, body.Length * 8, Body: body)))
            .OfType<UserMessage>()
            .ShouldHaveSingleItem();
    }

    /// <summary>The message id a name resolves to at the default protocol.</summary>
    /// <remarks>
    /// Searched rather than looked up, because the table only maps id to name — which is the
    /// direction a reader needs. Searching also asserts something worth asserting: that the name
    /// is reachable at this protocol at all. The era tables differ, and a message that does not
    /// exist yet would otherwise be tested against an id belonging to something else.
    /// </remarks>
    private static int IdOf(string name)
    {
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            if (string.Equals(
                UserMessageNames.Lookup(id, SyntheticDemo.DefaultProtocol),
                name,
                StringComparison.Ordinal))
            {
                return id;
            }
        }

        throw new InvalidOperationException(
            $"'{name}' has no user message id at protocol {SyntheticDemo.DefaultProtocol}.");
    }

    private static object? Field(
        IReadOnlyList<KeyValuePair<string, object?>> fields, string name) =>
        fields.First(pair => string.Equals(pair.Key, name, StringComparison.Ordinal)).Value;

    /// <summary>A leading byte followed by NUL-terminated strings.</summary>
    private static byte[] Bytes(byte first, params string[] strings)
    {
        List<byte> body = [first];
        foreach (string value in strings)
        {
            body.AddRange(Encoding.UTF8.GetBytes(value));
            body.Add(0);
        }

        return [.. body];
    }

    /// <summary>One NUL-terminated string.</summary>
    private static byte[] Bytes(string value) =>
        [.. Encoding.UTF8.GetBytes(value), 0];
}
