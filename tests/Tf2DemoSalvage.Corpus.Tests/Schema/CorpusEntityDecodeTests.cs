using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Entity decoding run against real demos, where a hand-built fixture cannot help.
/// </summary>
/// <remarks>
/// The fixtures in <c>EntityDecoderTests</c> (now in Tf2DemoSalvage.Core.Tests) prove the decoder matches the SDK's write
/// path as read. They cannot prove that reading is right, because both sides came from the same
/// head — and for a while they did not. Every fixture passed while real demos desynchronised
/// inside <c>CTFPlayer</c>, because the flattened property order was wrong in a way that
/// changes no property's identity, only its index.
///
/// These tests are the instrument that caught it, and the reason it stays: a misaligned bit
/// stream cannot stay plausible. Hundreds of entities that each name properties their own class
/// declares, at positions inside the world bounds, is not something a wrong reader produces.
/// </remarks>
public sealed class CorpusEntityDecodeTests
{
    // Five tests removed on 2026-08-19, each covered by a synthetic demo that asserts a value
    // rather than a range.
    //
    // EntityIndices_AscendAndStayInsideTheEntityLimit, PlayerPositions_LandInsideTheWorldBounds
    // and Tracker_HoldsPlayerPositionsInsideTheMap were bounds checks — "inside MAX_EDICTS",
    // "inside the world". SyntheticSceneTests.Build_ThreePlayersAtChosenPositions_AreNotCollapsed
    // ToOne puts three players at chosen coordinates in slots 1, 2 and 5, which also exercises the
    // index delta: the encoder writes the GAP to the next occupied slot rather than the slot
    // itself, so 1, 2, 5 covers what 1, 2, 3 cannot.
    //
    // Tracker_HoldsMorePropertiesThanAnySingleUpdateCarried is SyntheticTimelineFrameTests
    // .Build_APropertyOnlyTheFirstSnapshotSent_IsRetainedAcrossDeltas, where the property rides on
    // the entering snapshot alone and is read three snapshots later.
    //
    // EntityProperties_TheCorpus_AreReported was a report.
    //
    // What stays here is what only real bytes can settle: that every entity a real snapshot names
    // decodes, that a hundred consecutive snapshots survive, that point-of-view recordings decode
    // as well as SourceTV ones, that every decoded property belongs to the class it was read for,
    // and how much of an entity its instance baseline supplies.

    /// <summary>
    /// Upper bound on snapshots read by a test that stops as soon as its claim is demonstrated.
    /// Generous on purpose: it is a runaway guard, not a tuned window.
    /// </summary>
    private const int SnapshotCap = 4000;

    [Test]
    public void OpeningSnapshot_DecodesEveryEntityItNames()
    {
        foreach (string path in SourceTvDemos())
        {
            (IReadOnlyList<DecodedEntity> entities, PacketEntitiesMessage header) = FirstFull(path);
            string name = Path.GetFileName(path);

            // The header says how many entities to expect. Producing exactly that many means
            // every entity's bits were consumed correctly - one bit short or long and the walk
            // diverges immediately rather than drifting.
            entities.Count.ShouldBe(header.UpdatedEntries, name);
            entities.Select(e => e.EntityIndex).ShouldBeUnique(name);
            entities.ShouldAllBe(e => e.UpdateType == EntityUpdateType.Enter);
            // Scaled to the snapshot, not an absolute count. This was `> 2000`, sized against
            // 12v12 matches whose opening snapshot names 855 entities; a one-player listen server
            // names 195 and carries 657 properties, and failed a threshold that was really
            // measuring how busy the server was.
            //
            // What the assertion is actually for is a decoder that walks the entity list correctly
            // but produces empty property lists - the counts above would all still pass. A mean
            // above one property per entity catches that at any scale. Some entities legitimately
            // enter with none at all (CWorld does), so a per-entity floor would be wrong.
            int properties = entities.Sum(e => e.Properties.Count);
            properties.ShouldBeGreaterThan(
                entities.Count, $"{name}: {entities.Count} entities, {properties} properties");
        }
    }


    [Test]
    public void EntityDecode_EveryProperty_BelongsToItsClass()
    {
        // Class names arrive from dem_datatables and entity data from the bit stream -
        // independent paths. That they agree across hundreds of thousands of properties is the
        // strongest evidence available without a second parser.
        foreach (string path in SourceTvDemos())
        {
            DemoSchema schema = Schema(path);
            (IReadOnlyList<DecodedEntity> entities, _) = FirstFull(path);
            string name = Path.GetFileName(path);

            foreach (DecodedEntity entity in entities)
            {
                IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(
                    schema, schema.ServerClasses.First(c => c.Id == entity.ClassId));

                foreach (DecodedProperty property in entity.Properties)
                {
                    property.Index.ShouldBeInRange(0, flat.Count - 1, name);
                    property.Definition.Property.Name
                        .ShouldBe(flat[property.Index].Property.Name, name);
                }
            }
        }
    }


    [Test]
    public void ContinuousDecoding_SurvivesAtLeastAHundredConsecutiveSnapshots()
    {
        // Every corpus demo now decodes end to end - 14,385 snapshots for z1800, 73,182 for
        // serveme, 118,280 for the POV demo - so this cap is arbitrary rather than a limit.
        //
        // The history is worth the comment. Zero before the flattening order was fixed, then
        // 62-205, then 332, then complete, and every jump came from implementing another
        // message type that had been truncating its packet rather than from any decoder fix.
        foreach (string path in SourceTvDemos())
        {
            DecodeRun run = DecodeContinuously(path, 500);
            TestContext.Out.WriteLine($"{Path.GetFileName(path)}: {run.Decoded} snapshots, " +
                             $"stopped: {run.Stopped ?? "not at all"}");
            run.Stopped.ShouldBeNull(Path.GetFileName(path));
            run.Decoded.ShouldBe(500);
        }
    }

    /// <summary>What a walk decoded, out of what the demo actually offered it.</summary>
    /// <param name="Decoded">Snapshots decoded from the first full one onward.</param>
    /// <param name="Available">
    /// Snapshots the walk was handed over the same span. Equal to <paramref name="Decoded"/> when
    /// nothing stopped, and it is the pair that makes the claim testable on a demo of any length.
    /// </param>
    /// <param name="Stopped">The error that ended the walk, or <c>null</c>.</param>
    private sealed record DecodeRun(int Decoded, int Available, string? Stopped);

    private static DecodeRun DecodeContinuously(string path, int limit)
    {
        DemoSchema schema = Schema(path);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        bool started = false;
        int decoded = 0;

        int available = 0;

        foreach (PacketEntitiesMessage message in Snapshots(path).Take(limit))
        {
            started |= message.IsFullSnapshot;
            if (!started)
            {
                continue;
            }

            available++;

            try
            {
                decoder.Decode(message.Body.Span, message, message.LengthBits);
                decoded++;
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            {
                // Running off the end of the body is the same desynchronisation reported a
                // different way - the reader asks for bits the message does not have.
                return new DecodeRun(decoded, available, error.Message);
            }
        }

        return new DecodeRun(decoded, available, null);
    }

    [Test]
    public void PointOfViewDemos_DecodeToo()
    {
        // This test used to assert the opposite - that a POV demo carries no full snapshot at
        // all, having scanned two thousand consecutive deltas without finding one. That was
        // never a property of POV recordings. The full snapshot was there and sat behind an
        // unimplemented message, so the walk never reached it.
        //
        // Worth keeping as a caution: "I scanned 2,000 and found none" was evidence about the
        // reader, and it read as evidence about the format.
        // Every POV demo, not the first one found. This asserted `Decoded == 5000` against
        // whichever file happened to sort first, which was a 45 MB match with snapshots to spare.
        // When the corpus was trimmed the first POV file became a two-minute recording with 2,764
        // packets, and the test failed for having been sized to a specific demo rather than to the
        // claim. The claim is that a POV demo decodes without stopping - the count was a proxy for
        // "a long way in", and a proxy that breaks when the corpus changes is the wrong measure.
        string[] povDemos =
        [
            .. Corpus.Files().Where(f => Path.GetFileName(f).Contains("pov", StringComparison.Ordinal))
        ];

        povDemos.ShouldNotBeEmpty();
        int offered = 0;

        foreach (string pov in povDemos)
        {
            DecodeRun run = DecodeContinuously(pov, SnapshotCap);

            TestContext.Out.WriteLine(
                $"{Path.GetFileName(pov)}: {run.Decoded} of {run.Available} snapshots, " +
                $"stopped: {run.Stopped ?? "not at all"}");

            run.Stopped.ShouldBeNull(Path.GetFileName(pov));

            // Every snapshot the demo offered, rather than a fixed number of them. An absolute
            // count is the mistake this test has now made twice: sized to a 45 MB match, broken
            // when the corpus was trimmed, resized to 2,000, then broken again by a 52-second
            // protocol-11 recording that offered 1,029 snapshots and decoded all of them. The
            // claim was never "at least N" - it is "it does not stop" - and a demo shorter than
            // the floor fails a test it satisfies perfectly.
            //
            // Being straight about what this line does: it cannot currently fail on its own,
            // because the only path that leaves Decoded below Available is the catch that also
            // sets Stopped. It is here as the literal statement of the claim, so a later change
            // to the walk cannot quietly skip snapshots and still pass.
            run.Decoded.ShouldBe(run.Available, Path.GetFileName(pov));

            offered += run.Available;
        }

        // The floor is on the corpus, not on any file in it, and that distinction is the whole
        // point. A demo may legitimately be one frame long - `record` and `stop` on consecutive
        // ticks produces a valid file - so every per-demo minimum eventually rejects a demo for
        // being short rather than for being wrong. This test has now done that twice, at 5,000
        // and at 2,000, both times against a file that decoded perfectly.
        //
        // What actually needs guarding is that the run as a whole did something, which is a
        // property of the corpus. Here it is roughly 30,000, so this cannot fire without most of
        // the corpus vanishing - at which point the empty check above is the honest report.
        offered.ShouldBeGreaterThan(1000, "the POV corpus offered almost no snapshots");
    }



    /// <summary>Demos that open with a full snapshot, which is every SourceTV recording.</summary>
    /// <summary>SourceTV demos that carry a usable schema.</summary>
    /// <remarks>
    /// Filtered on the schema, not on a file name. The protocol-11 SourceTV demo truncates its
    /// dem_datatables at 64 KiB, so it has no schema to decode entities against - a property of
    /// that recording rather than of this parser, asserted directly in CorpusSchemaTests.
    /// </remarks>
    private static string[] SourceTvDemos() =>
    [
        .. Corpus.FilesWithSchema()
            .Where(f => !Path.GetFileName(f).Contains("pov", StringComparison.Ordinal)),
    ];

    /// <summary>Decodes the first full snapshot, which is self-contained: every entity enters.</summary>
    private static (IReadOnlyList<DecodedEntity> Entities, PacketEntitiesMessage Header) FirstFull(
        string path)
    {
        DemoSchema schema = Schema(path);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        foreach (PacketEntitiesMessage message in Snapshots(path))
        {
            if (message.IsFullSnapshot)
            {
                return (decoder.Decode(message.Body.Span, message, message.LengthBits), message);
            }
        }

        throw new InvalidDataException($"{Path.GetFileName(path)} contains no full snapshot.");
    }

    /// <summary>The demo's schema, parsed once per process by <see cref="Corpus"/>.</summary>
    private static DemoSchema Schema(string path) => Corpus.Schema(path);

    private static IEnumerable<PacketEntitiesMessage> Snapshots(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        // Seeded from the header. Without it this yielded *zero* snapshots for the protocol-15
        // demo rather than wrong ones (RISKS B17), so every corpus test built on it looped no
        // times and passed vacuously - a test that cannot fail because its condition never
        // occurs, which is the hardest kind to notice.
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
        };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (PacketEntitiesMessage message in
                NetMessageReader.Read(command.Payload.Span, state)
                    .Messages.OfType<PacketEntitiesMessage>())
            {
                if (!message.Body.IsEmpty)
                {
                    yield return message;
                }
            }
        }
    }



    [Test]
    public void Baselines_SupplyMostOfWhatAnEntityKnows()
    {
        // A snapshot sends an entering entity as a delta against its class baseline, so every
        // property still at its default never reaches the wire. Measured across the corpus, that
        // is roughly ninety percent of entity state: z1800 goes from 3,369 known properties to
        // 49,452 over the same 300 snapshots.
        //
        // Asserted as a ratio rather than a total. The totals are real but depend on how many
        // snapshots are read and on decode details elsewhere, so pinning them would make this a
        // change detector. A broken baseline path does not produce a slightly smaller number -
        // it produces exactly the without-baselines number, so a five-fold margin separates
        // "working" from "not wired up" with room to spare.
        foreach (string path in Corpus.FilesWithSchema())
        {
            string name = Path.GetFileName(path);
            DemoSchema schema = Schema(path);
            EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
            EntityTracker withBaselines = new();
            EntityTracker without = new();
            int baselines = 0;
            bool started = false;

            foreach ((PacketEntitiesMessage snapshot, IReadOnlyList<StringTableEntry> entries)
                in EntityStream(path).Take(400))
            {
                if (entries.Count > 0)
                {
                    BaselineBuilder.Apply(entries, decoder);
                    baselines += entries.Count;
                    continue;
                }

                started |= snapshot.IsFullSnapshot;
                if (!started)
                {
                    continue;
                }

                IReadOnlyList<DecodedEntity> decoded =
                    decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
                withBaselines.Apply(decoded, decoder.Baseline);
                without.Apply(decoded);
            }

            // Asserted, not assumed: a demo whose baselines never arrived would otherwise make
            // the comparison below vacuous rather than failing. RISKS B20.
            baselines.ShouldBeGreaterThan(0, name);

            int with = Total(withBaselines);
            int bare = Total(without);

            bare.ShouldBeGreaterThan(0, name);
            with.ShouldBeGreaterThan(bare * 5, name);
        }
    }

    private static int Total(EntityTracker tracker) =>
        tracker.ActiveEntities
            .Select(tracker.State)
            .Where(state => state is not null)
            .Sum(state => state!.Count);

    /// <summary>
    /// Entity snapshots and <c>instancebaseline</c> entries, interleaved in stream order.
    /// </summary>
    /// <remarks>
    /// Interleaved rather than collected separately because baselines are rewritten *during* a
    /// match - up to 101 times in one corpus demo - so an entity entering at tick 5,000 must be
    /// seeded from the baseline as it stood then, not from the final one.
    /// </remarks>
    private static IEnumerable<(PacketEntitiesMessage Snapshot, IReadOnlyList<StringTableEntry> Baselines)>
        EntityStream(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new()
        {
            NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol,
        };

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                switch (message)
                {
                    case CreateStringTableMessage { Name: BaselineBuilder.TableName } create:
                        yield return (null!, create.Entries);
                        break;

                    case UpdateStringTableMessage update
                        when state.StringTableName(update.TableId) == BaselineBuilder.TableName:
                        yield return (null!, update.Entries);
                        break;

                    case PacketEntitiesMessage snapshot:
                        yield return (snapshot, []);
                        break;

                    default:
                        break;
                }
            }
        }
    }

}
