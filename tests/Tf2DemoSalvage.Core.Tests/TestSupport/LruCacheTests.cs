using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.TestSupport;

/// <summary>
/// That the corpus suite's timeline cache keeps only the values it built most recently, builds each
/// once while it keeps it, and hands back one a caller still holds rather than building it again.
/// </summary>
/// <remarks>
/// **Why it is bounded at all** (B439). <c>TimelineCache</c> held every demo's timeline for the life
/// of the corpus run; with the 49 local demos present the test host reached 39 GB private on a 32 GB
/// machine and was stopped (B438). One local timeline alone measures up to 4.7 GB live, so no count
/// of them fits, and the cache has to let them go.
///
/// **What is measured, and how.** Whether the cache still holds a value is asked of the garbage
/// collector through a weak reference; whether it built one is a count the factory keeps; whether two
/// callers share one is object identity. Threads meet on conditions — a build has started, a caller
/// is blocked — never on a clock, so nothing here passes or fails by being fast. <see cref="Tripwire"/>
/// only stops a hang from becoming a stuck run.
///
/// The values are plain objects, not timelines: the policy is the subject and does not depend on what
/// is kept. The corpus suite exercises it on real timelines.
/// </remarks>
public sealed class LruCacheTests
{
    /// <summary>How many threads ask at once where concurrency is the question.</summary>
    private const int Callers = 8;

    /// <summary>How long a condition may take before the test gives up: a hang tripwire, not a timing.</summary>
    private static readonly TimeSpan Tripwire = TimeSpan.FromSeconds(30);

    [Test]
    public void Get_TheSameKeyTwice_BuildsOnceAndReturnsTheSameValue()
    {
        Counter builds = new();
        LruCache<string, object> cache = new(2, builds.Build);

        object first = cache.Get("a");
        object second = cache.Get("a");

        second.ShouldBeSameAs(first);
        builds.Of("a").ShouldBe(1);
    }

    [Test]
    public void Get_PastCapacity_ReleasesTheLeastRecentlyUsed()
    {
        LruCache<string, object> cache = new(2, _ => new object());

        (WeakReference a, WeakReference b, WeakReference c) = AskInTurn(cache);

        CollectEverythingUnreachable();

        // a is the OLDEST and b the least recently USED, so a cache that evicted by age would
        // release a and keep b. The three together tell the two policies apart.
        b.IsAlive.ShouldBeFalse("b was the least recently used when c arrived, so the cache let it go");
        a.IsAlive.ShouldBeTrue("a was asked for again after b, so it is one of the two kept");
        c.IsAlive.ShouldBeTrue("c is the newest, so it is kept");
    }

    [Test]
    public void Get_ManyCallersWhileTheFirstBuildRuns_BuildOnce()
    {
        using Gate gate = new();
        int builds = 0;
        LruCache<string, object> cache = new(2, _ =>
        {
            Interlocked.Increment(ref builds);
            return gate.Hold();
        });

        object?[] got = new object?[Callers];
        using CountdownEvent arrived = new(Callers - 1);
        Thread first = new(() => got[0] = cache.Get("a"));
        Thread[] late =
        [
            .. Enumerable.Range(1, Callers - 1).Select(i => new Thread(() =>
            {
                arrived.Signal();
                got[i] = cache.Get("a");
            })),
        ];

        first.Start();
        gate.AwaitStart().ShouldBeTrue("the first build never started");

        foreach (Thread thread in late)
        {
            thread.Start();
        }

        // Every later caller is inside the cache and waiting: on the first build if the cache is
        // right, on a build of its own if it is not. The count below decides which.
        arrived.Wait(Tripwire).ShouldBeTrue("a later caller never ran");
        Blocked(late).ShouldBeTrue("a later caller never blocked in the cache");

        gate.Release();
        JoinAll([first, .. late]);

        builds.ShouldBe(1);
        got.Distinct(ReferenceEqualityComparer.Instance).Count().ShouldBe(1);
    }

    [Test]
    public void Get_PastCapacityWhileAKeyIsStillBuilding_DoesNotBuildItAgain()
    {
        using Gate gate = new();
        Counter builds = new();
        LruCache<string, object> cache = new(1, key =>
        {
            object built = builds.Build(key);
            return key == "a" ? gate.Hold() : built;
        });

        object? firstA = null;
        object? secondA = null;
        Thread first = new(() => firstA = cache.Get("a"));

        first.Start();
        gate.AwaitStart().ShouldBeTrue("the build of a never started");

        // Two more keys past a capacity of one, while a is still building.
        _ = cache.Get("b");
        _ = cache.Get("c");

        Thread second = new(() => secondA = cache.Get("a"));
        second.Start();
        Blocked([second]).ShouldBeTrue("the second caller for a never blocked in the cache");

        gate.Release();
        JoinAll([first, second]);

        builds.Of("a").ShouldBe(1, "a was evicted while it was still building, so it was built twice");
        secondA.ShouldBeSameAs(firstA);
    }

    [Test]
    public void Get_AValueTheCacheReleasedButACallerStillHolds_ReturnsItRatherThanRebuilding()
    {
        Counter builds = new();
        LruCache<string, object> cache = new(1, builds.Build);

        object held = cache.Get("a");

        // A capacity of one: the cache lets a go here, and this test still holds it.
        _ = cache.Get("b");

        cache.Get("a").ShouldBeSameAs(held);
        builds.Of("a").ShouldBe(1);
    }

    [Test]
    public void Get_TwoKeysAtOnce_BuildAtTheSameTime()
    {
        using ManualResetEventSlim aStarted = new();
        using ManualResetEventSlim bStarted = new();
        ConcurrentDictionary<string, bool> sawTheOther = new();
        LruCache<string, object> cache = new(2, key =>
        {
            (key == "a" ? aStarted : bStarted).Set();

            // Each build waits to see the other begin. Builds taken one at a time never do.
            sawTheOther[key] = (key == "a" ? bStarted : aStarted).Wait(Tripwire);
            return new object();
        });

        Thread a = new(() => cache.Get("a"));
        Thread b = new(() => cache.Get("b"));
        a.Start();
        b.Start();
        JoinAll([a, b]);

        sawTheOther["a"].ShouldBeTrue("b did not start building until a had finished");
        sawTheOther["b"].ShouldBeTrue("a did not start building until b had finished");
    }

    [Test]
    public void WarmFirst_AKeyAlreadyBuilt_ComesBeforeTheColdOnes()
    {
        LruCache<string, object> cache = new(2, _ => new object());
        _ = cache.Get("c");

        cache.WarmFirst(["a", "b", "c", "d"]).ShouldBe(["c", "a", "b", "d"]);
    }

    [Test]
    public void WarmFirst_AKeyBuiltPartWayThrough_IsVisitedNext()
    {
        LruCache<string, object> cache = new(2, _ => new object());

        using IEnumerator<string> sweep = cache.WarmFirst(["a", "b", "c", "d"]).GetEnumerator();

        sweep.MoveNext().ShouldBeTrue();
        sweep.Current.ShouldBe("a");

        // Another test builds d while this sweep is between demos: the sweep joins it next.
        _ = cache.Get("d");

        sweep.MoveNext().ShouldBeTrue();
        sweep.Current.ShouldBe("d");
    }

    [Test]
    public void WarmFirst_AKeyReleasedButStillHeld_CountsAsWarm()
    {
        LruCache<string, object> cache = new(1, _ => new object());

        object held = cache.Get("a");
        _ = cache.Get("b");

        cache.WarmFirst(["c", "a"]).ShouldBe(["a", "c"]);
        GC.KeepAlive(held);
    }

    /// <summary>Asks for a, b, a again and c, keeping nothing but weak references to what came back.</summary>
    /// <remarks>
    /// A separate frame that cannot be inlined, so no local of the calling test roots a value and
    /// the weak references report only what the cache itself holds.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference A, WeakReference B, WeakReference C) AskInTurn(LruCache<string, object> cache)
    {
        WeakReference a = new(cache.Get("a"));
        WeakReference b = new(cache.Get("b"));
        _ = cache.Get("a");
        WeakReference c = new(cache.Get("c"));

        return (a, b, c);
    }

    /// <summary>Runs full blocking collections, so a weak reference reports what is reachable now.</summary>
    private static void CollectEverythingUnreachable()
    {
        // The instrument, not a tuning knob: a weak reference reports a release only once a full
        // collection has run, and nothing else in this test forces one.
#pragma warning disable S1215
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#pragma warning restore S1215
    }

    /// <summary>Whether every thread is blocked in a wait, polled on the condition itself.</summary>
    private static bool Blocked(IEnumerable<Thread> threads) =>
        SpinWait.SpinUntil(
            () => threads.All(thread => thread.ThreadState.HasFlag(ThreadState.WaitSleepJoin)),
            Tripwire);

    private static void JoinAll(IEnumerable<Thread> threads)
    {
        foreach (Thread thread in threads)
        {
            thread.Join(Tripwire).ShouldBeTrue("a caller never finished");
        }
    }

    /// <summary>Counts builds per key, and builds a fresh object each time.</summary>
    private sealed class Counter
    {
        private readonly ConcurrentDictionary<string, int> _builds = new(StringComparer.Ordinal);

        public object Build(string key)
        {
            _builds.AddOrUpdate(key, 1, (_, count) => count + 1);
            return new object();
        }

        public int Of(string key) => _builds.GetValueOrDefault(key);
    }

    /// <summary>A build that says it has started and then holds until released.</summary>
    private sealed class Gate : IDisposable
    {
        private readonly ManualResetEventSlim _started = new();
        private readonly ManualResetEventSlim _released = new();

        public object Hold()
        {
            _started.Set();
            _released.Wait(Tripwire);
            return new object();
        }

        public bool AwaitStart() => _started.Wait(Tripwire);

        public void Release() => _released.Set();

        public void Dispose()
        {
            _started.Dispose();
            _released.Dispose();
        }
    }
}
