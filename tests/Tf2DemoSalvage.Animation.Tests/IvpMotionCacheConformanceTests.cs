using System;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The per-body transform cache IVP's time-of-impact search reads its lattice times from (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly of `vphysics.dll`** — `docs/findings/51`, *Each object's motion cache*.
/// `FUN_1800a0800` builds it with 21 slots: an object whose movement-state byte at `object+0x78` is 8 or
/// more has every slot pointed at its current matrix, and any other has slot 0 pointed there and the rest
/// null. `FUN_1800b6210` and `FUN_1800b6590` then read slot `n` for their running tick total `n`, and fill
/// a null slot ONCE, with `FUN_1800734e0` at the time that first asked — **keyed by the index, not the
/// time**.
/// </remarks>
public sealed class IvpMotionCacheConformanceTests
{
    /// <summary>A current matrix no transform of <see cref="Rising"/> can produce, so a slot that computed instead of pointing at it fails.</summary>
    private static readonly IvpMatrix Marker = IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (7d, 8d, 9d));

    private static IvpRigidBody Rising() => new()
    {
        Position = (1d, 2d, 3d),
        PreviousVelocity = (0f, 0f, 100f),
        Orientation = (0f, 0f, 0f, 1f),
        WorkingOrientation = (0f, 0f, 0f, 1f),
        LastStepped = 0d,
        InverseStep = 66f,
    };

    /// <remarks>
    /// **Slot 0 is the current matrix whatever time is asked** — the search's start reads it directly.
    /// </remarks>
    [Test]
    public void At_TickZero_IsTheCurrentMatrixAtAnyTime()
    {
        IvpMotionCache cache = new(Rising(), Marker, resting: false);

        cache.At(0, 0.05d).ShouldBe(Marker);
    }

    /// <remarks>
    /// **A resting object points every slot at the current matrix**, so a lattice time deep in the interval
    /// still reads it and nothing is computed.
    /// </remarks>
    [Test]
    public void At_ARestingBodyAtALaterTick_IsStillTheCurrentMatrix()
    {
        IvpMotionCache cache = new(Rising(), Marker, resting: true);

        cache.At(5, 0.025d).ShouldBe(Marker);
    }

    /// <remarks>
    /// A moving body's slot `n` is `FUN_1800734e0` at the time that filled it — here its transform at 0.02.
    /// </remarks>
    [Test]
    public void At_AMovingBodysEmptySlot_IsItsTransformAtThatTime()
    {
        IvpRigidBody body = Rising();
        IvpMotionCache cache = new(body, Marker, resting: false);

        ((double X, double Y, double Z) position, (float X, float Y, float Z, float W) rotation) =
            body.TransformAt(0.02d);

        cache.At(4, 0.02d).ShouldBe(IvpMatrix.FromRotation(rotation, position));
    }

    /// <remarks>
    /// **Keyed by the index.** Slot 3 filled at 0.015 holds the body at height `3 + 100 × 0.015` = 4.5, and
    /// asking slot 3 again at 0.05 gets that same matrix, not the height of 8 the later time would give.
    /// </remarks>
    [Test]
    public void At_TheSameTickAskedAtASecondTime_KeepsTheFirstFill()
    {
        IvpMotionCache cache = new(Rising(), Marker, resting: false);

        IvpMatrix first = cache.At(3, 0.015d);
        IvpMatrix second = cache.At(3, 0.05d);

        second.ShouldBe(first);
        second.Translation.Z.ShouldBe(4.5d, 1e-5d);
    }

    /// <remarks>
    /// **The engine does not bound the index**; its interval is a single simulation step, a handful of
    /// ticks, so slot 21 is never asked for. Past the last slot the engine's storage would be overrun, and
    /// the port refuses instead of reading beside the cache.
    /// </remarks>
    [Test]
    public void At_PastTheTwentyFirstSlot_Throws()
    {
        IvpMotionCache cache = new(Rising(), Marker, resting: false);

        Should.Throw<ArgumentOutOfRangeException>(() => cache.At(21, 0.105d));
    }
}
