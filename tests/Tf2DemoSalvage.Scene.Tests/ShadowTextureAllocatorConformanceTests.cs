using Tf2DemoSalvage.Scene.ProjectedShadows;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// <c>CTextureAllocator</c> (`clientshadowmgr.cpp:117-673`): the render-to-texture shadow atlas, one 1024² page cut into
/// sixteen 256² blocks of 16, 32, 64, 128 and 256 texel cells, an LRU per cell size (finding 107).
/// </summary>
/// <remarks>
/// **The cells' order is the engine's.** `Reset` adds the blocks 0 to 15 in turn, and `AddBlockToLRU` links each block's
/// cells to the HEAD of its size's list from the last index down (`:361-381`), so the head of a size's list — the first
/// cell `UseTexture` takes — is index 0 of the LAST block of that size. Every expected rectangle below follows from that:
/// a block sits at `(block % 4, block / 4) × 256`, a cell at `(index % perRow, index / perRow) × size` inside it
/// (`GetTextureRect`, `:654-673`).
/// </remarks>
public sealed class ShadowTextureAllocatorConformanceTests
{
    private const float Huge = 1e9f;

    [Test]
    public void UseTexture_ALargeTextureOnAFreshPage_TakesTheLastLargeBlock()
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(256);

        atlas.UseTexture(texture, willRedraw: true, Huge).ShouldBeTrue("a new cell needs drawing");

        // The 256² cells are blocks 10–15, one each; block 15 is linked last, so it is the head: (3, 3) × 256.
        atlas.TextureRect(texture).ShouldBe((768, 768, 256, 256));
    }

    [Test]
    public void UseTexture_TwoSmallTextures_TakeBlockZerosFirstTwoCells()
    {
        ShadowTextureAllocator atlas = new();
        int first = atlas.AllocateTexture(16);
        int second = atlas.AllocateTexture(16);

        atlas.UseTexture(first, willRedraw: true, 0f);
        atlas.UseTexture(second, willRedraw: true, 0f);

        // Block 0 is the only 16² block: index 0 is the head, index 1 next — sixteen to a row.
        atlas.TextureRect(first).ShouldBe((0, 0, 16, 16));
        atlas.TextureRect(second).ShouldBe((16, 0, 16, 16));
    }

    /// <remarks>The desired size grows from 16 while its square is below the screen area: 1600 asks for 64 (4096 ≥ 1600).</remarks>
    [Test]
    public void UseTexture_AnAreaOf1600_TakesA64Cell()
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(256);

        atlas.UseTexture(texture, willRedraw: true, 1600f);

        // The 64² cells are blocks 2 and 3; block 3 is linked last, so its index 0: (768, 0).
        atlas.TextureRect(texture).ShouldBe((768, 0, 64, 64));
    }

    /// <remarks>Never larger than the texture's own size: a 32 texture asked for a huge area still takes a 32 cell, block 1.</remarks>
    [Test]
    public void UseTexture_AHugeAreaForASmallTexture_StopsAtItsOwnSize()
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(32);

        atlas.UseTexture(texture, willRedraw: true, Huge);

        atlas.TextureRect(texture).ShouldBe((256, 0, 32, 32));
    }

    [TestCase(8, 16)]
    [TestCase(1024, 256)]
    public void AllocateTexture_ASizeOutsideTheCells_IsClamped(int asked, int cell)
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(asked);

        atlas.UseTexture(texture, willRedraw: true, Huge);

        atlas.TextureRect(texture).Width.ShouldBe(cell);
    }

    /// <remarks>
    /// **A cell one size too small is kept unless the shadow redraws anyway** (`bShouldKeepTexture`, `:538`): no copy is
    /// worth a redraw. The control redraws, and so moves to the cell it wants.
    /// </remarks>
    [Test]
    public void UseTexture_OneSizeTooSmallWithoutARedraw_KeepsItsCell()
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(256);
        atlas.UseTexture(texture, willRedraw: true, 1600f);
        atlas.AdvanceFrame();

        atlas.UseTexture(texture, willRedraw: false, 128f * 128f).ShouldBeFalse();
        atlas.TextureRect(texture).ShouldBe((768, 0, 64, 64));

        atlas.UseTexture(texture, willRedraw: true, 128f * 128f).ShouldBeTrue();
        atlas.TextureRect(texture).Width.ShouldBe(128);
    }

    /// <remarks>
    /// **Too many of one size in one frame takes a smaller one** (`:560-571`): the six 256² cells are all used this frame,
    /// so the seventh 256 texture falls to the 128² cells, whose head is block 9's index 0: (256, 512).
    /// </remarks>
    [Test]
    public void UseTexture_ASeventhLargeTextureInOneFrame_TakesA128Cell()
    {
        ShadowTextureAllocator atlas = new();

        for (int at = 0; at < 6; at++)
        {
            atlas.UseTexture(atlas.AllocateTexture(256), willRedraw: true, Huge);
        }

        int seventh = atlas.AllocateTexture(256);

        atlas.UseTexture(seventh, willRedraw: true, Huge).ShouldBeTrue();
        atlas.TextureRect(seventh).ShouldBe((256, 512, 128, 128));
    }

    /// <remarks>
    /// **On a later frame the least recently used cell is taken from its owner** (`:599-604`): the seventh large texture,
    /// a frame on, takes the first one's cell, and the first has none left.
    /// </remarks>
    [Test]
    public void UseTexture_ALaterFrameWithEveryLargeCellTaken_EvictsTheLeastRecentlyUsed()
    {
        ShadowTextureAllocator atlas = new();
        int first = atlas.AllocateTexture(256);
        atlas.UseTexture(first, willRedraw: true, Huge);

        for (int at = 0; at < 5; at++)
        {
            atlas.UseTexture(atlas.AllocateTexture(256), willRedraw: true, Huge);
        }

        atlas.AdvanceFrame();
        int late = atlas.AllocateTexture(256);

        atlas.UseTexture(late, willRedraw: true, Huge).ShouldBeTrue();
        atlas.TextureRect(late).ShouldBe((768, 768, 256, 256), "the first texture's cell, block 15");
        atlas.HasValidTexture(first).ShouldBeFalse();
    }

    /// <remarks>A cell already the size wanted is kept and reports no redraw; the frame's use moves it to the tail.</remarks>
    [Test]
    public void UseTexture_TheSameSizeAgain_KeepsItsCellWithNoRedraw()
    {
        ShadowTextureAllocator atlas = new();
        int texture = atlas.AllocateTexture(64);
        atlas.UseTexture(texture, willRedraw: true, Huge);
        atlas.AdvanceFrame();

        atlas.UseTexture(texture, willRedraw: true, Huge).ShouldBeFalse();
        atlas.TextureRect(texture).ShouldBe((768, 0, 64, 64));
    }

    /// <remarks>A freed cell goes to the head of its list (`MarkUnused`, `:411-419`), so the next texture of that size takes it.</remarks>
    [Test]
    public void DeallocateTexture_ItsCell_IsTheNextTaken()
    {
        ShadowTextureAllocator atlas = new();
        int first = atlas.AllocateTexture(16);
        int second = atlas.AllocateTexture(16);
        atlas.UseTexture(first, willRedraw: true, 0f);
        atlas.UseTexture(second, willRedraw: true, 0f);

        atlas.DeallocateTexture(first);
        int third = atlas.AllocateTexture(16);
        atlas.UseTexture(third, willRedraw: true, 0f);

        atlas.TextureRect(third).ShouldBe((0, 0, 16, 16));
    }
}
