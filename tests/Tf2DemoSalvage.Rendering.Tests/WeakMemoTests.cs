using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The map cache's release: shared while held, gone once not (D213).</summary>
public sealed class WeakMemoTests
{
    /// <remarks>**Held, it is served again without a load**: the same object, one load.</remarks>
    [Test]
    public void Get_WhileTheValueIsHeld_ServesItWithoutLoading()
    {
        WeakMemo<object> memo = new();
        object first = memo.Get("a", () => new object());

        object second = memo.Get("a", () => new object());

        (ReferenceEquals(first, second), memo.Loads).ShouldBe((true, 1));
        GC.KeepAlive(first);
    }

    /// <remarks>**Released, it is collected and loaded again** — what keeps the run from holding every map.</remarks>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The subject is collection: the test must force one to observe a released value going.")]
    public void Get_AfterEveryHolderLetGo_LoadsAgain()
    {
        WeakMemo<object> memo = new();
        LoadAndDrop(memo);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _ = memo.Get("a", () => new object());

        memo.Loads.ShouldBe(2);
    }

    /// <remarks>**Two asks at once load once**: the second waits on the first's load.</remarks>
    [Test]
    public async Task Get_TwoAsksAtOnce_LoadOnce()
    {
        WeakMemo<object> memo = new();
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        object Load()
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue();
            return new object();
        }

        Task<object> first = Task.Run(() => memo.Get("a", Load));
        started.Wait(TimeSpan.FromSeconds(30)).ShouldBeTrue();
        Task<object> second = Task.Run(() => memo.Get("a", Load));
        SpinWait.SpinUntil(() => memo.Waiting == 2, TimeSpan.FromSeconds(30)).ShouldBeTrue("the second ask must be waiting on the first's load before it is released");
        release.Set();

        object[] both = await Task.WhenAll(first, second).ConfigureAwait(false);

        (ReferenceEquals(both[0], both[1]), memo.Loads).ShouldBe((true, 1));
    }

    /// <remarks>**A failed load is not remembered**: the next ask loads again rather than rethrowing the old failure.</remarks>
    [Test]
    public void Get_AfterAFailedLoad_LoadsAgain()
    {
        WeakMemo<object> memo = new();
        Should.Throw<InvalidOperationException>(() => memo.Get("a", () => throw new InvalidOperationException("no map")));

        object loaded = memo.Get("a", () => new object());

        (loaded is not null, memo.Loads).ShouldBe((true, 2));
    }

    /// <remarks>**A collected entry is pruned** on the next miss, so the table holds only what is alive.</remarks>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Major Code Smell",
        "S1215:\"GC.Collect\" should not be called",
        Justification = "The subject is collection: the test must force one to observe a released value going.")]
    public void Get_AMissAfterAnEntryWasCollected_PrunesIt()
    {
        WeakMemo<object> memo = new();
        LoadAndDrop(memo);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        object other = memo.Get("b", () => new object());

        memo.Tracked.ShouldBe(1);
        GC.KeepAlive(other);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadAndDrop(WeakMemo<object> memo) => _ = memo.Get("a", () => new object());
}
