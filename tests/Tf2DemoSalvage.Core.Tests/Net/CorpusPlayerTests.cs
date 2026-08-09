using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Xunit.Abstractions;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The <c>userinfo</c> table read from real demos.
/// </summary>
/// <remarks>
/// A fixture proves the record layout is read consistently; only a real demo proves the layout
/// is right. The check here is that the names look like names — a field read at the wrong offset
/// still produces text, but it produces text that fails these assertions.
/// </remarks>
public sealed class CorpusPlayerTests(ITestOutputHelper output)
{
    [Fact]
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
                player.UserId.ShouldBeInRange(0, 1024, name);
                player.EntityIndex.ShouldBeInRange(0, 2048, name);
            }
        }
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public void ReportRosters()
    {
        foreach (string path in Corpus.Files())
        {
            IReadOnlyList<PlayerInfo> players = Players(path);
            output.WriteLine($"{Path.GetFileName(path)}: {players.Count} slots");

            foreach (PlayerInfo player in players.Take(8))
            {
                output.WriteLine(
                    $"  entity {player.EntityIndex,-4} userid {player.UserId,-4} " +
                    $"{player.Name,-24} {player.SteamId}");
            }

            output.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    /// <summary>Collects every player named by the demo's <c>userinfo</c> table.</summary>
    private static IReadOnlyList<PlayerInfo> Players(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        // Seeded from the header: the protocol sizes the message type field, so a
        // protocol-15 demo yields no messages at all without it (RISKS B17).
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
        };
        Dictionary<int, PlayerInfo> byEntity = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (CreateStringTableMessage table in NetMessageReader
                .Read(command.Payload.Span, state)
                .Messages.OfType<CreateStringTableMessage>()
                .Where(t => t.Name == "userinfo"))
            {
                foreach (StringTableEntry entry in table.Entries)
                {
                    if (entry.UserData.Count < PlayerInfo.RecordBytes ||
                        !int.TryParse(entry.Text, out int entityIndex))
                    {
                        continue;
                    }

                    byEntity[entityIndex] = PlayerInfo.Parse(
                        [.. entry.UserData], entityIndex);
                }
            }
        }

        return [.. byEntity.Values.OrderBy(p => p.EntityIndex)];
    }
}
