using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The game event field types, upgraded from arithmetic to a published citation.
/// </summary>
/// <remarks>
/// **`docs/CONFORMANCE.md` listed this among the things "genuinely outside the SDK", pinned by
/// arithmetic because `GameEventManager` is closed.** Half of that was already wrong before this
/// batch — <c>igameevents.h:52</c> states the ordering outright:
///
/// <blockquote>
/// Valid data types are string, float, long, short, byte &amp; bool. If a data field should not be
/// broadcasted to clients, use the type "local".
/// </blockquote>
///
/// which is 1 through 6, then 7, and the enum already cited it. What was genuinely missing is the
/// **width and signedness of each type**, and that turns out to be published too — not in a header,
/// but in the comment block at the top of a shipped game resource file,
/// <c>game/mod_hl2mp/resource/modevents.res</c>:
///
/// <code>
/// //   string : a zero terminated string
/// //   bool   : unsigned int, 1 bit
/// //   byte   : unsigned int, 8 bit
/// //   short  : signed int, 16 bit
/// //   long   : signed int, 32 bit
/// //   float  : float, 32 bit
/// //   local  : any data, but not networked to clients
/// </code>
///
/// **Signedness is the part that was previously assumed.** `short` and `long` are signed and `byte`
/// and `bool` are unsigned, stated by Valve, and a decoder that got either backwards produces a
/// plausible number — a negative score reading as 65,000-odd, or the reverse.
///
/// **Worth generalising: the answer was in a `.res` file, not a header.** This project's source
/// menu lists the SDK, the Rust parser, the wiki and a decompiler. A shipped resource file's
/// comment block was in none of those categories and settled a question filed as closed.
/// </remarks>
public sealed class GameEventTypeWidthConformanceTests
{
    /// <summary>The resource file whose comment documents the types.</summary>
    private const string ModEvents = "game/mod_hl2mp/resource/modevents.res";

    /// <summary>The interface header that documents the ordering.</summary>
    private const string EventsHeader = "src/public/igameevents.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void GameEventTypes_TheOrdering_IsDocumentedInTheInterfaceHeader()
    {
        // The citation the enum already carried, verified rather than trusted. It was checked
        // during this batch precisely because an earlier grep of this header for "TYPE_" and "enum"
        // found nothing and briefly suggested the citation was invented — the prose is there, the
        // search patterns were wrong.
        string header = SourceSdk.Text(EventsHeader).ShouldNotBeNull();

        header.ShouldContain("Valid data types are string, float, long, short, byte & bool.");
        header.ShouldContain("should not be broadcasted to clients, use the type \"local\".");

        // The ordering in that sentence, read back as our enum. String first, bool last of the
        // networked six, local seventh.
        ((byte)GameEventValueType.String).ShouldBe((byte)1);
        ((byte)GameEventValueType.Float).ShouldBe((byte)2);
        ((byte)GameEventValueType.Long).ShouldBe((byte)3);
        ((byte)GameEventValueType.Short).ShouldBe((byte)4);
        ((byte)GameEventValueType.Byte).ShouldBe((byte)5);
        ((byte)GameEventValueType.Bool).ShouldBe((byte)6);
        ((byte)GameEventValueType.Local).ShouldBe((byte)7);
    }

    [Test]
    public void GameEventTypes_WidthAndSign_AreDocumentedInTheShippedResource()
    {
        // The new citation, and the one that was actually missing. Each line is asserted separately
        // so a failure names which type moved rather than reporting that a block of text changed.
        string events = SourceSdk.Text(ModEvents).ShouldNotBeNull();

        Dictionary<string, string> documented = new(StringComparer.Ordinal)
        {
            ["string"] = "a zero terminated string",
            ["bool"] = "unsigned int, 1 bit",
            ["byte"] = "unsigned int, 8 bit",
            ["short"] = "signed int, 16 bit",
            ["long"] = "signed int, 32 bit",
            ["float"] = "float, 32 bit",
            ["local"] = "any data, but not networked to clients",
        };

        // The type name is asserted alongside its description rather than passed as a Shouldly
        // message: the two-argument string overload resolves to IEnumerable<char> and does not
        // compile, which is a small reminder that an assertion library's overloads are part of the
        // API surface.
        foreach ((string type, string description) in documented)
        {
            events.ShouldContain(type);
            events.ShouldContain(description);
        }
    }

    [Test]
    public void GameEventTypes_EveryNetworkedType_FitsTheThreeBitTag()
    {
        // The arithmetic that was the ONLY support for this before, kept because it is still what
        // rules out the rival hypothesis: CS:GO's protobuf ordering puts val_uint64 eighth and
        // val_wstring ninth, which needs four bits. Three bits stops at seven, so that ordering
        // cannot be this one — and B14 was settled by exactly this reasoning after the enum had
        // wrongly called value 7 a 64-bit integer.
        //
        // Now it is corroboration rather than the whole case, which is the point of the batch.
        GameEventCodec.ValueTypeBits.ShouldBe(3);
        ((byte)GameEventValueType.Local).ShouldBeLessThan((byte)(1 << GameEventCodec.ValueTypeBits));

        // And the terminator is not a type, so the tag space is exactly full: 0 ends a field list,
        // 1-7 are the seven types. Nothing is spare, which is why adding a type would have been a
        // wire break and is presumably why Valve never did.
        ((byte)GameEventValueType.None).ShouldBe((byte)0);
    }
}
