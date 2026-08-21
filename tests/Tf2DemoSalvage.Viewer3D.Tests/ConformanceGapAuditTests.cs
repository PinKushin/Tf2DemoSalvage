using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Whether the suite's "not implemented" markers are still telling the truth.
/// </summary>
/// <remarks>
/// **A gap marker cannot notice its own gap closing, and five of them did not.** The conformance
/// suites carry a test per unimplemented feature whose body is
/// <c>Assert.Ignore("X is not implemented")</c>. They are the map of what is missing, they are
/// quoted into <c>docs/CONFORMANCE.md</c>, and a skipped test is invisible in a green run — so when
/// a feature landed, its marker went on skipping and went on claiming the feature was absent.
///
/// Measured 2026-08-21, when the owner asked how many were still skipping: <c>Cubemaps_AreNotRead</c>
/// against a complete <c>BspCubemaps</c>, <c>EnvironmentMaps_AreNotImplemented</c> against
/// reflections drawing on screen, <c>Attachments_AreNotRead</c> and
/// <c>AttachmentPoints_AreNotImplemented</c> against <c>AttachmentPlacement.Matrix</c> called from
/// <c>EntityModels</c>, and <c>ViewModels_AreNotDrawn</c> against a first-person view whose arms,
/// weapon and off-hand watch all draw. **Five false statements in the document this project uses to
/// decide what to build next** — and one of them, the cubemap lump, was rebuilt from scratch by
/// someone who believed it.
///
/// The owner's diagnosis is the design here: *"they were suppose to auto start working or you were
/// suppose to keep them updated so they follow what we have integrated"*. The second half is a
/// discipline and disciplines lapse; the first half is a mechanism, so this is the mechanism.
///
/// **It is policed in BOTH directions, which is what makes it survive.** A row names a marker and a
/// probe. If the marker is gone the row must go, or the audit is quietly checking nothing; if the
/// marker is present and the probe says the feature works, the marker must go. The first half is the
/// one that is easy to leave out and is why this file's own first version failed for ever after the
/// markers were deleted — it asked "does the feature work" instead of "does anything still claim it
/// does not".
///
/// **It FAILS rather than skips.** A marker that has outlived its gap is not a gap; it is a wrong
/// entry in the map, and a wrong map means the next person builds something twice.
/// </remarks>
public sealed class ConformanceGapAuditTests
{
    /// <summary>A gap marker, and how to tell whether its gap is still open.</summary>
    /// <param name="Suite">The conformance class holding it, unqualified.</param>
    /// <param name="Test">The marker's method name.</param>
    /// <param name="StillMissing">True while the feature really is absent.</param>
    private sealed record Marker(string Suite, string Test, Func<MapAssets, bool> StillMissing);

    /// <summary>
    /// Every gap marker this audit can check, with the evidence that would settle it.
    /// </summary>
    /// <remarks>
    /// **Named by hand rather than scraped.** A regex over the test files would tie this to how the
    /// markers happen to be written and would go quietly blind the first time one is phrased
    /// differently — the same class of failure it exists to catch.
    ///
    /// Two kinds of probe, and the second is much the stronger. A parameter gap is checked against
    /// <c>MaterialCensus.ImplementedParameters</c>, which is maintained for its own reasons: leaving
    /// a parameter out means the asset log goes on reporting it missing on every map load, and its
    /// own comment says *"Adding a feature means moving its parameter into this set"*. A behavioural
    /// gap is checked by loading a real map and asking whether the feature produced anything, which
    /// measures the output rather than a list somebody keeps.
    /// </remarks>
    private static readonly Marker[] Markers =
    [
        new("SourceConformanceTests", "LightWarpTexture_IsNotImplemented", _ => Unread("$lightwarptexture")),
        new("SourceConformanceTests", "TextureTransforms_AreNotParsed", _ => Unread("$basetexturetransform")),
        new("SourceConformanceTests", "EyeRefract_IsNotImplemented", _ => Unread("$iris")),
    ];

    // **Two rows stood here for `Cubemaps_AreNotRead` and `EnvironmentMaps_AreNotImplemented` and
    // both markers had just been deleted.** `TheAudit_NamesOnlyMarkersThatStillExist` caught it on
    // the first run, which is the whole reason that test is here: a row for a marker that no longer
    // exists checks nothing while looking exactly like coverage.
    //
    // **And a third stood here for `$normalmapalphaenvmapmask`, which is the mechanism working as
    // designed rather than another lapse.** The marker was written, the feature was implemented in
    // the same session, the parameter moved into `MaterialCensus.Implemented` — and this audit went
    // red naming the marker to delete. That is the loop the owner asked for: nobody had to remember.

    [Test]
    public void GapMarkers_WhoseFeatureNowWorks_AreReported()
    {
        MapAssets assets = MapCache.Load();

        string[] stale =
        [
            .. Markers
                .Where(marker => Method(marker) is not null && !marker.StillMissing(assets))
                .Select(marker => $"{marker.Suite}.{marker.Test}"),
        ];

        stale.ShouldBeEmpty(
            "these markers claim a feature is unimplemented that demonstrably works; delete the " +
            "test, its row here, and its section in docs/CONFORMANCE.md");
    }

    [Test]
    public void TheAudit_NamesOnlyMarkersThatStillExist()
    {
        // **The half that is easy to omit, and omitting it is what makes an audit rot.** A row for a
        // deleted marker checks nothing while looking like coverage — and the next person adding a
        // row copies its shape without noticing the subject is gone.
        string[] missing =
        [
            .. Markers.Where(marker => Method(marker) is null)
                .Select(marker => $"{marker.Suite}.{marker.Test}"),
        ];

        missing.ShouldBeEmpty("a row naming a marker that no longer exists checks nothing; remove it");
    }

    [Test]
    public void TheAudit_CoversEveryMarkerThatCanBeChecked()
    {
        // Pinned to an exact count so a new marker reddens here and has to be classified: either a
        // parameter gap, or a behavioural one, or something neither can see — which is worth
        // knowing explicitly rather than by absence.
        //
        // Raise this WITH the row, never on its own. A count lowered to make a run pass is the
        // failure this whole file is about.
        Markers.Length.ShouldBe(3, "every checkable gap marker needs a row here to be policed");

        Markers.Select(marker => $"{marker.Suite}.{marker.Test}").Distinct(StringComparer.Ordinal)
            .Count().ShouldBe(Markers.Length, "two rows must not name the same marker");
    }

    /// <summary>The marker's method, or null when it has been removed.</summary>
    private static MethodInfo? Method(Marker marker) =>
        typeof(ConformanceGapAuditTests).Assembly
            .GetType($"Tf2DemoSalvage.Viewer3D.Tests.{marker.Suite}")
            ?.GetMethod(marker.Test, BindingFlags.Public | BindingFlags.Instance);

    /// <summary>Whether the census still counts a parameter as unimplemented.</summary>
    private static bool Unread(string parameter) =>
        !MaterialCensus.ImplementedParameters.Contains(parameter, StringComparer.OrdinalIgnoreCase);
}
