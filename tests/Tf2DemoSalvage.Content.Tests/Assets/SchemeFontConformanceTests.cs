using System;
using System.Text;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// How VGUI's scheme declares a font, written down before anything reads one.
/// </summary>
/// <remarks>
/// **Read from the shipped scheme rather than from code, because the code is closed.**
/// `vguimatsurface` is not in `source-sdk-2013` — only `fontabc.h` and `BitmapFontFile.h` are — so
/// the rasteriser that consumes these fields cannot be read. The DECLARATION can: it is plain
/// KeyValues in a file the game ships, and `docs/memory/shipped-data-is-a-source.md` is the standing
/// reminder that this counts as a source.
///
/// **The specimen is `platform/Resource/SourceScheme.res`**, and finding it took one wrong turn
/// worth recording: `DefaultFixedOutline` is in neither `tf/resource/ClientScheme.res` nor
/// `hl2/resource/ClientScheme.res`. It is a platform-level font, which is why every Source game's
/// fps meter looks the same.
///
/// **Valve explains the numbered entries in a comment above them**, which is the whole of the
/// fallback rule and is quoted here because nothing else states it:
///
/// <code>
/// // fonts are used in order that they are listed
/// // fonts listed later in the order will only be used if they fulfill a range not already filled
/// // if a font fails to load then the subsequent fonts will replace
/// </code>
///
/// So a font is a LIST of candidates, each optionally bounded by a screen-height range, and the
/// first one whose range covers the current resolution wins.
/// </remarks>
public sealed class SchemeFontConformanceTests
{
    /// <summary>
    /// The block TF2's frame rate meter draws with, copied byte for byte from the shipped scheme.
    /// </summary>
    /// <remarks>
    /// **Copied rather than paraphrased, tabs and CRLF included.** The shipped file is CRLF and the
    /// keys are tab-indented; a reader that only ever met the tidied version in a test would be
    /// untested against the one artefact it exists to read.
    /// </remarks>
    private const string DefaultFixedOutlineBlock =
        "Scheme\r\n" +
        "{\r\n" +
        "\tFonts\r\n" +
        "\t{\r\n" +
        "\t\t// fonts are used in order that they are listed\r\n" +
        "\t\t\"DebugFixed\"\r\n" +
        "\t\t{\r\n" +
        "\t\t\t\"1\"\r\n" +
        "\t\t\t{\r\n" +
        "\t\t\t\t\"name\"\t\t\"Courier New\"\r\n" +
        "\t\t\t\t\"tall\"\t\t\"10\"\r\n" +
        "\t\t\t\t\"weight\"\t\"500\"\r\n" +
        "\t\t\t\t\"antialias\" \"1\"\r\n" +
        "\t\t\t}\r\n" +
        "\t\t}\r\n" +
        "\t\t\"DefaultFixedOutline\"\r\n" +
        "\t\t{\r\n" +
        "\t\t\t\"1\"\r\n" +
        "\t\t\t{\r\n" +
        "\t\t\t\t\"name\"\t\t\"Lucida Console\"\r\n" +
        "\t\t\t\t\"tall\"\t\t\"10\"\r\n" +
        "\t\t\t\t\"weight\"\t\"0\"\r\n" +
        "\t\t\t\t\"outline\"\t\"1\"\r\n" +
        "\t\t\t}\r\n" +
        "\t\t}\r\n" +
        "\t}\r\n" +
        "}\r\n";

    private static ReadOnlySpan<byte> Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// The meter's font is Lucida Console at ten pixels, normal weight, outlined.
    /// </summary>
    /// <remarks>
    /// This is the single fact the whole HUD hangs off, so it is asserted field by field rather
    /// than as a shape. **`weight 0` is not "unset"** — it is `FW_DONTCARE`, which GDI resolves to
    /// the regular face, and it is why the meter is not bold while `DebugFixed` beside it (`500` =
    /// `FW_MEDIUM`) is heavier.
    /// </remarks>
    [Test]
    public void Find_DefaultFixedOutline_IsLucidaConsoleTenOutlined()
    {
        SchemeFont font = SchemeFonts.Find(Bytes(DefaultFixedOutlineBlock), "DefaultFixedOutline")
            .ShouldNotBeNull();

        font.Name.ShouldBe("Lucida Console");
        font.Tall.ShouldBe(10);
        font.Weight.ShouldBe(0);
        font.Outline.ShouldBeTrue();

        // Not declared in this block, so it must come back off rather than defaulted to on. An
        // outlined font that also antialiased would be a different picture entirely.
        font.Antialias.ShouldBeFalse();
    }

    /// <summary>
    /// A font is found by name and the neighbouring ones are not confused with it.
    /// </summary>
    /// <remarks>
    /// **The control this suite would be blind without.** The specimen carries two fonts and they
    /// differ in every field, so a reader that latched onto the first `name` it met — or the last —
    /// would pass a single-font test and fail here. `DebugFixed` is `Courier New`/`500`/antialiased
    /// against `Lucida Console`/`0`/outlined.
    /// </remarks>
    [Test]
    public void Find_ANeighbouringFont_ReturnsItsOwnFieldsRatherThanTheOtherFonts()
    {
        SchemeFont debug = SchemeFonts.Find(Bytes(DefaultFixedOutlineBlock), "DebugFixed")
            .ShouldNotBeNull();

        debug.Name.ShouldBe("Courier New");
        debug.Weight.ShouldBe(500);
        debug.Antialias.ShouldBeTrue();
        debug.Outline.ShouldBeFalse();
    }

    /// <summary>
    /// A font the scheme does not declare is absent rather than a default.
    /// </summary>
    /// <remarks>
    /// Null, not a fabricated font. A HUD asking for a font nobody declared has a bug in it, and
    /// silently handing back Arial would hide it — the same argument as
    /// `docs/memory/sentinels-conflate-unknown-with-answer.md`.
    /// </remarks>
    [Test]
    public void Find_AFontTheSchemeDoesNotDeclare_IsNull()
    {
        SchemeFonts.Find(Bytes(DefaultFixedOutlineBlock), "NoSuchFont").ShouldBeNull();
    }

    /// <summary>
    /// The first candidate whose height range covers the screen is the one used.
    /// </summary>
    /// <remarks>
    /// Valve's rule, from the comment above the fonts: *"fonts listed later in the order will only
    /// be used if they fulfill a range not already filled"*. `yres` bounds a candidate to a range of
    /// screen heights, inclusive at both ends — that is how a scheme ships one font for 480-line
    /// displays and a larger one for 1200.
    ///
    /// A candidate with no `yres` is unbounded and therefore matches anything, which is why the
    /// unbounded one is listed last in real schemes.
    /// </remarks>
    [Test]
    public void Find_WithSeveralHeightRanges_TakesTheOneCoveringTheScreen()
    {
        const string ranged =
            """
            Scheme
            {
                Fonts
                {
                    "HudFont"
                    {
                        "1"
                        {
                            "name"  "Small"
                            "tall"  "12"
                            "yres"  "480 599"
                        }
                        "2"
                        {
                            "name"  "Large"
                            "tall"  "24"
                            "yres"  "600 767"
                        }
                        "3"
                        {
                            "name"  "Fallback"
                            "tall"  "32"
                        }
                    }
                }
            }
            """;

        SchemeFonts.Find(Bytes(ranged), "HudFont", screenHeight: 500)!.Name.ShouldBe("Small");
        SchemeFonts.Find(Bytes(ranged), "HudFont", screenHeight: 600)!.Name.ShouldBe("Large");
        SchemeFonts.Find(Bytes(ranged), "HudFont", screenHeight: 767)!.Name.ShouldBe("Large");

        // Above every declared range, so only the unbounded candidate is left.
        SchemeFonts.Find(Bytes(ranged), "HudFont", screenHeight: 1440)!.Name.ShouldBe("Fallback");
    }

    /// <summary>
    /// The same font declared twice keeps its blocks apart rather than merging them.
    /// </summary>
    /// <remarks>
    /// **This is what a <c>#base</c> override produces**, and VGUI schemes use it heavily: a
    /// derived scheme redeclares a font the base already had. Both declarations reach a reader as
    /// two blocks with the same name.
    ///
    /// **Written because a sabotage found the guard for it was untested.** Deleting the line that
    /// ends a font at its name boundary left every other test in this file green — so the guard was
    /// either dead or protecting an input nobody had written. It was the second. Without it the
    /// trailing candidate of the first block stays pending and merges with the first candidate of
    /// the second, yielding `Lucida Console` at `tall 24` — a font that appears in neither
    /// declaration, which is the worst shape of wrong answer because it looks plausible.
    ///
    /// The first declaration wins here, which is only the consequence of "first candidate that
    /// covers the screen"; `#base` precedence is a separate question this reader does not answer,
    /// since it does not follow includes at all.
    /// </remarks>
    [Test]
    public void Find_AFontDeclaredTwice_DoesNotMergeTheTwoDeclarations()
    {
        const string twice =
            """
            Scheme
            {
                Fonts
                {
                    "HudFont"
                    {
                        "1" { "name" "Lucida Console" "tall" "10" }
                    }
                    "OtherFont"
                    {
                        "1" { "name" "Tahoma" "tall" "16" }
                    }
                    "HudFont"
                    {
                        "1" { "name" "Verdana" "tall" "24" }
                    }
                }
            }
            """;

        SchemeFont font = SchemeFonts.Find(Bytes(twice), "HudFont").ShouldNotBeNull();

        // Whichever declaration wins, the answer must be a font that was actually written down.
        // The merge produces "Lucida Console" at 24, which is neither.
        (font.Name, font.Tall).ShouldBeOneOf(("Lucida Console", 10), ("Verdana", 24));
        font.Tall.ShouldBe(10, "the first candidate that covers the screen is taken");
    }

    /// <summary>
    /// A candidate whose range excludes the screen is skipped even when it is listed first.
    /// </summary>
    /// <remarks>
    /// Separated from the test above because that one could pass by returning the LAST candidate
    /// that matches rather than the first, and this one could not. Here the answer is the second of
    /// three, so neither "first declared" nor "last declared" is the rule being measured.
    /// </remarks>
    [Test]
    public void Find_WhenTheFirstCandidateIsOutOfRange_SkipsItRatherThanFallingBackToIt()
    {
        const string ranged =
            """
            Scheme
            {
                Fonts
                {
                    "HudFont"
                    {
                        "1" { "name" "TooSmall" "tall" "8"  "yres" "480 599" }
                        "2" { "name" "Wanted"   "tall" "16" "yres" "600 1199" }
                        "3" { "name" "TooBig"   "tall" "32" "yres" "1200 2000" }
                    }
                }
            }
            """;

        SchemeFonts.Find(Bytes(ranged), "HudFont", screenHeight: 1080)!.Name.ShouldBe("Wanted");
    }
}
