using System;
using System.Collections.Generic;
using System.Text;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// <see cref="RosterBuilder"/>: the create-and-update path that lets a roster include players who
/// joined after signon (RISKS B22).
/// </summary>
/// <remarks>
/// Every test here pairs the subject with a **bystander** — a second, valid entry that must be
/// present afterwards. Without one, "skipped the bad entry" and "skipped everything" are the same
/// observation, and a mutant that breaks the loop outright passes.
/// </remarks>
public class RosterBuilderTests
{
    private const int NameOffset = 0;
    private const int UserIdOffset = 32;
    private const int BystanderIndex = 7;

    /// <summary>Builds a userinfo record of exactly the wire size.</summary>
    private static byte[] Record(string name, int userId, int extraBytes = 0)
    {
        byte[] data = new byte[PlayerInfo.RecordBytes + extraBytes];
        Encoding.UTF8.GetBytes(name).CopyTo(data, NameOffset);
        BitConverter.GetBytes(userId).CopyTo(data, UserIdOffset);
        return data;
    }

    private static StringTableEntry Bystander() =>
        new(BystanderIndex, BystanderIndex.ToString(provider: null), Record("bystander", 700));

    private static Dictionary<int, PlayerInfo> Apply(params StringTableEntry[] entries)
    {
        Dictionary<int, PlayerInfo> players = [];
        RosterBuilder.Apply(entries, players);
        return players;
    }

    [Test]
    public void Apply_EntryWithNoText_IsAccepted()
    {
        // The update path. `svc_UpdateStringTable` entries carry user data and no text at all, so
        // if a null name were treated as a disagreement every mid-match joiner would vanish -
        // which is the exact shape of B22, one layer down.
        Dictionary<int, PlayerInfo> players = Apply(
            new StringTableEntry(3, null, Record("joined_late", 42)),
            Bystander());

        players[3].Name.ShouldBe("joined_late");
        players[3].UserId.ShouldBe(42);
        players[3].EntityIndex.ShouldBe(3);
        players.Count.ShouldBe(2);
    }

    [Test]
    public void Apply_TextDisagreeingWithIndex_IsSkipped()
    {
        // Text says 9, the entry sits at 3. One of the two readings is wrong and the entry is
        // dropped rather than guessed at.
        Dictionary<int, PlayerInfo> players = Apply(
            new StringTableEntry(3, "9", Record("mismatched", 42)),
            Bystander());

        players.ShouldNotContainKey(3);
        players.ShouldContainKey(BystanderIndex);
    }

    [Test]
    public void Apply_UnparseableTextAtIndexZero_IsSkipped()
    {
        // Index zero deliberately. `int.TryParse` writes 0 on failure, so at any other index a
        // "did it parse" check and a "does it match" check reach the same verdict by different
        // routes, and dropping the parse check entirely changes nothing observable. Only at index
        // 0 do the two disagree.
        Dictionary<int, PlayerInfo> players = Apply(
            new StringTableEntry(0, "not_a_number", Record("garbled", 42)),
            Bystander());

        players.ShouldNotContainKey(0);
        players.ShouldContainKey(BystanderIndex);
    }

    [Test]
    public void Apply_RecordShorterThanWireFormat_IsSkipped()
    {
        // A short record must not reach PlayerInfo.Parse, which throws on one. The bystander is
        // what proves the loop continued rather than the whole call aborting.
        Dictionary<int, PlayerInfo> players = Apply(
            new StringTableEntry(3, "3", new byte[PlayerInfo.RecordBytes - 1]),
            Bystander());

        players.ShouldNotContainKey(3);
        players.ShouldContainKey(BystanderIndex);
    }

    [Test]
    public void Apply_RecordLongerThanWireFormat_IsAccepted()
    {
        // The complement of the test above, and the reason the guard is `<` rather than `>`:
        // trailing bytes are not a reason to drop a player. Without this case, reversing the
        // comparison is invisible - a short record is skipped either way.
        Dictionary<int, PlayerInfo> players = Apply(
            new StringTableEntry(3, "3", Record("padded", 42, extraBytes: 16)));

        players[3].Name.ShouldBe("padded");
        players[3].UserId.ShouldBe(42);
    }

    [Test]
    public void Apply_EmptyUserData_IsSkippedNotRemoved()
    {
        // A vacated slot. The question the roster answers is "who played in this match", so a
        // player who left stays named; the entry is skipped rather than treated as a removal.
        Dictionary<int, PlayerInfo> players = [];
        RosterBuilder.Apply([new StringTableEntry(3, "3", Record("was_here", 42))], players);
        RosterBuilder.Apply([new StringTableEntry(3, "3", [])], players);

        players[3].Name.ShouldBe("was_here");
    }

    [Test]
    public void Apply_ReusedSlot_TakesTheLaterRecord()
    {
        // A slot freed and refilled. Later wins, which is what makes the create-plus-updates
        // sequence produce the same roster the game showed.
        Dictionary<int, PlayerInfo> players = [];
        RosterBuilder.Apply([new StringTableEntry(3, "3", Record("first", 1))], players);
        RosterBuilder.Apply([new StringTableEntry(3, null, Record("second", 2))], players);

        players[3].Name.ShouldBe("second");
        players[3].UserId.ShouldBe(2);
    }

    [Test]
    public void Apply_ReusedSlot_StillRemembersWhoHeldItBefore()
    {
        // **The slot map is "who is here now"; the history is "who played in this match".** Those
        // are different questions and the same dictionary cannot answer both, because a slot has one
        // occupant and a match has many.
        //
        // Found through the kill feed. In the modern corpus demo, ids 700, 703, 710, 712, 713 and
        // 717 appear as killers and victims and resolve to no name — their slots were taken over by
        // later joiners and by bots, so the only record of them was overwritten. The feed printed
        // bare numbers for players the demo names perfectly well.
        //
        // This is the case the existing doc comment got wrong: it said a reused slot overwriting the
        // record "is the correct outcome for both questions". It is correct for one of them.
        Dictionary<int, PlayerInfo> players = [];
        Dictionary<int, PlayerInfo> everyone = [];

        RosterBuilder.Apply([new StringTableEntry(3, "3", Record("first", 1))], players, everyone);
        RosterBuilder.Apply([new StringTableEntry(3, null, Record("second", 2))], players, everyone);

        // Current occupancy, unchanged.
        players.Count.ShouldBe(1);
        players[3].Name.ShouldBe("second");

        // And both players remain nameable by the id an event would carry.
        everyone[1].Name.ShouldBe("first");
        everyone[2].Name.ShouldBe("second");
    }

    [Test]
    public void Apply_WithoutAHistory_BehavesExactlyAsBefore()
    {
        // The history is optional so every existing caller is unaffected. Asserted rather than
        // assumed, because "I added an optional parameter" is exactly the change that quietly
        // alters a default.
        Dictionary<int, PlayerInfo> players = [];

        RosterBuilder.Apply([new StringTableEntry(3, "3", Record("only", 1))], players);

        players[3].Name.ShouldBe("only");
    }

    [Test]
    public void Apply_NullEntries_DoesNothing()
    {
        Dictionary<int, PlayerInfo> players = [];
        players[BystanderIndex] = PlayerInfo.Parse(Record("already_there", 700), BystanderIndex);

        Should.NotThrow(() => RosterBuilder.Apply(null!, players));

        // The bystander is the control: "returned early" and "cleared the roster" would otherwise
        // look identical from an exception-free call.
        players[BystanderIndex].Name.ShouldBe("already_there");
    }

    [Test]
    public void Apply_NullPlayers_DoesNothing()
    {
        // Separate from the entries case on purpose: one guard covering both with `&&` instead of
        // `||` still returns early when both are null, and still handles the case above.
        Should.NotThrow(() => RosterBuilder.Apply([Bystander()], null!));
    }
}
