using System;
using System.IO;

using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// A <c>dem_datatables</c> cut at 2^16 bytes, completed from a build's known schema (B24).
/// </summary>
/// <remarks>
/// TF2's launch build (3258) wrote its SourceTV schema into a 65,536-byte buffer, and the schema is
/// 85,063. The first 65,535 bytes of the cut payload equal the whole one's; the 65,536th is the byte
/// the buffer ended inside. So a payload of exactly that length whose first 65,535 bytes match is
/// that schema, and anything else is not — those are the three cases here, on a synthetic schema so
/// each has ground truth.
/// </remarks>
public sealed class KnownSchemaTests
{
    /// <summary>A stand-in for a known full schema, longer than the cut.</summary>
    private static byte[] Known()
    {
        byte[] known = new byte[70_000];
        new Random(3258).NextBytes(known);
        return known;
    }

    /// <summary>The known schema cut the way the writer cut it: 2^16 bytes, the last one partial.</summary>
    private static byte[] CutFrom(byte[] known, int length = KnownSchema.CutLength)
    {
        byte[] cut = known.AsSpan(0, length).ToArray();
        cut[^1] ^= 0x20;
        return cut;
    }

    [Test]
    public void Complete_ACutPayloadMatchingTheKnownSchema_IsTheKnownSchema()
    {
        byte[] known = Known();

        KnownSchema.Complete(CutFrom(known), known).ToArray().ShouldBe(known);
    }

    [Test]
    public void Complete_ACutLengthPayloadDifferingInsideThePrefix_IsThePayloadItself()
    {
        byte[] known = Known();
        byte[] other = CutFrom(known);
        other[100] ^= 0x01;

        KnownSchema.Complete(other, known).ToArray().ShouldBe(other);
    }

    [TestCase(KnownSchema.CutLength - 1)]
    [TestCase(KnownSchema.CutLength + 1)]
    public void Complete_APayloadOfAnotherLength_IsThePayloadItself(int length)
    {
        byte[] known = Known();
        byte[] payload = known.AsSpan(0, length).ToArray();

        KnownSchema.Complete(payload, known).ToArray().ShouldBe(payload);
    }

    [Test]
    public void Parse_Build3258SchemaCutAtSixtyFourKilobytes_ReadsAsTheWholeSchema()
    {
        // The cut exactly as the SourceTV writer left it: 2^16 bytes, the last one 0E where the
        // whole schema has 2E (measured on tf2-2007-build3258-stv-cp_granary.dem).
        byte[] cut = KnownSchema.Build3258.Span[..KnownSchema.CutLength].ToArray();
        cut[^1] = 0x0E;
        DemoSchema whole = SendTableParser.Parse(KnownSchema.Build3258.Span, 11);

        DemoSchema read = SendTableParser.Parse(cut, 11);

        read.ServerClasses.Count.ShouldBe(whole.ServerClasses.Count);
        read.Tables.Count.ShouldBe(whole.Tables.Count);
    }

    [Test]
    public void Parse_ACutLengthPayloadOfNoKnownSchema_IsStillRefused()
    {
        byte[] cut = KnownSchema.Build3258.Span[..KnownSchema.CutLength].ToArray();
        cut[100] ^= 0x01;

        Should.Throw<InvalidDataException>(() => SendTableParser.Parse(cut, 11));
    }

    [Test]
    public void Build3258_TheShippedSchema_IsTheBuildsWholeDataTables()
    {
        // The POV demo tools/corpus/demos/tf2-2007-build3258-pov-cp_granary.dem carries it; SHA-256
        // measured on that payload when it was extracted.
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(KnownSchema.Build3258.Span))
            .ShouldBe("ea020a813d6ab230800b6a6410b833cef3daf5538b33c9dca8da6b63cf87e637");
    }
}
