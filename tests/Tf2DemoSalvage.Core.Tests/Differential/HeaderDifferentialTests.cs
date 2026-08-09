using System;
using System.IO;
using System.Text.Json;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Tests.Differential;

/// <summary>
/// Compares this parser's output against <c>tf-demo-parser</c>, an independent implementation.
/// </summary>
/// <remarks>
/// Every other check in this suite is internal: our decoders agreeing with each other, or with
/// values the demo states about itself. Those catch misalignment, but they cannot catch a
/// shared misunderstanding — if the format were read wrongly in a self-consistent way, nothing
/// would notice.
///
/// This is the only test that can. It is currently limited to header fields, because that is
/// all both parsers produce in comparable form; its real value arrives with entity property
/// values, where a wrong answer is a plausible number rather than a broken structure
/// (<c>RISKS.md</c> B4).
///
/// The oracle is optional. Set <c>TF2DEMOSALVAGE_ORACLE</c> to a built <c>parse_demo</c>
/// binary — see <c>docs/DIFFERENTIAL.md</c>. Without it these tests report that they skipped
/// rather than passing silently.
/// </remarks>
public sealed class HeaderDifferentialTests(ITestOutputHelper output)
{
    [Fact]
    public void HeaderFields_MatchAnIndependentParser()
    {
        string? oracle = ReferenceParser.Locate();
        if (oracle is null)
        {
            // Reported, not silent: a skipped differential test is the one case where green
            // means nothing was actually compared.
            output.WriteLine(
                "SKIPPED: no reference parser. Set TF2DEMOSALVAGE_ORACLE to a parse_demo " +
                "binary (see docs/DIFFERENTIAL.md). No cross-parser comparison was made.");
            return;
        }

        int compared = 0;

        foreach (string path in Corpus.Files())
        {
            DemoHeader ours = DemoHeader.Parse(File.ReadAllBytes(path));

            using JsonDocument theirs = ReferenceParser.Run(oracle, path);
            JsonElement header = theirs.RootElement.GetProperty("header");

            header.GetProperty("version").GetInt32().ShouldBe(ours.DemoProtocol, path);
            header.GetProperty("protocol").GetInt32().ShouldBe(ours.NetworkProtocol, path);
            header.GetProperty("server").GetString().ShouldBe(ours.ServerName, path);
            header.GetProperty("nick").GetString().ShouldBe(ours.ClientName, path);
            header.GetProperty("map").GetString().ShouldBe(ours.MapName, path);
            header.GetProperty("game").GetString().ShouldBe(ours.GameDirectory, path);
            header.GetProperty("ticks").GetInt32().ShouldBe(ours.PlaybackTicks, path);
            header.GetProperty("frames").GetInt32().ShouldBe(ours.PlaybackFrames, path);
            header.GetProperty("signon").GetInt32().ShouldBe(ours.SignonLengthBytes, path);
            header.GetProperty("duration").GetSingle()
                .ShouldBe(ours.PlaybackTimeSeconds, 0.001f, path);

            compared++;
            output.WriteLine($"{Path.GetFileName(path)}: header agrees on 10 fields");
        }

        compared.ShouldBeGreaterThan(0, "no demos were compared");
    }

    [Fact]
    public void OracleAvailability_IsReportedSoASkipIsNeverMistakenForAPass()
    {
        string? oracle = ReferenceParser.Locate();

        output.WriteLine(oracle is null
            ? "Reference parser NOT configured - differential tests are skipping."
            : $"Reference parser: {oracle}");

        // Deliberately not an assertion. Requiring the oracle would fail every machine that
        // has not built it, which is most of them; the point is that its absence is visible.
        Corpus.Files().ShouldNotBeEmpty();
    }
}
