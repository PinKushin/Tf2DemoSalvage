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
            players.Count.ShouldBeGreaterThan(6, name);
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
        // "[U:1:1234567]" for a real account, "BOT" for a fake player. Reading the field at the
        // wrong offset gives leftover bytes from the name or the friends field instead, which
        // is text but not this shape.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);

            foreach (PlayerInfo player in Players(path).Where(p => !p.IsSourceTv))
            {
                player.SteamId.ShouldNotBeNullOrEmpty(name);
                (player.SteamId.StartsWith("[U:", StringComparison.Ordinal) ||
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
        NetDecodeState state = new();
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
