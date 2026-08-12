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
    public void TheEntitySectionEncodesToExactlyWhatItDecodedFrom()
    {
        Dictionary<int, int> deltas = [];
        long snapshots = 0;
        List<string> examples = [];

        foreach (string path in Corpus.Files())
        {
            byte[] bytes = File.ReadAllBytes(path);
            ushort protocol = Corpus.ProtocolOf(path);
            NetDecodeState state = new() { NetworkProtocol = protocol };
            EntityDecoder? decoder = null;

            foreach (DemoCommand command in
                DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(4000))
            {
                if (command.Type == DemoCommandType.DataTables)
                {
                    try
                    {
                        DemoSchema schema = SendTableParser.Parse(command.Payload.Span, protocol);
                        decoder = new EntityDecoder(
                            schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
                    }
                    catch (InvalidDataException)
                    {
                        decoder = null;
                    }

                    continue;
                }

                if (command.Type is not (DemoCommandType.Signon or DemoCommandType.Packet) ||
                    decoder is null)
                {
                    continue;
                }

                foreach (INetMessage message in
                    NetMessageReader.Read(command.Payload.Span, state).Messages)
                {
                    if (message is not PacketEntitiesMessage snapshot)
                    {
                        continue;
                    }

                    IReadOnlyList<DecodedEntity> entities;
                    try
                    {
                        entities = decoder.Decode(
                            snapshot.Body.Span, snapshot, snapshot.LengthBits);
                    }
                    catch (Exception error)
                        when (error is InvalidDataException or EndOfStreamException)
                    {
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
                        // The file name matters more than it looks. A residue this small - nine
                        // snapshots in a hundred and eleven thousand - is only tractable if you
                        // can tell which recording produced it, and the answer turned out to be
                        // the discriminator: whether the shortfall is the writer giving up
                        // mid-message or a genuine encoder bug depends on the demo it came from.
                        examples.Add(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{Path.GetFileName(path)}: consumed {consumed}, " +
                            $"produced {producedWithFlag}, {entities.Count} entities, last is " +
                            $"{entities[^1].UpdateType} with {entities[^1].Properties.Count} props, " +
                            $"delta={snapshot.IsDelta}, stated={snapshot.LengthBits}"));
                    }
                }
            }
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
        deltas.Keys.Where(difference => difference != 0).ShouldBeEmpty(
            "the encoder must write exactly what the decoder consumed");
    }
}
