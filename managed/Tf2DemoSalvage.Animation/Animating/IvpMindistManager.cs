using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The fields of an IVP real object the far and exact handoffs read (B369).</summary>
/// <remarks>
/// Its hull manager, the byte at <c>+0x78</c>, its lists of exact and invalid synapse records, and its contact points. **What
/// the byte's low three bits mean is not established**: a core coming to rest writes <c>8</c>, and a side whose low bits are
/// clear takes no hull allowance when its pair is filed.
/// </remarks>
public sealed class IvpCollisionObject
{
    /// <summary>The hull manager at <c>+0x80</c>.</summary>
    public IvpHullManager Hull { get; } = new();

    /// <summary>The byte at <c>+0x78</c>.</summary>
    public int MovementState { get; set; }

    /// <summary>The exact synapse records at <c>+0x40</c>, the latest first.</summary>
    public LinkedList<IvpMindistHullRecord> Synapses { get; } = new();

    /// <summary>The invalid synapse records at <c>+0x48</c>, the latest first.</summary>
    public LinkedList<IvpMindistHullRecord> InvalidSynapses { get; } = new();

    /// <summary>
    /// The contact points it takes part in, the latest first — the friction synapses <c>FUN_180082ed0</c> links at the head of
    /// the list at <c>+0x50</c>, each leading back to its contact point.
    /// </summary>
    public LinkedList<IvpContactPoint> ContactPoints { get; } = new();

    /// <summary>Whether <see cref="MovementState"/>'s low three bits are clear — <c>TEST byte ptr [obj + 0x78], 0x7</c>.</summary>
    internal bool StateBitsClear => (MovementState & 7) == 0;
}

/// <summary>One of a mindist's two synapse records, as its object's lists and hull manager see it (B369).</summary>
/// <remarks>The record's listener table is <c>1800fdea0</c>; slots 1 and 3 are what a hull manager calls.</remarks>
public sealed class IvpMindistHullRecord : IIvpHullSynapse
{
    internal IvpMindistHullRecord(IvpMindist mindist, int index)
    {
        Mindist = mindist;
        Index = index;
    }

    /// <summary>The mindist the record belongs to — found from the record's <c>+0x30</c> word.</summary>
    public IvpMindist Mindist { get; }

    /// <summary>0 or 1.</summary>
    public int Index { get; }

    /// <inheritdoc/>
    public int? HullSlot { get; set; }

    /// <summary>The record's place in its object's exact or invalid list — its <c>+0x10</c>/<c>+0x18</c> links.</summary>
    internal LinkedListNode<IvpMindistHullRecord>? ObjectNode { get; set; }

    /// <summary>What slot 1 hands the mindist to, set when the pair is filed far.</summary>
    internal Action<IvpMindist, float>? OnPassed { get; set; }

    /// <summary>Slot 1, <c>FUN_180097570</c>: the mindist found from the record, handed to <c>FUN_180097f00</c>.</summary>
    /// <param name="manager">The manager telling it.</param>
    /// <param name="overshoot">The list's minimum less the next PSI's value.</param>
    /// <exception cref="InvalidOperationException">The record was never filed far, so has nothing to hand the mindist to.</exception>
    /// <remarks>The handler is the filing's <see cref="IvpFarFiling.HullPassed"/> — <see cref="IvpMindistHull.HullPassed"/> over the environment.</remarks>
    public void HullPassed(IvpHullManager manager, float overshoot) =>
        (OnPassed ?? throw new InvalidOperationException("A synapse record never filed far was told its hull passed."))(Mindist, overshoot);

    /// <summary>Slot 3, <c>FUN_1800975a0</c>: the difference of the shifts, in float, added to the mindist's <c>+0xa0</c>.</summary>
    /// <param name="valueShift">Minus the manager's value.</param>
    /// <param name="centerShift">Minus the manager's center value.</param>
    public void Rebased(float valueShift, float centerShift) =>
        Mindist.HullPastCenters = (double)(valueShift - centerShift) + Mindist.HullPastCenters;
}

/// <summary>What a far pair asked to be removed is filed with.</summary>
/// <param name="Manager">The environment's mindist manager, <c>env+0x20</c>.</param>
/// <param name="First">Synapse record 0's object.</param>
/// <param name="Second">Synapse record 1's object.</param>
/// <param name="HullPassed">What either record's slot 1 hands the mindist to — <see cref="IvpMindistHull.HullPassed"/> over the environment.</param>
public readonly record struct IvpFarFiling(
    IvpMindistManager Manager, IvpCollisionObject First, IvpCollisionObject Second, Action<IvpMindist, float> HullPassed);

/// <summary>IVP's mindist manager, <c>env+0x20</c>: the exact and invalid mindists, and those rechecked every PSI (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*). The
/// exact list is headed at <c>+0x10</c> and the invalid list at <c>+0x28</c>, both linked through each mindist's
/// <c>+0xc8</c>/<c>+0xd0</c>; the rechecked array is <c>+0x18</c>, <c>+0x1a</c> and <c>+0x20</c>, walked by
/// <c>FUN_180098610</c> each PSI.
/// </remarks>
public sealed class IvpMindistManager
{
    private const int ExactClears = 0x300000;

    private const int InvalidClears = 0x340000;

    private readonly List<IvpMindist> _rechecked = [];

    /// <summary>The exact mindists, the latest first.</summary>
    public LinkedList<IvpMindist> Exact { get; } = new();

    /// <summary>The invalid mindists, the latest first.</summary>
    public LinkedList<IvpMindist> Invalid { get; } = new();

    /// <summary>The mindists rechecked every PSI, in the order they were added less the swaps unfiling makes.</summary>
    public IReadOnlyList<IvpMindist> Rechecked => _rechecked;

    /// <summary>Links a mindist becoming exact — the first half of <c>FUN_1800977f0</c>.</summary>
    /// <param name="mindist">The mindist; its state bits are written.</param>
    /// <param name="first">Synapse record 0's object.</param>
    /// <param name="second">Synapse record 1's object.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Flags `&amp; ~0x300000 | 0xc0000`; the mindist at the head of the exact list, and each record at the head of its
    /// object's list.
    /// </remarks>
    public void LinkExact(IvpMindist mindist, IvpCollisionObject first, IvpCollisionObject second)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        mindist.Flags = (mindist.Flags & ~ExactClears) | IvpMindistHull.ExactState;
        mindist.ListNode = Exact.AddFirst(mindist);
        LinkRecords(mindist, first.Synapses, second.Synapses);
    }

    /// <summary>Appends a mindist to the rechecked array, as <c>FUN_1800977f0</c> does when either core's <c>+0x58</c> is set.</summary>
    /// <param name="mindist">The mindist.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mindist"/> is null.</exception>
    public void AddRechecked(IvpMindist mindist)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        _rechecked.Add(mindist);
    }

    /// <summary>Unfiles an exact mindist — <c>FUN_180098dd0</c>.</summary>
    /// <param name="mindist">The mindist.</param>
    /// <param name="queue">The time manager's queue, which a queued mindist leaves.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The mindist or a record is not on its exact list, where the engine would write a null next into the list's head.
    /// </exception>
    /// <remarks>
    /// Out of the queue if queued; off the exact list; each record off its object's list; and out of the rechecked array,
    /// searched from its end, **the last entry moving into its place**.
    /// </remarks>
    public void Unlink(IvpMindist mindist, IvpMinList<IvpMindist> queue)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(queue);

        IvpMindistHullRecord firstRecord = mindist.HullRecord(0);
        IvpMindistHullRecord secondRecord = mindist.HullRecord(1);

        if (mindist.ListNode is not { } node || node.List != Exact || firstRecord.ObjectNode is null || secondRecord.ObjectNode is null)
        {
            throw new InvalidOperationException("A mindist that is not exact is unfiled.");
        }

        if (mindist.QueueSlot is int slot)
        {
            queue.Remove(slot);
            mindist.QueueSlot = null;
        }

        Exact.Remove(node);
        mindist.ListNode = null;
        UnlinkRecord(firstRecord);
        UnlinkRecord(secondRecord);

        for (int index = _rechecked.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(_rechecked[index], mindist))
            {
                int last = _rechecked.Count - 1;
                _rechecked[index] = _rechecked[last];
                _rechecked.RemoveAt(last);
                return;
            }
        }
    }

    /// <summary>Makes an exact mindist invalid — <c>FUN_180097440</c>, a plain mindist's virtual <c>+0x38</c>.</summary>
    /// <param name="mindist">The mindist; its state bits are written.</param>
    /// <param name="first">Synapse record 0's object.</param>
    /// <param name="second">Synapse record 1's object.</param>
    /// <param name="queue">The time manager's queue.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The mindist is not exact.</exception>
    /// <remarks>
    /// Unfiled (<see cref="Unlink"/>); flags `&amp; ~0x340000 | 0x80000`; the mindist at the head of the invalid list and
    /// each record at the head of its object's invalid list.
    /// </remarks>
    public void Invalidate(IvpMindist mindist, IvpCollisionObject first, IvpCollisionObject second, IvpMinList<IvpMindist> queue)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        Unlink(mindist, queue);
        mindist.Flags = (mindist.Flags & ~InvalidClears) | IvpMindistHull.InvalidState;
        mindist.ListNode = Invalid.AddFirst(mindist);
        LinkRecords(mindist, first.InvalidSynapses, second.InvalidSynapses);
    }

    /// <summary>Minimizes every exact mindist — the PSI's phase 3, <c>FUN_1800983e0</c>, run right after the hull pass.</summary>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="invalidate">The mindist's <c>+0x38</c> — <see cref="Invalidate"/> over its objects.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">A pair holds bits of <c>0x3000</c>, whose phantom path is not ported.</exception>
    /// <remarks>
    /// **Head first, each next link read before anything is called**, so a pair made invalid does not end the walk. A plain
    /// pair whose minimize left bits of `0xc000` is made invalid and nothing else happens to it.
    /// </remarks>
    public void MinimizeExact(Action<IvpMindist> minimize, Action<IvpMindist> invalidate)
    {
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(invalidate);

        LinkedListNode<IvpMindist>? node = Exact.First;

        while (node is not null)
        {
            LinkedListNode<IvpMindist>? next = node.Next;
            Recheck(node.Value, minimize, invalidate);
            node = next;
        }
    }

    /// <summary>
    /// Minimizes every mindist in the rechecked array — <c>FUN_180098610</c>'s walk, each entry through <c>FUN_180098710</c>.
    /// </summary>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="invalidate">The mindist's <c>+0x38</c> — <see cref="Invalidate"/> over its objects.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// A pair holds bits of <c>0x3000</c>, or is a ball against a triangle, whose resting contact <c>FUN_180096460</c> is not
    /// ported.
    /// </exception>
    /// <exception cref="InvalidOperationException">The walk would read past the array's count.</exception>
    /// <remarks>
    /// **From the last entry to the first**, the count read once: making an entry invalid moves the last entry into its
    /// place, which the walk is already past.
    /// </remarks>
    public void RecheckEveryPsi(Action<IvpMindist> minimize, Action<IvpMindist> invalidate)
    {
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(invalidate);

        for (int index = _rechecked.Count - 1; index >= 0; index--)
        {
            if (index >= _rechecked.Count)
            {
                throw new InvalidOperationException("The rechecked array shrank past the walk, where the engine reads a stale slot.");
            }

            IvpMindist mindist = _rechecked[index];
            Recheck(mindist, minimize, invalidate);

            if (IsBallAgainstTriangle(mindist))
            {
                throw new NotSupportedException("A ball resting on a triangle goes to FUN_180096460, which is not ported.");
            }
        }
    }

    /// <summary>Hands every exact mindist to the scheduler — the PSI's phase 4, <c>FUN_1800985a0</c>.</summary>
    /// <param name="examine">
    /// <c>FUN_180099380(mindist, 1, 1)</c>: <see cref="IvpPairScheduler.Examine"/> with the pair's far filing and
    /// <see cref="IvpRecheck.AfterFeatureChange"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="examine"/> is null.</exception>
    /// <remarks>
    /// **Head first, each next link read before the call**, so a pair the scheduler files far — and so unfiles — does not end
    /// the walk. This is how an exact plain pair goes back to far.
    /// </remarks>
    public void ExamineExact(Action<IvpMindist> examine)
    {
        ArgumentNullException.ThrowIfNull(examine);

        LinkedListNode<IvpMindist>? node = Exact.First;

        while (node is not null)
        {
            LinkedListNode<IvpMindist>? next = node.Next;
            examine(node.Value);
            node = next;
        }
    }

    /// <summary>One exact mindist's minimize and split — the body shared by <c>FUN_1800983e0</c> and <c>FUN_180098710</c>.</summary>
    private static void Recheck(IvpMindist mindist, Action<IvpMindist> minimize, Action<IvpMindist> invalidate)
    {
        minimize(mindist);

        int flags = mindist.Flags;

        if ((flags & 0x3000) != 0)
        {
            throw new NotSupportedException(
                "A pair with bits of 0x3000 goes on to the phantom path, FUN_180098dd0 and FUN_180097d60, which is not ported.");
        }

        if ((flags & 0xC000) != 0)
        {
            invalidate(mindist);
        }
    }

    private static bool IsBallAgainstTriangle(IvpMindist mindist)
    {
        IvpFeatureKind first = mindist.Synapse(0).Kind;
        IvpFeatureKind second = mindist.Synapse(1).Kind;
        return (first == IvpFeatureKind.Ball && second == IvpFeatureKind.Triangle)
            || (first == IvpFeatureKind.Triangle && second == IvpFeatureKind.Ball);
    }

    private static void LinkRecords(
        IvpMindist mindist, LinkedList<IvpMindistHullRecord> firstList, LinkedList<IvpMindistHullRecord> secondList)
    {
        IvpMindistHullRecord firstRecord = mindist.HullRecord(0);
        firstRecord.ObjectNode = firstList.AddFirst(firstRecord);

        IvpMindistHullRecord secondRecord = mindist.HullRecord(1);
        secondRecord.ObjectNode = secondList.AddFirst(secondRecord);
    }

    private static void UnlinkRecord(IvpMindistHullRecord record)
    {
        if (record.ObjectNode is { List: { } list } node)
        {
            list.Remove(node);
        }

        record.ObjectNode = null;
    }
}

/// <summary>What <see cref="IvpMindistHull.HullPassed"/> did with a far pair.</summary>
public enum IvpHullPassOutcome
{
    /// <summary>The pair is still far, and both records were filed again with what is left.</summary>
    Refiled,

    /// <summary>Both records were unfiled and the pair handed to <c>FUN_1800977f0</c>.</summary>
    HandedOff,
}

/// <summary>What <see cref="IvpMindistHull.BecomeExact"/> did with a pair.</summary>
public enum IvpExactOutcome
{
    /// <summary>The minimize left bits of <c>0xc000</c>, and the pair was made invalid.</summary>
    Invalidated,

    /// <summary>The pair went to the scheduler.</summary>
    Examined,
}

/// <summary>What <see cref="IvpMindistHull.HullPassed"/> reads beyond the mindist — <c>FUN_180097f00</c>'s environment.</summary>
public sealed class IvpHullPass
{
    /// <summary>The environment's time, <c>env+0x188</c>.</summary>
    public required double Now { get; init; }

    /// <summary>The PSI step, <c>env+0x108</c>.</summary>
    public required double Step { get; init; }

    /// <summary>Synapse record 0's object.</summary>
    public required IvpCollisionObject First { get; init; }

    /// <summary>Synapse record 1's object.</summary>
    public required IvpCollisionObject Second { get; init; }

    /// <summary>Record 0's core: its position, previous velocity and last step.</summary>
    public required IvpRigidBody FirstBody { get; init; }

    /// <summary>Record 1's core.</summary>
    public required IvpRigidBody SecondBody { get; init; }

    /// <summary>Record 0's core's speed bounds.</summary>
    public required IvpCoreBounds FirstBounds { get; init; }

    /// <summary>Record 1's core's speed bounds.</summary>
    public required IvpCoreBounds SecondBounds { get; init; }

    /// <summary><c>FUN_1800977f0</c> — <see cref="IvpMindistHull.BecomeExact"/> over the environment.</summary>
    public required Action<IvpMindist> HandOff { get; init; }
}

/// <summary>What <see cref="IvpMindistHull.BecomeExact"/> reads beyond the mindist — <c>FUN_1800977f0</c>'s environment.</summary>
public sealed class IvpExactHandoff
{
    /// <summary>The environment's mindist manager.</summary>
    public required IvpMindistManager Manager { get; init; }

    /// <summary>Synapse record 0's object.</summary>
    public required IvpCollisionObject First { get; init; }

    /// <summary>Synapse record 1's object.</summary>
    public required IvpCollisionObject Second { get; init; }

    /// <summary>The time manager's queue.</summary>
    public required IvpMinList<IvpMindist> Queue { get; init; }

    /// <summary>Whether record 0's core has its <c>+0x58</c> set. <i>What the field is, is not established.</i></summary>
    public required bool FirstRechecked { get; init; }

    /// <summary>Whether record 1's core has its <c>+0x58</c> set.</summary>
    public required bool SecondRechecked { get; init; }

    /// <summary>Record 0's core's byte <c>+0x1</c>. <i>What its values mean is not established.</i></summary>
    public required int FirstCoreState { get; init; }

    /// <summary>Record 1's core's byte <c>+0x1</c>.</summary>
    public required int SecondCoreState { get; init; }

    /// <summary>The minimize, <c>FUN_180095cb0</c>.</summary>
    public required Action<IvpMindist> Minimize { get; init; }

    /// <summary>The scheduler, <c>FUN_180099380(mindist, removeFar, 0)</c>, handed <c>removeFar</c>.</summary>
    public required Action<bool> Examine { get; init; }
}

/// <summary>How a mindist is filed with its objects' hull managers, told it has passed, and made exact (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*). **The
/// speed floors are metres a second and are carried in inches**: `1e-10f` in the split, `1e-19` in the handler.
/// </remarks>
public static class IvpMindistHull
{
    /// <summary>The flags' state bits.</summary>
    public const int StateMask = 0x3C0000;

    /// <summary>Filed with the hull managers: a far pair.</summary>
    public const int FiledState = 0x140000;

    /// <summary>Exact.</summary>
    public const int ExactState = 0xC0000;

    /// <summary>Invalid: its minimize left bits of <c>0xc000</c> as it became exact.</summary>
    public const int InvalidState = 0x80000;

    /// <summary>A recursive mindist's state, whose handler <c>FUN_1800b28a0</c> is not read.</summary>
    public const int RecursiveState = 0x100000;

    private const int FiledClears = 0x280000;

    /// <summary><c>DAT_1800ea938</c>, a speed in IVP's metres a second.</summary>
    private const float SplitSpeedFloorMetres = 1e-10f;

    private const float SplitSpeedFloor = SplitSpeedFloorMetres * IvpTransform.InchesPerMetre;

    /// <summary><c>DAT_1800f4f20</c>, a speed in IVP's metres a second.</summary>
    private const double PassSpeedFloorMetres = 1e-19d;

    private const double PassSpeedFloor = PassSpeedFloorMetres * IvpTransform.InchesPerMetre;

    /// <summary><c>DAT_1800ea968</c>: the share of the other side's speed each side's weight takes.</summary>
    private const float OtherSpeedShare = 0.1f;

    /// <summary><c>DAT_1800efe20</c>: how many steps of the pair's speeds a refiled pair must be clear by.</summary>
    private const double RefileSteps = 6d;

    private const int UnmeasuredBits = 0x30000;
    private const int FrozenBits = 0xC000;
    private const int ParkedMask = 0x3000;
    private const int Parked = 0x1000;
    private const int RemovalStateLimit = 0x21;

    /// <summary>Splits a gap between two sides by speed — the split in <c>FUN_180099380</c> and <c>FUN_180097d60</c>.</summary>
    /// <param name="gap">The gap.</param>
    /// <param name="first">Record 0's core.</param>
    /// <param name="second">Record 1's core.</param>
    /// <returns>Each side's allowance.</returns>
    /// <remarks>
    /// Each speed is `+0x254 + +0x1dc + 1e-10f`; each weight its own speed plus a tenth of the other's; each allowance the gap
    /// over the weights' sum, `second + first`, times its own weight — all in float.
    /// </remarks>
    public static (float First, float Second) SplitGap(float gap, IvpCoreBounds first, IvpCoreBounds second)
    {
        float secondSpeed = second.SurfaceSpeedBound + second.LinearSpeed + SplitSpeedFloor;
        float firstSpeed = first.SurfaceSpeedBound + first.LinearSpeed + SplitSpeedFloor;
        float firstWeight = (secondSpeed * OtherSpeedShare) + firstSpeed;
        float secondWeight = (firstSpeed * OtherSpeedShare) + secondSpeed;
        float share = gap / (secondWeight + firstWeight);
        return (share * firstWeight, share * secondWeight);
    }

    /// <summary>Files a far pair's records over now — <c>FUN_180099380</c>'s far branch past its unfiling, and <c>FUN_180097bd0</c>.</summary>
    /// <param name="mindist">The mindist; its state bits and <c>+0xa0</c> are written.</param>
    /// <param name="filing">The two records' objects, and what their slot 1 hands the mindist to.</param>
    /// <param name="first">Record 0's core.</param>
    /// <param name="second">Record 1's core.</param>
    /// <param name="now">The environment's time.</param>
    /// <param name="gap">The mindist's length less its margin.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mindist"/> or the filing's handler is null.</exception>
    /// <remarks>
    /// Flags `&amp; ~0x280000 | 0x140000`. **Record 0's object with its low state bits clear takes no allowance and record
    /// 1 the gap**; else record 1's clear gives record 0 the gap; else the gap is split by speed. Each record is filed with
    /// its object's manager (<see cref="IvpHullManager.Install"/>), and the mindist keeps the two hulls past their centers,
    /// `second + first` in double.
    /// </remarks>
    public static void FileFar(
        IvpMindist mindist, IvpFarFiling filing, IvpCoreBounds first, IvpCoreBounds second, double now, float gap)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(filing.HullPassed);

        mindist.Flags = (mindist.Flags & ~FiledClears) | FiledState;

        double firstAllowance;
        double secondAllowance;

        if (filing.First.StateBitsClear)
        {
            firstAllowance = 0d;
            secondAllowance = gap;
        }
        else if (filing.Second.StateBitsClear)
        {
            firstAllowance = gap;
            secondAllowance = 0d;
        }
        else
        {
            (float firstShare, float secondShare) = SplitGap(gap, first, second);
            firstAllowance = firstShare;
            secondAllowance = secondShare;
        }

        IvpMindistHullRecord firstRecord = mindist.HullRecord(0);
        IvpMindistHullRecord secondRecord = mindist.HullRecord(1);
        firstRecord.OnPassed = filing.HullPassed;
        secondRecord.OnPassed = filing.HullPassed;

        double firstPast = filing.First.Hull.Install(firstRecord, now, firstAllowance);
        double secondPast = filing.Second.Hull.Install(secondRecord, now, secondAllowance);
        mindist.HullPastCenters = secondPast + firstPast;
    }

    /// <summary>A far pair told its hull passed — <c>FUN_180097f00</c>.</summary>
    /// <param name="mindist">The pair.</param>
    /// <param name="overshoot">The hull manager's minimum less its next PSI's value.</param>
    /// <param name="pass">The time, the step, both objects and cores, and the handoff.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The flags name a synapse record past the two.</exception>
    /// <exception cref="NotSupportedException">A recursive mindist, or a phantom's pair at its handoff.</exception>
    /// <remarks>
    /// **Unless the flags hold bits of `0x30000`, the pair is measured again from the cores and hulls at now**, synapse A
    /// first: `d` is `A − B` along the normal, each core placed at `+0x150 + +0x170·(float)(now − +0x1d0)` in double; each
    /// hull's past-center is the double `(gradient − center gradient)·(float)(now − time) + (value − center value)` narrowed;
    /// **the new length is `length − (h − +0xa0) − (+0x9c − d)`**. Over `(float)step · (s_B + s_A) · 6.0` with the shortfall
    /// added, the mindist keeps `h`, `d` and the new length and each record is filed again over now with what is left over the
    /// speeds times its own; otherwise both records are unfiled and the pair handed off.
    /// </remarks>
    public static IvpHullPassOutcome HullPassed(IvpMindist mindist, float overshoot, IvpHullPass pass)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(pass);

        int flags = mindist.Flags;

        if ((flags & StateMask) == RecursiveState)
        {
            throw new NotSupportedException("A recursive mindist's hull-passed handler, FUN_1800b28a0, is not read.");
        }

        if ((flags & 0x200) != 0)
        {
            throw new InvalidOperationException("A mindist's flags name a synapse record past its two.");
        }

        if ((flags & UnmeasuredBits) == 0)
        {
            bool recordOneIsA = mindist.SynapseA != 0;
            IvpCollisionObject objectA = recordOneIsA ? pass.Second : pass.First;
            IvpCollisionObject objectB = recordOneIsA ? pass.First : pass.Second;
            IvpRigidBody bodyA = recordOneIsA ? pass.SecondBody : pass.FirstBody;
            IvpRigidBody bodyB = recordOneIsA ? pass.FirstBody : pass.SecondBody;
            IvpCoreBounds boundsA = recordOneIsA ? pass.SecondBounds : pass.FirstBounds;
            IvpCoreBounds boundsB = recordOneIsA ? pass.FirstBounds : pass.SecondBounds;

            double now = pass.Now;
            double elapsedA = (float)(now - bodyA.LastStepped);
            double elapsedB = (float)(now - bodyB.LastStepped);
            double speedA = (double)(boundsA.SurfaceSpeedBound + boundsA.LinearSpeed) + PassSpeedFloor;
            double speedB = (double)(boundsB.SurfaceSpeedBound + boundsB.LinearSpeed) + PassSpeedFloor;
            double speeds = speedB + speedA;

            (float X, float Y, float Z) normal = mindist.Normal;
            double along =
                ((Placed(bodyA.PreviousVelocity.Y, elapsedA, bodyA.Position.Y) - Placed(bodyB.PreviousVelocity.Y, elapsedB, bodyB.Position.Y)) * normal.Y)
                + ((Placed(bodyA.PreviousVelocity.X, elapsedA, bodyA.Position.X) - Placed(bodyB.PreviousVelocity.X, elapsedB, bodyB.Position.X)) * normal.X);
            along += (Placed(bodyA.PreviousVelocity.Z, elapsedA, bodyA.Position.Z) - Placed(bodyB.PreviousVelocity.Z, elapsedB, bodyB.Position.Z)) * normal.Z;

            double past = PastCenter(objectA.Hull, now) + PastCenter(objectB.Hull, now);
            double length = (mindist.Length - (past - mindist.HullPastCenters)) - (mindist.ContactDot - along);
            double available = overshoot + length;

            if (available > (float)pass.Step * speeds * RefileSteps)
            {
                double share = available / speeds;
                mindist.HullPastCenters = past;
                mindist.ContactDot = (float)along;
                mindist.Length = (float)length;
                objectA.Hull.Reinstall(mindist.HullRecord(recordOneIsA ? 1 : 0), now, share * speedA);
                objectB.Hull.Reinstall(mindist.HullRecord(recordOneIsA ? 0 : 1), now, share * speedB);
                return IvpHullPassOutcome.Refiled;
            }
        }

        if ((mindist.Flags & ParkedMask) == Parked)
        {
            throw new NotSupportedException("A phantom's pair is handed to FUN_180097940, which is not ported.");
        }

        pass.First.Hull.Remove(mindist.HullRecord(0));
        pass.Second.Hull.Remove(mindist.HullRecord(1));
        pass.HandOff(mindist);
        return IvpHullPassOutcome.HandedOff;
    }

    /// <summary>A pair becoming exact — <c>FUN_1800977f0</c>.</summary>
    /// <param name="mindist">The pair.</param>
    /// <param name="handoff">The manager, both objects, the cores' fields, the minimize and the scheduler.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Linked exact (<see cref="IvpMindistManager.LinkExact"/>); minimized; appended to the rechecked array when either
    /// core's `+0x58` is set; then **a minimize that left bits of `0xc000` makes it invalid**, and anything else examines it,
    /// asking for a far pair's removal when the cores' state bytes ORed are under `0x21`.
    /// </remarks>
    public static IvpExactOutcome BecomeExact(IvpMindist mindist, IvpExactHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(handoff);

        handoff.Manager.LinkExact(mindist, handoff.First, handoff.Second);
        handoff.Minimize(mindist);

        if (handoff.FirstRechecked || handoff.SecondRechecked)
        {
            handoff.Manager.AddRechecked(mindist);
        }

        if ((mindist.Flags & FrozenBits) != 0)
        {
            handoff.Manager.Invalidate(mindist, handoff.First, handoff.Second, handoff.Queue);
            return IvpExactOutcome.Invalidated;
        }

        handoff.Examine((handoff.FirstCoreState | handoff.SecondCoreState) < RemovalStateLimit);
        return IvpExactOutcome.Examined;
    }

    /// <summary>A core's coordinate at now: <c>(double)+0x170 · elapsed + +0x150</c>.</summary>
    private static double Placed(float previousVelocity, double elapsed, double position) =>
        ((double)previousVelocity * elapsed) + position;

    /// <summary>A hull past its center at now, measured in double from float pieces and narrowed.</summary>
    private static double PastCenter(IvpHullManager hull, double now)
    {
        float gradients = hull.Gradient - hull.CenterGradient;
        float values = hull.Value - hull.CenterValue;
        double elapsed = (float)(now - hull.Time);
        return (float)(((double)gradients * elapsed) + values);
    }
}
