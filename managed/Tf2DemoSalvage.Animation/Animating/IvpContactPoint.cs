using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A pair's friction contact point — the <c>0xd0</c> bytes <c>FUN_18008c4b0</c> allocates for an exact mindist, built by
/// <c>FUN_180082ed0</c> (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*). It outlives a single collision:
/// <see cref="IvpContactRecord.Build"/> measures it again every time the pair collides, and what it carries between measures —
/// the gap, the slide along the record's two spans, the time of the last measure and whether the first measure is still
/// ahead — is what the friction solve warms from.
///
/// **Two friction synapses and the state after them.** Each synapse, at `+0x10` and `+0x38`, holds its object at `+0x10`, a
/// word at `+0x18` leading back to the contact point, the feature's kind at `+0x1a` and its edge at `+0x20`, and is linked at
/// the head of its object's list at `+0x50`. The constructor also hands each synapse's ledge to its surface manager's slot 7 —
/// a reference count for a surface that pages ledges in, which a polygon surface does nothing with — zeroes `+0x64`, `+0x7c`,
/// `+0x84..0x8b`, `+0x92` and `+0xc0`, and writes `20` to the byte at `+0x90`. *What reads those is not established.*
/// </remarks>
public sealed class IvpContactPoint
{
    /// <summary><c>DAT_1800fd268</c>: <c>1e-18f</c> widened, added to a triangle normal's length before its reciprocal.</summary>
    private const double LengthFloor = (double)1e-18f;

    /// <summary>Builds the contact point for an exact mindist, as <c>FUN_180082ed0</c> does.</summary>
    /// <param name="mindist">The mindist, whose flags name synapse A.</param>
    /// <param name="recordZeroObject">The object synapse record 0 belongs to.</param>
    /// <param name="recordZeroSide">The ledge synapse record 0's feature is on.</param>
    /// <param name="recordOneObject">The object synapse record 1 belongs to.</param>
    /// <param name="recordOneSide">The ledge synapse record 1's feature is on.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Synapse A's record comes first** — `(flags >> 8) &amp; 3` — and the other is `((flags ^ 0x100) >> 8) &amp; 3`, as
    /// the engine takes it. When the second feature is a triangle, its normal's length plus `1e-18f` is inverted in double and
    /// narrowed into <see cref="InverseTriangleDeterminant"/>; for any other kind the engine leaves those four bytes as the
    /// allocation left them, and only the point–triangle measure reads them.
    /// </remarks>
    public IvpContactPoint(
        IvpMindist mindist,
        IvpCollisionObject recordZeroObject,
        IvpLedgeSide recordZeroSide,
        IvpCollisionObject recordOneObject,
        IvpLedgeSide recordOneSide,
        double now)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(recordZeroObject);
        ArgumentNullException.ThrowIfNull(recordZeroSide);
        ArgumentNullException.ThrowIfNull(recordOneObject);
        ArgumentNullException.ThrowIfNull(recordOneSide);

        int first = mindist.SynapseA;
        int second = ((mindist.Flags ^ 0x100) >> 8) & 3;

        First = mindist.Synapse(first);
        Second = mindist.Synapse(second);
        FirstObject = first == 0 ? recordZeroObject : recordOneObject;
        SecondObject = second == 0 ? recordZeroObject : recordOneObject;

        FirstObject.ContactPoints.AddFirst(this);
        SecondObject.ContactPoints.AddFirst(this);

        LastMeasured = now;

        if (Second.Kind == IvpFeatureKind.Triangle)
        {
            IvpLedgeSide side = second == 0 ? recordZeroSide : recordOneSide;

            InverseTriangleDeterminant = (float)(1d / (IvpVector.Length(side.FaceNormal(Second.Feature)) + LengthFloor));
        }
    }

    /// <summary>Synapse A's feature and kind — the friction synapse at <c>+0x10</c>.</summary>
    public IvpSynapse First { get; }

    /// <summary>The other synapse record's feature and kind — the friction synapse at <c>+0x38</c>.</summary>
    public IvpSynapse Second { get; }

    /// <summary>The object <see cref="First"/> belongs to — <c>+0x20</c>.</summary>
    public IvpCollisionObject FirstObject { get; }

    /// <summary>The object <see cref="Second"/> belongs to — <c>+0x48</c>.</summary>
    public IvpCollisionObject SecondObject { get; }

    /// <summary>The record the last measure built — <c>+0x70</c>; null before the first.</summary>
    public IvpContactRecord? Record { get; internal set; }

    /// <summary>How far the contact has slid along the record's span and cross span — <c>+0x68</c> and <c>+0x6c</c>.</summary>
    /// <remarks>
    /// **Advanced by every measure**: each is less the relative velocity along its span times the float time since the last
    /// measure, computed in double and narrowed. Zero at construction.
    /// </remarks>
    public (float Span, float CrossSpan) Slide { get; internal set; }

    /// <summary>For a second feature that is a triangle, the reciprocal of its normal's length — <c>+0x80</c>.</summary>
    public float InverseTriangleDeterminant { get; }

    /// <summary>The gap between the features — <c>+0x8c</c>.</summary>
    /// <remarks>
    /// **A measure writes the distance it found; the builder then takes both extra radii off it and never leaves it below
    /// zero.** At construction it is <see cref="IvpCollisionTolerance.ContactGap"/>: the constructor copies
    /// `DAT_18012d64c`, the tolerance block's `[0x43]`, which the image holds as zero only because the block is filled at
    /// startup through its base address.
    /// </remarks>
    public float Gap { get; internal set; } = IvpCollisionTolerance.ContactGap;

    /// <summary>Whether the next measure is the first — the byte at <c>+0x91</c>, which a measure with a range clears as it checks.</summary>
    public bool FirstMeasure { get; internal set; } = true;

    /// <summary>The time of the last measure — <c>+0x98</c>, the construction's until the first.</summary>
    public double LastMeasured { get; internal set; }

    /// <summary>The record's position at the last measure, narrowed — <c>+0xa0</c>.</summary>
    public (float X, float Y, float Z) LastPosition { get; internal set; }

    /// <summary>
    /// The record's normal at the last measure — <c>x</c> stored at <c>+0xb0</c>, <c>y</c> at <c>+0xb4</c> and <c>z</c> at
    /// <c>+0xac</c>, as the builder writes them.
    /// </summary>
    public (float X, float Y, float Z) LastNormal { get; internal set; }
}
