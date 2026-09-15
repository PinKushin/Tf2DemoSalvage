using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The impact loop's mini-island: the stack <c>block</c> <c>FUN_180090700</c> assembles, <c>FUN_180090bd0</c>
/// drains one contact at a time, and <c>FUN_1800909d0</c> steps at the end (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, ahead of the loop that fills it** (`docs/findings/51`, *The impact loop*).
/// `FUN_18008ef60` hands `FUN_180090700` an empty block:
///
/// | offset | field |
/// |---|---|
/// | `+0x0` | the environment |
/// | `+0x8` | the pass count |
/// | `+0x10` | the cores an impact moved, to be integrated at the end (<see cref="CoresIntegrated"/>) |
/// | `+0x20` | the cores only brought to the event, put back untouched (<see cref="CoresAtEvent"/>) |
/// | `+0x30` | the pairs to scan (<see cref="Pairs"/>) |
/// | `+0x40` | the friction system (<see cref="System"/>) |
///
/// **Each is one of IVP's growable vectors** — a capacity word, a count word and a pointer — drained
/// <c>last to first</c> by every walk in the three functions above. A <see cref="List{T}"/> gives the
/// same order when read backwards; <see cref="PairsLastToFirst"/> is that read, named so the loop's
/// own direction is not re-derived at each call site.
///
/// **This is the container only.** The build (`FUN_180090700`, `FUN_18008da40`), the drain
/// (`FUN_180090bd0`) and the tail (`FUN_1800909d0`) land on top of it, each with its own conformance
/// pass; the environment field joins with the tail, the one function that reads it. What is settled
/// here — the vectors, their drain order, the add-unique guard on <see cref="Pairs"/>, and the pass
/// cap — is fixed by the quote with no ambiguity, so the loop is written against a stable shape.
/// </remarks>
public sealed class IvpImpactIsland
{
    /// <summary>The pass cap — <c>block+0x8 &gt; 0x1388</c> asks the mindist to stop (<c>FUN_180090700</c>).</summary>
    public const int PassCap = 0x1388;

    private readonly List<IvpFrictionPair> _pairs = [];
    private readonly List<IvpRigidBody> _coresIntegrated = [];
    private readonly List<IvpRigidBody> _coresAtEvent = [];

    /// <summary>Starts an empty island for one friction system — <c>block+0x40</c>.</summary>
    /// <param name="system">The friction system whose pairs the loop scans.</param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    public IvpImpactIsland(IvpFrictionSystem system)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
    }

    /// <summary>The friction system — <c>block+0x40</c>.</summary>
    public IvpFrictionSystem System { get; }

    /// <summary>How many passes the loop has run — <c>block+0x8</c>.</summary>
    public int Passes { get; set; }

    /// <summary>The pairs to scan — <c>block+0x30</c>, in the order they were added.</summary>
    public IReadOnlyList<IvpFrictionPair> Pairs => _pairs;

    /// <summary>The cores an impact moved, integrated by the tail — <c>block+0x10</c>.</summary>
    public IReadOnlyList<IvpRigidBody> CoresIntegrated => _coresIntegrated;

    /// <summary>The cores only brought to the event and put back — <c>block+0x20</c>.</summary>
    public IReadOnlyList<IvpRigidBody> CoresAtEvent => _coresAtEvent;

    /// <summary>Adds a pair to scan, unless it is already present — <c>FUN_18008da40</c>'s guard.</summary>
    /// <param name="pair">The pair.</param>
    /// <returns><c>true</c> when it was new, <c>false</c> when it was already on the list.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pair"/> is null.</exception>
    /// <remarks>
    /// **A linear scan, because the list is short and the order matters.** `FUN_18008da40` walks the
    /// pairs `not already on +0x30` before pushing, and the loop drains them last to first — a hash
    /// set would lose the order the drain depends on and the count never grows past a handful of pairs
    /// touching one core.
    /// </remarks>
    public bool AddPair(IvpFrictionPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        if (_pairs.Contains(pair))
        {
            return false;
        }

        _pairs.Add(pair);
        return true;
    }

    /// <summary>Records a core an impact moved, for the tail to integrate — pushed on <c>+0x10</c>.</summary>
    /// <param name="core">The core.</param>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    public void AddIntegrated(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        _coresIntegrated.Add(core);
    }

    /// <summary>Records a core brought to the event but not moved — pushed on <c>+0x20</c>.</summary>
    /// <param name="core">The core.</param>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    public void AddAtEvent(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        _coresAtEvent.Add(core);
    }

    /// <summary>Grows the island around a core an impact moved — <c>FUN_18008da40(block, core, pair)</c>.</summary>
    /// <param name="core">The moved core, whose snapshot the collision saved.</param>
    /// <param name="pair">The pair the impact came through, never scanned.</param>
    /// <param name="sides">Each contact's two ledge sides now, for <see cref="IvpFrictionSystem.RevalidatePair"/>.</param>
    /// <param name="materials">The material manager.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The core has no snapshot; the engine dereferences <c>core+0x260</c>.</exception>
    /// <remarks>
    /// <code>
    /// push core on +0x10;  core+0x260's +0x30 = 1
    /// every pair of the system, last to first, touching core, not pair, not already on +0x30:
    ///     FUN_180083b30(that pair, system) > 0 → push it on +0x30
    /// </code>
    /// </remarks>
    internal void Grow(
        IvpRigidBody core,
        IvpFrictionPair pair,
        Func<IvpContactPoint, (IvpLedgeSide First, IvpLedgeSide Second)> sides,
        IIvpMaterialManager materials,
        double now)
    {
        ArgumentNullException.ThrowIfNull(pair);

        AddIntegrated(core);
        (core.PendingSnapshot ?? throw new InvalidOperationException("A core the impact moved has no snapshot.")).Moved = true;

        List<IvpFrictionPair> pairs = System.Pairs;

        for (int index = pairs.Count - 1; index >= 0; index--)
        {
            IvpFrictionPair candidate = pairs[index];

            if (ReferenceEquals(candidate, pair) || _pairs.Contains(candidate) ||
                !(ReferenceEquals(candidate.FirstCore, core) || ReferenceEquals(candidate.SecondCore, core)))
            {
                continue;
            }

            if (System.RevalidatePair(candidate, sides, materials, now) > 0)
            {
                _pairs.Add(candidate);
            }
        }
    }

    /// <summary>The pairs in the order the loop drains them — <c>last to first</c>.</summary>
    /// <returns>The pairs, newest first.</returns>
    public IReadOnlyList<IvpFrictionPair> PairsLastToFirst()
    {
        IvpFrictionPair[] order = new IvpFrictionPair[_pairs.Count];

        for (int at = 0; at < _pairs.Count; at++)
        {
            order[at] = _pairs[_pairs.Count - 1 - at];
        }

        return order;
    }
}
