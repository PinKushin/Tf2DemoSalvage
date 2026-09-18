using System;

using Tf2DemoSalvage.Content.Assets;

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

    /// <summary><c>DAT_1800efdf8</c>: <c>0.25</c>, the most <see cref="SpinShare"/> of a spin may be.</summary>
    private const double MostSpinShare = 0.25d;

    /// <summary><c>DAT_1800eb920</c>: the gap a record too far apart is estimated at.</summary>
    private const float NoEstimate = 1e20f;

    /// <summary><c>DAT_1800ea984</c>.</summary>
    private const float Half = 0.5f;

    /// <summary><c>DAT_1800fd860</c>: <c>2.5e-5</c>, the share of a core's squared spin whose root the push-out estimate takes the cosine of.</summary>
    private static readonly double SpinShare = BitConverter.Int64BitsToDouble(0x3efa36e2eb1c432d);

    private readonly IvpLedgeTopology _firstTopology;
    private readonly IvpLedgeTopology _secondTopology;

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
        FirstLedge = mindist.Ledge(first)?.Ledge;
        SecondLedge = mindist.Ledge(second)?.Ledge;
        _firstTopology = (first == 0 ? recordZeroSide : recordOneSide).Topology;
        _secondTopology = (second == 0 ? recordZeroSide : recordOneSide).Topology;

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

    /// <summary>The ledge <see cref="First"/>'s edge lies in — named by the mindist it was made from; null for a pair of features alone.</summary>
    /// <remarks>The engine's friction synapse holds the edge's address, which lies inside its ledge; this carries the ledge beside it.</remarks>
    public PhysicsLedge? FirstLedge { get; }

    /// <summary>The ledge <see cref="Second"/>'s edge lies in.</summary>
    public PhysicsLedge? SecondLedge { get; }

    /// <summary>The topology of <see cref="FirstLedge"/>, which a feature match walks for an edge's twin or a point's vertex.</summary>
    internal IvpLedgeTopology FirstTopology => _firstTopology;

    /// <summary>The topology of <see cref="SecondLedge"/>.</summary>
    internal IvpLedgeTopology SecondTopology => _secondTopology;

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

    /// <summary>Whether the byte at <c>+0x64</c> is set — whether the impact's entry shapes its cone along the materials' axes.</summary>
    /// <remarks>**Written by <see cref="Weigh"/>** (<c>FUN_180083a60</c>), when either side's material has a second friction.</remarks>
    public bool UsesMaterialAxes { get; set; }

    /// <summary>
    /// How much slide this contact's clamps have thrown away, weighted — the float at <c>+0x7c</c>, accumulated by
    /// <see cref="IvpTangentialSolve.ClampSlide"/>'s excess.
    /// </summary>
    /// <remarks>
    /// **Per contact and never chained between them**: `FUN_1800836b0` adds each contact's own excess to its own
    /// <c>+0x7c</c>. *What reads it is not established.*
    /// </remarks>
    public float SlideExcess { get; internal set; }

    /// <summary>The work the last tangential solve did along the slide — the float at <c>+0x84</c>.</summary>
    /// <remarks>
    /// **Written by <see cref="IvpTangentialSolve.SolveContact"/>** (<c>FUN_1800857c0</c>), which answers the change against the
    /// value it replaces; the many-contact driver banks a positive sum of those changes on the pair.
    /// </remarks>
    public float SlideWork { get; internal set; }

    /// <summary>The mass a unit push along this contact's normal has to move, inverted — the float at <c>+0x60</c>.</summary>
    /// <remarks>
    /// **Written by <see cref="Weigh"/>** (<c>FUN_180083a60</c>), and read by the friction controller as the third factor of a
    /// pair's cone budget — `NormalPush × Friction × this`. *Recorded in `docs/HANDOFF.md` and `IvpTangentialSolve` as a field
    /// with no writer found; the writer was read on 2026-09-15.*
    /// </remarks>
    public float InverseContactMass { get; internal set; }

    /// <summary>Weighs the contact and sets its material-axis gate — <c>FUN_180083a60(cp)</c>.</summary>
    /// <exception cref="InvalidOperationException">The point has no record, or neither object has a core.</exception>
    /// <remarks>
    /// <code>
    /// each side's material (FUN_18008fb60): its +0xc nonzero → cp+0x64 = 1
    /// A = cp+0x20's object's core, B = cp+0x48's;  arms record+0xd0 and +0xe0
    /// neither flagged 0x12:  m = (mB·mA) / (mB + mA)      -- FUN_180077840 per core, its own arm
    /// A flagged:             m = FUN_180077840(B, record+0xe0)
    /// otherwise:             m = FUN_180077840(A, record+0xd0)
    /// cp+0x60 = (float)(1.0 / m)
    /// </code>
    /// **A flagged core is left out entirely rather than given an infinite mass**, so a body against the world is weighed by
    /// itself alone.
    /// </remarks>
    public void Weigh()
    {
        IvpContactRecord record = RecordOrThrow();

        if (record.FirstMaterial is { HasSecondFriction: true } || record.SecondMaterial is { HasSecondFriction: true })
        {
            UsesMaterialAxes = true;
        }

        IvpRigidBody first = FirstObject.Core ?? throw new InvalidOperationException("A weighed contact's first object has no core.");
        IvpRigidBody second = SecondObject.Core ?? throw new InvalidOperationException("A weighed contact's second object has no core.");

        double mass;

        if (!first.Immovable && !second.Immovable)
        {
            double a = first.EffectiveMassAlong(record.FirstArm);
            double b = second.EffectiveMassAlong(record.SecondArm);

            mass = (b * a) / (b + a);
        }
        else if (first.Immovable)
        {
            mass = second.EffectiveMassAlong(record.SecondArm);
        }
        else
        {
            mass = first.EffectiveMassAlong(record.FirstArm);
        }

        InverseContactMass = (float)(1d / mass);
    }

    /// <summary>The pair's friction factor — <c>+0x78</c>, narrowed from the material manager's slot 2 by <see cref="SetMaterials"/>.</summary>
    public float Friction { get; internal set; }

    /// <summary>The next contact in its friction system's list — <c>+0x0</c>.</summary>
    public IvpContactPoint? Next { get; internal set; }

    /// <summary>The previous contact in its friction system's list — <c>+0x8</c>.</summary>
    public IvpContactPoint? Previous { get; internal set; }

    /// <summary>The friction system this contact is filed in — <c>+0xc0</c>, written by <see cref="IvpFrictionSystem.Link"/>.</summary>
    public IvpFrictionSystem? FrictionSystem { get; internal set; }

    /// <summary>The push the heap solve last gave this contact, times the PSI event's <c>+0x4</c> — the float at <c>+0x88</c>.</summary>
    public float NormalPush { get; internal set; }

    /// <summary>
    /// How many solves in a row have pushed this contact, negative, or left it unpushed, positive — the signed word at <c>+0x92</c>.
    /// </summary>
    /// <remarks>
    /// **The heap solve sorts its list by it and warm-starts on the pushed ones at the head.** A push takes it to `−1`, or one
    /// further below; no push to `0`; a pull from a negative streak to `1`, or one further up, and past `9` back to `0`
    /// (`FUN_1800aa5c0`). The constructor zeroes it.
    /// </remarks>
    public short PushStreak { get; internal set; }

    /// <summary>Writes the record's objects, features and materials, its elasticity and this point's friction — <c>FUN_1800908d0(cp, record)</c>.</summary>
    /// <param name="manager">The environment's material manager, <c>env+0xe8</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The point has no record, or an object has no material of its own where a triangle asks for it.</exception>
    /// <remarks>
    /// <code>
    /// record+0x60, +0x68 = each synapse's material: its object's +0xd0 when its triangle's material index is zero, else slot 1
    /// record+0x40, +0x48 = the objects;  +0x50, +0x58 = the synapses' edges
    /// record+0x80 = (float)slot 3(record);  cp+0x78 = (float)slot 2(record)
    /// </code>
    /// </remarks>
    public void SetMaterials(IIvpMaterialManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);

        IvpContactRecord record = RecordOrThrow();

        record.FirstMaterial = FirstMaterial(manager);
        record.SecondMaterial = SecondMaterial(manager);
        record.FirstObject = FirstObject;
        record.SecondObject = SecondObject;
        record.FirstFeature = First;
        record.SecondFeature = Second;
        record.Elasticity = (float)manager.Elasticity(record);
        Friction = (float)manager.FrictionFactor(record);
    }

    /// <summary>How fast the pair must part to regain the margin and outrun its spin — <c>FUN_18008fca0(cp, env)</c>.</summary>
    /// <param name="environment">The environment, whose inverse step both terms narrow to float.</param>
    /// <returns>The estimate, which the collision hands the impact solver as its <c>p5</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The point has no record.</exception>
    /// <remarks>
    /// <code>
    /// p = gap &lt; block[1] ? (block[1] − gap)·(2·(double)(float)env+0x110) : 0, and record+0x78 = 0 when not   -- COMISD/JNC: a NaN gap computes
    /// q = 0;  the record's first core unless the first feature is a ball, then its second unless the second is:
    ///     s = (double)((ω.x² + ω.y²) + ω.z²)·2.5e-5, MINSD 0.25
    ///     q = (float)((1.0 − cos(√s))·(double)core+0x4·(double)(float)env+0x110 [+ (double)q for the second])
    /// return (float)((double)(q + q) + p)
    /// </code>
    /// *The name is INFERRED from its inputs*: a margin's shortfall per step, and the sagitta each core's radius sweeps.
    /// </remarks>
    public float PushOut(IvpImpactEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        IvpContactRecord record = RecordOrThrow();
        double inverseStep = (float)environment.InverseStep;
        double margin = IvpCollisionTolerance.Margin;
        double push = 0d;

        if (Gap >= margin)
        {
            record.PushOut = 0f;
        }
        else
        {
            push = (margin - Gap) * (inverseStep + inverseStep);
        }

        float sweep = 0f;

        if (record.FirstCore is { } first && First.Kind != IvpFeatureKind.Ball)
        {
            sweep = (float)(Sagitta(first) * inverseStep);
        }

        if (record.SecondCore is { } second && Second.Kind != IvpFeatureKind.Ball)
        {
            sweep = (float)((Sagitta(second) * inverseStep) + sweep);
        }

        return (float)((double)(sweep + sweep) + push);
    }

    /// <summary>The record's estimate of the gap one step on — <c>FUN_18008db40(cp)</c>.</summary>
    /// <param name="environment">The environment, whose step the estimate narrows to float.</param>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The point has no record.</exception>
    /// <remarks>
    /// <code>
    /// record+0x74 = 1;  if !(block[0x48] ≥ gap):  record+0x7c = 1e20f;  return          -- COMISS/JNC: a NaN gap takes this
    /// f = FUN_18008fca0(cp, env);  record+0x78 = f
    /// s = (double)(t·ω) + (double)(n·v) for the first core;  s += (double)−(t'·ω') − (double)(n·v') for the second
    /// record+0x7c = (float)((double)gap − ((double)(0.5f·f) + s)·(double)(float)env+0x108)
    /// </code>
    /// `t` and `t'` are the record's turns; every product and sum is in float before its widening.
    /// </remarks>
    public void Estimate(IvpImpactEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        IvpContactRecord record = RecordOrThrow();

        record.Estimated = true;

        if (!(IvpCollisionTolerance.EstimateGap >= Gap))
        {
            record.PredictedGap = NoEstimate;
            return;
        }

        float push = PushOut(environment);
        record.PushOut = push;

        double closing = 0d;

        if (record.FirstCore is { } first)
        {
            closing = (double)IvpVector.Dot(record.FirstTurn, first.AngularVelocity) + IvpVector.Dot(record.Normal, first.Velocity);
        }

        if (record.SecondCore is { } second)
        {
            closing += (double)-IvpVector.Dot(record.SecondTurn, second.AngularVelocity) - IvpVector.Dot(record.Normal, second.Velocity);
        }

        record.PredictedGap = (float)(Gap - (((double)(push * Half) + closing) * (float)environment.Step));
    }

    /// <summary>The first synapse's material — <c>FUN_1800863d0</c> on its triangle, then the object's or the manager's.</summary>
    internal IIvpMaterial FirstMaterial(IIvpMaterialManager manager) => MaterialOf(FirstObject, _firstTopology, First, manager);

    /// <summary>The second synapse's material, the same way.</summary>
    internal IIvpMaterial SecondMaterial(IIvpMaterialManager manager) => MaterialOf(SecondObject, _secondTopology, Second, manager);

    private static IIvpMaterial MaterialOf(
        IvpCollisionObject owner, IvpLedgeTopology topology, IvpSynapse synapse, IIvpMaterialManager manager)
    {
        int index = topology.MaterialIndex(synapse.Feature);

        return index == 0
            ? owner.Material ?? throw new InvalidOperationException("A triangle asks for its object's own material, and the object has none.")
            : manager.MaterialAt(owner, index);
    }

    /// <summary><c>(1.0 − cos(√min(s·2.5e-5, 0.25)))·radius</c> for a core's squared spin <c>s</c>, summed in float.</summary>
    private static double Sagitta(IvpRigidBody core)
    {
        (float X, float Y, float Z) spin = core.AngularVelocity;
        double share = (double)((spin.X * spin.X) + (spin.Y * spin.Y) + (spin.Z * spin.Z)) * SpinShare;

        // MINSD answers its second operand when the first is NaN.
        share = share < MostSpinShare ? share : MostSpinShare;

        return (1d - IvpMath.Cos(Math.Sqrt(share))) * core.Radius;
    }

    private IvpContactRecord RecordOrThrow() =>
        Record ?? throw new InvalidOperationException("The contact point has no record; the engine builds one (FUN_18008d0c0) before it asks.");
}
