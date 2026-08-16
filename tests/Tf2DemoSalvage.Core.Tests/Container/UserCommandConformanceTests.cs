using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Container;

/// <summary>
/// The usercmd field order and widths, extracted from Valve's writer rather than transcribed.
/// </summary>
/// <remarks>
/// **This is the highest-consequence decode in the file for anyone documenting a run.**
/// <c>dem_usercmd</c> carries the view angles and <c>sidemove</c>/<c>forwardmove</c> of every tick —
/// which is the strafe itself, not a summary of it — so surf and jump records are only as good as
/// this stream. A wrong field order does not fail: it produces a complete command with plausible
/// numbers in the wrong members, and the run it describes never happened.
///
/// **Read the encoder, not the decoder.** <c>WriteUsercmd</c> in <c>game/shared/usercmd.cpp</c>
/// states the intent that <c>ReadUsercmd</c> only implies, and it is written as one regular block per
/// field — a presence bit, then the payload. That regularity is what makes the list extractable
/// instead of copied, which matters because a copy is exactly as wrong as the reading that produced
/// it. <see cref="UserCommandTests"/> already checks the VALUES against hand-built fixtures; this
/// checks the SHAPE against the source.
/// </remarks>
public sealed class UserCommandConformanceTests
{
    /// <summary>Where the engine writes a user command.</summary>
    private const string UserCmd = "src/game/shared/usercmd.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryFieldIsReadInTheOrderTheEngineWritesIt()
    {
        List<(string Name, int Bits)> engine = Written();
        IReadOnlyList<(string Name, int Bits)> ours = UserCommand.WireFields;

        ours.Count.ShouldBe(
            engine.Count,
            $"the engine writes {engine.Count} fields and this reads {ours.Count}: " +
            string.Join(", ", engine.Select(field => field.Name)));

        List<string> wrong = [];

        for (int at = 0; at < engine.Count; at++)
        {
            if (!string.Equals(engine[at].Name, ours[at].Name, StringComparison.Ordinal))
            {
                wrong.Add($"position {at}: the engine writes {engine[at].Name}, we read {ours[at].Name}");
            }
            else if (engine[at].Bits != ours[at].Bits)
            {
                wrong.Add(
                    $"{engine[at].Name}: the engine writes {engine[at].Bits} bits, we read {ours[at].Bits}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void TheCommandEndsAtMouseDyBecauseTf2IsNotHl2()
    {
        // **The trailing field that is NOT there, stated so it is not rediscovered as a bug.**
        // WriteUsercmd ends with an entitygroundcontact block — a presence bit, a count, and a
        // per-entity list — wrapped in `#if defined( HL2_CLIENT_DLL )`. TF2 is not that build, so
        // the field is absent from its demos and a decoder that reads the presence bit anyway
        // consumes a bit that belongs to the next command.
        //
        // The check is that resolving the guard REMOVES it: with HL2_CLIENT_DLL defined the field
        // appears, and without it the command ends at mousedy.
        Written()[^1].Name.ShouldBe("mousedy");

        List<(string Name, int Bits)> hl2 = Written(
            new HashSet<string>(StringComparer.Ordinal) { "HL2_CLIENT_DLL" });

        hl2.Count.ShouldBeGreaterThan(
            Written().Count,
            "if this stops differing, the guard has moved and the note above is stale");

        // The extraction names the member being written, which for the ground-contact list is the
        // array rather than the field inside it — `to->entitygroundcontact[i].entindex`.
        hl2.Select(field => field.Name).ShouldContain("entitygroundcontact");
    }

    [Test]
    public void TheWidthsThatAreNamedConstantsResolveToTheEnginesValues()
    {
        // weaponselect is MAX_EDICT_BITS and weaponsubtype is WEAPON_SUBTYPE_BITS — neither is a
        // literal in Valve's source, and both are widths where being one bit out desynchronises
        // everything after them.
        Dictionary<string, int> engine = Widths();

        engine["MAX_EDICT_BITS"].ShouldBe(11);
        engine["WEAPON_SUBTYPE_BITS"].ShouldBe(6);
    }

    [Test]
    public void TheExtractionFoundAWriterAtAll()
    {
        // The control. Every assertion above is vacuous if the regex matched nothing, and a
        // zero-length list would make the count comparison the only thing that noticed — after the
        // fact, with a confusing message.
        Written().Count.ShouldBeGreaterThan(10, "no writes were extracted from WriteUsercmd");
    }

    /// <summary>Every field <c>WriteUsercmd</c> writes, in order, with its width in bits.</summary>
    /// <remarks>
    /// Presence bits are skipped: <c>WriteOneBit</c> appears twice per field, once for present and
    /// once for absent, and neither is a payload. <c>WriteFloat</c> and <c>WriteShort</c> carry
    /// their widths in the call rather than as an argument, so they are named here — 32 and 16, from
    /// <c>bf_write</c> in <c>tier1/bitbuf.cpp</c>.
    /// </remarks>
    private static List<(string Name, int Bits)> Written(IReadOnlySet<string>? defined = null)
    {
        string source = SourceSdk.Text(UserCmd)
            ?? throw new InvalidOperationException($"{UserCmd} is missing from the SDK checkout");

        string resolved = CStruct.Conditioned(
            CStruct.Uncommented(source), defined, Widths(), out string? unhandled)
            ?? throw new InvalidOperationException(
                $"a preprocessor directive in {UserCmd} could not be resolved: {unhandled}");

        // The writer's body only, because ReadUsercmd sits in the same file and would double every
        // field — with the same names, which is the worst way for an extraction to be wrong.
        Match body = Regex.Match(
            resolved,
            @"void\s+WriteUsercmd\s*\([^)]*\)\s*\{(?<body>.*?)\n\}",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(10));

        body.Success.ShouldBeTrue($"WriteUsercmd was not found in {UserCmd}");

        Dictionary<string, int> widths = Widths();
        List<(string Name, int Bits)> fields = [];

        foreach (Match write in Regex.Matches(
            body.Groups["body"].Value,
            @"buf->Write(?<how>UBitLong|Float|Short)\(\s*(?:to->)?(?<field>[A-Za-z_][A-Za-z0-9_]*)"
                + @"(?:\s*\[[^\]]*\])?(?:\.[A-Za-z_]+)?(?:\s*,\s*(?<bits>[A-Za-z_0-9]+))?\s*\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(10)))
        {
            string how = write.Groups["how"].Value;

            int bits = how switch
            {
                "Float" => 32,
                "Short" => 16,
                _ => Width(write.Groups["bits"].Value, widths),
            };

            fields.Add((write.Groups["field"].Value, bits));
        }

        return fields;
    }

    /// <summary>Resolves a width that is written as a literal or as a named constant.</summary>
    private static int Width(string text, Dictionary<string, int> named)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int literal))
        {
            return literal;
        }

        return named.TryGetValue(text, out int value)
            ? value
            : throw new InvalidOperationException($"the width {text} could not be resolved");
    }

    /// <summary>The named bit widths, from the file that uses them and the header that shares them.</summary>
    private static Dictionary<string, int> Widths()
    {
        Dictionary<string, int> widths =
            new(SourceSdk.Constants("src/public/const.h"), StringComparer.Ordinal);

        foreach ((string name, int value) in SourceSdk.Constants(UserCmd))
        {
            widths[name] = value;
        }

        return widths;
    }
}
