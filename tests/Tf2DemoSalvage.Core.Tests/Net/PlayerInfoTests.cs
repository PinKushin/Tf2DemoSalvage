using System;
using System.IO;
using System.Text;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Tests for the fixed-size player record carried in the <c>userinfo</c> string table.
/// </summary>
/// <remarks>
/// This is what turns <c>userid=12</c> into a name. Two identifiers are involved and they are
/// not the same number: game events speak <c>user_id</c>, which lives inside this record, while
/// entities are addressed by index, which is the string table <em>entry's name</em>. Confusing
/// them attributes events to the wrong player and nothing fails.
///
/// The record is a C struct written straight to the wire, so every field is fixed-width and
/// position matters more than content. A field read at the wrong offset yields a plausible
/// name — the bytes there are still text.
/// </remarks>
public sealed class PlayerInfoTests
{
    /// <summary>Builds a 132-byte record the way the engine lays it out.</summary>
    private static byte[] Record(
        string name = "b4nny",
        uint userId = 12,
        string steamId = "[U:1:1234567]",
        bool fake = false,
        bool hltv = false)
    {
        byte[] data = new byte[PlayerInfo.RecordBytes];

        Encoding.ASCII.GetBytes(name).CopyTo(data, 0);
        BitConverter.GetBytes(userId).CopyTo(data, 32);
        Encoding.ASCII.GetBytes(steamId).CopyTo(data, 36);
        data[108] = fake ? (byte)1 : (byte)0;
        data[109] = hltv ? (byte)1 : (byte)0;

        return data;
    }

    [Fact]
    public void Parse_ReadsNameUserIdAndSteamId()
    {
        PlayerInfo info = PlayerInfo.Parse(Record(), entityIndex: 3);

        info.Name.ShouldBe("b4nny");
        info.UserId.ShouldBe(12);
        info.SteamId.ShouldBe("[U:1:1234567]");
        info.EntityIndex.ShouldBe(3);
    }

    [Fact]
    public void Parse_EmptyNameField_IsAnEmptyStringNotThePadding()
    {
        // A terminator at offset zero. Every other case here puts at least one character before
        // the NUL, so they cannot tell "found the terminator at 0" from "found no terminator" -
        // and treating the two alike returns the entire 32-byte field, which is 32 NULs rendered
        // as a string. That compares unequal to "" and prints as nothing, so it looks fine in a
        // trace and breaks every lookup keyed on the name.
        //
        // Empty names are not hypothetical: a slot mid-connection carries one.
        PlayerInfo info = PlayerInfo.Parse(Record(name: ""), entityIndex: 4);

        info.Name.ShouldBe("");
        info.Name.Length.ShouldBe(0);

        // The control. The SteamID field uses the same reader, and must still be read normally -
        // otherwise "the name is empty" and "the reader is broken" look the same.
        info.SteamId.ShouldBe("[U:1:1234567]");
    }

    [Fact]
    public void Parse_TrimsTheNulPaddingRatherThanKeepingIt()
    {
        // The name field is 32 bytes whatever the name's length. Keeping the padding gives a
        // string that prints correctly and compares unequal to itself everywhere else.
        PlayerInfo info = PlayerInfo.Parse(Record(name: "ab"), entityIndex: 1);

        info.Name.Length.ShouldBe(2);
        info.Name.ShouldBe("ab");
    }

    [Fact]
    public void Parse_NameFillingTheWholeField_IsNotTruncated()
    {
        // Exactly 32 characters, so there is no terminator inside the field at all. A reader
        // that scans for one and stops there loses the last character.
        string full = new('x', 32);

        PlayerInfo.Parse(Record(name: full), entityIndex: 1).Name.ShouldBe(full);
    }

    [Fact]
    public void Parse_UserIdIsLittleEndian()
    {
        // Source byte-swaps some fields and not this one, which is worth pinning: read the
        // other way, user 1 becomes 16,777,216 and every event attribution silently misses.
        PlayerInfo.Parse(Record(userId: 1), entityIndex: 1).UserId.ShouldBe(1);
        PlayerInfo.Parse(Record(userId: 258), entityIndex: 1).UserId.ShouldBe(258);
    }

    [Fact]
    public void Parse_DistinguishesBotsAndSourceTv()
    {
        // A SourceTV demo always contains an HLTV slot that is not a player. Counting it as one
        // makes every roster one too large.
        PlayerInfo.Parse(Record(), entityIndex: 1).IsBot.ShouldBeFalse();
        PlayerInfo.Parse(Record(fake: true), entityIndex: 1).IsBot.ShouldBeTrue();
        PlayerInfo.Parse(Record(hltv: true), entityIndex: 1).IsSourceTv.ShouldBeTrue();
    }

    [Fact]
    public void Parse_EntityIndexComesFromTheCaller_NotTheRecord()
    {
        // The entity index is the string table entry's name, not a field in the payload. It has
        // to be passed in, and that is exactly the join between events and entities.
        PlayerInfo.Parse(Record(), entityIndex: 7).EntityIndex.ShouldBe(7);
    }

    [Fact]
    public void Parse_ShortRecord_IsRejected()
    {
        // A truncated record would otherwise read whatever follows it as a steam id.
        Should.Throw<InvalidDataException>(() => PlayerInfo.Parse(new byte[64], entityIndex: 1));
    }

    [Fact]
    public void Parse_NonAsciiName_SurvivesAsUtf8()
    {
        // Names are arbitrary bytes from the client. TF2 players use them.
        byte[] data = new byte[PlayerInfo.RecordBytes];
        Encoding.UTF8.GetBytes("ĐŦ").CopyTo(data, 0);

        PlayerInfo.Parse(data, entityIndex: 1).Name.ShouldBe("ĐŦ");
    }
}
