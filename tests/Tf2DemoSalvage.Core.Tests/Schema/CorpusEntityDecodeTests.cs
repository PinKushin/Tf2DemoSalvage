using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Xunit.Abstractions;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Entity decoding run against real demos, where a hand-built fixture cannot help.
/// </summary>
/// <remarks>
/// The fixtures in <see cref="EntityDecoderTests"/> prove the decoder matches the SDK's write
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
        string pov = Corpus.Files()
            .First(f => Path.GetFileName(f).Contains("pov", StringComparison.Ordinal));

        DecodeRun run = DecodeContinuously(pov, 5000);

        run.Stopped.ShouldBeNull(Path.GetFileName(pov));
        run.Decoded.ShouldBe(5000);
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

    private static DemoSchema Schema(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type == DemoCommandType.DataTables)
            {
                return SendTableParser.Parse(command.Payload.Span);
            }
        }

        throw new InvalidDataException($"{Path.GetFileName(path)} has no dem_datatables command.");
    }

    private static IEnumerable<PacketEntitiesMessage> Snapshots(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new();

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
}
