using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// A two-property class and the instance baselines that go with it.
/// </summary>
/// <remarks>
/// **Written rather than found, because the corpus cannot express this case.** Applying baselines
/// changes no property count on any demo available here — real TF2 entities resend most of what
/// their baseline says within a second or two — so a demo cannot distinguish a reader that merges
/// baselines from one that ignores them. A two-property class whose entity omits one of them can,
/// and that is the whole reason this fixture exists.
///
/// Shared by the baseline decode tests and the accumulator tests, which ask different questions of
/// the same setup: whether the baseline is read at all, and whether the state table applies it.
///
/// Not to be confused with <c>Tf2DemoSalvage.Core.Tests.SyntheticSchema</c>, which writes a whole
/// <c>dem_datatables</c> payload so a synthetic demo can carry a schema. This one builds a decoder
/// directly and never goes near the wire format.
/// </remarks>
internal static class BaselineFixture
{
    /// <summary>The only class this schema declares.</summary>
    internal const int ClassId = 0;

    /// <summary>The send table its properties belong to, which is half of every state key.</summary>
    internal const string Table = "DT_Test";

    /// <summary>A decoder over the schema, holding no baselines.</summary>
    internal static EntityDecoder Decoder()
    {
        DemoSchema schema = new(
            [
                new SendTable(Table, NeedsDecoder: true,
                [
                    new SendProperty(SendPropType.Int, "m_iHealth", 0, "", 0f, 0f, 11, 0),
                    new SendProperty(SendPropType.Int, "m_iAmmo", 0, "", 0f, 0f, 11, 0),
                ]),
            ],
            [new ServerClass(ClassId, "CTest", Table)]);

        return new EntityDecoder(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));
    }

    /// <summary>A decoder whose class carries the given baseline properties.</summary>
    internal static EntityDecoder WithBaseline(params (string Name, int Value)[] properties)
    {
        EntityDecoder decoder = Decoder();

        BaselineBuilder.Apply(
            [
                new StringTableEntry(
                    0, ClassId.ToString(CultureInfo.InvariantCulture), Payload(properties)),
            ],
            decoder);

        return decoder;
    }

    /// <summary>The encoded property block an instance baseline carries.</summary>
    /// <remarks>
    /// A baseline is encoded exactly like an entity delta's property list — no separate codec —
    /// which is what makes this a one-line fixture rather than a second encoder.
    /// </remarks>
    internal static byte[] Payload(params (string Name, int Value)[] properties) =>
        EntityDecoder.EncodeProperties(Properties(properties));

    /// <summary>Decoded properties for this class, in flattened index order.</summary>
    internal static List<DecodedProperty> Properties(params (string Name, int Value)[] properties)
    {
        IReadOnlyList<FlatProperty> flat = Decoder().FlattenedFor(ClassId);

        List<DecodedProperty> decoded = [];

        foreach ((string name, int value) in properties)
        {
            int index = flat.Select((entry, i) => (entry, i))
                .First(pair => pair.entry.Property.Name == name).i;

            decoded.Add(new DecodedProperty(index, flat[index], PropertyValue.FromInt(value)));
        }

        decoded.Sort((left, right) => left.Index.CompareTo(right.Index));

        return decoded;
    }
}
