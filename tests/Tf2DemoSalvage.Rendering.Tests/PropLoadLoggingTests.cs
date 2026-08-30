using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The static-prop loader reports through the log the viewer actually reads.
/// </summary>
/// <remarks>
/// **This is a wiring test, and nothing below it can fail when the wiring is absent** —
/// `PropModels.Load` writes four categories of finding and two chequer warnings, every one of them
/// correct, into whatever <see cref="Microsoft.Extensions.Logging.ILogger"/> it is handed. Its
/// parameter was <c>ILogger? props = null</c> with a `NullLogger` default, `MapAssets` was the only
/// caller in the repository, and it never passed one. So the entire static-prop path had been
/// silent since it was written.
///
/// **The cost was four dead hypotheses.** B229 — 19,274 triangles drawing in the missing-material
/// chequer on `cp_fulgur` — was chased by reading the viewer log for `Register`'s two warnings,
/// which name the model whose mesh cannot resolve a material. Both report at `Warning`, both fired
/// zero times, and that was taken as evidence the −1 arrived by some other route. It was evidence
/// about the sink. The same log carried 125 `pairing` lines, which is exactly the number of ENTITY
/// models — those go through `LoadFrames`, which is handed `factory.CreateLogger("props")` four
/// lines away.
///
/// `docs/memory/a-null-object-default-hides-a-missed-wiring.md` records the previous instance,
/// where a suite stayed green while the log lost 202 lines. The parameter is required now, so the
/// compiler makes the omission impossible; this test is the assertion that the caller passes
/// something real rather than <c>NullLogger.Instance</c> to satisfy it.
/// </remarks>
public sealed class PropLoadLoggingTests
{
    /// <summary>The subsystem static props report under (D83).</summary>
    private const string Props = "props";

    [Test]
    public void Load_AMapWithStaticProps_ReportsThroughThePropsArea()
    {
        MapCache.LoadedMap loaded = MapCache.With();

        IReadOnlyList<string> props = loaded.Log.From(Props);

        // **Asserted on the SUMMARY line rather than on any count**, because what is being tested
        // is that the area speaks at all. The numbers in it are a property of the map.
        props.ShouldContain(
            line => line.Contains("PRODUCED", StringComparison.Ordinal)
                && line.Contains("triangles", StringComparison.Ordinal),
            "PropModels.Load writes one summary per map load; its absence means MapAssets handed "
            + "it a NullLogger and every warning the static-prop path produces is being discarded. "
            + $"The area wrote {props.Count} lines: "
            + string.Join(" | ", props.Take(5)));
    }

    [Test]
    public void Load_AMapWithStaticProps_ReportsUnderTheSameAreaAsEntityModels()
    {
        // **The control, and it is what makes the test above mean something.** `LoadFrames` logs
        // under the same `props` area for ENTITY models, and in the viewer it was writing 125
        // `pairing` lines into it throughout the period the static-prop half was mute — so "did
        // the props area speak" is a question that would have answered yes the whole time. Both
        // halves reach `Read`, so both produce a `pairing` line and only one produces the
        // placement summary; asserting the pair is what tells them apart.
        MapCache.LoadedMap loaded = MapCache.With();

        IReadOnlyList<string> props = loaded.Log.From(Props);

        props.ShouldContain(
            line => line.StartsWith("pairing ", StringComparison.Ordinal),
            "no entity or prop model reported its mesh-to-material pairing at all");

        props.ShouldContain(
            line => line.Contains("ASKED FOR", StringComparison.Ordinal)
                && line.Contains("placements", StringComparison.Ordinal),
            "the entity-model lines are present and the static-prop summary is not, which is "
            + "exactly the split that hid B229's cause");
    }
}
