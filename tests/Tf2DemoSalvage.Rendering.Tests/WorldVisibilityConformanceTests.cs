using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What the engine decides is visible before it draws the world, and from which data.
/// </summary>
/// <remarks>
/// **Written before this project culled any world geometry.** The world is uploaded once, batched by
/// material across the whole map, and drawn in full every frame regardless of where the camera is —
/// so nothing here describes what was built.
///
/// **The BSP walk itself is engine-side and not published**, which decides how much of this can be
/// a citation. What IS published pins the shape of the answer rather than the algorithm:
///
/// * `WorldListInfo_t` (`public/ivrenderview.h:87`) is what `BuildWorldLists` fills in, and it is a
///   **list of LEAVES** — `m_LeafCount` and `m_pLeafList` — not a list of surfaces. So the unit of
///   world visibility is the leaf, and surfaces follow from it.
/// * `dleaf_t` carries `cluster`, a `mins`/`maxs` pair Valve's own comment marks *"for frustum
///   culling"*, and `firstleafface`/`numleaffaces` into the LEAFFACES lump. Those four fields are
///   exactly what a leaf-based cull needs and the only fields it needs.
/// * `PVSCheck` (`utils/vrad/vrad.h:372`) is the published PVS test, bit arithmetic and all —
///   including its rule for a cluster of −1.
/// * `CViewRender::SetupVis` sets visibility up from **the view origin**, one point, before
///   anything is built.
///
/// So the parts asserted here are the data and the rules; the traversal order is not published and
/// is not claimed.
/// </remarks>
public sealed class WorldVisibilityConformanceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private const string RenderView = "src/public/ivrenderview.h";
    private const string BspFile = "src/public/bspfile.h";
    private const string Vrad = "src/utils/vrad/vrad.h";
    private const string ViewRender = "src/game/client/viewrender.cpp";

    /// <summary>That what the engine builds for a view is a list of leaves.</summary>
    /// <remarks>
    /// **This is the decision the whole design rests on.** A surface-keyed visibility structure and
    /// a leaf-keyed one lead to different code everywhere downstream, and Valve's is leaf-keyed:
    /// `BuildWorldLists` hands back leaves and the surfaces are read out of them.
    /// </remarks>
    [Test]
    public void Sdk_WhatBuildWorldListsProduces_IsAListOfLeaves()
    {
        string source = Flat(Sdk(RenderView));

        source.ShouldContain("Describes the leaves to be rendered this view, set by BuildWorldLists");

        Match info = Regex.Match(
            source,
            @"struct WorldListInfo_t\s*\{\s*int m_ViewFogVolume;\s*int m_LeafCount;\s*"
            + @"LeafIndex_t\* m_pLeafList;",
            RegexOptions.Singleline,
            Limit);

        info.Success.ShouldBeTrue("the world list is a leaf count and a leaf list");
    }

    /// <summary>That a leaf carries its cluster, its cull box and its face range.</summary>
    /// <remarks>
    /// **The field ORDER is the assertion, because the offsets follow from it.** `firstleafface`
    /// sits at byte 20 only because four fields totalling twenty bytes precede it, and this project
    /// reads it at that offset in both versions of the struct. Valve's comment *"for frustum
    /// culling"* on `mins` is quoted rather than paraphrased: it says what the box is FOR, which is
    /// why a loose box is the right one to test against.
    /// </remarks>
    [Test]
    public void Sdk_ALeaf_CarriesItsClusterCullBoxAndFaceRange()
    {
        string source = Flat(Sdk(BspFile));

        Match leaf = Regex.Match(
            source,
            @"struct dleaf_t\s*\{(.*?)\n\};",
            RegexOptions.Singleline,
            Limit);

        leaf.Success.ShouldBeTrue("dleaf_t is the leaf on disk");

        string body = leaf.Groups[1].Value;

        Match order = Regex.Match(
            body,
            @"int contents;.*?short cluster;.*?short area:9;\s*short flags:7;.*?"
            + @"short mins\[3\]; // for frustum culling\s*short maxs\[3\];\s*"
            + @"unsigned short firstleafface;\s*unsigned short numleaffaces;",
            RegexOptions.Singleline,
            Limit);

        order.Success.ShouldBeTrue(
            "contents, cluster, area/flags, mins, maxs, then the face range");
    }

    /// <summary>That an unknown cluster is treated as VISIBLE, not as hidden.</summary>
    /// <remarks>
    /// **The rule most likely to be got backwards, and Valve states its reason in the code:**
    ///
    /// <code>
    /// if ( iCluster >= 0 ) { return pvs[iCluster >> 3] &amp; ( 1 &lt;&lt; ( iCluster &amp; 7 ) ); }
    /// else {
    ///     // PointInLeaf still returns -1 for valid points sometimes and rather than
    ///     // have black samples, we assume the sample is in the PVS.
    ///     return 1;
    /// }
    /// </code>
    ///
    /// A cull that answered "not visible" for cluster −1 would be doing the opposite of what the
    /// engine does with the same value, and it would do it for solid leaves and for points the
    /// tree walk cannot place — which for a free camera is routine.
    ///
    /// **The bit arithmetic is asserted too**, because `1 &lt;&lt; (c &amp; 7)` and `0x80 &gt;&gt;
    /// (c &amp; 7)` are both plausible-looking and only one matches the lump.
    /// </remarks>
    [Test]
    public void Sdk_PvsCheck_TreatsAnUnknownClusterAsVisible()
    {
        string source = Flat(Sdk(Vrad));

        Match check = Regex.Match(
            source,
            @"inline byte PVSCheck\( const byte \*pvs, int iCluster \)\s*\{\s*"
            + @"if \( iCluster >= 0 \)\s*\{\s*"
            + @"return pvs\[iCluster >> 3\] & \( 1 << \( iCluster & 7 \) \);\s*\}\s*"
            + @"else\s*\{(.*?)return 1;",
            RegexOptions.Singleline,
            Limit);

        check.Success.ShouldBeTrue("PVSCheck answers 1 for a negative cluster");

        check.Groups[1].Value.ShouldContain("we assume the sample is in the PVS");
    }

    /// <summary>That visibility is set up from the view origin, as one point.</summary>
    /// <remarks>
    /// **One origin in the ordinary case**, which is what this project needs; the array form exists
    /// for portals and mirrors, which merge several viewpoints' visibility. Pinned so that adding
    /// a second origin later is a deliberate change rather than a discovery.
    /// </remarks>
    [Test]
    public void Sdk_SetupVis_UsesTheViewOriginAsTheVisOrigin()
    {
        string source = Flat(Sdk(ViewRender));

        Match setup = Regex.Match(
            source,
            @"// Use render origin as vis origin by default\s*"
            + @"render->ViewSetupVisEx\( ShouldForceNoVis\(\), 1, &viewRender.origin, visFlags \);",
            RegexOptions.Singleline,
            Limit);

        setup.Success.ShouldBeTrue("the view origin is the vis origin");
    }

    /// <summary>That this project reads a leaf's face range where Valve declares it.</summary>
    /// <remarks>
    /// **The link between the citation above and the code, chosen to be the one nothing else
    /// covers.** The PVS bit arithmetic already has its link in
    /// `BspVisibilityConformanceTests` (Content.Tests), and repeating it here would mean a second
    /// copy of that suite's lump builder — a fixture helper duplicated is a fixture helper that
    /// drifts.
    ///
    /// What is asserted instead is the offset this work added: `firstleafface` at 20 and
    /// `numleaffaces` at 22, following `contents`(4) + `cluster`(2) + `area`/`flags`(2) +
    /// `mins`(6) + `maxs`(6). The bytes below are one leaf carrying a face range of 7..10 and
    /// nothing else; a reader off by one field answers with a cluster or a coordinate instead.
    /// </remarks>
    [Test]
    public void LeafFaces_ForALeafOnDisk_ComeFromOffsetsTwentyAndTwentyTwo()
    {
        byte[] leaf = new byte[32];

        BitConverter.TryWriteBytes(leaf.AsSpan(0), 1);           // contents
        BitConverter.TryWriteBytes(leaf.AsSpan(4), (short)3);    // cluster
        BitConverter.TryWriteBytes(leaf.AsSpan(8), (short)-64);  // mins.x
        BitConverter.TryWriteBytes(leaf.AsSpan(14), (short)64);  // maxs.x
        BitConverter.TryWriteBytes(leaf.AsSpan(20), (ushort)7);  // firstleafface
        BitConverter.TryWriteBytes(leaf.AsSpan(22), (ushort)10); // numleaffaces

        BspLeafTree tree = BspLeafTree.FromLumps(default, default, leaf);

        tree.LeafFaces(0).ShouldBe((7, 10));
        tree.Cluster(0).ShouldBe(3, "the neighbouring field, so a shifted read shows up here too");
    }

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
