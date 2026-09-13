using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The fields of an IVP real object the far and exact handoffs read (B369).</summary>
/// <remarks>
/// Its hull manager, the byte at <c>+0x78</c>, and its list of exact synapse records. **What the byte's low three bits mean
/// is not established**: a core coming to rest writes <c>8</c>, and a side whose low bits are clear takes no hull
/// allowance when its pair is filed.
/// </remarks>
public sealed class IvpCollisionObject
{
    /// <summary>The hull manager at <c>+0x80</c>.</summary>
    public IvpHullManager Hull { get; } = new();

    /// <summary>The byte at <c>+0x78</c>.</summary>
    public int MovementState { get; set; }

    /// <summary>The exact synapse records at <c>+0x40</c>, the latest first.</summary>
    public LinkedList<IvpMindistHullRecord> Synapses { get; } = new();

    /// <summary>Whether <see cref="MovementState"/>'s low three bits are clear — <c>TEST byte ptr [obj + 0x78], 0x7</c>.</summary>
    internal bool StateBitsClear => (MovementState & 7) == 0;
}

/// <summary>One of a mindist's two synapse records, as its object's list and hull manager see it (B369).</summary>
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

    /// <summary>The record's place in its object's exact list — its <c>+0x10</c>/<c>+0x18</c> links.</summary>
    internal LinkedListNode<IvpMindistHullRecord>? ObjectNode { get; set; }

    /// <summary>Slot 1, <c>FUN_180097570</c>, which tails into <c>FUN_180097f00</c>.</summary>
    /// <param name="manager">The manager telling it.</param>
    /// <param name="overshoot">The list's minimum less the next PSI's value.</param>
    /// <exception cref="NotSupportedException">Always: the handler is read and not yet ported.</exception>
    public void HullPassed(IvpHullManager manager, float overshoot) =>
        throw new NotSupportedException(
            "A far pair's hull-passed handler, FUN_180097f00, is read and not yet ported (docs/findings/51).");

    /// <summary>Slot 3, <c>FUN_1800975a0</c>: the difference of the shifts, in float, added to the mindist's <c>+0xa0</c>.</summary>
    /// <param name="valueShift">Minus the manager's value.</param>
    /// <param name="centerShift">Minus the manager's center value.</param>
    public void Rebased(float valueShift, float centerShift) =>
        Mindist.HullPastCenters = (double)(valueShift - centerShift) + Mindist.HullPastCenters;
}

/// <summary>What a far pair asked to be removed is filed with: the mindist manager and its two records' objects.</summary>
/// <param name="Manager">The environment's mindist manager, <c>env+0x20</c>.</param>
/// <param name="First">Synapse record 0's object.</param>
/// <param name="Second">Synapse record 1's object.</param>
public readonly record struct IvpFarFiling(IvpMindistManager Manager, IvpCollisionObject First, IvpCollisionObject Second);

/// <summary>IVP's mindist manager, <c>env+0x20</c>: the exact mindists, and those rechecked every PSI (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*). The
/// exact list is headed at <c>+0x10</c> and linked through each mindist's <c>+0xc8</c>/<c>+0xd0</c>; the rechecked array is
/// <c>+0x18</c>, <c>+0x1a</c> and <c>+0x20</c>, walked by <c>FUN_180098610</c> each PSI. *The invalid list at <c>+0x28</c>
/// and the objects' <c>+0x48</c> lists, which <c>FUN_180097440</c> fills, are not carried yet.*
/// </remarks>
public sealed class IvpMindistManager
{
    private const int ExactClears = 0x300000;

    private readonly List<IvpMindist> _rechecked = [];

    /// <summary>The exact mindists, the latest first.</summary>
    public LinkedList<IvpMindist> Exact { get; } = new();

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

        IvpMindistHullRecord firstRecord = mindist.HullRecord(0);
        firstRecord.ObjectNode = first.Synapses.AddFirst(firstRecord);

        IvpMindistHullRecord secondRecord = mindist.HullRecord(1);
        secondRecord.ObjectNode = second.Synapses.AddFirst(secondRecord);
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
    /// The mindist or a record is not on its list, where the engine would write a null next into the list's head.
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

    private static void UnlinkRecord(IvpMindistHullRecord record)
    {
        if (record.ObjectNode is { List: { } list } node)
        {
            list.Remove(node);
        }

        record.ObjectNode = null;
    }
}

/// <summary>How a mindist is filed with its objects' hull managers (B369).</summary>
/// <remarks>**Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*).</remarks>
public static class IvpMindistHull
{
    /// <summary>The flags' state bits.</summary>
    public const int StateMask = 0x3C0000;

    /// <summary>Filed with the hull managers: a far pair.</summary>
    public const int FiledState = 0x140000;

    /// <summary>Exact.</summary>
    public const int ExactState = 0xC0000;

    private const int FiledClears = 0x280000;

    /// <summary><c>DAT_1800ea938</c>: the floor added to each side's speed.</summary>
    private const float SpeedFloor = 1e-10f;

    /// <summary><c>DAT_1800ea968</c>: the share of the other side's speed each side's weight takes.</summary>
    private const float OtherSpeedShare = 0.1f;

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
        float secondSpeed = second.SurfaceSpeedBound + second.LinearSpeed + SpeedFloor;
        float firstSpeed = first.SurfaceSpeedBound + first.LinearSpeed + SpeedFloor;
        float firstWeight = (secondSpeed * OtherSpeedShare) + firstSpeed;
        float secondWeight = (firstSpeed * OtherSpeedShare) + secondSpeed;
        float share = gap / (secondWeight + firstWeight);
        return (share * firstWeight, share * secondWeight);
    }

    /// <summary>Files a far pair's records over now — <c>FUN_180099380</c>'s far branch past its unfiling, and <c>FUN_180097bd0</c>.</summary>
    /// <param name="mindist">The mindist; its state bits and <c>+0xa0</c> are written.</param>
    /// <param name="filing">The two records' objects.</param>
    /// <param name="first">Record 0's core.</param>
    /// <param name="second">Record 1's core.</param>
    /// <param name="now">The environment's time.</param>
    /// <param name="gap">The mindist's length less its margin.</param>
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

        double firstPast = filing.First.Hull.Install(mindist.HullRecord(0), now, firstAllowance);
        double secondPast = filing.Second.Hull.Install(mindist.HullRecord(1), now, secondAllowance);
        mindist.HullPastCenters = secondPast + firstPast;
    }
}
