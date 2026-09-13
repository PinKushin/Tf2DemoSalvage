using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One side of a contact as <c>FUN_18008d0c0</c> reads it (B369).</summary>
/// <param name="Side">The object's ledge where the object is now — the cache object the builder refreshes before it measures.</param>
/// <param name="Core">The object's core, <c>object+0xe8</c>.</param>
/// <param name="CoreMatrix">The core's transform at <c>core+0x90</c>, which the arm and the normal are put into.</param>
/// <param name="ExtraRadius">The object's extra radius, <c>object+0xe0</c>.</param>
public sealed record IvpContactBody(IvpLedgeSide Side, IvpRigidBody Core, IvpMatrix CoreMatrix, float ExtraRadius);

/// <summary>
/// The record IVP builds each time a pair's contact point collides — the <c>0x110</c> bytes <c>FUN_18008d0c0</c> takes from
/// the environment's arena (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The contact point and its record*). The builder measures the contact
/// point with <see cref="IvpContactGeometry"/> and then, for each core not flagged `2`, puts the position and the normal into
/// the core's own frame for the arm and the turn the impact and friction solves push through.
///
/// **Not ported yet**: the materials at `+0x60`/`+0x68`, the objects and edges at `+0x40..0x58`, the friction factor the
/// contact point takes at `+0x78` and the float at `+0x80`, all of which `FUN_1800908d0` writes after the builder returns, and
/// the count at `+0x72` that the impact solver is handed. The allocation zeroes `+0x20..0x2b`, `+0x72..0x75` and `+0x76`.
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
    public IvpRigidBody? FirstCore { get; private set; }

    /// <summary>The second feature's core, when it is movable — <c>+0xa0</c>.</summary>
    public IvpRigidBody? SecondCore { get; private set; }

    /// <summary>A unit direction across the normal — <c>+0xb0</c>.</summary>
    public (float X, float Y, float Z) Span { get; internal set; }

    /// <summary><c>normal × span</c> — <c>+0xc0</c>.</summary>
    public (float X, float Y, float Z) CrossSpan { get; private set; }

    /// <summary>The position in the first core's frame — <c>+0xd0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) FirstArm { get; private set; }

    /// <summary>The position in the second core's frame — <c>+0xe0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) SecondArm { get; private set; }

    /// <summary><c>arm × normal</c> in the first core's frame — <c>+0xf0</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) FirstTurn { get; private set; }

    /// <summary><c>arm × normal</c> in the second core's frame — <c>+0x100</c>; zero for a static core.</summary>
    public (float X, float Y, float Z) SecondTurn { get; private set; }

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
            relative = VelocityAt(first, record.FirstArm);
            inverseMass = InverseMassAlong(record.FirstTurn, first.Core);
            record.FirstCore = first.Core;
        }

        if (!second.Core.Immovable)
        {
            (record.SecondArm, record.SecondTurn) = Lever(second, record.Position, normal);
            (float X, float Y, float Z) velocity = VelocityAt(second, record.SecondArm);
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
        (double X, double Y, double Z) local = body.CoreMatrix.ToObject(position);
        (float X, float Y, float Z) arm = ((float)local.X, (float)local.Y, (float)local.Z);

        (double X, double Y, double Z) turned = body.CoreMatrix.RotateInverse((normal.X, normal.Y, normal.Z));
        (float X, float Y, float Z) axis = ((float)turned.X, (float)turned.Y, (float)turned.Z);

        return (arm, (
            (arm.Y * axis.Z) - (arm.Z * axis.Y),
            (axis.X * arm.Z) - (axis.Z * arm.X),
            (axis.Y * arm.X) - (axis.X * arm.Y)));
    }

    /// <summary>The velocity of a point fixed to a core — <c>FUN_180077fa0</c>.</summary>
    /// <remarks>
    /// `ω × arm` in float, turned into the world by the core's matrix in double and narrowed, then the linear velocity added in
    /// float.
    /// </remarks>
    private static (float X, float Y, float Z) VelocityAt(IvpContactBody body, (float X, float Y, float Z) arm)
    {
        (float X, float Y, float Z) spin = body.Core.AngularVelocity;

        (float X, float Y, float Z) swept = (
            (spin.Y * arm.Z) - (spin.Z * arm.Y),
            (arm.X * spin.Z) - (spin.X * arm.Z),
            (spin.X * arm.Y) - (arm.X * spin.Y));

        (double X, double Y, double Z) turned = body.CoreMatrix.Rotate((swept.X, swept.Y, swept.Z));
        (float X, float Y, float Z) velocity = body.Core.Velocity;

        return ((float)turned.X + velocity.X, (float)turned.Y + velocity.Y, (float)turned.Z + velocity.Z);
    }

    /// <summary>A core's inverse mass along the normal, from its turn — <c>((t.y·I.y)·t.y + (t.x·I.x)·t.x) + (t.z·I.z)·t.z + m</c>, in float.</summary>
    private static float InverseMassAlong((float X, float Y, float Z) turn, IvpRigidBody core)
    {
        (float X, float Y, float Z) inertia = core.InverseInertia;

        return (turn.Y * inertia.Y * turn.Y) + (turn.X * inertia.X * turn.X) + (turn.Z * inertia.Z * turn.Z) + core.InverseMass;
    }
}
