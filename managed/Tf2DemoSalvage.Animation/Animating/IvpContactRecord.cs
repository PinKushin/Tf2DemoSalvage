using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One side of a contact as <c>FUN_18008d0c0</c> reads it (B369).</summary>
/// <param name="Side">The object's ledge where the object is now — the cache object the builder refreshes before it measures.</param>
/// <param name="Core">The object's core, <c>object+0xe8</c>, whose <see cref="IvpRigidBody.CoreMatrix"/> the arm and the normal are put into.</param>
/// <param name="ExtraRadius">The object's extra radius, <c>object+0xe0</c>.</param>
public sealed record IvpContactBody(IvpLedgeSide Side, IvpRigidBody Core, float ExtraRadius);

/// <summary>
/// The record IVP builds each time a pair's contact point collides — the <c>0x110</c> bytes <c>FUN_18008d0c0</c> takes from
/// the environment's arena (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*). The builder measures the contact
/// point with <see cref="IvpContactGeometry"/> and then, for each core not flagged `2`, puts the position and the normal into
/// the core's own frame for the arm and the turn the impact and friction solves push through.
///
/// **What `FUN_1800908d0` writes after the builder returns** — the objects, features and materials at `+0x40..0x68`, the
/// elasticity at `+0x80` and the contact point's friction — is <see cref="IvpContactPoint.SetMaterials"/>; the estimate at
/// `+0x74..0x7c` is <see cref="IvpContactPoint.Estimate"/>. *What increments the impact count at `+0x72` is not read yet.* The
/// allocation zeroes `+0x20..0x2b`, `+0x72..0x75` and `+0x76`.
/// </remarks>
public sealed class IvpContactRecord
{
    /// <summary>Where the features touch — <c>+0x00</c>, in doubles: the first feature's point, moved along the normal by its object's extra radius.</summary>
    public (double X, double Y, double Z) Position { get; internal set; }

    /// <summary>The direction from the first feature toward the second — <c>+0x20</c>.</summary>
    public (float X, float Y, float Z) Normal { get; internal set; }

    /// <summary>Whether a first measure found the touch beyond a feature — the word at <c>+0x76</c> set to one.</summary>
    public bool Outside { get; internal set; }

    /// <summary>The reciprocal of <see cref="InverseMass"/> — <c>+0x90</c>.</summary>
    public float VirtualMass { get; private set; }

    /// <summary>Both movable cores' inverse masses along the normal, summed — <c>+0x94</c>.</summary>
    public float InverseMass { get; private set; }

    /// <summary>The first feature's core, when it is movable — <c>+0x98</c>.</summary>
    public IvpRigidBody? FirstCore { get; internal set; }

    /// <summary>The second feature's core, when it is movable — <c>+0xa0</c>.</summary>
    public IvpRigidBody? SecondCore { get; internal set; }

    /// <summary>A unit direction across the normal — <c>+0xb0</c>.</summary>
    public (float X, float Y, float Z) Span { get; internal set; }

    /// <summary><c>normal × span</c> — <c>+0xc0</c>.</summary>
    public (float X, float Y, float Z) CrossSpan { get; private set; }

    /// <summary>The position in the first core's frame — <c>+0xd0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) FirstArm { get; internal set; }

    /// <summary>The position in the second core's frame — <c>+0xe0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) SecondArm { get; internal set; }

    /// <summary><c>arm × normal</c> in the first core's frame — <c>+0xf0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) FirstTurn { get; internal set; }

    /// <summary><c>arm × normal</c> in the second core's frame — <c>+0x100</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) SecondTurn { get; internal set; }

    /// <summary>The relative velocity the impact solver began with — <c>+0x30</c>, written through the solver's <c>+0x148</c>.</summary>
    public (float X, float Y, float Z) RelativeVelocity { get; internal set; }

    /// <summary>The first synapse's object — <c>+0x40</c>, written by <see cref="IvpContactPoint.SetMaterials"/>.</summary>
    public IvpCollisionObject? FirstObject { get; internal set; }

    /// <summary>The second synapse's object — <c>+0x48</c>.</summary>
    public IvpCollisionObject? SecondObject { get; internal set; }

    /// <summary>The first synapse's feature — its edge at <c>+0x50</c>.</summary>
    public IvpSynapse? FirstFeature { get; internal set; }

    /// <summary>The second synapse's feature — its edge at <c>+0x58</c>.</summary>
    public IvpSynapse? SecondFeature { get; internal set; }

    /// <summary>The first synapse's material — <c>+0x60</c>.</summary>
    public IIvpMaterial? FirstMaterial { get; internal set; }

    /// <summary>The second synapse's material — <c>+0x68</c>.</summary>
    public IIvpMaterial? SecondMaterial { get; internal set; }

    /// <summary>The impacts this contact has counted — the signed word at <c>+0x72</c>, which the impact solver is handed.</summary>
    /// <remarks>The allocation zeroes it; *its incrementer is not read yet.*</remarks>
    public short Impacts { get; internal set; }

    /// <summary>Whether <see cref="IvpContactPoint.Estimate"/> has run — the word at <c>+0x74</c> set to one.</summary>
    public bool Estimated { get; internal set; }

    /// <summary>The push-out estimate — <c>+0x78</c>, from <see cref="IvpContactPoint.PushOut"/>.</summary>
    public float PushOut { get; internal set; }

    /// <summary>The gap estimated one step on — <c>+0x7c</c>, from <see cref="IvpContactPoint.Estimate"/>.</summary>
    public float PredictedGap { get; internal set; }

    /// <summary>The pair's elasticity — <c>+0x80</c>, narrowed from the material manager's slot 3.</summary>
    public float Elasticity { get; internal set; }

    /// <summary>Builds a contact point's record, as <c>FUN_18008d0c0</c> does.</summary>
    /// <param name="point">The contact point, whose gap, slide, last measure and record are written.</param>
    /// <param name="first">The side of the contact point's first feature.</param>
    /// <param name="second">The side of its second.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <returns>The record, also left at <see cref="IvpContactPoint.Record"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The engine would assert: the first feature is a triangle, or a kind has no measure.</exception>
    /// <remarks>
    /// <code>
    /// first kind   point → at = its edge's start in the world;  ball → at = its object's position, the first-measure flag set
    ///              edge → the edge–edge measure, whatever the second is;  anything else → assertion, line 0x1c4
    /// second kind  point → point–point;  edge → point–edge;  triangle → point–triangle;  ball → point–point with its
    ///              object's position;  anything else → assertion, line 0x1ba
    /// position += (double)normal·(double)r₀;   gap −= r₀ + r₁ in float, then zero unless it is at least zero
    /// span scaled (FUN_18006dff0);   cross = normal × span, in float
    /// each core not flagged 2:  arm = (float)M.ToObject(position);  n' = (float)M.RotateInverse(normal);  turn = arm × n'
    ///     velocity = (float)M.Rotate(ω × arm) + v (FUN_180077fa0);  inverse = ((t.y·I.y)·t.y + (t.x·I.x)·t.x) + (t.z·I.z)·t.z + m
    /// relative = the first's velocity less the second's;  inverse mass = second's + first's;  virtual mass = 1 / that
    /// dt = (float)(now − last);  slide = (float)(slide − (double)(relative · span)·dt), for each span
    /// </code>
    /// **A static core contributes nothing** — no velocity, no inverse mass, and it names no core — so against a static second
    /// body the relative velocity is the first body's alone.
    /// </remarks>
    public static IvpContactRecord Build(IvpContactPoint point, IvpContactBody first, IvpContactBody second, double now)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        IvpContactRecord record = new();
        point.Record = record;

        Measure(point, first, second, record);

        (float X, float Y, float Z) normal = record.Normal;
        float firstRadius = first.ExtraRadius;
        (double X, double Y, double Z) touched = record.Position;

        record.Position = (
            ((double)normal.X * firstRadius) + touched.X,
            ((double)normal.Y * firstRadius) + touched.Y,
            ((double)normal.Z * firstRadius) + touched.Z);

        float gap = point.Gap - (firstRadius + second.ExtraRadius);
        point.Gap = gap >= 0f ? gap : 0f;

        (float X, float Y, float Z) span = record.Span;
        _ = IvpVector.TryScaleToUnitLength(ref span);
        record.Span = span;

        (float X, float Y, float Z) cross = (
            (span.Z * normal.Y) - (normal.Z * span.Y),
            (normal.Z * span.X) - (span.Z * normal.X),
            (span.Y * normal.X) - (normal.Y * span.X));
        record.CrossSpan = cross;

        (float X, float Y, float Z) relative = (0f, 0f, 0f);
        float inverseMass = 0f;

        if (!first.Core.Immovable)
        {
            (record.FirstArm, record.FirstTurn) = Lever(first, record.Position, normal);
            relative = first.Core.PointVelocity(record.FirstArm, first.Core.Velocity, first.Core.AngularVelocity);
            inverseMass = InverseMassAlong(record.FirstTurn, first.Core);
            record.FirstCore = first.Core;
        }

        if (!second.Core.Immovable)
        {
            (record.SecondArm, record.SecondTurn) = Lever(second, record.Position, normal);
            (float X, float Y, float Z) velocity =
                second.Core.PointVelocity(record.SecondArm, second.Core.Velocity, second.Core.AngularVelocity);
            relative = (relative.X - velocity.X, relative.Y - velocity.Y, relative.Z - velocity.Z);
            inverseMass = InverseMassAlong(record.SecondTurn, second.Core) + inverseMass;
            record.SecondCore = second.Core;
        }

        record.InverseMass = inverseMass;
        record.VirtualMass = 1f / inverseMass;

        double elapsed = (float)(now - point.LastMeasured);
        point.LastMeasured = now;

        float alongSpan = (relative.X * span.X) + (relative.Y * span.Y) + (relative.Z * span.Z);
        float alongCross = (relative.Y * cross.Y) + (relative.X * cross.X) + (relative.Z * cross.Z);

        point.Slide = (
            (float)(point.Slide.Span - ((double)alongSpan * elapsed)),
            (float)(point.Slide.CrossSpan - ((double)alongCross * elapsed)));

        point.LastPosition = ((float)record.Position.X, (float)record.Position.Y, (float)record.Position.Z);
        point.LastNormal = normal;

        return record;
    }

    /// <summary>
    /// One push along this record's normal staged into both movable cores, each then held to the limits — <c>FUN_1800a9280(record, x)</c>,
    /// which the heap solve calls for every contact whose push is not zero.
    /// </summary>
    /// <param name="push">The push, <c>x</c>: the first core is pushed by <c>−x</c>, the second by <c>+x</c>.</param>
    /// <param name="limits">The environment's limits, for <see cref="IvpPush.Limit"/>.</param>
    /// <param name="inverseStep">The environment's inverse step.</param>
    /// <exception cref="ArgumentNullException"><paramref name="limits"/> is null.</exception>
    /// <remarks>
    /// <code>
    /// first core:  staged ω += (d)(f)(turn ⊙ I⁻¹)·−x;  staged v += (d)n·−((d)m⁻¹·x);  FUN_180076710
    /// second core: staged ω += (d)(f)(turn′ ⊙ I⁻¹)·x;   staged v += (d)n·((d)m⁻¹·x);   FUN_180076710      -- each lane narrowed
    /// </code>
    /// **The first core's turn products are not written the same way round in every lane**: `x` takes the turn as destination,
    /// `y` and `z` the inertia (`MOVSS XMM4,[core+0x44]; MULSS XMM4,XMM3`), while the second core's all take the turn. Which NaN
    /// survives two decides it, so each lane names the binary's destination.
    /// </remarks>
    internal void Push(double push, IvpAnomalyLimits limits, double inverseStep)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (FirstCore is { } first)
        {
            double against = -push;
            (float X, float Y, float Z) turn = FirstTurn;
            (float X, float Y, float Z) inertia = first.InverseInertia;
            (float X, float Y, float Z) spin = first.PendingAngularVelocity;

            first.PendingAngularVelocity = (
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(turn.X, inertia.X), against), spin.X),
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(inertia.Y, turn.Y), against), spin.Y),
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(inertia.Z, turn.Z), against), spin.Z));

            double share = -IvpMath.Mulsd(first.InverseMass, push);
            (float X, float Y, float Z) velocity = first.PendingVelocity;

            first.PendingVelocity = (
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.X, share), velocity.X),
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.Y, share), velocity.Y),
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.Z, share), velocity.Z));

            IvpPush.Limit(first, limits, inverseStep);
        }

        if (SecondCore is { } second)
        {
            (float X, float Y, float Z) turn = SecondTurn;
            (float X, float Y, float Z) inertia = second.InverseInertia;
            (float X, float Y, float Z) spin = second.PendingAngularVelocity;

            second.PendingAngularVelocity = (
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(turn.X, inertia.X), push), spin.X),
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(turn.Y, inertia.Y), push), spin.Y),
                (float)IvpMath.Addsd(IvpMath.Mulsd(IvpMath.Mulss(turn.Z, inertia.Z), push), spin.Z));

            double share = IvpMath.Mulsd(second.InverseMass, push);
            (float X, float Y, float Z) velocity = second.PendingVelocity;

            second.PendingVelocity = (
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.X, share), velocity.X),
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.Y, share), velocity.Y),
                (float)IvpMath.Addsd(IvpMath.Mulsd(Normal.Z, share), velocity.Z));

            IvpPush.Limit(second, limits, inverseStep);
        }
    }

    /// <summary>Picks and runs the measure the contact point's two kinds call for.</summary>
    private static void Measure(IvpContactPoint point, IvpContactBody first, IvpContactBody second, IvpContactRecord record)
    {
        (double X, double Y, double Z) at;

        switch (point.First.Kind)
        {
            case IvpFeatureKind.Point:
                at = IvpCompactLedgeSolver.PointInWorld(first.Side, point.First.Feature);
                break;

            case IvpFeatureKind.Edge:
                IvpContactGeometry.EdgeEdge(point, point.First.Feature, first.Side, point.Second.Feature, second.Side, record);
                return;

            case IvpFeatureKind.Ball:
                at = first.Side.Current.Translation;
                point.FirstMeasure = true;
                break;

            default:
                throw new InvalidOperationException(
                    "The engine asserts at line 0x1c4 of its contact builder: a contact point's first feature is not a point, an edge or a ball.");
        }

        switch (point.Second.Kind)
        {
            case IvpFeatureKind.Point:
                IvpContactGeometry.PointPoint(
                    point, at, IvpCompactLedgeSolver.PointInWorld(second.Side, point.Second.Feature), record);
                break;

            case IvpFeatureKind.Edge:
                IvpContactGeometry.PointEdge(point, at, point.Second.Feature, second.Side, record);
                break;

            case IvpFeatureKind.Triangle:
                IvpContactGeometry.PointTriangle(point, at, point.Second.Feature, second.Side, record);
                break;

            case IvpFeatureKind.Ball:
                IvpContactGeometry.PointPoint(point, at, second.Side.Current.Translation, record);
                break;

            default:
                throw new InvalidOperationException(
                    "The engine asserts at line 0x1ba of its contact builder: a contact point's second feature has no measure.");
        }
    }

    /// <summary>The position and the normal in a core's frame, and their cross product — the inlined start of each core's terms.</summary>
    /// <remarks>
    /// The arm is `FUN_1800708a0` — the translation off in double, the transpose, narrowed — and the normal goes through the same
    /// transpose inline; every column grouped as <see cref="IvpMatrix.RotateInverse"/> groups it.
    /// </remarks>
    private static ((float X, float Y, float Z) Arm, (float X, float Y, float Z) Turn) Lever(
        IvpContactBody body, (double X, double Y, double Z) position, (float X, float Y, float Z) normal)
    {
        (double X, double Y, double Z) local = body.Core.CoreMatrix.ToObject(position);
        (float X, float Y, float Z) arm = ((float)local.X, (float)local.Y, (float)local.Z);

        (double X, double Y, double Z) turned = body.Core.CoreMatrix.RotateInverse((normal.X, normal.Y, normal.Z));
        (float X, float Y, float Z) axis = ((float)turned.X, (float)turned.Y, (float)turned.Z);

        return (arm, (
            (arm.Y * axis.Z) - (arm.Z * axis.Y),
            (axis.X * arm.Z) - (axis.Z * arm.X),
            (axis.Y * arm.X) - (axis.X * arm.Y)));
    }

    /// <summary>A core's inverse mass along the normal, from its turn — <c>((t.y·I.y)·t.y + (t.x·I.x)·t.x) + (t.z·I.z)·t.z + m</c>, in float.</summary>
    private static float InverseMassAlong((float X, float Y, float Z) turn, IvpRigidBody core)
    {
        (float X, float Y, float Z) inertia = core.InverseInertia;

        return (turn.Y * inertia.Y * turn.Y) + (turn.X * inertia.X * turn.X) + (turn.Z * inertia.Z * turn.Z) + core.InverseMass;
    }
}
