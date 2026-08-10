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
public sealed class CorpusEntityDecodeTests(ITestOutputHelper output)
{
    /// <summary><c>MAX_EDICTS</c>.</summary>
    private const int EntityLimit = 2048;

    /// <summary>
    /// Upper bound on snapshots read by a test that stops as soon as its claim is demonstrated.
    /// Generous on purpose: it is a runaway guard, not a tuned window.
    /// </summary>
    private const int SnapshotCap = 4000;

    /// <summary>Half Source's world extent, in units, per axis.</summary>
    private const float WorldHalfExtent = 16384f;

    [Fact]
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
            entities.Sum(e => e.Properties.Count).ShouldBeGreaterThan(2000, name);
        }
    }

    [Fact]
    public void EntityIndices_AscendAndStayInsideTheEntityLimit()
    {
        foreach (string path in SourceTvDemos())
        {
            (IReadOnlyList<DecodedEntity> entities, _) = FirstFull(path);
            string name = Path.GetFileName(path);

            int previous = -1;
            foreach (DecodedEntity entity in entities)
            {
                entity.EntityIndex.ShouldBeGreaterThan(previous, name);
                entity.EntityIndex.ShouldBeLessThan(EntityLimit, name);
                previous = entity.EntityIndex;
            }
        }
    }

    [Fact]
    public void EveryPropertyBelongsToTheClassItWasReadFor()
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

    [Fact]
    public void PlayerPositions_LandInsideTheWorldBounds()
    {
        // m_vecOrigin is SPROP_COORD_MP. A wrong coordinate decoder yields a plausible number
        // rather than an error, so the check is that positions fall inside Source's world.
        foreach (string path in SourceTvDemos())
        {
            string name = Path.GetFileName(path);
            List<(float X, float Y, float Z)> origins = Origins(FirstFull(path).Entities);

            origins.Count.ShouldBeGreaterThan(50, name);

            foreach ((float x, float y, float z) in origins)
            {
                float.IsFinite(x).ShouldBeTrue(name);
                MathF.Abs(x).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
                MathF.Abs(y).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
                MathF.Abs(z).ShouldBeLessThanOrEqualTo(WorldHalfExtent, name);
            }
        }
    }

    [Fact]
    public void PlayerPositions_AreSpreadAcrossTheMap()
    {
        // Without this, a decoder returning a constant passes the bounds check above.
        foreach (string path in SourceTvDemos())
        {
            List<(float X, float Y, float Z)> origins = Origins(FirstFull(path).Entities);
            string name = Path.GetFileName(path);

            origins.Select(o => o.X).Distinct().Count().ShouldBeGreaterThan(10, name);
            (origins.Max(o => o.X) - origins.Min(o => o.X)).ShouldBeGreaterThan(100f, name);
        }
    }

    [Fact]
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
            output.WriteLine($"{Path.GetFileName(path)}: {run.Decoded} snapshots, " +
                             $"stopped: {run.Stopped ?? "not at all"}");
            run.Stopped.ShouldBeNull(Path.GetFileName(path));
            run.Decoded.ShouldBe(500);
        }
    }

    private sealed record DecodeRun(int Decoded, string? Stopped);

    private static DecodeRun DecodeContinuously(string path, int limit)
    {
        DemoSchema schema = Schema(path);
        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
        bool started = false;
        int decoded = 0;

        foreach (PacketEntitiesMessage message in Snapshots(path).Take(limit))
        {
            started |= message.IsFullSnapshot;
            if (!started)
            {
                continue;
            }

            try
            {
                decoder.Decode(message.Body.Span, message, message.LengthBits);
                decoded++;
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            {
                // Running off the end of the body is the same desynchronisation reported a
                // different way - the reader asks for bits the message does not have.
                return new DecodeRun(decoded, error.Message);
            }
        }

        return new DecodeRun(decoded, null);
    }

    [Fact]
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

        foreach (string pov in povDemos)
        {
            DecodeRun run = DecodeContinuously(pov, SnapshotCap);

            run.Stopped.ShouldBeNull(Path.GetFileName(pov));

            // A floor every POV recording in the corpus clears, including the shortest. It guards
            // against a run that stops after two snapshots and reports no error, which is what
            // `Stopped is null` alone would accept.
            run.Decoded.ShouldBeGreaterThan(2000, Path.GetFileName(pov));
        }
    }

    [Fact]
    public void ReportWhatTheEntitiesSay()
    {
        foreach (string path in SourceTvDemos())
        {
            (IReadOnlyList<DecodedEntity> entities, PacketEntitiesMessage header) = FirstFull(path);

            output.WriteLine(
                $"{Path.GetFileName(path)}: opening snapshot {entities.Count} entities, " +
                $"{entities.Sum(e => e.Properties.Count)} values, {header.LengthBits} bits");

            output.WriteLine("  classes: " + string.Join(", ", entities
                .GroupBy(e => e.ClassId)
                .OrderByDescending(g => g.Count())
                .Take(4)
                .Select(g => $"{g.Key} x{g.Count()}")));

            List<(float X, float Y, float Z)> origins = Origins(entities);
            if (origins.Count > 0)
            {
                output.WriteLine(
                    $"  {origins.Count} origins, x {origins.Min(o => o.X):F0}..{origins.Max(o => o.X):F0}, " +
                    $"z {origins.Min(o => o.Z):F0}..{origins.Max(o => o.Z):F0}");
            }

            output.WriteLine(string.Empty);
        }

        SourceTvDemos().ShouldNotBeEmpty();
    }

    private static List<(float X, float Y, float Z)> Origins(IReadOnlyList<DecodedEntity> entities) =>
    [
        .. entities
            .SelectMany(e => e.Properties)
            .Where(p => p.Definition.Property.Name == "m_vecOrigin" &&
                        p.Value.Kind == PropertyValueKind.Vector)
            .Select(p => p.Value.AsVector),
    ];

    /// <summary>Demos that open with a full snapshot, which is every SourceTV recording.</summary>
    private static string[] SourceTvDemos() =>
    [
        .. Corpus.Files()
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

    [Fact]
    public void Tracker_HoldsMorePropertiesThanAnySingleUpdateCarried()
    {
        // The claim merging makes, stated as a comparison so no threshold has to be invented:
        // some entity must end up knowing more than the largest single update to *that entity*
        // contained. A tracker that replaced state instead of merging would sit exactly at that
        // maximum and never above it.
        //
        // The subject is chosen from the data rather than assumed. An earlier version of this
        // test pinned entity 1, which is the recording player in a POV demo and a worldspawn-ish
        // slot with two properties in a SourceTV one - so it measured merging in one file and
        // nothing at all in another.
        //
        // It stops as soon as the claim is demonstrated, and the cap is generous rather than
        // tuned. A fixed 400-snapshot window was enough for every demo in the corpus until a
        // quiet one arrived: two minutes on a listen server with a single player, where nothing
        // enters or leaves for a long stretch after the full snapshot, and every entity sat at
        // exactly its largest single update. It accumulates like the others, just later. Raising
        // the window for everyone would have cost about 18% of this project's runtime, which
        // matters because the mutation run re-executes it per mutant; breaking early costs the
        // busy demos nothing and lets the quiet one read as far as it needs.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            DemoSchema schema = Schema(path);
            EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
            EntityTracker tracker = new();
            Dictionary<int, int> largestUpdate = [];
            bool started = false;
            bool anyAccumulated = false;

            foreach (PacketEntitiesMessage message in Snapshots(path).Take(SnapshotCap))
            {
                started |= message.IsFullSnapshot;
                if (!started)
                {
                    continue;
                }

                IReadOnlyList<DecodedEntity> entities =
                    decoder.Decode(message.Body.Span, message, message.LengthBits);

                foreach (DecodedEntity entity in entities)
                {
                    largestUpdate[entity.EntityIndex] = Math.Max(
                        largestUpdate.GetValueOrDefault(entity.EntityIndex),
                        entity.Properties.Count);
                }

                tracker.Apply(entities);

                anyAccumulated = tracker.ActiveEntities.Any(index =>
                    tracker.State(index) is { } state &&
                    state.Count > largestUpdate.GetValueOrDefault(index));

                if (anyAccumulated)
                {
                    break;
                }
            }

            tracker.ActiveEntities.ShouldNotBeEmpty(name);

            // On failure, say how far every entity got. "No entity accumulated" and "the decode
            // produced nothing to accumulate" are different problems and the counts separate them.
            string held = string.Join(", ", tracker.ActiveEntities
                .Select(index => (index,
                                  state: tracker.State(index)?.Count ?? 0,
                                  max: largestUpdate.GetValueOrDefault(index)))
                .OrderByDescending(entry => entry.state - entry.max)
                .Take(6)
                .Select(entry => $"e{entry.index} held={entry.state} maxUpdate={entry.max}"));

            anyAccumulated.ShouldBeTrue(
                $"{name}: {tracker.ActiveEntities.Count} entities | {held}");
        }
    }

    [Fact]
    public void Tracker_HoldsPlayerPositionsInsideTheMap()
    {
        // Accumulated state has to be usable, not merely present - this is the query a 2D
        // viewer makes. Source's coordinate space is bounded at +/-16384, so anything outside
        // it is decoded garbage rather than a place someone stood.
        //
        // Both encodings are accepted because the era difference is real and visible in the
        // corpus: the 2009 demo sends m_vecOrigin as a single Vector, while modern demos send
        // it as a VectorXY plus a separate m_vecOrigin[2] float. That is DPT_VectorXY (B18)
        // showing up in the data rather than in a header.
        foreach (string path in Corpus.Files())
        {
            string name = Path.GetFileName(path);
            DemoSchema schema = Schema(path);
            EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
            EntityTracker tracker = new();
            bool started = false;

            foreach (PacketEntitiesMessage message in Snapshots(path).Take(400))
            {
                started |= message.IsFullSnapshot;
                if (started)
                {
                    tracker.Apply(decoder.Decode(message.Body.Span, message, message.LengthBits));
                }
            }

            List<float> coordinates = [];
            foreach (int index in tracker.ActiveEntities)
            {
                if (tracker.State(index) is not { } state)
                {
                    continue;
                }

                foreach ((string key, PropertyValue value) in state)
                {
                    if (!key.EndsWith(".m_vecOrigin", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (value.Kind == PropertyValueKind.Vector)
                    {
                        (float x, float y, float z) = value.AsVector;
                        coordinates.AddRange([x, y, z]);
                    }
                    else if (value.Kind == PropertyValueKind.VectorXY)
                    {
                        (float x, float y) = value.AsVectorXY;
                        coordinates.AddRange([x, y]);
                    }
                }
            }

            coordinates.ShouldNotBeEmpty(name);
            coordinates.ShouldAllBe(c => Math.Abs(c) < 16384f, name);
        }
    }

    [Fact]
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
        foreach (string path in Corpus.Files())
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
