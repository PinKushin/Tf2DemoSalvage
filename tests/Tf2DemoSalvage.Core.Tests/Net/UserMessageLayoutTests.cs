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

    private static UserMessage Decode(string name, byte[] body, int networkProtocol) =>
        UserMessageBody.Decode(0, name, body, body.Length * 8, networkProtocol);

    private static object? Value(UserMessage message, string field) =>
        message.Fields!.First(pair => pair.Key == field).Value;

    [Test]
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

    [Test]
    public void Fade_OfTheWrongWidth_IsRefused()
    {
        // 80 bits is the whole layout, so anything else is a different message being read as this
        // one. The corpus cannot make this case - every Fade in it is 80 bits - which is exactly
        // why it is asserted here.
        Decode("Fade", new byte[9]).Fields.ShouldBeNull();
        Decode("Fade", new byte[11]).Fields.ShouldBeNull();
    }

    [Test]
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

    [Test]
    public void Rumble_ReadsThreeBytes()
    {
        UserMessage message = Decode("Rumble", [7, 200, 3]);

        message.Fields.ShouldNotBeNull();
        Value(message, "waveform").ShouldBe(7);
        Value(message, "data").ShouldBe(200);
        Value(message, "flags").ShouldBe(3);
    }

    [Test]
    public void ResetHUD_ReadsThePlaceholderByte()
    {
        // The client's reader takes nothing at all, but the server writes WRITE_BYTE(0), so the
        // byte is on the wire and has to be accounted for. Reporting it is how the trace stays
        // able to state that the whole body was consumed.
        UserMessage message = Decode("ResetHUD", [0]);

        message.Fields.ShouldNotBeNull();
        Value(message, "unused").ShouldBe(0);
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public void PlayerStatsUpdate_WidthIsTheHeaderPlusOneValuePerSetBit()
    {
        // The claim the corpus widths confirmed - 48 bits plus 32 each - stated directly. A body
        // carrying three values for a field naming two has a value nothing will read.
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1, 2)).Fields.ShouldNotBeNull();
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1, 2, 3)).Fields.ShouldBeNull();
        Decode("PlayerStatsUpdate", StatsBody([3, 1], 0b11u, 1)).Fields.ShouldBeNull();
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public void PlayerTauntSoundLoopStart_IsAnEntityAndASoundName()
    {
        List<byte> body = [12, .. Encoding.UTF8.GetBytes("Taunt.MedicHeroic"), 0];

        UserMessage message = Decode("PlayerTauntSoundLoopStart", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "entity").ShouldBe(12);
        Value(message, "sound").ShouldBe("Taunt.MedicHeroic");
    }

    [Test]
    public void PlayerShieldBlocked_IsTwoEntityIndices()
    {
        UserMessage message = Decode("PlayerShieldBlocked", [5, 9]);

        message.Fields.ShouldNotBeNull();
        Value(message, "attacker").ShouldBe(5);
        Value(message, "victim").ShouldBe(9);
        Decode("PlayerShieldBlocked", [5, 9, 1]).Fields.ShouldBeNull();
    }

    [Test]
    public void CloseCaption_ReadsATokenADurationAndItsFlags()
    {
        // The most numerous user message in the game by a wide margin - 616 in one seven-minute
        // pub round, more than every other user message in that demo combined. It was invisible
        // until a real multiplayer demo arrived, because listen-server recordings with one or two
        // players barely produce any.
        List<byte> body = [.. Encoding.UTF8.GetBytes("spy.taunts02"), 0, 31, 0, 0b0101];

        UserMessage message = Decode("CloseCaption", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "token").ShouldBe("spy.taunts02");

        // Tenths on the wire. Reporting the raw 31 would invite reading it as ticks.
        Value(message, "seconds").ShouldBe(3.1f);
        Value(message, "warn_if_missing").ShouldBe(true);
        Value(message, "from_player").ShouldBe(false);
        Value(message, "male").ShouldBe(true);
        Value(message, "female").ShouldBe(false);
    }

    [Test]
    public void CloseCaption_WithTrailingBytes_IsRefused()
    {
        List<byte> body = [.. Encoding.UTF8.GetBytes("x"), 0, 10, 0, 1, 0xFF];

        Decode("CloseCaption", [.. body]).Fields.ShouldBeNull();
    }

    [Test]
    public void VoiceMask_ReadsTwoInterleavedDwordArrays()
    {
        // The interleaving is the part worth asserting. voice_gamemgr.cpp writes a dword of the
        // can-hear mask and a dword of the mute mask alternately, and two contiguous arrays would
        // consume exactly the same 264 bits while producing a completely different answer - so
        // exact consumption cannot catch that mistake and only this test can.
        byte[] body = new byte[33];
        for (int word = 0; word < 4; word++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(word * 8), (uint)(0x1000 + word));
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan((word * 8) + 4), (uint)(0x2000 + word));
        }

        body[32] = 1;

        UserMessage message = Decode("VoiceMask", body);

        message.Fields.ShouldNotBeNull();
        Value(message, "can_hear0").ShouldBe(0x1000);
        Value(message, "muted0").ShouldBe(0x2000);
        Value(message, "can_hear3").ShouldBe(0x1003);
        Value(message, "muted3").ShouldBe(0x2003);
        Value(message, "mod_enable").ShouldBe(true);
    }

    [Test]
    public void VoiceMask_WidthComesFromMaxPlayers()
    {
        // VOICE_MAX_PLAYERS_DW*4*2 + 1 where VOICE_MAX_PLAYERS is MAX_PLAYERS = 101, so four
        // dwords and 33 bytes. Predicted from two levels of macro before a body was read, and
        // every VoiceMask in the corpus is 264 bits.
        Decode("VoiceMask", new byte[33]).Fields.ShouldNotBeNull();
        Decode("VoiceMask", new byte[32]).Fields.ShouldBeNull();
        Decode("VoiceMask", new byte[34]).Fields.ShouldBeNull();
    }
    [TestCase(11, 9)]
    [TestCase(14, 9)]
    [TestCase(15, 17)]
    [TestCase(16, 33)]
    [TestCase(24, 33)]
    public void VoiceMask_WidthFollowsTheErasMaxPlayers(int networkProtocol, int bytes)
    {
        // Read from the registered sizes in the shipped clients, not inferred: VoiceMask is
        // registered at 9 bytes in the 2007 and 2008 builds, 17 in 2009, and 33 from 2011 on.
        // The size inverts through VOICE_MAX_PLAYERS_DW*4*2 + 1 to a player ceiling of 32, 64
        // and 128 - a Valve internal constant dated by measurement, which no changelog records.
        Decode("VoiceMask", new byte[bytes], networkProtocol).Fields.ShouldNotBeNull();

        // The control, and the reason this is a Theory rather than three asserts: every era must
        // REFUSE its neighbours' widths. A decoder that accepted any of 9, 17 or 33 everywhere
        // would satisfy the line above at every row while being exactly the bug this fixes.
        Decode("VoiceMask", new byte[bytes + 8], networkProtocol).Fields.ShouldBeNull();
        if (bytes > 9)
        {
            Decode("VoiceMask", new byte[bytes - 8], networkProtocol).Fields.ShouldBeNull();
        }
    }

    [Test]
    public void VoiceMask_AtLaunchCarriesOneDwordPair()
    {
        // The width is only half of it - the field COUNT has to follow too, or a 9-byte body
        // would be read as four pairs off the end of the span. One pair, then the flag byte.
        byte[] body = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 0xAAAA);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), 0xBBBB);
        body[8] = 1;

        UserMessage message = Decode("VoiceMask", body, 11);

        message.Fields.ShouldNotBeNull();
        Value(message, "can_hear0").ShouldBe(0xAAAA);
        Value(message, "muted0").ShouldBe(0xBBBB);
        Value(message, "mod_enable").ShouldBe(true);
        message.Fields!.ShouldNotContain(pair => pair.Key == "can_hear1");
    }

    [Test]
    public void WhenTheModernNamesLayoutRefuses_TheMarch2013NameIsTried()
    {
        // The B29 case, measured on the corpus: the March 2013 demo carries three messages at id
        // 69 with 32-bit bodies. The modern table calls that PlayerLoadoutUpdated - a single
        // WRITE_BYTE - so the layout refuses, and until now the name was withheld and the id
        // reported bare. The March 2013 client registers HapSetDrag there instead, one float of
        // haptic drag, which fits.
        UserMessage message = UserMessageBody.Decode(
            69, "PlayerLoadoutUpdated", new byte[4], 32, Protocol, "HapSetDrag");

        message.Name.ShouldBe("HapSetDrag");
    }

    [Test]
    public void UserMessageLayout_TheAlternate_IsReachedOnlyWhenThePrimaryRefuses()
    {
        // The control, and the reason the fallback is safe. A one-byte body IS a valid
        // PlayerLoadoutUpdated, so the primary stands and the alternate is never consulted -
        // otherwise every modern demo's id 69 would be renamed to a haptics message.
        UserMessage message = UserMessageBody.Decode(
            69, "PlayerLoadoutUpdated", new byte[1], 8, Protocol, "HapSetDrag");

        message.Name.ShouldBe("PlayerLoadoutUpdated");
        message.Fields.ShouldNotBeNull();
    }

    [Test]
    public void WhenBothCandidatesRefuse_NeitherNameIsClaimed()
    {
        // Two wrong answers do not make a right one. PlayerTauntSoundLoopEnd is one byte and
        // HapMeleeContact is registered at zero, so a 32-bit body is neither, and the honest
        // report is the number alone.
        UserMessage message = UserMessageBody.Decode(
            71, "PlayerTauntSoundLoopEnd", new byte[4], 32, Protocol, "HapMeleeContact");

        message.Name.ShouldBeNull();
    }

    [Test]
    public void PlayerIgnited_NamesTheIgniterTheVictimAndTheWeapon()
    {
        UserMessage message = Decode("PlayerIgnited", [3, 9, 21]);

        message.Fields.ShouldNotBeNull();
        Value(message, "igniter").ShouldBe(3);
        Value(message, "victim").ShouldBe(9);
        Value(message, "weapon").ShouldBe(21);
    }

    [Test]
    public void TheTwoEntityMessages_NameTheirOwnRoles()
    {
        // Same two bytes, different meanings, and the names are the whole value of decoding them.
        // A shared "entity0/entity1" would consume the body just as exactly and say nothing.
        Value(Decode("PlayerExtinguished", [4, 7]), "healer").ShouldBe(4);
        Value(Decode("PlayerJarated", [4, 7]), "thrower").ShouldBe(4);
        Value(Decode("PlayerShieldBlocked", [4, 7]), "attacker").ShouldBe(4);

        Decode("PlayerExtinguished", [4]).Fields.ShouldBeNull();
    }

    /// <summary>Team byte, vote index long, then whatever the message adds.</summary>
    private static List<byte> VoteHeader(byte team, uint index)
    {
        byte[] head = new byte[5];
        head[0] = team;
        BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(1), index);
        return [.. head];
    }

    [Test]
    public void VoteStart_HasAOneBitFlagBetweenTwoByteFields()
    {
        // The reason this layout is worth a test of its own: WRITE_BOOL is a single bit, and it
        // sits between the strings and the target entity. So the body is byte-aligned up to the
        // flag and bit-aligned after it, and its length is not a multiple of eight - the corpus
        // holds 329-, 369- and 481-bit instances.
        //
        // A decoder reading the flag as a byte would be seven bits out for the target and would
        // report a plausible player index rather than failing, which is the failure mode this
        // whole file exists to avoid.
        List<byte> body = VoteHeader(2, 17);
        body.Add(23);
        body.AddRange(Encoding.UTF8.GetBytes("#TF_vote_kick_player_cheating"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("someone"));
        body.Add(0);

        // The flag and the target share a byte: flag set, then 12 shifted up one bit.
        Tf2DemoSalvage.Core.Primitives.BitWriter tail = new();
        tail.WriteBit(true).Write(12, 8);
        body.AddRange(tail.Build());

        UserMessage message = UserMessageBody.Decode(
            46, "VoteStart", [.. body], ((body.Count - 2) * 8) + 9, Protocol);

        message.Fields.ShouldNotBeNull();
        Value(message, "team").ShouldBe(2);
        Value(message, "vote").ShouldBe(17);
        Value(message, "caller").ShouldBe(23);
        Value(message, "issue").ShouldBe("#TF_vote_kick_player_cheating");
        Value(message, "details").ShouldBe("someone");
        Value(message, "yes_no").ShouldBe(true);
        Value(message, "target").ShouldBe(12);
    }

    [Test]
    public void VotePass_IsTheHeaderAndTwoStrings()
    {
        List<byte> body = VoteHeader(2, 17);
        body.AddRange(Encoding.UTF8.GetBytes("#TF_vote_passed_ban_player"));
        body.Add(0);
        body.AddRange(Encoding.UTF8.GetBytes("cwed2k5"));
        body.Add(0);

        UserMessage message = Decode("VotePass", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "vote").ShouldBe(17);
        Value(message, "passed").ShouldBe("#TF_vote_passed_ban_player");
        Value(message, "details").ShouldBe("cwed2k5");
    }

    [Test]
    public void VoteFailed_IsExactlyFortyEightBits()
    {
        // Byte, long, byte - and tf_usermessages.cpp registers the message at 6 bytes, so the
        // width was predicted from the registration table and the writer independently.
        List<byte> body = VoteHeader(2, 6);
        body.Add(3);

        UserMessage message = Decode("VoteFailed", [.. body]);

        message.Fields.ShouldNotBeNull();
        Value(message, "team").ShouldBe(2);
        Value(message, "vote").ShouldBe(6);
        Value(message, "reason").ShouldBe(3);

        Decode("VoteFailed", [.. VoteHeader(2, 6)]).Fields.ShouldBeNull();
    }

    [Test]
    public void CallVoteFailed_IsAReasonAndACooldown()
    {
        UserMessage message = Decode("CallVoteFailed", [4, 0x1E, 0x00]);

        message.Fields.ShouldNotBeNull();
        Value(message, "reason").ShouldBe(4);
        Value(message, "seconds").ShouldBe(30);
    }

    [Test]
    public void AKnownLayoutThatRefuses_WithholdsTheNameToo()
    {
        // A name is a claim, and a layout that refuses is evidence against it. Reporting
        // "PlayerLoadoutUpdated" over a body that is not one asserts something unsupported.
        //
        // The case is real rather than hypothetical: that message's writer is a single
        // WRITE_BYTE, and the March 2013 demo carries 32 bits at its id - at protocol 24, the
        // protocol this table was transcribed for. So the registration order is a property of the
        // game DLL, not of the network protocol, and protocol 24 alone cannot vouch for it.
        UserMessage refused = Decode("PlayerLoadoutUpdated", [1, 2, 3, 4]);

        refused.Name.ShouldBeNull();
        refused.Fields.ShouldBeNull();
        refused.UserMessageType.ShouldBe(0);
        refused.BodyBits.ShouldBe(32);
    }

    [Test]
    public void AKnownLayoutThatFits_KeepsItsName()
    {
        // The control. Withholding every name would satisfy the test above while destroying the
        // point, so a body that does fit has to keep its name.
        Decode("PlayerLoadoutUpdated", [7]).Name.ShouldBe("PlayerLoadoutUpdated");
    }

    [Test]
    public void AMessageWithNoLayoutAtAll_KeepsItsName()
    {
        // The other control, and the more important one. Withholding is evidence-driven: it fires
        // only where a layout exists to contradict the name. A message this project has never
        // decoded says nothing either way, so removing its name would discard information rather
        // than avoid a false claim.
        UserMessage message = Decode("HudText", [1, 2, 3]);

        message.Name.ShouldBe("HudText");
        message.Fields.ShouldBeNull();
    }

    [Test]
    public void AnUnknownClassNumber_IsReportedAsItsNumber()
    {
        // Nine classes have existed since 2007 and no tenth is expected, but a number outside the
        // table must not silently become a name. Reporting the digit says "this build sent
        // something this table does not cover", which is the honest statement.
        UserMessage message = Decode("PlayerStatsUpdate", StatsBody([99, 0], 1u, 5));

        Value(message, "class").ShouldBe("99");
    }
}
