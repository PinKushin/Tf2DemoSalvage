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

    /// <summary>Solves the contact predicted to close first, and grows the island around what it moved — <c>FUN_180090bd0(block)</c>.</summary>
    /// <param name="environment">The environment, <c>block+0x0</c>.</param>
    /// <param name="sides">Each contact's two ledge sides now, for <see cref="Grow"/>.</param>
    /// <param name="materials">The material manager.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <returns><c>true</c> when a contact was solved; <c>false</c> when none was below the ramp's end.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// best = (double)block[0x42];  none
    /// every pair of +0x30, last to first, unless BOTH cores' (flags >> 5 | flags &amp; 2) &amp; 6:
    ///     every contact, last to first:  record+0x74 != 1 → env+0xa8 += 1, FUN_18008db40(contact)
    ///         e = (double)record+0x7c;  best > e → this contact and pair;  best = MINSD(best, e)    -- NaN either side answers e
    /// none → 0
    /// record+0xa0's core, then +0x98's:  unless +0x260 is set:  push on +0x20;  state +0x1 &lt; 8 and not flags &amp; 0x10 → FUN_180078d60
    /// record+0x72 += 1;  FUN_18008ed60(record, cores, record+0x78, contact)
    /// each core the solve left, second then first, unless flags &amp; 0x12:
    ///     +0x260 set and its +0x30 zero → FUN_18008da40(block, core, pair)
    ///     else every contact of FUN_180077f00(core, system):  record+0x74 = 0
    /// → 1
    /// </code>
    /// **`MINSD` answers its second operand when either is NaN**, written out so the order is the instruction's.
    /// </remarks>
    internal bool Drain(
        IvpImpactEnvironment environment,
        Func<IvpContactPoint, (IvpLedgeSide First, IvpLedgeSide Second)> sides,
        IIvpMaterialManager materials,
        double now)
    {
        ArgumentNullException.ThrowIfNull(environment);

        double best = IvpCollisionTolerance.RampEnd;
        IvpContactPoint? found = null;
        IvpFrictionPair? foundPair = null;

        for (int pairIndex = _pairs.Count - 1; pairIndex >= 0; pairIndex--)
        {
            IvpFrictionPair pair = _pairs[pairIndex];

            if (Frozen(pair.FirstCore) && Frozen(pair.SecondCore))
            {
                continue;
            }

            for (int index = pair.Contacts.Count - 1; index >= 0; index--)
            {
                IvpContactPoint contact = pair.Contacts[index];
                IvpContactRecord record = contact.Record ?? throw new InvalidOperationException("A contact on the island has no record.");

                if (!record.Estimated)
                {
                    environment.Estimates++;
                    contact.Estimate(environment);
                }

                double estimate = record.PredictedGap;

                if (best > estimate)
                {
                    found = contact;
                    foundPair = pair;
                }

                best = best < estimate ? best : estimate;
            }
        }

        if (found is null || foundPair is null)
        {
            return false;
        }

        IvpContactRecord solved = found.Record!;

        BringToEvent(solved.SecondCore, now);
        BringToEvent(solved.FirstCore, now);

        solved.Impacts++;

        IvpRigidBody?[] cores = [solved.FirstCore, solved.SecondCore];
        IvpImpactSolver.Enter(environment, found, cores, solved.PushOut);

        for (int slot = 1; slot >= 0; slot--)
        {
            if (cores[slot] is not { Immovable: false } core)
            {
                continue;
            }

            if (core.PendingSnapshot is { Moved: false })
            {
                Grow(core, foundPair, sides, materials, now);
            }
            else if (core.FrictionInfoIn(System) is { } share)
            {
                foreach (IvpContactPoint contact in share.Contacts)
                {
                    contact.Record?.Estimated = false;
                }
            }
        }

        return true;

        static bool Frozen(IvpRigidBody core) => core.Immovable || core.CollisionFreeze != 0;

        void BringToEvent(IvpRigidBody? core, double time)
        {
            if (core is null || core.PendingSnapshot is not null)
            {
                return;
            }

            AddAtEvent(core);

            if (core.UnitState < 8 && !core.SkipsGravity)
            {
                core.RebuildMatrixAtEventTime(time);
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
