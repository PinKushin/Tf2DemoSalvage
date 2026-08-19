using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Decodes <c>svc_TempEntities</c> bodies out of real demos.
/// </summary>
/// <remarks>
/// **The layout came from another parser, so a fixture cannot validate it** — a fixture built from
/// the same reading proves only that the reading is self-consistent, which is the trap recorded in
/// <c>docs/memory/differential-beats-fixtures.md</c>. Real bodies are the test.
///
/// Two things make that test sharp. The message states its own body length, so a correct layout
/// lands on it and a wrong one overruns. And every effect's class id must address a real server
/// class whose flattened property list contains every property index the body names — a wrong
/// layout produces indices that miss.
/// </remarks>
public sealed class CorpusTempEntityTests
{
    private sealed record Totals(int Bodies, int EffectCount, int ValueCount, int Failed);

    [Test]
    public void TempEntities_DecodeAgainstEveryDemoThatCarriesThem()
    {
        int demosWithEffects = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);
            EntityDecoder decoder =
                new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            Totals totals = Decode(path, decoder);
            if (totals.Bodies == 0)
            {
                continue;
            }

            demosWithEffects++;
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)}: {totals.Bodies} messages, {totals.EffectCount} effects, " +
                $"{totals.ValueCount} properties, {totals.Failed} failed");

            // Not "most of them". A layout that is right decodes all of them, and one that is
            // wrong for this era fails loudly rather than at a rate.
            totals.Failed.ShouldBe(0, Path.GetFileName(path));
            totals.EffectCount.ShouldBeGreaterThan(0, Path.GetFileName(path));
        }

        demosWithEffects.ShouldBeGreaterThan(0, "no demo carried svc_TempEntities");
    }

    [Test]
    public void EveryTempEntityProperty_BelongsToTheClassItWasReadFor()
    {
        // The same check the entity tests make, and the reason it is strong: class names come from
        // dem_datatables and effect bodies from the bit stream, by independent paths. A wrong
        // layout yields property indices that address the wrong class or none at all.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);
            EntityDecoder decoder =
                new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
            string name = Path.GetFileName(path);

            foreach (DecodedTempEntity effect in Effects(path, decoder).Take(500))
            {
                schema.ServerClasses.ShouldContain(c => c.Id == effect.ClassId, name);

                IReadOnlyList<FlatProperty> flat = SchemaFlattener.Flatten(
                    schema, schema.ServerClasses.First(c => c.Id == effect.ClassId));

                foreach (DecodedProperty property in effect.Properties)
                {
                    property.Index.ShouldBeInRange(0, flat.Count - 1, name);
                    property.Definition.Property.Name
                        .ShouldBe(flat[property.Index].Property.Name, name);
                }
            }
        }
    }

    [Test]
    public void TempEntityEffects_TheCorpus_AreReported()
    {
        // What a viewer would draw. Explosions, tracers and impacts all arrive as temp entities,
        // so this is the first look the project has had at any of them.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);
            EntityDecoder decoder =
                new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            Dictionary<string, int> byClass = new(StringComparer.Ordinal);
            foreach (DecodedTempEntity effect in Effects(path, decoder).Take(4000))
            {
                string className = schema.ServerClasses
                    .FirstOrDefault(c => c.Id == effect.ClassId).ClassName ?? "?";
                byClass[className] = byClass.TryGetValue(className, out int seen) ? seen + 1 : 1;
            }

            if (byClass.Count == 0)
            {
                continue;
            }

            TestContext.Out.WriteLine($"{Path.GetFileName(path)}:");
            foreach ((string className, int count) in byClass.OrderByDescending(e => e.Value).Take(8))
            {
                TestContext.Out.WriteLine($"    {count,6}  {className}");
            }
        }

        Corpus.FilesWithSchema().ShouldNotBeEmpty();
    }

    [Test]
    public void PlayerAnimEvents_TheCorpus_AreReported()
    {
        // **The trigger for the gesture layer (B112 slice 3b), measured before any enum is ported.**
        // m_iEvent is a PlayerAnimEvent_t ordinal, and inserting an enum member shifts every value
        // after it — so what a value MEANS is era-dependent and cannot be read off one build's SDK.
        // This prints the raw distribution per era so the mapping can be established from data rather
        // than assumed. The owner's era specimens are SOLO recordings, so the events present are
        // whatever one player does alone: fire, reload, jump.
        int demosWithAnimEvents = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);
            EntityDecoder decoder =
                new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            Dictionary<(string Event, string Data), int> byEvent = new();
            foreach (DecodedTempEntity effect in Effects(path, decoder).Take(8000))
            {
                string className = schema.ServerClasses
                    .FirstOrDefault(c => c.Id == effect.ClassId).ClassName ?? "?";
                if (className != "CTEPlayerAnimEvent")
                {
                    continue;
                }

                // **Absent is reported as absent, never as a fabricated -1.** A temp entity carries
                // only the properties that differ from the class baseline, so an omitted m_iEvent
                // means "equals baseline", not "-1" — and folding those together with a real value
                // is exactly the sentinel trap in docs/memory/sentinels-conflate-unknown-with-answer.
                (string, string) key = (
                    PropertyLabel(effect, "m_iEvent"), PropertyLabel(effect, "m_nData"));
                byEvent[key] = byEvent.TryGetValue(key, out int seen) ? seen + 1 : 1;
            }

            if (byEvent.Count == 0)
            {
                continue;
            }

            demosWithAnimEvents++;
            TestContext.Out.WriteLine(
                $"{Path.GetFileName(path)} (protocol {Corpus.ProtocolOf(path)}):");
            foreach (((string eventValue, string dataValue), int count) in
                byEvent.OrderByDescending(e => e.Value))
            {
                TestContext.Out.WriteLine(
                    $"    m_iEvent={eventValue,7}  m_nData={dataValue,7}  x{count}");
            }
        }

        // No assertion on which events appear — that is the very thing being measured. The control
        // is only that the corpus was walked at all.
        Corpus.FilesWithSchema().ShouldNotBeEmpty();
        TestContext.Out.WriteLine($"demos carrying CTEPlayerAnimEvent: {demosWithAnimEvents}");
    }

    private static string PropertyLabel(DecodedTempEntity effect, string name)
    {
        foreach (DecodedProperty property in effect.Properties)
        {
            if (property.Definition.Property.Name == name)
            {
                return property.Value.AsInt.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return "absent";
    }

    private static Totals Decode(string path, EntityDecoder decoder)
    {
        int messages = 0;
        int effects = 0;
        int properties = 0;
        int failed = 0;

        foreach (TempEntitiesMessage message in Messages(path))
        {
            messages++;
            try
            {
                IReadOnlyList<DecodedTempEntity> decoded = decoder.DecodeTempEntities(
                    message.Body.Span, message.Count, message.BodyBits);
                effects += decoded.Count;
                properties += decoded.Sum(e => e.Properties.Count);
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            {
                failed++;
            }
        }

        return new Totals(messages, effects, properties, failed);
    }

    private static IEnumerable<DecodedTempEntity> Effects(string path, EntityDecoder decoder)
    {
        foreach (TempEntitiesMessage message in Messages(path))
        {
            IReadOnlyList<DecodedTempEntity> decoded;
            try
            {
                decoded = decoder.DecodeTempEntities(
                    message.Body.Span, message.Count, message.BodyBits);
            }
            catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
            {
                continue;
            }

            foreach (DecodedTempEntity effect in decoded)
            {
                yield return effect;
            }
        }
    }

    private static IEnumerable<TempEntitiesMessage> Messages(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        NetDecodeState state = new() { NetworkProtocol = Corpus.ProtocolOf(path) };

        foreach (DemoCommand command in
            DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(2000))
        {
            if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet))
            {
                continue;
            }

            foreach (INetMessage message in
                NetMessageReader.Read(command.Payload.Span, state).Messages)
            {
                if (message is TempEntitiesMessage temp)
                {
                    yield return temp;
                }
            }
        }
    }
}
