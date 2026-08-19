using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// The game event encoding, checked against what <c>igameevents.h</c> actually states.
/// </summary>
/// <remarks>
/// **Half of this is in the SDK and half is not, and the split is the useful part.**
/// <c>igameevents.h</c> declares the event index width and documents the set of data types an event
/// may carry, in prose it means as a specification: "Valid data types are string, float, long,
/// short, byte &amp; bool", plus <c>local</c> for a field the server does not broadcast. What it does
/// not publish is the NUMBER each type is sent as — that lives in <c>GameEventManager</c>, in the
/// closed engine — so the numbering came from elsewhere and no header can confirm it.
///
/// So this asserts the parts that are stated, and pins the rest by arithmetic: seven named types
/// plus the absent case is eight values, which is exactly three bits. A ninth type would not fit,
/// and that is a real constraint on any future guess about the numbering rather than a restatement
/// of the current one.
/// </remarks>
public sealed class GameEventConformanceTests
{
    /// <summary>Where the engine declares the event interface.</summary>
    private const string Events = "src/public/igameevents.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void GameEvents_TheEventIndexWidth_IsTheEngines()
    {
        // MAX_EVENT_BITS bounds both the id on the wire and the number of descriptors a list can
        // carry, because the list is indexed by that id. One constant, used twice, and being one
        // bit short would truncate every event id above 255 onto a different event's descriptor.
        Declared()["MAX_EVENT_BITS"].ShouldBe(GameEventCodec.EventIdBits);
        Declared()["MAX_EVENT_BITS"].ShouldBe(GameEventCodec.CountBits);
    }

    [Test]
    public void GameEvents_EveryDecodedType_IsOneTheEngineDocuments()
    {
        // **The header's prose is the specification here**, because the resource files servers ship
        // are written against exactly these words: a field declaring "long" is a long. Reading the
        // sentence rather than a table is unusual and is what the SDK offers — the numbering is not
        // published at all.
        HashSet<string> documented = DocumentedTypes();

        List<string> unknown =
        [
            .. Enum.GetNames<GameEventValueType>()
                .Where(name => !string.Equals(name, "None", StringComparison.Ordinal))
                .Where(name => !documented.Contains(name)),
        ];

        unknown.ShouldBeEmpty(
            "these are decoded as game event field types and igameevents.h documents no such type: " +
            string.Join(", ", unknown));
    }

    [Test]
    public void GameEvents_EveryDocumentedType_IsDecoded()
    {
        // The other direction, which is the one that finds a gap rather than a mistake. A type this
        // project does not know would stop an event mid-field, and events are length-prefixed only
        // at the message level — so the loss is the whole event, not one value.
        HashSet<string> ours = new(Enum.GetNames<GameEventValueType>(), StringComparer.Ordinal);

        List<string> missing = [.. DocumentedTypes().Where(name => !ours.Contains(name))];

        missing.ShouldBeEmpty(
            "the engine documents these field types and this decoder has no case for them: " +
            string.Join(", ", missing));
    }

    [Test]
    public void GameEvents_ThreeBits_AreExactlyEnoughForThoseTypes()
    {
        // **Arithmetic, and it constrains the unpublished half.** Seven documented types plus the
        // absent case is eight values, which is three bits with nothing to spare. That says the
        // width is not a guess with slack in it: an eighth type would need a fourth bit, so if one
        // ever turns up in a demo the encoding changed rather than this being off by one.
        int values = DocumentedTypes().Count + 1;

        values.ShouldBe(8);
        (1 << GameEventCodec.ValueTypeBits).ShouldBe(values);

        // And every value this project uses fits, which is the half that would break decoding.
        Enum.GetValues<GameEventValueType>()
            .Max(type => (int)type)
            .ShouldBeLessThan(1 << GameEventCodec.ValueTypeBits);
    }

    [Test]
    public void GameEvents_TheHeader_WasActuallyRead()
    {
        // The control: the prose scan is the fragile instrument here, so it says so directly.
        Declared().ShouldContainKey("MAX_EVENT_BITS");
        DocumentedTypes().Count.ShouldBe(7, "string, float, long, short, byte, bool and local");
    }

    /// <summary>The named integers the header declares.</summary>
    private static IReadOnlyDictionary<string, int> Declared() => SourceSdk.Constants(Events);

    /// <summary>The field types the header documents, in this project's spelling.</summary>
    /// <remarks>
    /// Read from the sentence listing them rather than pattern-matched loosely, because a permissive
    /// scan of a comment block would pick up ordinary English words and quietly report that every
    /// type is accounted for.
    /// </remarks>
    private static HashSet<string> DocumentedTypes()
    {
        string text = SourceSdk.Text(Events)
            ?? throw new InvalidOperationException($"{Events} is missing from the SDK checkout");

        Match sentence = Regex.Match(
            text,
            @"Valid data types are (?<list>[^.]+)\.",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(10));

        sentence.Success.ShouldBeTrue(
            $"the sentence listing the field types was not found in {Events}; if it was reworded, " +
            "this test needs rewriting rather than relaxing");

        HashSet<string> types = new(StringComparer.Ordinal);

        foreach (Match word in Regex.Matches(
            sentence.Groups["list"].Value,
            @"\b(string|float|long|short|byte|bool)\b",
            RegexOptions.None,
            TimeSpan.FromSeconds(10)))
        {
            types.Add(char.ToUpperInvariant(word.Value[0]) + word.Value[1..]);
        }

        // `local` is documented in the following sentence rather than that list, because it is the
        // one type that is deliberately NOT broadcast — which is exactly why a decoder has to know
        // about it.
        text.ShouldContain("use the type \"local\"");
        types.Add("Local");

        return types;
    }
}
