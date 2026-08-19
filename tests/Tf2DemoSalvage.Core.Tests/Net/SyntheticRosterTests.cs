using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The match roster, built from a <c>userinfo</c> table this test wrote.
/// </summary>
/// <remarks>
/// **Converted from <c>CorpusPlayerTests</c>, and it was blocked twice over.** A roster needs a
/// string table, which could not be written in a fixture until <c>svc_CreateStringTable</c> gained
/// a wire form; and it needs USER DATA rather than entry text, because a <c>userinfo</c> entry is
/// named for its entity index and carries a 132-byte <c>player_info_t</c> in its payload. The name,
/// the user id and the Steam id are all in there.
///
/// The corpus version asserted shapes — the Steam id looks like a rendered id, user ids and entity
/// indices are distinct sets, a plausible roster comes out. Those are what you write when the
/// names came off a recording. Here they are chosen, so a field read at the wrong offset produces
/// a wrong name rather than a differently-shaped one.
///
/// **The two identifiers are the point of the record.** Game events carry <c>user_id</c>; entities
/// are addressed by index. Using one where the other belongs attributes a kill to the wrong player
/// and nothing fails, because both are small integers and both are usually valid — so every
/// fixture here gives them deliberately different values.
/// </remarks>
public sealed class SyntheticRosterTests
{
    [Test]
    public void Roster_EveryFieldOfAPlayerRecord_ComesBackAsItWentIn()
    {
        PlayerInfo player = Roster(
            Entry(entityIndex: 3, name: "Heavy", userId: 77, steamId: "[U:1:1234567]"))
            .ShouldHaveSingleItem();

        player.Name.ShouldBe("Heavy");
        player.UserId.ShouldBe(77);
        player.SteamId.ShouldBe("[U:1:1234567]");
        player.EntityIndex.ShouldBe(3);
        player.IsBot.ShouldBeFalse();
        player.IsSourceTv.ShouldBeFalse();
    }

    [Test]
    public void Roster_TheUserIdAndTheEntityIndex_AreNotConfused()
    {
        // **The record's whole reason for existing**, and the confusion is silent: both are small
        // integers and both are usually valid, so attributing a kill to the wrong player fails
        // nothing. The entity index comes from the ENTRY NAME and the user id from inside the
        // payload, so they arrive by different routes and are given values that cannot be swapped
        // unnoticed.
        IReadOnlyList<PlayerInfo> roster = Roster(
            Entry(entityIndex: 2, name: "Scout", userId: 91, steamId: "[U:1:11]"),
            Entry(entityIndex: 9, name: "Medic", userId: 14, steamId: "[U:1:22]"));

        roster.Select(player => (player.EntityIndex, player.UserId))
            .ShouldBe([(2, 91), (9, 14)]);
    }

    [Test]
    public void Roster_ABotAndTheSourceTvSlot_AreFlaggedSeparately()
    {
        // Two single bytes one apart in the record, which is exactly the pair a transposition
        // survives when both are set or both clear. Each is set on its own.
        IReadOnlyList<PlayerInfo> roster = Roster(
            Entry(1, "Bot", 1, "BOT", isBot: true),
            Entry(2, "SourceTV", 2, "[U:1:0]", isSourceTv: true),
            Entry(3, "Human", 3, "[U:1:33]"));

        roster[0].IsBot.ShouldBeTrue();
        roster[0].IsSourceTv.ShouldBeFalse();

        roster[1].IsBot.ShouldBeFalse();
        roster[1].IsSourceTv.ShouldBeTrue();

        roster[2].IsBot.ShouldBeFalse();
        roster[2].IsSourceTv.ShouldBeFalse();
    }

    [Test]
    public void Roster_AnInternationalName_SurvivesAsUtf8()
    {
        // The name is a fixed 32-byte NUL-padded field, so a reader that stopped at the width in
        // characters rather than bytes truncates a multi-byte name mid-character. Player names are
        // the least ASCII data in a demo — see docs/memory/international-names-are-required.md.
        Roster(Entry(4, "Ко́т", 5, "[U:1:44]"))
            .ShouldHaveSingleItem().Name.ShouldBe("Ко́т");
    }

    [Test]
    public void Roster_AMidGameJoin_AppearsWithoutDisturbingWhoWasAlreadyThere()
    {
        // **A roster is built from the create message AND every later update**, which is what a
        // mid-match join is: a second table message naming a slot the first did not. A reader
        // that rebuilt from the create alone would show the match's opening roster for its whole
        // length.
        IReadOnlyList<PlayerInfo> roster = Roster(
            [Entry(1, "First", 10, "[U:1:1]")],
            [Entry(5, "Late", 50, "[U:1:5]")]);

        roster.Count.ShouldBe(2);
        roster.Select(player => player.Name).OrderBy(name => name).ShouldBe(["First", "Late"]);
    }

    [Test]
    public void Roster_AnUpdatedSlot_CarriesTheLaterRecord()
    {
        // The same slot named twice — a reconnect, or a name change. The later record wins, and
        // the roster holds one entry rather than two, because a slot is an identity rather than an
        // event.
        IReadOnlyList<PlayerInfo> roster = Roster(
            [Entry(3, "Before", 20, "[U:1:3]")],
            [Entry(3, "After", 20, "[U:1:3]")]);

        roster.ShouldHaveSingleItem().Name.ShouldBe("After");
    }

    /// <summary>The roster a demo carrying one userinfo table produces.</summary>
    /// <remarks>
    /// The array is wrapped explicitly rather than forwarded. Passing it straight to the
    /// group-taking overload binds back to this one — a params array is a valid argument for its
    /// own parameter — and recurses forever.
    /// </remarks>
    private static IReadOnlyList<PlayerInfo> Roster(
        params (string Name, IReadOnlyList<byte> Data)[] entries) =>
        RosterOf([entries]);

    /// <summary>The roster a demo carrying one userinfo table per group produces.</summary>
    private static IReadOnlyList<PlayerInfo> Roster(
        params (string Name, IReadOnlyList<byte> Data)[][] groups) =>
        RosterOf(groups);

    private static IReadOnlyList<PlayerInfo> RosterOf(
        (string Name, IReadOnlyList<byte> Data)[][] groups)
    {
        Dictionary<int, PlayerInfo> roster = [];

        foreach ((string Name, IReadOnlyList<byte> Data)[] group in groups)
        {
            CreateStringTableMessage table = SyntheticDemo.StringTable(
                RosterBuilder.TableName, AtTheirSlots(group), maxEntries: 64);

            foreach (INetMessage message in
                SyntheticDemo.MessagesIn(SyntheticDemo.Containing(table)))
            {
                if (message is CreateStringTableMessage { Name: RosterBuilder.TableName } read)
                {
                    RosterBuilder.Apply(read.Entries, roster);
                }
            }
        }

        return [.. roster.Values.OrderBy(player => player.EntityIndex)];
    }

    /// <summary>Places each entry at the table position its entity index names.</summary>
    /// <remarks>
    /// **The entry's INDEX is the entity index, and its text is only a cross-check.**
    /// <c>RosterBuilder</c> takes the entity from <c>entry.Index</c> and skips any entry whose text
    /// disagrees with it — deliberately, because preferring one silently would hide the
    /// disagreement, and a missing player is more visible than a wrong one.
    ///
    /// The first version of this fixture put the entity index in the TEXT and let the entries fall
    /// at positions 0, 1, 2. Every one was skipped as inconsistent and the roster came back empty,
    /// which read as user data not surviving the round trip. It survived perfectly; the entries
    /// were simply in the wrong slots.
    ///
    /// A real <c>userinfo</c> table is indexed by client slot, so the gaps below are what an
    /// unoccupied slot actually looks like: an entry with no user data, which the builder skips.
    /// </remarks>
    private static List<(string Text, IReadOnlyList<byte> UserData)> AtTheirSlots(
        IReadOnlyList<(string Name, IReadOnlyList<byte> Data)> entries)
    {
        int slots = entries.Max(
            entry => int.Parse(entry.Name, CultureInfo.InvariantCulture)) + 1;

        List<(string Text, IReadOnlyList<byte> UserData)> table =
        [
            .. Enumerable.Range(0, slots).Select(
                slot => (
                    slot.ToString(CultureInfo.InvariantCulture),
                    (IReadOnlyList<byte>)Array.Empty<byte>())),
        ];

        foreach ((string name, IReadOnlyList<byte> data) in entries)
        {
            int slot = int.Parse(name, CultureInfo.InvariantCulture);
            table[slot] = (name, data);
        }

        return table;
    }

    /// <summary>
    /// One <c>userinfo</c> entry: named for its entity index, carrying a <c>player_info_t</c>.
    /// </summary>
    /// <remarks>
    /// The record is 132 bytes with fixed offsets — a 32-byte NUL-padded name, a four-byte user
    /// id, a 32-byte Steam id, then the two flag bytes at 108 and 109. Built to those offsets
    /// rather than by inverting the reader, so the two agreeing is evidence rather than a
    /// tautology; the offsets themselves are checked against Valve's struct by
    /// <c>PlayerInfoConformanceTests</c>.
    /// </remarks>
    private static (string Name, IReadOnlyList<byte> Data) Entry(
        int entityIndex,
        string name,
        int userId,
        string steamId,
        bool isBot = false,
        bool isSourceTv = false)
    {
        byte[] record = new byte[PlayerInfo.RecordBytes];

        Encoding.UTF8.GetBytes(name).CopyTo(record.AsSpan(0, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(32), (uint)userId);
        Encoding.UTF8.GetBytes(steamId).CopyTo(record.AsSpan(36, 32));

        record[108] = isBot ? (byte)1 : (byte)0;
        record[109] = isSourceTv ? (byte)1 : (byte)0;

        // The entry's NAME is the entity index, which is why the record does not carry one.
        return (entityIndex.ToString(CultureInfo.InvariantCulture), record);
    }
}
