using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A body's transforms at lattice times inside one time-of-impact search — <c>FUN_1800a0800</c>'s 21-slot
/// motion cache (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *Each object's motion cache*). An object whose
/// movement-state byte at `object+0x78` is 8 or more has every slot pointed at its current matrix; any other
/// has slot 0 pointed there and the rest null. The searches read slot `n` for their running tick total `n`
/// and fill a null slot once, with `FUN_1800734e0` at the time that first asked, so **a slot is keyed by its
/// index, not by a time**.
///
/// **Where the current matrix comes from is not established here** — it is the `+0x40` matrix of the
/// structure the engine builds the cache from, and no call to `FUN_1800734e0` writes it — so it is taken
/// from the caller.
/// </remarks>
public sealed class IvpMotionCache
{
    /// <summary>The start and twenty lattice ticks: the refinement gives up at a tick total of 20.</summary>
    public const int SlotCount = 21;

    private readonly IvpRigidBody _body;
    private readonly IvpMatrix _current;
    private readonly bool _resting;
    /// <summary>The slots, made on the first fill — the engine's sit in the searcher's own storage; a cache here is made per search, and a
    /// resting body's is never read.</summary>
    private IvpMatrix?[]? _slots;

    /// <summary>Builds the cache for a body, as <c>FUN_1800a0800</c> does.</summary>
    /// <param name="body">The body whose transforms fill the slots.</param>
    /// <param name="current">The body's current matrix, which slot 0 is.</param>
    /// <param name="resting">Whether the body is not moving, so every slot is <paramref name="current"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public IvpMotionCache(IvpRigidBody body, IvpMatrix current, bool resting)
    {
        ArgumentNullException.ThrowIfNull(body);

        _body = body;
        _current = current;
        _resting = resting;
    }

    /// <summary>The body's current matrix — the cache object's <c>+0x40</c>, which slot 0 points at.</summary>
    /// <remarks>
    /// `FUN_1800a1b50` reads it directly, not through a slot, to take the face normal into the vertex's frame
    /// before its edge ring.
    /// </remarks>
    public IvpMatrix Current => _current;

    /// <summary>The body's transform at a lattice tick, filled on first use.</summary>
    /// <param name="tick">The search's running tick total.</param>
    /// <param name="time">The time to fill the slot at, if it is empty.</param>
    /// <returns>The slot's matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tick"/> is outside the 21 slots.</exception>
    /// <remarks>
    /// **The engine does not bound the index** — an interval is one simulation step, a handful of ticks — and
    /// past the last slot it would read beside its own storage. The port refuses instead.
    /// </remarks>
    public IvpMatrix At(int tick, double time)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tick, SlotCount);

        if (tick == 0 || _resting)
        {
            return _current;
        }

        _slots ??= new IvpMatrix?[SlotCount];
        IvpMatrix? filled = _slots[tick];

        if (filled.HasValue)
        {
            return filled.Value;
        }

        IvpMatrix computed = Fresh(time);
        _slots[tick] = computed;
        return computed;
    }

    /// <summary>The object's transform at a time, computed and not kept — a direct call to <c>FUN_1800734e0</c>.</summary>
    /// <param name="time">An absolute environment time.</param>
    /// <returns>The transform of the OBJECT, whose frame the hull's points are stored in.</returns>
    /// <remarks>
    /// The refinement's regula falsi evaluates at arbitrary times this way, bypassing the slots.
    ///
    /// **The core's transform first, then the object composed into it** (B403): `FUN_1800734e0` fills the matrix
    /// from the core's interpolated rotation and position and then, unless bit `0x800` marks the offset as zero,
    /// replaces its translation with the float offset at `object+0x60` put through that matrix (`FUN_180070b20`,
    /// grouped as <see cref="IvpMatrix.ToWorld"/>). The object's own rotation inside its core is the identity for
    /// every object `FUN_180073df0` creates, so the `object+0x58` product never runs.
    /// </remarks>
    public IvpMatrix Fresh(double time)
    {
        ((double X, double Y, double Z) position, (double X, double Y, double Z, double W) rotation) = _body.TransformAt(time);

        IvpMatrix core = IvpMatrix.FromRotation(rotation, position);
        (float X, float Y, float Z) offset = _body.ObjectOffset;

        if (offset == (0f, 0f, 0f))
        {
            return core;
        }

        return core with { Translation = core.ToWorld((offset.X, offset.Y, offset.Z)) };
    }
}
