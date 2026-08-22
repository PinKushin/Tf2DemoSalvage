using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Schema;

/// <summary>
/// The instrument the schema gap markers rest on, tested before they use it.
/// </summary>
/// <remarks>
/// **A search that always returns false would make every gap marker pass for ever**, which is the
/// exact failure the conformance sweep is removing — so the search itself gets tested first, in
/// both directions.
/// </remarks>
public sealed class SchemaGapTests
{
    [Test]
    public void AnyProductionAssemblyMentions_ANameTheDecoderReads_IsFound()
    {
        // The positive control. EntityState.Fog looks this up, so it must be in the build.
        SchemaGap.AnyProductionAssemblyMentions(SchemaGap.KnownPresent).ShouldBeTrue(
            "the search cannot find a literal that is demonstrably compiled in, so every " +
            "absence it reports is meaningless");
    }

    [Test]
    public void AnyProductionAssemblyMentions_ANameNothingUses_IsNotFound()
    {
        // The negative control, and it has to be a name that could plausibly exist. A string of
        // random characters would pass against a search that only ever returns false for long
        // inputs; this one looks exactly like the wire names the markers ask about.
        SchemaGap.AnyProductionAssemblyMentions("m_nThisPropertyDoesNotExist").ShouldBeFalse();
    }

    [Test]
    public void AnyProductionAssemblyMentions_ATypeNameRatherThanALiteral_IsAlsoFound()
    {
        // **The UTF-8 half, and it covers the case that matters most.** A wire name passed to a
        // lookup is a string literal, stored UTF-16; a type or enum member name is metadata, stored
        // UTF-8. Somebody closing one of these gaps properly writes an ENUM of named flags and
        // leaves no literal behind — so a UTF-16-only search would go on reporting the gap open.
        //
        // `DemoCommandType` is a real production enum, so its name must be found.
        SchemaGap.AnyProductionAssemblyMentions("DemoCommandType").ShouldBeTrue(
            "a metadata name must be found, or a marker cannot notice a feature implemented as a type");
    }

    [Test]
    public void AnyProductionAssemblyMentions_ASubstringOfARealName_IsStillFound()
    {
        // Substring matching is deliberate: a decoder may build a lookup key by concatenation, and
        // a marker asking about "m_fog" should see "m_fog.start". Stated so the looseness is a
        // decision rather than an accident — it makes the markers CONSERVATIVE, which is the safe
        // direction: they close early rather than late.
        SchemaGap.AnyProductionAssemblyMentions("m_fog").ShouldBeTrue();
    }
}
