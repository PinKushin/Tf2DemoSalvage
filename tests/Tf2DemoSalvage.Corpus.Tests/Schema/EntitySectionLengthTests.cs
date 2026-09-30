using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The entity section must encode to exactly the bits it decoded from.
/// </summary>
/// <remarks>
/// **A gate, and a narrow one on purpose.** Every other round-trip check compares against the
/// body's stated length, which conflates three things: the entities, the removal list, and
/// whatever the sender left after them. This compares the two halves of our own codec on the
/// entity section alone — decoder consumed against encoder produced — so a regression there
/// cannot hide behind a question about the removal list.
///
/// It is what localised the remaining mismatch: 61,701 of 61,701 snapshots agree exactly here,
/// which is how the last discrepancy was pinned to the removal list rather than to entities or
/// properties (RISKS B25).
/// </remarks>
public sealed class EntitySectionLengthTests
{
    [Test]
    public void EntitySection_Encode_ReproducesWhatItDecodedFrom()
    {
        Dictionary<int, int> deltas = [];
        long snapshots = 0;
        List<string> examples = [];
        List<string> undecodable = [];

        // **The production walk and schema** (B443): see DemoCorpus.EntitySnapshots for what the walk
        // this replaced skipped, and why that misaligned the packets it did read.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoSchema schema = Corpus.Schema(path);
            EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

            foreach ((DemoCommand command, PacketEntitiesMessage snapshot, _) in
                Corpus.EntitySnapshots(path, 4000))
            {
                IReadOnlyList<DecodedEntity> entities;
                try
                {
                    entities = decoder.Decode(snapshot.Body.Span, snapshot, snapshot.LengthBits);
                }
                catch (Exception error)
                    when (error is InvalidDataException or EndOfStreamException)
                {
                    // **A failure, never a skip** (B443): a snapshot that does not decode is one this
                    // test never compared, and dropping it silently is how a misaligned walk passed.
                    undecodable.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(path)} tick {command.Tick}: {error.Message}"));
                    continue;
                }

                int consumed = decoder.EntitySectionBits;
                decoder.EncodeEntities(
                    entities, [], isDelta: false, lengthBits: 0, out int producedWithFlag);

                // The encode above appends no removal list, but EncodeEntities always writes
                // the property terminator per entity - so what it produced IS the entity
                // section and nothing else.
                int difference = producedWithFlag - consumed;
                deltas[difference] = deltas.GetValueOrDefault(difference) + 1;
                snapshots++;

                if (difference != 0 && examples.Count < 12)
                {
                    // The file name matters more than it looks: which recording produced a residue
                    // is what tells the writer giving up mid-message from a genuine encoder bug.
                    examples.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Path.GetFileName(path)}: consumed {consumed}, " +
                        $"produced {producedWithFlag}, {entities.Count} entities, last is " +
                        $"{entities[^1].UpdateType} with {entities[^1].Properties.Count} props, " +
                        $"delta={snapshot.IsDelta}, stated={snapshot.LengthBits}"));
                }
            }
        }

        foreach (string failure in undecodable.Take(12))
        {
            TestContext.Out.WriteLine("    does not decode - " + failure);
        }

        TestContext.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture, $"{snapshots:N0} snapshots compared"));

        TestContext.Out.WriteLine("produced minus consumed, for the entity section alone:");
        foreach ((int difference, int count) in deltas.OrderByDescending(entry => entry.Value))
        {
            TestContext.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    {count,8:N0}  {difference:+#;-#;0} bits"));
        }

        foreach (string example in examples)
        {
            TestContext.Out.WriteLine("    " + example);
        }

        // A corpus that stopped being read would otherwise pass without comparing anything.
        snapshots.ShouldBeGreaterThan(1000);
        undecodable.ShouldBeEmpty("every snapshot must decode; one that does not was never compared (B443)");
        deltas.Keys.Where(difference => difference != 0).ShouldBeEmpty(
            "the encoder must write exactly what the decoder consumed");
    }
}
