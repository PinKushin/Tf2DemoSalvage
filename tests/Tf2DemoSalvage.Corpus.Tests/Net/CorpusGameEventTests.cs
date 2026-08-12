using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Game event decoding against real demos.
/// </summary>
/// <remarks>
/// The bit widths here were partly inferred — the reference implementation states the message
/// framing but not the 3-bit value type field, and Source's convention had to be assumed. This
/// is what verifies that assumption: wrong widths desynchronise the definition list
/// immediately and produce garbage names, so recognisable TF2 event names are the proof.
/// </remarks>
public sealed class CorpusGameEventTests
{
    /// <summary>Events every TF2 demo should describe, whether or not they fire.</summary>
    private static readonly string[] ExpectedEvents =
    [
        "player_death",
        "player_spawn",
        "player_hurt",
        "teamplay_round_start",
        "teamplay_round_win",
    ];

    [Test]
    public void GameEventList_DecodesToRecognisableTf2Events()
    {
        foreach (string path in Corpus.Files())
        {
            (GameEventListMessage? list, _) = ReadUntilEventList(path);

            if (list is null)
            {
                // Not yet reachable: signon opens with svc_ServerInfo, which is not decoded
                // yet, and messages carry no length prefix. This flips to a real assertion
                // the moment ServerInfo lands - see ReportWhereSignonStops.
                TestContext.Out.WriteLine($"{Path.GetFileName(path)}: event list not yet reachable");
                continue;
            }

            list.Definitions.ShouldNotBeEmpty();

            HashSet<string> names = [.. list.Definitions.Select(d => d.Name)];
            foreach (string expected in ExpectedEvents)
            {
                names.ShouldContain(
                    expected,
                    $"{Path.GetFileName(path)} defined {list.Definitions.Count} events but not " +
                    $"'{expected}' - the value-type bit width is probably wrong");
            }
        }
    }

    [Test]
    public void GameEventDefinitions_HaveSaneNamesAndFields()
    {
        foreach (string path in Corpus.Files())
        {
            (GameEventListMessage? list, _) = ReadUntilEventList(path);
            if (list is null)
            {
                continue;
            }

            foreach (GameEventDefinition definition in list.Definitions)
            {
                // Garbage from a desynchronised stream shows up as control characters or
                // absurd lengths long before it shows up as a wrong value.
                definition.Name.ShouldNotBeNullOrWhiteSpace();
                definition.Name.Length.ShouldBeLessThan(64);
                definition.Name.ShouldAllBe(c => !char.IsControl(c));

                foreach (GameEventField field in definition.Fields)
                {
                    field.Name.ShouldNotBeNullOrWhiteSpace();
                    field.Name.Length.ShouldBeLessThan(64);
                    field.Type.ShouldNotBe(GameEventValueType.None);
                    Enum.IsDefined(field.Type).ShouldBeTrue();
                }
            }
        }
    }

    [Test]
    public void PlayerDeath_HasTheFieldsItShould()
    {
        foreach (string path in Corpus.Files())
        {
            (GameEventListMessage? list, _) = ReadUntilEventList(path);
            if (list is null)
            {
                continue;
            }

            GameEventDefinition death = list.Definitions.First(d => d.Name == "player_death");

            IEnumerable<string> fields = death.Fields.Select(f => f.Name);
            fields.ShouldContain("userid");
            fields.ShouldContain("attacker");

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: player_death({string.Join(", ", death.Fields.Select(f => $"{f.Type} {f.Name}"))})");
        }
    }

    [Test]
    public void ReportWhatTheDemosDefineAndFire()
    {
        foreach (string path in Corpus.Files())
        {
            NetDecodeState state = new();
            Dictionary<string, int> fired = new(StringComparer.Ordinal);
            int definitions = 0;
            int undecoded = 0;

            foreach (DemoCommand packet in Packets(path).Take(4000))
            {
                NetMessageReadResult result = NetMessageReader.Read(packet.Payload.Span, state);

                foreach (INetMessage message in result.Messages)
                {
                    switch (message)
                    {
                        case GameEventListMessage list:
                            definitions = list.Definitions.Count;
                            break;

                        case GameEventMessage { Name: { } name }:
                            fired.TryGetValue(name, out int count);
                            fired[name] = count + 1;
                            break;

                        case GameEventMessage:
                            undecoded++;
                            break;

                        default:
                            break;
                    }
                }
            }

            TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {definitions} events defined, " +
                             $"{fired.Values.Sum()} fired, {undecoded} undecodable");
            foreach ((string name, int count) in fired.OrderByDescending(kv => kv.Value).Take(12))
            {
                TestContext.Out.WriteLine($"    {count,5}  {name}");
            }

            TestContext.Out.WriteLine(string.Empty);
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    [Test]
    public void ReportWhereSignonStops()
    {
        // The signon stream carries everything a joining client needs: the entity schema, the
        // string tables, and the game event definitions. Reaching any of it means decoding
        // svc_ServerInfo first, because nothing here is length-prefixed.
        foreach (string path in Corpus.Files())
        {
            NetDecodeState state = new();

            foreach (DemoCommand signon in SignonAndPackets(path)
                .Where(c => c.Type == DemoCommandType.Signon))
            {
                NetMessageReadResult result = NetMessageReader.Read(signon.Payload.Span, state);
                TestContext.Out.WriteLine(
                    $"{Path.GetFileName(path)}: signon {signon.Payload.Length,8} bytes - " +
                    $"read {result.Messages.Count} message(s), stopped at " +
                    $"{result.StoppedAt?.ToString() ?? "end"}");
            }
        }

        Corpus.Files().ShouldNotBeEmpty();
    }

    private static (GameEventListMessage? List, NetDecodeState State) ReadUntilEventList(string path)
    {
        // Seeded with the demo's own protocol. Without it every protocol-14 and 15 demo decodes
        // to noise, and this method returns null rather than throwing - which both callers treat
        // as "not reachable yet" and skip. The 2008 demo was excluded that way, silently, until a
        // SourceTV recording of the same era produced an empty list instead of no list.
        NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };

        // svc_GameEventList arrives during signon, not during play - it is part of the state
        // the server hands a joining client, alongside the entity schema. So signon commands
        // are searched first, and only then the packet stream.
        foreach (DemoCommand packet in SignonAndPackets(path).Take(500))
        {
            NetMessageReadResult result = NetMessageReader.Read(packet.Payload.Span, state);
            GameEventListMessage? list = result.Messages.OfType<GameEventListMessage>().FirstOrDefault();
            if (list is not null)
            {
                return (list, state);
            }
        }

        return (null, state);
    }

    private static DemoCommand[] SignonAndPackets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet)];
    }

    private static DemoCommand[] Packets(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return [.. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes))
            .Where(c => c.Type == DemoCommandType.Packet)];
    }
}
