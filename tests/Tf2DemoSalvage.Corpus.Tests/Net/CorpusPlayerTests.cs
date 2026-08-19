using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>userinfo</c> table read from real demos.
/// </summary>
/// <remarks>
/// A fixture proves the record layout is read consistently; only a real demo proves the layout
/// is right. The check here is that the names look like names — a field read at the wrong offset
/// still produces text, but it produces text that fails these assertions.
/// </remarks>
public sealed class CorpusPlayerTests
{
    [Test]
    public void EveryDemo_YieldsAPlausibleRoster()
    {
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            IReadOnlyList<PlayerInfo> players = Players(path);

            // A competitive match is six a side plus a SourceTV slot, give or take spectators
            // and substitutions across a whole demo.
            // Not "more than six". That encoded an assumption the corpus made true by
            // accident - every demo was a competitive match until one recorded alone on a
            // listen server was added. The real invariant is that a demo which decoded at all
            // named at least the player recording it, and that no roster exceeds the engine's
            // limit.
            players.Count.ShouldBeGreaterThan(0, name);
            players.Count.ShouldBeLessThan(64, name);

            foreach (PlayerInfo player in players)
            {
                player.Name.ShouldNotBeNullOrWhiteSpace(name);
                player.Name.Length.ShouldBeLessThanOrEqualTo(32, name);
                player.Name.ShouldAllBe(c => !char.IsControl(c), name);
                // **A user id is a per-connection counter, not a player slot.** The server
                // increments it for every client that has ever joined, so a busy pub that has
                // been up for hours is well past a thousand: the 2026 pub demo's roster runs
                // 1090-1147 across 23 players. The old ceiling of 1024 was the same mistake as
                // the "more than six players" one above - an assumption the corpus made true by
                // accident, because every demo in it was recorded on a freshly started listen
                // server where the counter had barely moved.
                //
                // What is actually structural: the field is a signed int, so the only values a
                // correct read cannot produce are negative ones, and a bit-level misread lands
                // in the billions rather than the thousands. Hence a bound that only a misread
                // reaches, rather than one describing how busy a server has been.
                player.UserId.ShouldBeInRange(0, 1_000_000, name);

                // The entity index IS slot-bounded, by MAX_EDICTS. This is the tight one.
                player.EntityIndex.ShouldBeInRange(0, 2048, name);
            }
        }
    }

    [Test]
    public void SteamIds_AreInTheRenderedTextFormat()
    {
        // The field holds a *rendered* id, and which rendering depends on the era:
        //
        //   Steam3, current   [U:1:1234567]
        //   Steam2, 2009      STEAM_0:0:0
        //   either            BOT, for a fake player
        //
        // A fourth era difference, found the same way as the others - by adding a demo old
        // enough to disagree. It is cosmetic rather than structural, which is exactly why it
        // is worth pinning: nothing downstream would fail on it, so an unnoticed change here
        // would silently reshape any output keyed on the id.
        //
        // The check is still narrow on purpose. Reading this field at the wrong offset yields
        // leftover bytes from the name or the friends field, which is text but matches none of
        // these three shapes.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);

            foreach (PlayerInfo player in Players(path).Where(p => !p.IsSourceTv))
            {
                player.SteamId.ShouldNotBeNullOrEmpty(name);
                (player.SteamId.StartsWith("[U:", StringComparison.Ordinal) ||
                 player.SteamId.StartsWith("STEAM_", StringComparison.Ordinal) ||
                 player.SteamId == "BOT").ShouldBeTrue($"{name}: {player.SteamId}");
            }
        }
    }

    [Test]
    public void UserIdsAndEntityIndices_AreDistinctIdentifiers()
    {
        // The join that makes events attributable. If these were the same number the
        // distinction would not matter and the mapping could be skipped - on real demos they
        // differ for most players, which is exactly why confusing them is silent.
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<PlayerInfo> players = Players(path);

            players.Select(p => p.EntityIndex).ShouldBeUnique(Path.GetFileName(path));
            players.ShouldContain(p => p.UserId != p.EntityIndex, Path.GetFileName(path));
        }
    }

    [Test]
    public void Rosters_TheCorpus_AreReported()
    {
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<PlayerInfo> players = Players(path);
            TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {players.Count} slots");

            foreach (PlayerInfo player in players.Take(8))
            {
                TestContext.Out.WriteLine(
                    $"  entity {player.EntityIndex,-4} userid {player.UserId,-4} " +
                    $"{player.Name,-24} {player.SteamId}");
            }

            TestContext.Out.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }
    [TestCase("demostf-koth_product_final-2026-08-07.dem", 19)]
    [TestCase("z1800.dem", 26)]
    public void MidGameJoins_AreInTheRoster(string fileName, int expected)
    {
        // RISKS B22. `userinfo` is created once during signon, so anyone who connects later
        // arrives as an svc_UpdateStringTable. Reading only the create message yields the
        // *signon* roster, not the match roster - and players dropping and rejoining mid-match
        // is routine in TF2.
        //
        // Exact counts, not "more than before". Measured across the corpus: the create table
        // names 18 and 25 players in these two demos respectively, and the updates each
        // introduce one entity index that the create message never mentioned. A test asserting
        // only ">= 18" would pass on the broken code.
        //
        // This stayed invisible because every other check here asks whether the roster is
        // *plausible* - names non-empty, ids in range, count under 64 - and a roster missing one
        // player is entirely plausible. Same shape as B20 and B21.
        string? path = Corpus.Files().FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (path is null)
        {
            return;                                  // corpus not checked out
        }

        Players(path).Count.ShouldBe(expected, fileName);
    }

    [Test]
    public void AnUpdatedSlot_CarriesTheLaterRecord()
    {
        // The other half of B22, and the half a count cannot see: an update may *replace* an
        // existing slot rather than add one, when a player reconnects into it. Six of the eight
        // corpus demos have such updates, so a roster built from the create message alone can
        // name the wrong person at the right index while having exactly the right size.
        //
        // Measured as an invariant rather than a fixed name, because which slot gets reused is a
        // property of the match, not of the parser: every roster entry must carry a full record.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);

            foreach (PlayerInfo player in Players(path))
            {
                player.Name.ShouldNotBeNullOrWhiteSpace($"{name}: entity {player.EntityIndex}");
                player.SteamId.ShouldNotBeNullOrWhiteSpace($"{name}: entity {player.EntityIndex}");
            }
        }
    }

    /// <summary>Every player named by the demo's <c>userinfo</c> table, walked once.</summary>
    /// <remarks>
    /// Cached in <see cref="Corpus"/> rather than walked per test. Each of this class's tests
    /// needs the same roster, and building it means reading every packet in every demo —
    /// measured at 6 to 12 seconds per test, which made this class the whole suite's critical
    /// path at roughly 32 of its 34 seconds. Tests within a class run sequentially, so the four
    /// walks did not even overlap.
    /// </remarks>
    private static IReadOnlyList<PlayerInfo> Players(string path) => Corpus.Players(path);
}
