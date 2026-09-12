using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// A <c>userinfo</c> entry names a CLIENT SLOT, and a player's entity is that slot plus one (B398).
/// </summary>
/// <remarks>
/// **Players are entities 1 through <c>maxClients</c>, because entity 0 is the world.**
/// `UTIL_PlayerByIndex` (`game/server/util.cpp:565`):
///
/// <code>
///   if ( playerIndex &gt; 0 &amp;&amp; playerIndex &lt;= gpGlobals-&gt;maxClients )
///   {
///       edict_t *pPlayerEdict = INDEXENT( playerIndex );
/// </code>
///
/// and every player loop in the shared game code runs `for ( int i = 1; i &lt;= gpGlobals-&gt;maxClients;
/// i++ )` (`multiplay_gamerules.cpp:398`, `basecombatweapon_shared.cpp:750`). The <c>userinfo</c>
/// table's entries start at 0 — the first client slot — so a reader that takes the entry index AS the
/// entity is one short for every player.
///
/// **Measured, on the demo that exposed it.** `demostf-cp_process_f12-2026-08-07.dem` puts its
/// SourceTV bot at entry 0 and `Beleleu` at entry 1. `PlayersAt` at tick 51093 reports entity 1 as
/// team 1, class 0 — the SourceTV observer — and entity 2 as a live red soldier holding item 513,
/// which is Beleleu exactly as the real client showed him. Read as entities, the roster put SourceTV
/// on the world and every name one entity to the left, so `--spectate Beleleu` resolved to the
/// SourceTV slot, found it "not playing", and fell back to the default target.
///
/// **This project already knew the rule for one caller.** `RecorderEntityIndex` is
/// `svc_ServerInfo.PlayerSlot + 1`; the roster was the one place still reading a slot as an entity.
/// </remarks>
public sealed class RosterEntityIndexConformanceTests
{
    /// <summary>A <c>player_info_t</c> of exactly the wire size, carrying a name and user id.</summary>
    private static byte[] Record(string name, int userId)
    {
        byte[] data = new byte[PlayerInfo.RecordBytes];
        Encoding.UTF8.GetBytes(name).CopyTo(data, 0);
        System.BitConverter.GetBytes(userId).CopyTo(data, 32);
        return data;
    }

    [Test]
    public void Apply_AnEntryAtSlotThree_IsEntityFour()
    {
        Dictionary<int, PlayerInfo> players = [];

        RosterBuilder.Apply([new StringTableEntry(3, "3", Record("slot_three", 42))], players);

        PlayerInfo player = players.ShouldHaveSingleItem().Value;
        player.Name.ShouldBe("slot_three");
        player.EntityIndex.ShouldBe(4, "userinfo entry 3 is client slot 3, which is entity 4");
        players.ShouldContainKey(4);
    }

    [Test]
    public void Apply_TheFirstSlot_IsEntityOneAndNeverTheWorld()
    {
        // **The case the corpus demo sat in**: SourceTV at entry 0. Entity 0 is worldspawn and no
        // player can hold it, so an entry-0 player read as entity 0 is wrong by construction.
        Dictionary<int, PlayerInfo> players = [];

        RosterBuilder.Apply([new StringTableEntry(0, "0", Record("SourceTV", 2))], players);

        players.ShouldNotContainKey(0);
        players[1].EntityIndex.ShouldBe(1);
    }

    [Test]
    public void Apply_AnUpdateWithNoText_TakesTheSameOffset()
    {
        // An `svc_UpdateStringTable` entry carries no text, so the index is its only source — the
        // offset has to apply on that path as well, or a mid-match joiner lands one entity away
        // from where the create path would have put him.
        Dictionary<int, PlayerInfo> players = [];

        RosterBuilder.Apply([new StringTableEntry(5, null, Record("joined_late", 50))], players);

        players[6].EntityIndex.ShouldBe(6);
    }
}
