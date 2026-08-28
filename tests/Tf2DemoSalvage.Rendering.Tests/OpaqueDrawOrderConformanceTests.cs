using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The order the engine draws opaque models in, and why it is a size sort rather than a list walk.
/// </summary>
/// <remarks>
/// **Written from `viewrender.cpp` and `clientleafsystem.cpp` before this renderer sorted
/// anything.** `Device3D` walks its instance list in whatever order the scene handed it; the engine
/// buckets by size and draws the biggest first, and that difference is worth having before any
/// culling work, because it is the cheap half of the same idea.
///
/// **Why biggest first:** a large object drawn early fills the depth buffer, and everything behind
/// it that comes later fails the depth test before its pixels are shaded. It is occlusion bought
/// with a sort rather than with a visibility structure — which is exactly why it belongs before
/// culling rather than after.
///
/// **The thresholds are Valve's, with Valve's own comments naming what each size IS:**
///
/// <code>
/// float const arrThresholds[ 3 ] = {
///     200.f,  // tree size
///     80.f,   // player size
///     30.f,   // crate size
/// };
/// </code>
///
/// **This is parity in what gets drawn when, which changes the picture** wherever anything blends
/// or wherever depth decides — so it is a conformance question rather than an optimisation.
/// Skipping a redundant material bind is the opposite: it produces byte-identical output, so it is
/// ours to measure rather than Valve's to cite. Only the ordering is here.
/// </remarks>
public sealed class OpaqueDrawOrderConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string LeafSystem = "src/game/client/clientleafsystem.cpp";
    private const string ViewRender = "src/game/client/viewrender.cpp";

    /// <summary>That the buckets are 200, 80 and 30 units, on the largest bounding axis.</summary>
    [Test]
    public void Sdk_TheOpaqueSizeBuckets_AreTwoHundredEightyAndThirty()
    {
        string source = Sdk(LeafSystem);

        Match thresholds = Regex.Match(
            source,
            @"arrThresholds\[\s*3\s*\]\s*=\s*\{\s*([\d.]+)f,[^}]*?([\d.]+)f,[^}]*?([\d.]+)f,",
            RegexOptions.Singleline,
            Limit);

        thresholds.Success.ShouldBeTrue("DetectBucketedRenderGroup declares the size thresholds");

        thresholds.Groups[1].Value.ShouldBe("200.");
        thresholds.Groups[2].Value.ShouldBe("80.");
        thresholds.Groups[3].Value.ShouldBe("30.");
    }

    /// <summary>That the measure is the largest axis of the world-space bounds, not a volume.</summary>
    /// <remarks>
    /// **A tall thin lamp post buckets as huge**, which a volume or a diagonal would not do. That
    /// is deliberate on Valve's part: what fills the depth buffer is the silhouette's extent, and a
    /// post occludes a whole column of the screen.
    /// </remarks>
    [Test]
    public void Sdk_TheBucketMeasure_IsTheLargestBoundingAxis()
    {
        string source = Flat(Sdk(LeafSystem));

        source.ShouldContain("VectorSubtract( absMaxs, absMins, dims );");
        source.ShouldContain(
            "float const fDimension = MAX( MAX( fabs(dims.x), fabs(dims.y) ), fabs(dims.z) );");
    }

    /// <summary>That brush models are drawn before any studio model.</summary>
    [Test]
    public void Sdk_BrushModels_AreDrawnBeforeTheBucketedModels()
    {
        string source = Sdk(ViewRender);

        int brush = source.IndexOf(
            "DrawOpaqueRenderables_DrawBrushModels( pEntitiesBegin, pEntitiesEnd, DepthMode );",
            StringComparison.Ordinal);

        int buckets = source.IndexOf(
            "Draw static props + opaque entities from the biggest bucket to the smallest",
            StringComparison.Ordinal);

        brush.ShouldBeGreaterThan(0, "the brush pass is in DrawOpaqueRenderables");
        buckets.ShouldBeGreaterThan(brush, "and it runs before the bucketed passes");
    }

    /// <summary>That the buckets run biggest first, entities before static props within each.</summary>
    /// <remarks>
    /// Valve's own comment says the direction — *"from the biggest bucket to the smallest"* — and
    /// the loop counts UP from bucket zero, so bucket zero is the huge one. Getting that backwards
    /// would draw the crates first and buy nothing, while looking like a sort was in place.
    /// </remarks>
    [Test]
    public void Sdk_WithinABucket_EntitiesAreDrawnBeforeStaticProps()
    {
        string source = Flat(Sdk(ViewRender));

        source.ShouldContain("Draw static props + opaque entities from the biggest bucket to the smallest");

        Match loop = Regex.Match(
            source,
            @"for \( int bucket = 0; bucket < RENDER_GROUP_CFG_NUM_OPAQUE_ENT_BUCKETS; \+\+ bucket \)\s*\{.*?DrawOpaqueRenderables_Range\( pEnts\[bucket\]\[0\], pEnts\[bucket\]\[1\], DepthMode \);\s*DrawOpaqueRenderables_DrawStaticProps\( pProps\[bucket\]\[0\], pProps\[bucket\]\[1\], DepthMode \);",
            RegexOptions.Singleline,
            Limit);

        loop.Success.ShouldBeTrue(
            "the draw loop counts up from bucket zero, entities then props");
    }

    /// <summary>That this project's thresholds are the same three numbers.</summary>
    /// <remarks>
    /// The link between the citation above and the code — without it the two halves can drift and
    /// each still passes its own test.
    /// </remarks>
    [Test]
    public void OpaqueBuckets_AgainstValvesThresholds_AreTheSame()
    {
        OpaqueBuckets.Thresholds.ToArray().ShouldBe([200f, 80f, 30f]);
    }

    /// <summary>That the bucket a size falls in matches Valve's nesting, boundaries included.</summary>
    /// <remarks>
    /// **The comparisons are `>=`, so a size exactly on a threshold takes the LARGER bucket.**
    /// `fDimension >= arrThresholds[0]` — an object exactly 200 units across is huge, not big. A
    /// reimplementation using `>` puts it one bucket down, which no ordinary content would reveal
    /// because exact sizes are rare, and a crate authored at exactly 30 would.
    /// </remarks>
    [TestCase(500f, 0)]
    [TestCase(200f, 0)]
    [TestCase(199.9f, 1)]
    [TestCase(80f, 1)]
    [TestCase(79.9f, 2)]
    [TestCase(30f, 2)]
    [TestCase(29.9f, 3)]
    [TestCase(0f, 3)]
    public void BucketFor_AtAndAroundEachThreshold_MatchesValvesNesting(float size, int bucket)
    {
        OpaqueBuckets.BucketFor(size).ShouldBe(bucket);
    }

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
