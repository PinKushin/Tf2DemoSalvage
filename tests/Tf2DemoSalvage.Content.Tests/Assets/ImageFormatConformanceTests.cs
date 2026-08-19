using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Every VTF image format number, derived from the engine's own enum.
/// </summary>
/// <remarks>
/// **A texture's format byte selects how its bytes are interpreted, and a wrong selection produces
/// a picture.** Reading a DXT1 texture as DXT3 halves the apparent resolution and fills the surface
/// with garbage that still tiles; reading BGRA as RGBA swaps red and blue on every pixel of a map.
/// Neither throws, and neither is distinguishable from an artist's choice without knowing what the
/// file said.
///
/// **The numbering is almost entirely implicit**, which is what makes this worth deriving rather
/// than reading. <c>ImageFormat</c> assigns values to exactly two of its forty members — −1 and 0 —
/// so <c>IMAGE_FORMAT_DXT1</c> being 13 is a fact about its POSITION in a list, not about anything
/// written down. Counting that list by hand is how one gets 12 or 14, and both of those are real
/// formats.
/// </remarks>
public sealed class ImageFormatConformanceTests
{
    /// <summary>Where the engine declares the formats.</summary>
    private const string Formats = "src/public/bitmap/imageformat.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void EveryFormatWeDecode_HasTheEnginesNumber()
    {
        IReadOnlyDictionary<string, int> engine = Declared();

        (string Name, VtfFormat Ours)[] claims =
        [
            ("IMAGE_FORMAT_RGBA8888", VtfFormat.Rgba8888),
            ("IMAGE_FORMAT_RGB888", VtfFormat.Rgb888),
            ("IMAGE_FORMAT_BGR888", VtfFormat.Bgr888),
            ("IMAGE_FORMAT_BGRA8888", VtfFormat.Bgra8888),
            ("IMAGE_FORMAT_DXT1", VtfFormat.Dxt1),
            ("IMAGE_FORMAT_DXT3", VtfFormat.Dxt3),
            ("IMAGE_FORMAT_DXT5", VtfFormat.Dxt5),
            ("IMAGE_FORMAT_DXT1_ONEBITALPHA", VtfFormat.Dxt1OneBitAlpha),
        ];

        List<string> wrong = [];

        foreach ((string name, VtfFormat ours) in claims)
        {
            if (!engine.TryGetValue(name, out int theirs))
            {
                wrong.Add($"{name} is not declared by the engine at all");
            }
            else if (theirs != (int)ours)
            {
                wrong.Add($"{name}: we use {(int)ours}, the engine declares {theirs}");
            }
        }

        wrong.ShouldBeEmpty(string.Join("; ", wrong));
    }

    [Test]
    public void ImageFormats_TheSentinels_DoNotCollideWithARealFormat()
    {
        // **Two negative values that are ours, not Valve's**, and the reason they must be checked.
        // None is −1 to mirror IMAGE_FORMAT_UNKNOWN, but Unknown is −2 and has no counterpart — it
        // means "a format the engine has and this reader does not". If the engine ever numbered a
        // format −2, a texture in it would decode as our sentinel rather than being reported.
        IReadOnlyDictionary<string, int> engine = Declared();

        engine["IMAGE_FORMAT_UNKNOWN"].ShouldBe((int)VtfFormat.None);

        engine.Values.ShouldNotContain(
            (int)VtfFormat.Unknown,
            "the sentinel for an unsupported format must not BE a format");
    }

    [Test]
    public void ImageFormats_TheUndecodedOnes_AreTheMajority()
    {
        // **A coverage statement, and an honest one.** Eight of forty is what this reader handles,
        // and the gap is deliberate: TF2's own content is overwhelmingly DXT1 and DXT5, so the rest
        // are unimplemented rather than missing. Naming the count keeps that a decision.
        //
        // The reader reports an unsupported format rather than guessing, which is why this is a
        // number and not a defect. If it ever silently fell back to a format, this test would be
        // measuring the wrong thing and the fallback would be the bug.
        int handled = Enum.GetValues<VtfFormat>()
            .Count(format => (int)format >= 0);

        int declared = Declared().Values.Count(value => value >= 0);

        handled.ShouldBe(8);
        declared.ShouldBeGreaterThan(30);
    }

    [Test]
    public void ImageFormats_TheImplicitNumbering_WasCountedFromTheSdk()
    {
        // The control, and it is specific rather than a floor. If the parser only saw explicit
        // assignments it would return two entries — UNKNOWN at −1 and RGBA8888 at 0 — and every
        // assertion above that looks up a DXT format would fail with "not declared", which reads
        // like a missing header rather than a broken counter. This says which it is.
        IReadOnlyDictionary<string, int> engine = Declared();

        engine.Count.ShouldBeGreaterThan(30, "the implicit members were not counted");

        // ABGR8888 has no explicit value anywhere and sits immediately after RGBA8888.
        engine["IMAGE_FORMAT_ABGR8888"].ShouldBe(1);
    }

    /// <summary>Every image format the engine declares, implicit numbering included.</summary>
    private static IReadOnlyDictionary<string, int> Declared() =>
        SourceSdk.Enumerators(Formats, "ImageFormat");
}
