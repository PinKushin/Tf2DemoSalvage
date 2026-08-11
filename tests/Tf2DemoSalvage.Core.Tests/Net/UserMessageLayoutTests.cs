using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for the user message layouts transcribed from Valve's client readers.
/// </summary>
/// <remarks>
/// **Each of these predicted a body width before any body was read, and the corpus matched.**
/// Fade is three shorts and four bytes, so 80 bits, and all 20 Fades in the corpus are 80 bits.
/// Shake is a byte and three floats, so 104, and all 6 are 104. PlayerStatsUpdate is 48 bits plus
/// 32 per stat, and the six widths present — 112, 144, 176, 208, 240, 272 — are exactly that.
///
/// That is why these tests are worth writing even though the corpus already exercises them: the
/// corpus can only confirm the widths it happens to contain. A layout is a claim about widths that
/// do not appear too, and a hand-built body is the only way to state one.
/// </remarks>
public sealed class UserMessageLayoutTests
{
    private const int Protocol = 24;

    private static UserMessage Decode(string name, byte[] body) =>
        UserMessageBody.Decode(0, name, body, body.Length * 8, Protocol);

    private static object? Value(UserMessage message, string field) =>
        message.Fields!.First(pair => pair.Key == field).Value;

    [Fact]
    public void Fade_ReadsThreeShortsAndAColour()
    {
        byte[] body = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(body, 1500);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), 400);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 0x0002);
        body[6] = 255;
        body[7] = 128;
        body[8] = 64;
        body[9] = 200;

        UserMessage message = Decode("Fade", body);

        message.Fields.ShouldNotBeNull();
        Value(message, "duration").ShouldBe(1500);
        Value(message, "holdtime").ShouldBe(400);
        Value(message, "flags").ShouldBe(2);
        Value(message, "r").ShouldBe(255);
        Value(message, "g").ShouldBe(128);
        Value(message, "b").ShouldBe(64);
        Value(message, "a").ShouldBe(200);
    }

    [Fact]
    public void Fade_OfTheWrongWidth_IsRefused()
    {
        // 80 bits is the whole layout, so anything else is a different message being read as this
        // one. The corpus cannot make this case - every Fade in it is 80 bits - which is exactly
        // why it is asserted here.
        Decode("Fade", new byte[9]).Fields.ShouldBeNull();
        Decode("Fade", new byte[11]).Fields.ShouldBeNull();
    }

    [Fact]
    public void Shake_ReadsACommandByteAndThreeFloats()
    {
        byte[] body = new byte[13];
        body[0] = 1;
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(1), 16.5f);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(5), 40f);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(9), 0.75f);

        UserMessage message = Decode("Shake", body);

        message.Fields.ShouldNotBeNull();
        Value(message, "command").ShouldBe(1);
        Value(message, "amplitude").ShouldBe(16.5f);
        Value(message, "frequency").ShouldBe(40f);
        Value(message, "duration").ShouldBe(0.75f);
    }

    [Fact]
    public void Rumble_ReadsThreeBytes()
    {
        UserMessage message = Decode("Rumble", [7, 200, 3]);

        message.Fields.ShouldNotBeNull();
        Value(message, "waveform").ShouldBe(7);
        Value(message, "data").ShouldBe(200);
        Value(message, "flags").ShouldBe(3);
    }

    [Fact]
    public void ResetHUD_ReadsThePlaceholderByte()
    {
        // The client's reader takes nothing at all, but the server writes WRITE_BYTE(0), so the
        // byte is on the wire and has to be accounted for. Reporting it is how the trace stays
        // able to state that the whole body was consumed.
        UserMessage message = Decode("ResetHUD", [0]);

        message.Fields.ShouldNotBeNull();
        Value(message, "unused").ShouldBe(0);
    }

    [Fact]
    public void VguiMenu_ReadsThePanelAndItsKeyValues()
    {
        List<byte> body = [.. Encoding.UTF8.GetBytes("MOTD"), 0, 1, 2];
        body.AddRange(Encoding.UTF8.GetBytes("title"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("Welcome"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("msg"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("gg"));
        body.Add(0);

        UserMessage message = Decode("VGUIMenu", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "panel").ShouldBe("MOTD");
        Value(message, "show").ShouldBe(true);
        Value(message, "title").ShouldBe("Welcome");
        Value(message, "msg").ShouldBe("gg");
    }

    [Fact]
    public void VguiMenu_WithFewerPairsThanItClaims_IsRefused()
    {
        // The count drives the read, so a count larger than the pairs present runs off the end.
        // Reporting the pairs it did find would present a truncated body as a complete one.
        List<byte> body = [.. Encoding.UTF8.GetBytes("info"), 0, 0, 3];
        body.AddRange(Encoding.UTF8.GetBytes("a"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("b"));
        body.Add(0);

        Decode("VGUIMenu", [.. body]).Fields.ShouldBeNull();
    }

    [Fact]
    public void VguiMenu_WithTrailingBytes_IsRefused()
    {
        List<byte> body = [.. Encoding.UTF8.GetBytes("info"), 0, 0, 0, 0xFF];

        Decode("VGUIMenu", [.. body]).Fields.ShouldBeNull();
    }

    /// <summary>Builds a stats body: a header, a set-bit field, then one value per set bit.</summary>
    private static byte[] StatsBody(byte[] header, uint sent, params int[] values)
    {
        byte[] body = new byte[header.Length + 4 + (values.Length * 4)];
        header.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(header.Length), sent);

        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                body.AsSpan(header.Length + 4 + (index * 4)), values[index]);
        }

        return body;
    }

    [Fact]
    public void PlayerStatsUpdate_NamesTheStatsItsBitFieldSelects()
    {
        // Bits 0 and 2 of the field, counting from stat 1: shots_hit and kills.
        UserMessage message = Decode(
            "PlayerStatsUpdate", StatsBody([3, 1], 0b101u, 42, 7));

        message.Fields.ShouldNotBeNull();
        Value(message, "class").ShouldBe("soldier");
        Value(message, "alive").ShouldBe(true);
        Value(message, "shots_hit").ShouldBe(42);
        Value(message, "kills").ShouldBe(7);
        message.Fields!.Any(field => field.Key == "shots_fired").ShouldBeFalse();
    }

    [Fact]
    public void PlayerStatsUpdate_WidthIsTheHeaderPlusOneValuePerSetBit()
    {
        // The claim the corpus widths confirmed - 48 bits plus 32 each - stated directly. A body
        // carrying three values for a field naming two has a value nothing will read.
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1, 2)).Fields.ShouldNotBeNull();
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1, 2, 3)).Fields.ShouldBeNull();
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1)).Fields.ShouldBeNull();
    }

    [Fact]
    public void OnlyTheFirstThirtyTwoStats_CanEverBeSent()
    {
        // Found by writing the opposite test and having it fail. The intent was to check that a
        // bit past the end of the stat table is refused, and no such bit can be set: the field is
        // 32 bits wide while TFStatType_t runs to 44, so bit 31 selects stat 32 and stats 33
        // through 44 are unreachable through this message. Valve's own guard,
        // `iStat <= TFSTAT_LAST`, is dead code in this build for the same reason.
        //
        // It is not dead in every build. The guard bites when the table is SHORTER than 32, which
        // is what an older era looks like - and the reason this table's era caveat is about
        // labels rather than about widths.
        UserMessage message = Decode("PlayerStatsUpdate", StatsBody([3, 1], 1u << 31, 4242));

        message.Fields.ShouldNotBeNull();
        Value(message, "damage_assist").ShouldBe(4242);
    }

    [Fact]
    public void MapStatsUpdate_ReadsTheMapIdAndItsOneStat()
    {
        byte[] identifier = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(identifier, 12345);

        UserMessage message = Decode("MapStatsUpdate", StatsBody(identifier, 1u, 900));

        message.Fields.ShouldNotBeNull();
        Value(message, "map").ShouldBe(12345);
        Value(message, "playtime").ShouldBe(900);
    }

    /// <summary>SPROP_COORD, which is what every position and angle in these messages is.</summary>
    private const int CoordFlag = 1 << 1;

    /// <summary>Builds a BitVec3Coord: three presence bits, then the axes present.</summary>
    private static Tf2DemoSalvage.Core.Primitives.BitWriter Vector(float x, float y, float z)
    {
        Tf2DemoSalvage.Core.Primitives.BitWriter axes = new();
        SendPropEncoder.WriteCoord(axes, x, CoordFlag);
        SendPropEncoder.WriteCoord(axes, y, CoordFlag);
        SendPropEncoder.WriteCoord(axes, z, CoordFlag);

        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.WriteBit(true).WriteBit(true).WriteBit(true)
            .AppendBits(axes.Build(), axes.BitCount);
        return whole;
    }

    [Fact]
    public void CheapBreakModel_IsAModelAndAPositionInEightyFiveBits()
    {
        // The width that exposed the era shift in the id table. A short and a full coordinate
        // vector is 16 + 3 + 66, and 85 bits is distinctive enough to identify the message
        // wherever it appears regardless of which id carries it.
        Tf2DemoSalvage.Core.Primitives.BitWriter position = Vector(1113.25f, 3686.875f, -455.875f);
        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.Write(1234, 16).AppendBits(position.Build(), position.BitCount);

        whole.BitCount.ShouldBe(85);

        UserMessage message = UserMessageBody.Decode(
            42, "CheapBreakModel", whole.Build(), whole.BitCount, Protocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "model").ShouldBe(1234);
        Value(message, "x").ShouldBe(1113.25f);
        Value(message, "z").ShouldBe(-455.875f);
    }

    [Fact]
    public void BreakModel_CarriesAnOrientationEncodedAsAPosition()
    {
        // WRITE_ANGLES is WRITE_VEC3COORD - bf_write::WriteBitAngles copies the angle triple into
        // a Vector and calls WriteBitVec3Coord. So the angles cost exactly what a position costs,
        // and this test would fail against any separate angle encoding.
        Tf2DemoSalvage.Core.Primitives.BitWriter origin = Vector(64f, -128.5f, 32.25f);
        Tf2DemoSalvage.Core.Primitives.BitWriter angles = Vector(0f, 90f, 0f);
        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.Write(77, 16)
            .AppendBits(origin.Build(), origin.BitCount)
            .AppendBits(angles.Build(), angles.BitCount)
            .Write(3, 16);

        UserMessage message = UserMessageBody.Decode(
            41, "BreakModel", whole.Build(), whole.BitCount, Protocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "model").ShouldBe(77);
        Value(message, "y").ShouldBe(-128.5f);
        Value(message, "ang_y").ShouldBe(90f);
        Value(message, "skin").ShouldBe(3);
    }

    [Fact]
    public void SpawnFlyingBird_IsAPositionAndFiveFloats()
    {
        // Fractional coordinates deliberately. An axis is 22 bits with a fraction and 17 without,
        // so a whole-numbered position encodes to 54 bits rather than 69 and the body comes to 214
        // instead of 229. The corpus's birds are all 229, so the fixture has to carry fractions to
        // be stating the same claim the corpus does.
        Tf2DemoSalvage.Core.Primitives.BitWriter position = Vector(10.5f, 20.25f, 30.75f);
        Tf2DemoSalvage.Core.Primitives.BitWriter whole = new();
        whole.AppendBits(position.Build(), position.BitCount);

        foreach (float value in new[] { 1.5f, 2.5f, -3.5f, 400f, 6.25f })
        {
            whole.Write((uint)BitConverter.SingleToInt32Bits(value), 32);
        }

        whole.BitCount.ShouldBe(229);

        UserMessage message = UserMessageBody.Decode(
            52, "SpawnFlyingBird", whole.Build(), whole.BitCount, Protocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "fly_angle").ShouldBe(1.5f);
        Value(message, "speed").ShouldBe(400f);
        Value(message, "glide_time").ShouldBe(6.25f);
    }

    [Fact]
    public void AchievementEvent_AcceptsBothTheOldAndTheNewLength()
    {
        // The message grew at a fixed id: the modern writer sends two shorts, the 2009 demo's is
        // one. The achievement occupies the same leading short in both, so both are readable - and
        // any other length is refused rather than guessed at.
        UserMessage modern = Decode("AchievementEvent", [0x39, 0x05, 0x02, 0x00]);
        modern.Fields.ShouldNotBeNull();
        Value(modern, "achievement").ShouldBe(1337);
        Value(modern, "count").ShouldBe(2);

        UserMessage old = Decode("AchievementEvent", [0x39, 0x05]);
        old.Fields.ShouldNotBeNull();
        Value(old, "achievement").ShouldBe(1337);
        old.Fields!.Any(field => field.Key == "count").ShouldBeFalse();

        Decode("AchievementEvent", [1, 2, 3]).Fields.ShouldBeNull();
    }

    [Fact]
    public void PlayerTauntSoundLoopStart_IsAnEntityAndASoundName()
    {
        List<byte> body = [12, .. Encoding.UTF8.GetBytes("Taunt.MedicHeroic"), 0];

        UserMessage message = Decode("PlayerTauntSoundLoopStart", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "entity").ShouldBe(12);
        Value(message, "sound").ShouldBe("Taunt.MedicHeroic");
    }

    [Fact]
    public void PlayerShieldBlocked_IsTwoEntityIndices()
    {
        UserMessage message = Decode("PlayerShieldBlocked", [5, 9]);

        message.Fields.ShouldNotBeNull();
        Value(message, "attacker").ShouldBe(5);
        Value(message, "victim").ShouldBe(9);
        Decode("PlayerShieldBlocked", [5, 9, 1]).Fields.ShouldBeNull();
    }

    [Fact]
    public void AnUnknownClassNumber_IsReportedAsItsNumber()
    {
        // Nine classes have existed since 2007 and no tenth is expected, but a number outside the
        // table must not silently become a name. Reporting the digit says "this build sent
        // something this table does not cover", which is the honest statement.
        UserMessage message = Decode("PlayerStatsUpdate", StatsBody([99, 0], 1u, 5));

        Value(message, "class").ShouldBe("99");
    }
}
