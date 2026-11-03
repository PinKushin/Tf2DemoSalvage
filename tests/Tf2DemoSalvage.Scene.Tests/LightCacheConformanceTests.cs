using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The engine's light cache memory: `LightcacheGet` (`engine.dll` `0x1801b9cd0`). 200 entries (`0x1e8` bytes each), keyed on
/// the cell `((int)c + 0x8000) >> 5` (`>> 7` in z) and the point's leaf, most recently used first (`0x1801baeb0`); a miss
/// takes the least recently used entry (`DAT_180773316`). `lightcache_maxmiss` (default 2, `0x18000fde0`) misses are built
/// per frame once 60 frames have passed; past that a caller allowing it (flag 8, which model lighting passes in `0xf`,
/// `0x1800f1bd0`) gets the nearest entry by cell, a different leaf counting 2 — never moved to the front.
/// </summary>
public sealed class LightCacheConformanceTests
{
    private static readonly Vector3 Here = new(10f, 10f, 10f);

    [Test]
    public void Get_TheSameCellAndLeafTwice_BuildsOnce()
    {
        LightCache<int> cache = new(unbuilt: -1);
        int builds = 0;

        cache.Get(Here, 5, 100, allowFast: true, () => ++builds).ShouldBe(1);
        cache.Get(Here + new Vector3(20f, 0f, 0f), 5, 100, allowFast: true, () => ++builds).ShouldBe(1, "x 30 is still cell 0");
        builds.ShouldBe(1);
    }

    [Test]
    public void Get_TheSameCellInAnotherLeaf_IsAnotherEntry()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(Here, 5, 1, allowFast: true, () => 1);
        cache.Get(Here, 6, 1, allowFast: true, () => 2).ShouldBe(2);
    }

    /// <remarks>`(int)` truncates toward zero before the bias: −0.5 becomes 0 and shares +1's cell, where flooring would put it with −1.</remarks>
    [Test]
    public void Get_MinusAHalf_SharesTheCellOfPlusOne()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(new Vector3(1f, 0f, 0f), 0, 1, allowFast: true, () => 1);
        cache.Get(new Vector3(-0.5f, 0f, 0f), 0, 1, allowFast: true, () => 2).ShouldBe(1);
    }

    [Test]
    public void Get_PastTheMissBudgetAfterSixtyFrames_TakesTheNearestEntry()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(new Vector3(0f, 0f, 0f), 0, 100, allowFast: true, () => 1);
        cache.Get(new Vector3(320f, 0f, 0f), 0, 100, allowFast: true, () => 2);

        // Both misses this frame are spent; a third cell reuses the nearest: cell 3 is 3 from cell 0 and 7 from cell 10.
        cache.Get(new Vector3(100f, 0f, 0f), 0, 100, allowFast: true, () => 3).ShouldBe(1);
    }

    /// <remarks>
    /// **The engine's build cannot throw; ours can, so what a throw leaves is ours to keep straight.** The victim's key is gone by the build. Left marked built with
    /// that old key, its later eviction would drop the key again, which by then may name a slot that rebuilt it: that entry is built a second time for nothing.
    /// </remarks>
    [Test]
    public void Get_AfterABuildThatThrew_TheVictimsLaterEvictionLeavesItsOldKeyAlone()
    {
        LightCache<int> cache = new(unbuilt: -1);
        static Vector3 Cell(int index) => new(index * 32f, 0f, 0f);
        const int Frame = 1;

        for (int index = 0; index < LightCache<int>.Capacity; index++)
        {
            cache.Get(Cell(index), 0, Frame, allowFast: true, () => 1);
        }

        // Cell 0 is at the back, so it is the victim; asked again it is built in cell 1's slot.
        Should.Throw<InvalidOperationException>(() =>
            cache.Get(Cell(1000), 0, Frame, allowFast: true, () => throw new InvalidOperationException("the trace failed")));
        cache.Get(Cell(0), 0, Frame, allowFast: true, () => 777).ShouldBe(777, "the control: the key was gone, so it built");

        // Cells 2 to 199, then the slot that threw: 199 misses, with cell 0's own slot still in front of it.
        for (int index = 0; index < LightCache<int>.Capacity - 1; index++)
        {
            cache.Get(Cell(2000 + index), 0, Frame, allowFast: true, () => 2);
        }

        cache.Get(Cell(0), 0, Frame, allowFast: true, () => 888).ShouldBe(777);
    }

    [Test]
    public void Get_PastTheMissBudgetWithoutAllowFast_BuildsAnyway()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(new Vector3(0f, 0f, 0f), 0, 100, allowFast: false, () => 1);
        cache.Get(new Vector3(320f, 0f, 0f), 0, 100, allowFast: false, () => 2);
        cache.Get(new Vector3(100f, 0f, 0f), 0, 100, allowFast: false, () => 3).ShouldBe(3);
    }

    [Test]
    public void Get_InTheFirstSixtyFrames_IsNeverThrottled()
    {
        LightCache<int> cache = new(unbuilt: -1);

        for (int cell = 0; cell < 5; cell++)
        {
            int value = cell + 1;
            cache.Get(new Vector3(cell * 32f, 0f, 0f), 0, 59, allowFast: true, () => value).ShouldBe(value);
        }
    }

    [Test]
    public void Get_ANewFrame_RefillsTheBudget()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(new Vector3(0f, 0f, 0f), 0, 100, allowFast: true, () => 1);
        cache.Get(new Vector3(32f, 0f, 0f), 0, 100, allowFast: true, () => 2);
        cache.Get(new Vector3(64f, 0f, 0f), 0, 101, allowFast: true, () => 3).ShouldBe(3);
    }

    /// <remarks>A different leaf counts 2 against a same-leaf entry's cell distance; the tie keeps the first found.</remarks>
    [Test]
    public void Get_TheNearestAcrossLeaves_CountsAnotherLeafAsTwo()
    {
        LightCache<int> cache = new(unbuilt: -1);

        cache.Get(new Vector3(0f, 0f, 0f), 9, 100, allowFast: true, () => 1);   // cell 0, other leaf: distance 2
        cache.Get(new Vector3(160f, 0f, 0f), 0, 100, allowFast: true, () => 2); // cell 5, same leaf: distance 4

        cache.Get(new Vector3(32f, 0f, 0f), 0, 100, allowFast: true, () => 3).ShouldBe(1);
    }

    /// <remarks>The 201st cell takes the least recently used entry; a hit moves an entry to the front first.</remarks>
    [Test]
    public void Get_TwoHundredAndOneCells_EvictsTheLeastRecentlyUsed()
    {
        LightCache<int> cache = new(unbuilt: -1);

        for (int cell = 0; cell < LightCache<int>.Capacity; cell++)
        {
            int value = cell;
            cache.Get(new Vector3(cell * 32f, 0f, 0f), 0, 1, allowFast: true, () => value);
        }

        cache.Get(new Vector3(0f, 0f, 0f), 0, 1, allowFast: true, () => -9).ShouldBe(0, "cell 0 was used again, so it moves up");
        cache.Get(new Vector3(-320f, 0f, 0f), 0, 1, allowFast: true, () => 500);

        cache.Get(new Vector3(0f, 0f, 0f), 0, 1, allowFast: true, () => -9).ShouldBe(0, "still cached");
        cache.Get(new Vector3(32f, 0f, 0f), 0, 1, allowFast: true, () => 777).ShouldBe(777, "cell 1 was the oldest and went");
    }

    /// <remarks>
    /// A fresh pool is 200 zeroed entries (`0x1801bb640`): unbuilt, and far away (key 0 is cell −1024 unbiased). The
    /// fallback can still reach one when nothing else is cached, and it answers what a zeroed entry holds.
    /// </remarks>
    [Test]
    public void Get_AFallbackWithNothingBuilt_AnswersUnbuilt()
    {
        LightCache<int> cache = new(unbuilt: -1) { MaxMiss = 0 };

        cache.Get(Here, 0, 100, allowFast: true, () => 1).ShouldBe(-1);
    }
}
