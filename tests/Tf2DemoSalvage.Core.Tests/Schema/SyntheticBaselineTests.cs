using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// Instance baselines: the properties an entering entity is a delta against.
/// </summary>
/// <remarks>
/// **An entity entering the visible set is not sent in full.** It is a delta against its class's
/// instance baseline, so its own update omits everything the baseline already said — which means a
/// reader that ignores baselines sees an entity missing most of what it knows, and sees it as an
/// entity that simply did not send those properties.
///
/// The corpus test this replaces measured how much of a real entity came from its baseline and
/// reported a percentage. That is a fact about how TF2 populates baselines; what it cannot do is
/// state which property came from where, because on found data nobody knows.
///
/// **Applying baselines changed no count on any demo in the corpus, era or modern**, which is why
/// the mechanism went unasserted for so long — it is invisible on the files available. "It changed
/// nothing measurable here" is not evidence that it never will, and a written baseline makes the
/// difference observable: the entity below omits a property the baseline supplies, so a reader
/// without baselines gets nothing for it and a reader with them gets the value.
/// </remarks>
public sealed class SyntheticBaselineTests
{
    private const int ClassId = BaselineFixture.ClassId;

    [Test]
    public void Baseline_APropertyTheEntityDidNotSend_ComesFromItsClassBaseline()
    {
        // The whole mechanism in one assertion. m_iHealth is in the baseline and absent from the
        // entity's own update, so it can only arrive one way.
        EntityDecoder decoder = WithBaseline(("m_iHealth", 125), ("m_iAmmo", 32));

        decoder.Baseline(ClassId).ShouldNotBeNull()
            .Select(property => (property.Definition.Property.Name, property.Value.AsInt))
            .ShouldBe([("m_iHealth", 125L), ("m_iAmmo", 32L)]);
    }

    [Test]
    public void Baseline_AClassWithNone_IsNullRatherThanEmpty()
    {
        // **Null rather than an empty list, deliberately.** "This class has no baseline" and
        // "this class's baseline is empty" are different facts, and an entity seeded from silence
        // that looks like a decoded answer is the harder of the two to notice.
        Decoder().Baseline(ClassId).ShouldBeNull();
    }

    [Test]
    public void Baseline_AnEntryWithNoUserData_IsSkippedRatherThanTreatedAsEmpty()
    {
        // An instancebaseline table has an entry per class and only some carry data. Treating an
        // empty payload as an empty baseline would turn "not stated" into "stated as nothing",
        // which is the same conflation the null above avoids.
        EntityDecoder decoder = Decoder();

        BaselineBuilder.Apply(
            [new StringTableEntry(0, ClassId.ToString(CultureInfo.InvariantCulture), [])],
            decoder);

        decoder.Baseline(ClassId).ShouldBeNull();
    }

    [Test]
    public void Baseline_AnEntryWhoseTextIsNotAClassId_IsSkipped()
    {
        // The entry text is the CLASS ID, not the entity index and not the class name — a
        // different key from the userinfo table, which is named for the entity slot. An entry that
        // does not parse is skipped rather than assigned to class zero.
        EntityDecoder decoder = Decoder();

        BaselineBuilder.Apply(
            [new StringTableEntry(0, "CTFPlayer", Payload(("m_iHealth", 125)))],
            decoder);

        decoder.Baseline(ClassId).ShouldBeNull();
    }

    [Test]
    public void Baseline_ASecondTableForTheSameClass_ReplacesTheFirst()
    {
        // Baselines arrive on the create message and on later updates, and a class restated is a
        // correction rather than an addition. Keeping both would leave an entity seeded from a
        // value the server had already replaced.
        EntityDecoder decoder = WithBaseline(("m_iHealth", 125));

        BaselineBuilder.Apply(
            [new StringTableEntry(0, ClassId.ToString(CultureInfo.InvariantCulture),
                Payload(("m_iHealth", 5)))],
            decoder);

        decoder.Baseline(ClassId).ShouldNotBeNull()
            .ShouldHaveSingleItem().Value.AsInt.ShouldBe(5);
    }

    /// <summary>A decoder whose class carries the given baseline properties.</summary>
    private static EntityDecoder WithBaseline(params (string Name, int Value)[] properties) =>
        BaselineFixture.WithBaseline(properties);

    /// <summary>The encoded property block an instance baseline carries.</summary>
    private static byte[] Payload(params (string Name, int Value)[] properties) =>
        BaselineFixture.Payload(properties);

    private static EntityDecoder Decoder() => BaselineFixture.Decoder();
}
