using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Core.Schema;

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
    public void Decode_SayText_SeparatesTheClientTheTextAndTheChatFlag()
    {
        // A byte, a NUL-terminated string, then one more byte. The trailing flag is what a reader
        // loses by treating the string as running to the end of the body — and losing it is
        // invisible, because the text still comes out right.
        byte[] body = [.. new byte[] { 6 }, .. Bytes("nice shot"), 1];

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("SayText", body);

        Field(fields, "client").ShouldBe(6);
        Field(fields, "text").ShouldBe("nice shot");
        Field(fields, "chat").ShouldBe(true);
    }

    [Test]
    public void Decode_VoiceSubtitle_IsThreeBytesInOrder()
    {
        // Three single bytes, which is three chances to transpose. Distinct values throughout.
        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("VoiceSubtitle", [4, 7, 2]);

        Field(fields, "client").ShouldBe(4);
        Field(fields, "menu").ShouldBe(7);
        Field(fields, "item").ShouldBe(2);
    }

    [Test]
    public void Decode_Rumble_IsThreeBytesAndCarriesNothingAReplayWants()
    {
        // Decoded anyway because it is cheap, and because "opaque" and "carries nothing anyone
        // wants" are different statements. This project can now say the second.
        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("Rumble", [1, 200, 3]);

        Field(fields, "waveform").ShouldBe(1);
        Field(fields, "data").ShouldBe(200);
        Field(fields, "flags").ShouldBe(3);
    }

    [Test]
    public void Decode_PlayerIgnited_NamesTheIgniterTheVictimAndTheWeapon()
    {
        // Three adjacent entity-ish bytes. Igniter and victim are the pair that matters — a swap
        // credits the burn to the wrong player and nothing fails.
        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("PlayerIgnited", [3, 9, 21]);

        Field(fields, "igniter").ShouldBe(3);
        Field(fields, "victim").ShouldBe(9);
        Field(fields, "weapon").ShouldBe(21);
    }

    [Test]
    public void Decode_AchievementEvent_ReadsBothOfItsTwoWidths()
    {
        // **Two legal lengths for one message**, which is a shape a fixed-width check rejects
        // outright. The short form is the achievement alone; the long one adds a count.
        byte[] longForm = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(longForm, 1234);
        BinaryPrimitives.WriteUInt16LittleEndian(longForm.AsSpan(2), 7);

        IReadOnlyList<KeyValuePair<string, object?>> full = Fields("AchievementEvent", longForm);
        Field(full, "achievement").ShouldBe(1234);
        Field(full, "count").ShouldBe(7);

        byte[] shortForm = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(shortForm, 4321);

        IReadOnlyList<KeyValuePair<string, object?>> brief =
            Fields("AchievementEvent", shortForm);

        Field(brief, "achievement").ShouldBe(4321);
        brief.Count.ShouldBe(1);
    }

    [Test]
    public void Decode_VoteFailed_ReadsItsTeamVoteAndReason()
    {
        // A five-byte vote header — a team byte and a four-byte vote index — then the reason.
        // The vote index is the only multi-byte field, so a reader taking the team as part of it
        // produces a plausible number rather than a failure.
        byte[] body = new byte[6];
        body[0] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), 987654);
        body[5] = 3;

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("VoteFailed", body);

        Field(fields, "team").ShouldBe(2);
        Field(fields, "vote").ShouldBe(987654);
        Field(fields, "reason").ShouldBe(3);
    }

    [Test]
    public void Decode_VotePass_ReadsItsHeaderThenTwoStrings()
    {
        // The header is fixed and the two strings are not, so the second string is the one a
        // reader loses by stopping at the first NUL.
        byte[] body =
        [
            2,
            .. BitConverter.GetBytes(4242u),
            .. Bytes("#TF_vote_passed_kick_player"),
            .. Bytes("Heavy"),
        ];

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("VotePass", body);

        Field(fields, "team").ShouldBe(2);
        Field(fields, "vote").ShouldBe(4242);
        Field(fields, "passed").ShouldBe("#TF_vote_passed_kick_player");
        Field(fields, "details").ShouldBe("Heavy");
    }

    [Test]
    public void Decode_CallVoteFailed_ReadsAReasonAndACooldown()
    {
        // A byte then a sixteen-bit count, so a reader taking three bytes as a run of bytes gets
        // two plausible small numbers instead of one correct one.
        byte[] body = new byte[3];
        body[0] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), 300);

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("CallVoteFailed", body);

        Field(fields, "reason").ShouldBe(5);
        Field(fields, "seconds").ShouldBe(300);
    }

    [Test]
    public void Decode_VoiceMask_ReadsAPairOfWordsPerBlockPlusItsTrailingByte()
    {
        // **The width is protocol-dependent**, because VOICE_MAX_PLAYERS_DW grew twice — one
        // dword at launch, two later, four now. At the modern protocol that is four blocks of two
        // words plus a trailing byte, and a reader using the launch width stops after an eighth of
        // the message.
        byte[] body = new byte[(4 * 4 * 2) + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 0x0000_00FF);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x0000_0F00);
        body[^1] = 1;

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("VoiceMask", body);

        // Numbered from zero, and the second block is asserted too — a reader that stopped after
        // the first would leave these absent rather than wrong, which an assertion on block zero
        // alone cannot see.
        Field(fields, "can_hear0").ShouldBe(0x0000_00FF);
        Field(fields, "muted0").ShouldBe(0x0000_0F00);

        Field(fields, "can_hear3").ShouldBe(0);
        Field(fields, "muted3").ShouldBe(0);
    }

    [Test]
    public void Decode_CloseCaption_ReadsATokenATenthSecondDurationAndFourFlagBits()
    {
        // **The duration is tenths of a second stored as a sixteen-bit count**, so a reader
        // treating it as seconds is out by a factor of ten and still plausible. The four flags
        // share one byte and each is a separate bit, which is four chances to read a neighbour.
        byte[] body = [.. Bytes("Announcer.AM_CapEnabledRandom"), 0, 0, 0];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(^3..), 25);
        body[^1] = 1 | 4;

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("CloseCaption", body);

        Field(fields, "token").ShouldBe("Announcer.AM_CapEnabledRandom");
        Field(fields, "seconds").ShouldBe(2.5f);
        Field(fields, "warn_if_missing").ShouldBe(true);
        Field(fields, "from_player").ShouldBe(false);
        Field(fields, "male").ShouldBe(true);
        Field(fields, "female").ShouldBe(false);
    }

    [Test]
    public void Decode_VguiMenu_ReadsItsPanelFlagAndKeyValuePairs()
    {
        // A string, a flag, a count, then that many key/value string pairs. The count is what
        // separates "read the stated number of pairs" from "read until the body runs out" — and
        // on a body that ends exactly after the pairs, both agree.
        byte[] body =
        [
            .. Bytes("class_red"),
            1,
            2,
            .. Bytes("team"),
            .. Bytes("red"),
            .. Bytes("class"),
            .. Bytes("medic"),
        ];

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("VGUIMenu", body);

        Field(fields, "panel").ShouldBe("class_red");
        Field(fields, "show").ShouldBe(true);
        Field(fields, "team").ShouldBe("red");
        Field(fields, "class").ShouldBe("medic");
    }

    [Test]
    public void Decode_PlayerStatsUpdate_NamesTheClassAndReadsOnlyTheStatsTheMaskSets()
    {
        // **A bitmask decides which stats follow**, so the body length varies with the mask and a
        // reader that assumed a fixed set would consume the wrong number of bytes. One bit set
        // means one value, and everything after it in the body belongs to nobody.
        byte[] body = new byte[2 + 4 + 4];
        body[0] = 5;
        body[1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2), 0b1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(6), 4242);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("PlayerStatsUpdate", body);

        Field(fields, "alive").ShouldBe(true);

        // Exactly three: the class, the alive flag, and the single stat the mask asked for.
        fields.Count.ShouldBe(3);
    }

    [Test]
    public void Decode_MapStatsUpdate_ReadsItsMapIndexThenItsMaskedStats()
    {
        // Same masked shape as the player stats, with a four-byte map index in front rather than
        // a class byte — so a reader using the wrong header width reads the mask from inside the
        // map index and asks for a set of stats nobody sent.
        byte[] body = new byte[4 + 4 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 77);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0b1);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 999);

        IReadOnlyList<KeyValuePair<string, object?>> fields = Fields("MapStatsUpdate", body);

        Field(fields, "map").ShouldBe(77);
        fields.Count.ShouldBe(2);
    }

    [Test]
    public void Decode_Damage_WithNoOrigin_StopsAfterItsFlagBit()
    {
        // **This is what draws a damage number in a point-of-view demo**, and it is the only place
        // the direction of incoming damage is recorded.
        //
        // The trailing flag is a genuine terminator rather than a field: a clear bit ends the
        // message, because the game returns there. A reader that always continued would consume
        // three presence bits that are not on the wire.
        BitWriter writer = new();
        writer.Write(250, 16).Write(0x0000_0040, 32).WriteBit(false);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("Damage", writer.Build(), writer.BitCount);

        Field(fields, "damage").ShouldBe(250);
        Field(fields, "bits").ShouldBe(0x0000_0040);
        fields.Count.ShouldBe(2);
    }

    [Test]
    public void Decode_Damage_WithAnOrigin_ReadsThePresenceBitsBeforeTheAxes()
    {
        // **All three presence bits first, then the values.** Reading each axis as its flag is met
        // would be correct here and wrong for an encoder, which is why the order is pinned: the
        // engine writes the flags together.
        //
        // Only two axes are sent, which is the case that separates the two orders — with all three
        // present they produce identical bits.
        BitWriter writer = new();
        writer.Write(120, 16).Write(0, 32).WriteBit(true)
            .WriteBit(true).WriteBit(false).WriteBit(true);

        SendPropEncoder.WriteCoord(writer, 512f, SendPropDecoder.CoordFlag);
        SendPropEncoder.WriteCoord(writer, -64f, SendPropDecoder.CoordFlag);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("Damage", writer.Build(), writer.BitCount);

        Field(fields, "damage").ShouldBe(120);
        ((float)Field(fields, "x")!).ShouldBe(512f, 0.05f);
        ((float)Field(fields, "z")!).ShouldBe(-64f, 0.05f);

        // y was not sent, so it must be absent rather than zero — "not stated" and "stated as
        // zero" are different facts about where the damage came from.
        fields.ShouldNotContain(pair => pair.Key == "y");
    }

    [Test]
    public void Decode_CheapBreakModel_ReadsAModelIndexThenAVector()
    {
        // Sixteen bits of model index then a coordinate triple, and the whole body must be
        // consumed exactly — the decoder checks its own bit count, so a vector read at the wrong
        // width fails rather than returning plausible coordinates.
        BitWriter writer = new();
        writer.Write(4321, 16).WriteBit(true).WriteBit(true).WriteBit(true);

        SendPropEncoder.WriteCoord(writer, 128f, SendPropDecoder.CoordFlag);
        SendPropEncoder.WriteCoord(writer, 256f, SendPropDecoder.CoordFlag);
        SendPropEncoder.WriteCoord(writer, -32f, SendPropDecoder.CoordFlag);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("CheapBreakModel", writer.Build(), writer.BitCount);

        Field(fields, "model").ShouldBe(4321);
        ((float)Field(fields, "x")!).ShouldBe(128f, 0.05f);
        ((float)Field(fields, "y")!).ShouldBe(256f, 0.05f);
        ((float)Field(fields, "z")!).ShouldBe(-32f, 0.05f);
    }

    [Test]
    public void Decode_BreakModel_ReadsAPositionAnAngleAndASkin()
    {
        // Two vectors then a skin, and the second vector's fields are prefixed — so a reader that
        // shared one naming would overwrite the position with the angle and report three fields
        // where six were sent.
        BitWriter writer = new();
        writer.Write(77, 16);

        WriteVector(writer, 64f, 0f, 0f);
        WriteVector(writer, 0f, 90f, 0f);

        writer.Write(3, 16);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("BreakModel", writer.Build(), writer.BitCount);

        Field(fields, "model").ShouldBe(77);
        Field(fields, "skin").ShouldBe(3);
        ((float)Field(fields, "x")!).ShouldBe(64f, 0.05f);
        ((float)Field(fields, "ang_y")!).ShouldBe(90f, 0.05f);
    }

    [Test]
    public void Decode_BreakModelRocketDud_IsTheSameLayoutWithoutASkin()
    {
        // The same decoder with one flag flipped, which is the case a shared implementation gets
        // wrong in exactly one direction: reading a skin that was never sent consumes sixteen bits
        // past the end and fails the body-length check rather than returning a wrong number.
        BitWriter writer = new();
        writer.Write(88, 16);

        WriteVector(writer, 32f, 0f, 0f);
        WriteVector(writer, 0f, 0f, 0f);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("BreakModelRocketDud", writer.Build(), writer.BitCount);

        Field(fields, "model").ShouldBe(88);
        fields.ShouldNotContain(pair => pair.Key == "skin");
    }

    /// <summary>Writes a coordinate triple: three presence bits, then the axes.</summary>
    private static void WriteVector(BitWriter writer, float x, float y, float z)
    {
        writer.WriteBit(true).WriteBit(true).WriteBit(true);

        SendPropEncoder.WriteCoord(writer, x, SendPropDecoder.CoordFlag);
        SendPropEncoder.WriteCoord(writer, y, SendPropDecoder.CoordFlag);
        SendPropEncoder.WriteCoord(writer, z, SendPropDecoder.CoordFlag);
    }

    [Test]
    public void Decode_VoteStart_ReadsItsHeaderTwoStringsAndATrailingBitAndByte()
    {
        // **The only layout here that ends on a bit boundary rather than a byte one.** Six header
        // bytes, two strings, then a flag bit and a byte — nine bits that start on a byte boundary
        // because the strings ended there. A reader that rounded the body up to whole bytes would
        // accept a message one bit short.
        byte[] head = [2, .. BitConverter.GetBytes(4242u), 9];
        byte[] strings = [.. Bytes("#TF_vote_kick_player"), .. Bytes("Heavy")];

        BitWriter writer = new();
        writer.WriteBytes(head).WriteBytes(strings).WriteBit(true).Write(7, 8);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("VoteStart", writer.Build(), writer.BitCount);

        Field(fields, "team").ShouldBe(2);
        Field(fields, "vote").ShouldBe(4242);
        Field(fields, "caller").ShouldBe(9);
        Field(fields, "issue").ShouldBe("#TF_vote_kick_player");
        Field(fields, "details").ShouldBe("Heavy");
        Field(fields, "yes_no").ShouldBe(true);
        Field(fields, "target").ShouldBe(7);
    }

    [Test]
    public void Decode_SpawnFlyingBird_ReadsAVectorThenItsFloats()
    {
        // A coordinate triple then a run of raw 32-bit floats, which is a different float encoding
        // from the coordinates before it — so a reader using one width throughout consumes the
        // wrong number of bits and fails the exact-length check rather than returning wrong values.
        BitWriter writer = new();
        WriteVector(writer, 100f, 200f, 300f);

        // Five of them — fly_angle, fly_angle_rate, accel_z, speed, glide_time — and the count is
        // part of the layout rather than a detail: the decoder checks it consumed the whole body,
        // so a fixture sending four fails rather than reporting four fields.
        foreach (float value in new[] { 1.5f, -2.5f, 3.5f, 4.5f, 5.5f })
        {
            writer.Write((uint)BitConverter.SingleToInt32Bits(value), 32);
        }

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("SpawnFlyingBird", writer.Build(), writer.BitCount);

        ((float)Field(fields, "x")!).ShouldBe(100f, 0.05f);
        ((float)Field(fields, "z")!).ShouldBe(300f, 0.05f);
        ((float)Field(fields, "glide_time")!).ShouldBe(5.5f);

        fields.Count.ShouldBe(8);
    }

    [Test]
    public void Decode_TheHapticsMessages_AreRecognisedByWidthAndCarryNoFields()
    {
        // **A message with no fields is still a decoded message**, and the distinction matters:
        // "recognised, carries nothing" and "not recognised" look identical in a field list and
        // are different facts about how much of the format is understood.
        //
        // HapMeleeContact is zero bits, which is the narrowest legal body there is and the one a
        // length check written as "greater than zero" rejects.
        Read("SPHapWeapEvent", new byte[4]).Fields.ShouldNotBeNull().ShouldBeEmpty();
        Read("HapMeleeContact", [], 0).Fields.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Test]
    public void Decode_DamageAtAnOldProtocol_UsesTheByteFormRatherThanTheShort()
    {
        // **The damage field was a BYTE through protocol 14 and a short after**, so the same
        // message has two layouts and the protocol is the only thing that says which. Reading the
        // modern form on an old demo takes the damage and the first byte of the flags as one
        // number — plausible, and wrong by a factor of the flags.
        //
        // The corpus has demos at 11, 14, 15 and 24, so it can reach this boundary; what it cannot
        // reach is 12, 13 and 17 through 23, and a written message can be sent at any of them.
        // **The old form sends no flags field at all**, which the first draft of this fixture
        // included — eleven bits, not forty-three. A damage value and the vector's three presence
        // bits, and nothing else.
        BitWriter writer = new();
        writer.Write(60, 8).WriteBit(false).WriteBit(false).WriteBit(false);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("Damage", writer.Build(), writer.BitCount, protocol: 14);

        Field(fields, "damage").ShouldBe(60);

        // No "bits" field, deliberately: reporting a zero would say the damage carried no type
        // flags rather than that the era never stated any.
        fields.ShouldNotContain(pair => pair.Key == "bits");
    }

    [Test]
    public void Decode_VoiceMaskAtLaunch_CarriesOneBlockRatherThanFour()
    {
        // VOICE_MAX_PLAYERS_DW grew twice — one dword at launch, two later, four now — so the
        // body length is an era question and a reader using the modern width rejects every old
        // demo outright.
        byte[] body = new byte[(1 * 4 * 2) + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 0x0000_00AB);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0x0000_00CD);

        IReadOnlyList<KeyValuePair<string, object?>> fields =
            Fields("VoiceMask", body, body.Length * 8, protocol: 11);

        Field(fields, "can_hear0").ShouldBe(0x0000_00AB);
        Field(fields, "muted0").ShouldBe(0x0000_00CD);

        // One block only, so the modern block names must be ABSENT rather than zero — asserted by
        // name rather than by a count, because the message also carries a trailing byte of its own
        // and a count would be measuring that too.
        fields.ShouldNotContain(pair => pair.Key == "can_hear1");
        fields.ShouldNotContain(pair => pair.Key == "can_hear3");
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
        Fields(name, body, body.Length * 8);

    /// <summary>The decoded fields of a message whose body is not a whole number of bytes.</summary>
    private static IReadOnlyList<KeyValuePair<string, object?>> Fields(
        string name, byte[] body, int bodyBits) =>
        Fields(name, body, bodyBits, SyntheticDemo.DefaultProtocol);

    /// <summary>The decoded fields of a message carried at a chosen protocol.</summary>
    private static IReadOnlyList<KeyValuePair<string, object?>> Fields(
        string name, byte[] body, int bodyBits, ushort protocol) =>
        Read(name, body, bodyBits, protocol).Fields.ShouldNotBeNull($"{name} did not decode");

    /// <summary>Puts a user message through a synthetic demo and reads it back.</summary>
    /// <remarks>
    /// Through a demo rather than straight into the decoder, because that is what says the message
    /// id resolves to this NAME at this protocol — the layout is chosen by name, and the name comes
    /// from a per-era table. A direct call would test the layout and skip the lookup.
    /// </remarks>
    private static UserMessage Read(string name, byte[] body) =>
        Read(name, body, body.Length * 8);

    private static UserMessage Read(string name, byte[] body, int bodyBits) =>
        Read(name, body, bodyBits, SyntheticDemo.DefaultProtocol);

    private static UserMessage Read(
        string name, byte[] body, int bodyBits, ushort protocol)
    {
        int type = IdOf(name, protocol);

        return SyntheticDemo.MessagesIn(SyntheticDemo.Containing(
                protocol,
                new UserMessage(type, null, bodyBits, Body: body)))
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
    private static int IdOf(string name, ushort protocol)
    {
        for (int id = 0; id <= byte.MaxValue; id++)
        {
            if (string.Equals(
                UserMessageNames.Lookup(id, protocol),
                name,
                StringComparison.Ordinal))
            {
                return id;
            }
        }

        throw new InvalidOperationException(
            $"'{name}' has no user message id at protocol {protocol}.");
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
