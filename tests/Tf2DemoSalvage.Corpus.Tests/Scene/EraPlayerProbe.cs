using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Why an era demo finds one player where a modern one finds twenty-four.
/// </summary>
/// <remarks>
/// **Every demo before 2013 yields exactly one positioned player, including SourceTV recordings
/// that watched twelve.** That is not a plausible number for a match, and one is the number a
/// reader gets when it can only see the recording client — so the question is whether the other
/// players are missing, unnamed, or merely without an origin this reader recognises.
///
/// Counted rather than reasoned: how many entities exist, how many are named CTFPlayer, how many
/// carry any origin-shaped property, and how many yield a position.
/// </remarks>
public sealed class EraPlayerProbe
{
    [Test]
    public void WhereDoTheOtherPlayersGo()
    {
        foreach (string path in Corpus.FilesWithSchema())
        {
            byte[] file = File.ReadAllBytes(path);
            DemoHeader header = DemoHeader.Parse(file);
            NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

            List<DemoCommand> commands =
                [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

            DemoCommand dataTables = commands.FirstOrDefault(
                c => c.Type == DemoCommandType.DataTables);

            if (dataTables.Type != DemoCommandType.DataTables)
            {
                continue;
            }

            DemoSchema schema = SendTableParser.Parse(
                dataTables.Payload.Span, (ushort)header.NetworkProtocol);

            EntityDecoder decoder = new(
                schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            EntityStateTable entities = new();

            foreach (ServerClass serverClass in schema.ServerClasses)
            {
                entities.SetClassName(serverClass.Id, serverClass.ClassName);
            }

            foreach (DemoCommand command in commands
                .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
            {
                foreach (INetMessage message in
                    NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    // **Instance baselines, which the scene layer was not applying.** An entity
                    // entering the potentially visible set is sent as a delta against its CLASS
                    // baseline rather than in full, so without these the entering update is being
                    // read against nothing.
                    switch (message)
                    {
                        case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                            BaselineBuilder.Apply(create.Entries, decoder);
                            continue;

                        case UpdateStringTableMessage update
                            when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                            BaselineBuilder.Apply(update.Entries, decoder);
                            continue;

                        default:
                            break;
                    }

                    if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                    {
                        continue;
                    }

                    foreach (DecodedEntity entity in
                        decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                    {
                        entities.Apply(entity);
                    }
                }
            }

            EntityState[] all = [.. entities.All];
            EntityState[] players = [.. entities.OfClass("CTFPlayer")];
            EntityState[] positioned = [.. players.Where(p => p.Origin() is not null)];

            // Which tables the origins actually came through, since that is the era split.
            int local = players.Count(p =>
                p.Properties.Keys.Any(k => k.StartsWith("DT_TFLocalPlayerExclusive", StringComparison.Ordinal)));
            int nonLocal = players.Count(p =>
                p.Properties.Keys.Any(k => k.StartsWith("DT_TFNonLocalPlayerExclusive", StringComparison.Ordinal)));

            string[] classesWithPlayers = [.. all
                .Where(e => e.ClassName is not null && e.ClassName.Contains("Player", StringComparison.Ordinal))
                .Select(e => e.ClassName!)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)];

            TestContext.Out.WriteLine(
                $"ERA {Path.GetFileName(path)}: {all.Length} entities, {players.Length} CTFPlayer, " +
                $"{positioned.Length} positioned, {local} with local table, {nonLocal} with non-local");

            TestContext.Out.WriteLine(
                $"ERA   player-ish classes: {string.Join(", ", classesWithPlayers)}");

            // A probe, but not an empty one: a demo that decoded no entities at all would make
            // every line above meaningless.
            all.Length.ShouldBeGreaterThan(0, path);
        }
    }
}
