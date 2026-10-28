using System;
using System.Linq;
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
    /// **This is `vrad`'s rule, and the renderer's is different** (B259, read 2026-10-28): the world
    /// walk's mark pass skips a TARGET leaf of cluster −1 (engine.dll `0x1801cadb6`), and only an EYE
    /// of cluster −1 stamps everything — see `Leaves_FromAClusterForALeafWithNoCluster_NeverListIt`.
    /// This test pins the lighting compiler's rule as published, not the cull's.
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

    /// <summary>That the walk never enters a node no stamped leaf lies under, testing the stamp before the box.</summary>
    /// <remarks>
    /// **Read from engine.dll (x64, installed 2026-10-07), in disassembly.** `R_RecursiveWorldNode` (`0x1800e1800`)
    /// returns on contents 1 (`0x1800e180f`), then on `visframe != r_visframecount` (`0x1800e1850`-`0x1800e185a`), and
    /// only then clips the node's box (`0x1800e188c`). The mark pass (`0x1801cacd0`) stamps a PVS leaf and each parent
    /// up to the first already stamped (`0x1801caddd`, `0x1801caee0`-`0x1801caffb`). So a subtree the PVS hides is not
    /// walked: no leaf listed, and no node step.
    ///
    /// The eye stands in leaf 3 (cluster 3), whose row sees clusters 3 and 0. The whole tree is in the frustum, so node
    /// 1 (the +x half) is pruned by the PVS alone. The control is the same view with no PVS, where node 1 is walked.
    /// </remarks>
    [Test]
    public void Walk_ASubtreeWithNoPvsVisibleLeaf_IsNeverEntered()
    {
        WorldVisibility open = new(WorldVisibilityTests.Quarters(), BspVisibility.None);
        open.Leaves(-400f, 0f, 0f, WorldVisibilityTests.AheadFromMinus400);
        open.Walk.Where(step => step.IsNode).Select(step => step.Index).ShouldBe([2, 0, 1]);

        WorldVisibility culled = new(
            WorldVisibilityTests.Quarters(), WorldVisibilityTests.Pvs(0x0F, 0x0F, 0x0F, 0b1001));
        culled.Leaves(-400f, 0f, 0f, WorldVisibilityTests.AheadFromMinus400);

        WorldVisibilityTests.Steps(culled).ShouldBe([(3, false), (2, true), (0, false), (0, true)]);
    }

    /// <summary>That from a real cluster a leaf with no cluster is not stamped, so neither it nor its parent is walked.</summary>
    /// <remarks>
    /// **Read: `0x1801cadb0`-`0x1801cadb9`** — `MOVSX RAX, word [leaf + 0x40]`, `CMP EAX, -1`, `JZ` past the stamp.
    /// `vrad`'s `PVSCheck` treats cluster −1 as visible, but that is the lighting compiler's rule, not the renderer's.
    /// Leaf 0 has no cluster; cluster 3's row sees 3 and 0. Leaf 3 stamps node 2 and the root; node 1 is never stamped.
    /// </remarks>
    [Test]
    public void Leaves_FromAClusterForALeafWithNoCluster_NeverListIt()
    {
        WorldVisibility visibility = new(
            WorldVisibilityTests.Quarters(cluster0: -1), WorldVisibilityTests.Pvs(0x0F, 0x0F, 0x0F, 0b1001));

        visibility.Leaves(-400f, 0f, 0f, WorldVisibilityTests.AheadFromMinus400).ShouldBe([3]);
        WorldVisibilityTests.Steps(visibility).ShouldBe([(3, false), (2, true), (0, true)]);
    }

    /// <summary>That a solid leaf in the PVS still stamps the nodes above it, and one with no cluster does not.</summary>
    /// <remarks>
    /// **Read: the mark pass `0x1801cacd0` has no contents test** — between the cluster test (`0x1801cadb6`), the PVS
    /// bit (`0x1801cadcf`) and the stamp (`0x1801caddd`) nothing reads `leaf + 0`. So node 1, over two solid leaves in
    /// visible clusters, is entered, and the walk's own solid return (`0x1800e180f`) keeps its leaves unlisted. A
    /// compiled solid leaf has cluster −1, and then node 1 is pruned.
    /// </remarks>
    [Test]
    public void Walk_ASolidLeafInThePvs_StampsTheNodesAboveIt()
    {
        BspVisibility seesAll = WorldVisibilityTests.Pvs(0x0F, 0x0F, 0x0F, 0x0F);

        WorldVisibility clustered = new(WorldVisibilityTests.Quarters(solidPlusX: true), seesAll);
        clustered.Leaves(-400f, 0f, 0f, WorldVisibilityTests.AheadFromMinus400).ShouldBe([3, 0]);
        WorldVisibilityTests.Steps(clustered).ShouldBe([(3, false), (2, true), (0, false), (0, true), (1, true)]);

        WorldVisibility unclustered = new(
            WorldVisibilityTests.Quarters(solidPlusX: true, plusXNoCluster: true), seesAll);
        unclustered.Leaves(-400f, 0f, 0f, WorldVisibilityTests.AheadFromMinus400).ShouldBe([3, 0]);
        WorldVisibilityTests.Steps(unclustered).ShouldBe([(3, false), (2, true), (0, false), (0, true)]);
    }

    /// <summary>That an eye in no cluster stamps every leaf and node, entering even a subtree of solid leaves.</summary>
    /// <remarks>
    /// **Read: `0x1801ca701`-`0x1801ca712`** sets a flag when a view cluster is −1, and `0x1801ca72f` then takes the
    /// mark-everything path (`0x1801caa53`-`0x1801caac8`: every leaf's and every node's `visframe`). The eye stands in
    /// leaf 0, which has no cluster.
    /// </remarks>
    [Test]
    public void Walk_FromAnEyeInNoCluster_EntersEveryNode()
    {
        WorldVisibility visibility = new(
            WorldVisibilityTests.Quarters(cluster0: -1, solidPlusX: true, plusXNoCluster: true),
            WorldVisibilityTests.Pvs(0x0F, 0x0F, 0x0F, 0x0F));

        visibility.Leaves(-100f, 0f, 0f, default).ShouldBe([0, 3]);
        visibility.Walk.Where(step => step.IsNode).Select(step => step.Index).ShouldBe([2, 0, 1]);
    }

    private static string Sdk(string relativePath) =>
        Skip.Unless(SourceSdk.Text(relativePath), SourceSdk.Missing);

    private static string Flat(string source) =>
        Regex.Replace(source, @"[ \t]+", " ", RegexOptions.None, Limit);
}
