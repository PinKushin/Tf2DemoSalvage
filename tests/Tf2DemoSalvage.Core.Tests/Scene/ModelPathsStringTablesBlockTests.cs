using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// <see cref="DemoTimeline.ModelPaths"/> for an entity whose model only the <c>dem_stringtables</c> block precaches (B452).
/// </summary>
/// <remarks>
/// **The shape is a real recording's.** The 2011 koth_viaduct POV (retired from gcor 2026-10-29, kept in lcor) creates
/// <c>modelprecache</c> with 552 entries in its signon, then writes a block of 553, and its <c>CBaseAnimating</c> 308
/// enters with <c>m_nModelIndex 552</c> — <c>models/props_forest/bird.mdl</c>, named nowhere but the block. The engine
/// rebuilds each table from the block (<c>StringTableReplaceTests</c>' disassembly), so the entity's model resolves.
/// </remarks>
public sealed class ModelPathsStringTablesBlockTests
{
    private const string Bird = "models/props_forest/bird.mdl";

    /// <summary>The index only the block states: the signon names 0 and 1.</summary>
    private const int BlockOnlyIndex = 2;

    [Test]
    public void ModelPaths_AModelOnlyTheBlockPrecaches_IncludesIt()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.DemoAfterABlock(BlockOnlyIndex, Block()));

        timeline.ModelPaths().ShouldContain(Bird);
    }

    [Test]
    public void ModelPaths_TheSameDemoWithoutTheBlock_LeavesItOut()
    {
        // The control: without the block the index names nothing, so the path above came from the block.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.DemoAfterABlock(BlockOnlyIndex, block: null));

        timeline.ModelPaths().ShouldNotContain(Bird);
    }

    /// <summary>A block whose <c>modelprecache</c> is the signon's two entries and one more.</summary>
    private static byte[] Block() => Net.DemoStringTablesTests.Block(
        (ModelPrecache.TableName,
        [
            (string.Empty, null),
            (SyntheticProp.ModelPath, null),
            (Bird, null),
        ]));
}
