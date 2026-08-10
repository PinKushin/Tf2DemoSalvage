using System.Collections.Generic;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Tests.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Routing <c>instancebaseline</c> table entries to the decoder.
/// </summary>
/// <remarks>
/// **The class id comes from the entry's text, not its index** — and that is the opposite of
/// <c>userinfo</c>, where RISKS B22 established that the entity index *is* the entry index.
/// Verified across the corpus: for `instancebaseline` the two differ on essentially every entry
/// (index 0 carries class 353, index 1 carries class 318). Reusing B22's rule here would file
/// every baseline under the wrong class, which then decodes real values into the wrong fields —
/// silent, and plausible.
/// </remarks>
public sealed class BaselineBuilderTests
{
    /// <summary>SPROP_UNSIGNED.</summary>
    private const int Unsigned = 1;

    /// <summary>Six properties, matching the schema the decoder tests use.</summary>
    private static DemoSchema Schema()
    {
        List<SendProperty> properties =
        [
            new(SendPropType.Int, "m_iHealth", Unsigned, string.Empty, 0f, 0f, 10, 0),
            new(SendPropType.Int, "m_iTeamNum", Unsigned, string.Empty, 0f, 0f, 3, 0),
        ];

        return new DemoSchema(
            [new SendTable("DT_Test", true, properties)],
            [new ServerClass(0, "CTest", "DT_Test"), new ServerClass(7, "COther", "DT_Test")]);
    }

    /// <summary>A baseline payload setting m_iHealth.</summary>
    private static byte[] HealthBaseline(uint health)
    {
        BitWriter writer = new();
        writer.Write(1, 1).UBitVar(0).Write(health, 10).Write(0, 1);
        return writer.Build();
    }

    [Fact]
    public void ClassId_ComesFromTheEntryText_NotItsIndex()
    {
        // The whole point. Entry at index 0 declares class 7, so a builder reading the index
        // would file this baseline under class 0 - a real baseline attached to the wrong class.
        EntityDecoder decoder = new(Schema(), 2);

        BaselineBuilder.Apply(
            [new StringTableEntry(0, "7", HealthBaseline(125))], decoder);

        decoder.Baseline(7).ShouldNotBeNull()[0].Value.AsInt.ShouldBe(125);

        // The control: nothing was filed under the index. Without this, "read the text" and
        // "wrote it to both" are indistinguishable.
        decoder.Baseline(0).ShouldBeNull();
    }

    [Fact]
    public void EntriesWithoutUsableText_AreSkipped()
    {
        // An entry whose text is missing or not a class id cannot be placed. Skipping is the
        // honest outcome; guessing an index would attach a baseline to an arbitrary class.
        EntityDecoder decoder = new(Schema(), 2);

        BaselineBuilder.Apply(
        [
            new StringTableEntry(0, null, HealthBaseline(1)),
            new StringTableEntry(1, "not-a-number", HealthBaseline(2)),
        ],
            decoder);

        decoder.Baseline(0).ShouldBeNull();
        decoder.Baseline(7).ShouldBeNull();
    }

    [Fact]
    public void EntriesWithoutUserData_AreSkipped()
    {
        // A cleared entry carries no payload. Recording an empty baseline would mean an entity
        // of that class gets seeded with nothing while appearing to have been seeded.
        EntityDecoder decoder = new(Schema(), 2);

        BaselineBuilder.Apply([new StringTableEntry(0, "7", [])], decoder);

        decoder.Baseline(7).ShouldBeNull();
    }

    [Fact]
    public void ALaterEntryForTheSameClass_Replaces()
    {
        // Baselines are rewritten mid-match through svc_UpdateStringTable - up to 101 times in
        // one corpus demo - so the last one written must win.
        EntityDecoder decoder = new(Schema(), 2);

        BaselineBuilder.Apply([new StringTableEntry(0, "7", HealthBaseline(125))], decoder);
        BaselineBuilder.Apply([new StringTableEntry(0, "7", HealthBaseline(42))], decoder);

        decoder.Baseline(7).ShouldNotBeNull()[0].Value.AsInt.ShouldBe(42);
    }
}
