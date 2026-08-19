using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// Which voice codec each era declares — original research, measured from the corpus.
/// </summary>
/// <remarks>
/// **Valve publishes what changed between protocols and never when.** The era table in
/// <c>docs/TIMELINE.md</c> exists because every fact on it had to be measured. This adds the voice
/// axis to it.
///
/// Measured 2026-08-16 across the committed corpus by reading each demo's <c>svc_VoiceInit</c>:
///
/// | Era | Codec | Quality field |
/// |---|---|---|
/// | 2007 build 3258 | <c>vaudio_speex</c> | 5 |
/// | 2008 build 3420 | <c>vaudio_speex</c> | 5 |
/// | 2009 build 3862 | <c>vaudio_speex</c> | 5 |
/// | 2011 build 4604 | <c>vaudio_speex</c> | 5 |
/// | 2013 build 1729296 | <c>vaudio_speex</c> | 5 |
/// | modern (<c>z1800</c>) | <c>vaudio_celt</c> | 22050 |
///
/// **Speex holds across the entire 2007–2013 range without wavering**, POV and SourceTV alike, and
/// the quality field is 5 every time. The modern demo is CELT and its quality field reads 22050 —
/// because at quality 255 the message carries a 16-bit sample rate instead, so the same field
/// changes meaning rather than changing value.
///
/// **Why this matters beyond bookkeeping**, and it is the project's founding thesis showing up in a
/// new place: the modern 64-bit client ships <c>vaudio_celt.dll</c> and <c>vaudio_minimp3.dll</c> in
/// <c>bin/x64</c> and **no <c>vaudio_speex.dll</c> at all**, while <c>x64/engine.dll</c> still
/// contains the string <c>vaudio_speex</c>. Every demo above from 2007 to 2013 asks for a codec that
/// client cannot load. This project decodes Speex, so it reads voice the live 64-bit game cannot.
///
/// That install-side half is deliberately NOT asserted here — it depends on a local Steam library
/// and would fail in CI for reasons that say nothing about the code. It is recorded in
/// <c>docs/findings/06-protocol-eras.md</c>, where the evidence class is stated as measured on one
/// machine.
/// </remarks>
public sealed class CorpusVoiceCodecEraTests
{
    /// <summary>The codec every committed era specimen from 2007 to 2013 declares.</summary>
    private const string Speex = "vaudio_speex";

    /// <summary>The codec the modern specimen declares.</summary>
    private const string Celt = "vaudio_celt";

    [Test]
    public void VoiceInit_EraSpecimens2007To2013_DeclareSpeex()
    {
        // Swept rather than sampled: the claim is about a RANGE holding without exception, so one
        // demo cannot support it and a gap in the middle would be the interesting result.
        //
        // POV and SourceTV both, deliberately. A codec differing by recording mode would be a
        // property of the writer rather than of the era, and the pairs in this corpus are what makes
        // that distinguishable.
        List<string> dated = [.. Corpus.Files().Where(path =>
            System.IO.Path.GetFileName(path).StartsWith("tf2-", StringComparison.Ordinal))];

        dated.ShouldNotBeEmpty("the era specimens should be present");

        foreach (string path in dated)
        {
            Corpus.Voice(path).Codec.ShouldBe(
                Speex,
                $"{System.IO.Path.GetFileName(path)} should declare {Speex}");
        }
    }

    [Test]
    public void VoiceInit_TheModernSpecimen_DeclaresCelt()
    {
        // The control for the sweep above. Without it, "every demo says speex" is equally consistent
        // with a decoder that returns a constant, and the assertion would pass against a broken
        // reader — the whole corpus agreeing is not evidence when the corpus is one era.
        string modern = Corpus.Demo("z1800");

        Corpus.Voice(modern).Codec.ShouldBe(Celt);
    }

    [Test]
    public void VoiceInit_TheCorpus_StraddlesTheCodecChangeWithoutDatingIt()
    {
        // **What the corpus cannot say, said explicitly.** Both codecs are present, so the change is
        // bracketed — after the 2013 build and at or before z1800's date — and the corpus contains
        // nothing between them. Recording that as a known gap is the point: an era table with an
        // undated transition is honest, and one that interpolates a date is not.
        //
        // Closing it needs a specimen from the 2014-2019 range, which is the same gap the protocol
        // axis has at 17-23. That is a recording problem, not a parsing one.
        Corpus.AnyDemoUses(Speex).ShouldBeTrue();
        Corpus.AnyDemoUses(Celt).ShouldBeTrue();

        Assert.Ignore(
            "the codec transition is bracketed but not dated: speex through the 2013 build, celt by " +
            "z1800, and no specimen in between. Closing it needs a demo from 2014-2019 — the same " +
            "range the protocol axis is missing at 17-23.");
    }
}
