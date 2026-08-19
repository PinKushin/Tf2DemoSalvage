using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Accumulates every corpus demo into a world and checks the result is a plausible TF2 match.
/// </summary>
/// <remarks>
/// **The point-of-view and SourceTV recordings of one session are the control for each other.**
/// They carry the same match through different tables — a POV demo sends the recorder's position
/// through <c>DT_TFLocalPlayerExclusive</c> and everyone else's through the non-local table, while
/// SourceTV is almost entirely non-local because there is no player behind the camera. A reader
/// that handles only one shape passes on half the corpus and produces an empty world on the other
/// half, which is why both are asserted here rather than whichever demo was convenient.
///
/// Runs over <c>FilesWithSchema</c> rather than every demo, matching the convention the entity
/// tests already use: the protocol-11 SourceTV recording truncates its <c>dem_datatables</c> at
/// 64 KiB and so has no schema to decode against. That is a property of the recording — a
/// writer-side cap proved by its paired POV demo — not of this parser, and it is asserted
/// directly in <c>CorpusSchemaTests</c> rather than worked around here.
/// </remarks>
public sealed class CorpusSceneTests
{
    private const string PlayerClass = "CTFPlayer";

    /// <summary>Source units. Any real TF2 map fits well inside this; nothing legitimate exceeds it.</summary>
    private const float WorldLimit = 33000f;

    [Test]
    public void EveryDemo_AccumulatesIntoPositionedPlayers()
    {
        int demosWithPlayers = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            string name = Path.GetFileName(path);
            (EntityStateTable table, int snapshots) = Accumulate(path, packetLimit: 3000);

            if (snapshots == 0)
            {
                continue;
            }

            EntityState[] players = [.. table.OfClass(PlayerClass)];
            EntityState[] positioned = [.. players.Where(p => p.Origin() is not null)];

            if (players.Length == 0)
            {
                continue;
            }

            demosWithPlayers++;

            // The claim is about the RESOLVER, not about the recording. "Every player has a
            // position" is false and the corpus says so: a 2008 SourceTV demo has two player
            // entities and one of them is SourceTV's own slot, which never sends an origin
            // because it is not standing anywhere. An unspawned player is the same shape.
            //
            // What must always hold is that a player carrying origin data resolves to a
            // position. A failure here means Origin() cannot read a shape the corpus contains -
            // which is exactly how the launch-era three-component vector was found.
            EntityState[] unresolved =
                [.. players.Where(p => HasAnyOriginProperty(p) && p.Origin() is null)];

            unresolved.ShouldBeEmpty(
                $"{name}: {unresolved.Length} players carry origin data that did not resolve");

            positioned.Length.ShouldBeGreaterThan(0, $"{name}: no player resolved a position");

            foreach (EntityState player in positioned)
            {
                (float x, float y, float z) = player.Origin()!.Value;

                // Inside the world, which is the cheapest falsification available: a misread
                // coordinate lands outside it by orders of magnitude rather than slightly.
                Math.Abs(x).ShouldBeLessThan(WorldLimit, name);
                Math.Abs(y).ShouldBeLessThan(WorldLimit, name);
                Math.Abs(z).ShouldBeLessThan(WorldLimit, name);
            }

            TestContext.Out.WriteLine(
                $"{name}: {players.Length} players, all positioned, " +
                $"{table.All.Count()} entities from {snapshots} snapshots");
        }

        demosWithPlayers.ShouldBeGreaterThan(0, "no demo produced a player");
    }

    [Test]
    public void Scene_BothExclusiveTables_AreExercisedInTheCorpus()
    {
        // This test began as an assertion that a point-of-view demo resolves through the local
        // table and a SourceTV demo through the non-local one. That rule is FALSE, and the corpus
        // said so: the 2013 SourceTV demo is 21 non-local against 2 local, but a modern demos.tf
        // SourceTV recording came back 12 local and 0 non-local.
        //
        // So which table a recording uses is not a property of the recording mode, and any
        // reader branching on POV-versus-SourceTV would be wrong on some era. What can be
        // asserted is the thing that actually matters: every positioned player resolved through
        // one of the two, and neither branch of the resolver is dead code that no demo reaches.
        int viaLocal = 0;
        int viaNonLocal = 0;
        int demos = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            string name = Path.GetFileName(path);
            (EntityStateTable table, int snapshots) = Accumulate(path, packetLimit: 3000);

            if (snapshots == 0)
            {
                continue;
            }

            EntityState[] players =
                [.. table.OfClass(PlayerClass).Where(p => p.Origin() is not null)];

            if (players.Length == 0)
            {
                continue;
            }

            demos++;
            int local = players.Count(HasLocalOrigin);
            viaLocal += local;
            viaNonLocal += players.Length - local;

            TestContext.Out.WriteLine(
                $"{name}: {(HasUserCommands(path) ? "POV" : "STV")} " +
                $"{players.Length} positioned ({local} local, {players.Length - local} non-local)");
        }

        demos.ShouldBeGreaterThan(1, "only one demo reached the scene, so nothing was compared");

        // Both branches reached. Either being zero would mean half the resolver is untested and
        // the corpus cannot say whether it works.
        viaLocal.ShouldBeGreaterThan(0, "no player in the corpus resolved through the local table");
        viaNonLocal.ShouldBeGreaterThan(
            0, "no player in the corpus resolved through the non-local table");

        TestContext.Out.WriteLine($"{viaLocal} players via the local table, {viaNonLocal} via non-local");
    }

    /// <summary>Whether any of the three tables sent this entity an origin at all.</summary>
    private static bool HasAnyOriginProperty(EntityState player) =>
        player.Properties.Keys.Any(
            key => key.EndsWith(".m_vecOrigin", StringComparison.Ordinal));

    private static bool HasLocalOrigin(EntityState player) =>
        player.Properties.ContainsKey("DT_TFLocalPlayerExclusive.m_vecOrigin");

    private static bool HasUserCommands(string path) =>
        DemoCommandReader
            .Read(File.ReadAllBytes(path).AsMemory(DemoHeader.SizeBytes))
            .Any(command => command.Type == DemoCommandType.UserCmd);

    /// <summary>Walks a demo's packets into an accumulated world.</summary>
    private static (EntityStateTable Table, int Snapshots) Accumulate(string path, int packetLimit)
    {
        byte[] file = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(file);
        NetDecodeState state = new() { NetworkProtocol = (ushort)header.NetworkProtocol };

        List<DemoCommand> commands =
            [.. DemoCommandReader.Read(file.AsMemory(DemoHeader.SizeBytes))];

        EntityDecoder? decoder = null;
        IReadOnlyList<ServerClass> classes = [];
        DemoCommand? tables = commands.FirstOrDefault(c => c.Type == DemoCommandType.DataTables);

        if (tables is { } dataTables)
        {
            DemoSchema schema = SendTableParser.Parse(
                dataTables.Payload.Span, (ushort)header.NetworkProtocol);
            decoder = new EntityDecoder(
                schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
            classes = schema.ServerClasses;
        }

        EntityStateTable table = new();

        // Class names come from dem_datatables, not from svc_ClassInfo: TF2 sets the
        // "create on client" flag and sends no names, so a reader waiting for that message
        // names nothing and finds no players while decoding every entity correctly.
        foreach (ServerClass serverClass in classes)
        {
            table.SetClassName(serverClass.Id, serverClass.ClassName);
        }

        if (decoder is null)
        {
            return (table, 0);
        }

        int snapshots = 0;

        foreach (DemoCommand command in commands
            .Where(c => c.Type is DemoCommandType.Signon or DemoCommandType.Packet))
        {
            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state)
                .Messages)
            {
                if (message is not PacketEntitiesMessage snapshot || snapshot.LengthBits <= 0)
                {
                    continue;
                }

                foreach (DecodedEntity entity in
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits))
                {
                    table.Apply(entity);
                }

                snapshots++;

                if (snapshots >= packetLimit)
                {
                    return (table, snapshots);
                }
            }
        }

        return (table, snapshots);
    }
}
