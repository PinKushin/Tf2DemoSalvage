using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The environment's fields the impact solver reads and writes (B369).</summary>
public sealed class IvpImpactEnvironment
{
    /// <summary>The reciprocal of the PSI step, <c>env+0x110</c>, which the spin limit narrows to float.</summary>
    public required double InverseStep { get; init; }

    /// <summary>The PSI step, <c>env+0x108</c>, which a record's estimate narrows to float.</summary>
    public required double Step { get; init; }

    /// <summary>The material manager, <c>env+0xe8</c>.</summary>
    public required IIvpMaterialManager Materials { get; init; }

    /// <summary>The anomaly limits, <c>env+0x48</c>.</summary>
    public required IvpAnomalyLimits Limits { get; init; }

    /// <summary>The anomaly manager, <c>env+0x40</c>.</summary>
    public required IIvpAnomalyManager Anomalies { get; init; }

    /// <summary>The impacts solved, <c>env+0x94</c>, counted through the first core's environment.</summary>
    public int Impacts { get; set; }

    /// <summary>The impacts whose heavier core was held back, <c>env+0xa4</c>.</summary>
    public int HeldBack { get; set; }

    /// <summary>The impacts that found either core's freeze bits set, <c>env+0xac</c>.</summary>
    public int Frozen { get; set; }
}

/// <summary>
/// IVP's impact solver: one collision between two cores, pushed apart to a separating speed inside a friction cone —
/// <c>FUN_18008e290</c> and every routine under it (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly instruction by instruction** (`docs/findings/51`, *The impact solver* and the sections after it),
/// **and pinned to the shipped `vphysics.dll` called in process** — `IvpImpactSolverConformanceTests` replays the cases the
/// `vphysics-impact` probe gave the binary, lane for lane.
///
/// **In IVP's own units — metres, kilograms, seconds — and with no conversion anywhere.** Every threshold here is a literal
/// the engine compares a metre-based quantity against, so a body in Source units would meet different thresholds and round
/// differently; the environment must hand this bodies in IVP space (`docs/RISKS.md` B369).
///
/// **The fields mirror the solver the entry builds on its stack** (`FUN_18008ed60`): the inputs are initialised by the caller,
/// and the working state — each side's velocity and spin at `+0x40..0x7b`, the last push's changes at `+0x80..0xbb`, the
/// relative velocity, the push direction and its fallback — is readable afterwards so a replay can compare all of it.
/// </remarks>
public sealed class IvpImpactSolver
{
    /// <summary><c>DAT_1800ea984</c>.</summary>
    private const float Half = 0.5f;

    /// <summary><c>DAT_1800ea988</c>.</summary>
    private const float One = 1f;

    /// <summary><c>DAT_1800fd858</c>: <c>1/24</c>, the fourth-order term of the cosine series.</summary>
    private const float TwentyFourth = 1f / 24f;

    /// <summary><c>DAT_1800fd868</c>: <c>1.2f</c>, the separating speed's margin over the push-out estimate.</summary>
    private const float Restitution = 1.2f;

    /// <summary><c>DAT_1800fd870</c>: the factor an immovable core's virtual mass takes over the other's.</summary>
    private const double StaticMass = 1e5;

    /// <summary><c>DAT_1800fd880</c>: <c>−0.1f</c> widened, the share of the approach one push removes.</summary>
    private const double Share = -0.1f;

    /// <summary><c>DAT_1800ee398</c>: the approach speed at or below which the pair is solved rather than separated.</summary>
    private const float Approaching = -1e-4f;

    /// <summary><c>DAT_1800eb150</c>: <c>0.01f</c> widened, the allowance each recorded impact's root adds.</summary>
    private const double ImpactAllowance = 0.01f;

    /// <summary><c>CMP EDI, 0x64</c>: the pushes the loop takes at most.</summary>
    private const int MaximumPushes = 100;

    /// <summary><c>CMP [RBP+0x77], 0xa</c>: past this many recorded impacts a separating pair may no longer be held back.</summary>
    private const int ImpactsBeforeRelease = 10;

    /// <summary><c>DAT_1800f50f8</c>: <c>1e-4f</c> widened, below which a unit push's effect is no measure at all.</summary>
    private const double Negligible = 1e-4f;

    /// <summary><c>DAT_1800fd878</c>: the share of the separating speed below which the heavier core is held back.</summary>
    private const float HoldBackShare = -0.8333333f;

    /// <summary><c>DAT_1800fd850</c>: <c>1e-15f</c> widened, added to the response before the separating push divides by it.</summary>
    private const double Stiffness = 1e-15f;

    /// <summary><c>DAT_1800f4f20</c>: <c>1e-19</c>, below which a material's axis has no direction.</summary>
    private const double AxisLengthFloor = 1e-19;

    private (float X, float Y, float Z) _firstSpin;
    private (float X, float Y, float Z) _secondSpin;
    private (float X, float Y, float Z) _firstVelocity;
    private (float X, float Y, float Z) _secondVelocity;
    private (float X, float Y, float Z) _firstSpinChange;
    private (float X, float Y, float Z) _secondSpinChange;
    private (float X, float Y, float Z) _firstVelocityChange;
    private (float X, float Y, float Z) _secondVelocityChange;
    private (float X, float Y, float Z) _relative;
    private (float X, float Y, float Z) _push;
    private (float X, float Y, float Z) _fallback;

    /// <summary>The first core, <c>+0x110</c> — a static one when the pair has one.</summary>
    public required IvpRigidBody First { get; init; }

    /// <summary>The second core, <c>+0x118</c>.</summary>
    public required IvpRigidBody Second { get; init; }

    /// <summary>Where the contact is in the first core's frame, through <c>+0x120</c>.</summary>
    public required (float X, float Y, float Z) FirstArm { get; init; }

    /// <summary>Where the contact is in the second core's frame, through <c>+0x128</c>.</summary>
    public required (float X, float Y, float Z) SecondArm { get; init; }

    /// <summary>The contact's normal, from the first core toward the second, through <c>+0x140</c>.</summary>
    public required (float X, float Y, float Z) Normal { get; init; }

    /// <summary>The pair's elasticity, <c>+0x130</c>.</summary>
    public required float Elasticity { get; init; }

    /// <summary>The friction cone's cosine, <c>+0x134</c>.</summary>
    public required float ConeCosine { get; init; }

    /// <summary>The friction cone's tangent term, <c>+0x138</c>.</summary>
    public required float ConeTangent { get; init; }

    /// <summary>Whether a material's axis shapes the cone, <c>+0xf0</c> (<c>FUN_18008fe70</c>).</summary>
    public bool UsesAxis { get; init; }

    /// <summary>The cone's tangent term along that axis, <c>+0xf4</c>.</summary>
    public float AxisTangent { get; init; }

    /// <summary>That axis, <c>+0x100</c>.</summary>
    public (float X, float Y, float Z) Axis { get; init; }

    /// <summary>The speed the pair is pushed apart to, <c>+0x0</c>.</summary>
    public float SeparationSpeed { get; private set; }

    /// <summary>The mass the first core presents along the normal, <c>+0x8</c>.</summary>
    public double FirstVirtualMass { get; private set; }

    /// <summary>The mass the second core presents, <c>+0x10</c>.</summary>
    public double SecondVirtualMass { get; private set; }

    /// <summary>Whether the heavier core may be held back, <c>+0x18</c>.</summary>
    public bool MayHoldBack { get; private set; }

    /// <summary>The first core's working angular velocity, <c>+0x40</c>.</summary>
    public (float X, float Y, float Z) FirstSpin => _firstSpin;

    /// <summary>The second core's working angular velocity, <c>+0x50</c>.</summary>
    public (float X, float Y, float Z) SecondSpin => _secondSpin;

    /// <summary>The first core's working velocity, <c>+0x60</c>.</summary>
    public (float X, float Y, float Z) FirstVelocity => _firstVelocity;

    /// <summary>The second core's working velocity, <c>+0x70</c>.</summary>
    public (float X, float Y, float Z) SecondVelocity => _secondVelocity;

    /// <summary>The first core's last change in angular velocity, <c>+0x80</c>.</summary>
    public (float X, float Y, float Z) FirstSpinChange => _firstSpinChange;

    /// <summary>The second core's last change in angular velocity, <c>+0x90</c>.</summary>
    public (float X, float Y, float Z) SecondSpinChange => _secondSpinChange;

    /// <summary>The first core's last change in velocity, <c>+0xa0</c>.</summary>
    public (float X, float Y, float Z) FirstVelocityChange => _firstVelocityChange;

    /// <summary>The second core's last change in velocity, <c>+0xb0</c>.</summary>
    public (float X, float Y, float Z) SecondVelocityChange => _secondVelocityChange;

    /// <summary>The second contact point's velocity less the first's, <c>+0xc0</c>.</summary>
    public (float X, float Y, float Z) Relative => _relative;

    /// <summary>The direction the pair is pushed along, <c>+0xd0</c>.</summary>
    public (float X, float Y, float Z) Push => _push;

    /// <summary>The direction a push takes when the pair already separates, <c>+0xe0</c> — zeroed before the loop.</summary>
    public (float X, float Y, float Z) Fallback => _fallback;

    /// <summary>The pushes the approaching loop took — <c>EDI</c>, a local; an instrument, so a probe can find the cap.</summary>
    public int Pushes { get; private set; }

    /// <summary>The heavier core's closing speed the hold-back test compared — <c>XMM3</c>, a local; an instrument.</summary>
    public double HoldBackSpeed { get; private set; }

    /// <summary>The relative velocity as the solve began, written through <c>+0x148</c> to the record's <c>+0x30</c>.</summary>
    public (float X, float Y, float Z) RecordRelative { get; private set; }

    /// <summary>Builds the solver for a contact point's record and solves its impact — <c>FUN_18008ed60(record, cores, p5, cp)</c>.</summary>
    /// <param name="environment">The environment both cores are in.</param>
    /// <param name="point">The contact point, whose record <see cref="IvpContactPoint.SetMaterials"/> has written.</param>
    /// <param name="cores">Two slots, as <see cref="Solve"/> takes them.</param>
    /// <param name="pushOut"><c>p5</c>: the contact point's <see cref="IvpContactPoint.PushOut"/>.</param>
    /// <returns>The solver, after its solve.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The point has no record, or a core the entry reads is absent — where the engine reads through a null pointer.</exception>
    /// <remarks>
    /// <code>
    /// if record+0xa0 is null:  A = record+0x48's core;  B = record+0x98;  arms = (+0xe0, +0xd0);  n = −record+0x20
    /// else:                    A = record+0x98, or record+0x40's core;  B = record+0xa0;  arms = (+0xd0, +0xe0);  n = record+0x20
    /// A+0x58 or B+0x58 set:  cone (1f, 0f)
    /// else:  t = (√(double)record+0x80 + 1.0)·(double)cp+0x78;  x = (float)atan(t);  c = (1f − x²·0.5f) + (x²·(1/24f))·x²
    ///        cone (c, (float)((double)c·t));  cp+0x64 set → FUN_18008fe70
    /// FUN_18008e290(solver, cores, 1, record+0x72, p5)
    /// </code>
    /// **The static body is always the solver's first**, the normal turned to match.
    /// </remarks>
    public static IvpImpactSolver Enter(IvpImpactEnvironment environment, IvpContactPoint point, IvpRigidBody?[] cores, float pushOut)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(cores);

        IvpContactRecord record = point.Record ?? throw new InvalidOperationException("The contact point has no record to solve.");
        bool staticSecond = record.SecondCore is null;
        IvpRigidBody first = staticSecond ? CoreOf(record.SecondObject) : record.FirstCore ?? CoreOf(record.FirstObject);
        IvpRigidBody second = record.SecondCore ?? record.FirstCore ??
            throw new InvalidOperationException("Neither of the record's cores is movable; the engine would read through a null core.");

        float cosine = One;
        float tangent = 0f;
        (bool Uses, float Tangent, (float X, float Y, float Z) Axis) axis = default;

        if (!first.HasOffset58 && !second.HasOffset58)
        {
            (cosine, tangent) = Cone((Math.Sqrt(record.Elasticity) + 1d) * point.Friction);

            if (point.UsesMaterialAxes)
            {
                axis = MaterialAxes(environment, point, record);
            }
        }

        IvpImpactSolver solver = new()
        {
            First = first,
            Second = second,
            FirstArm = staticSecond ? record.SecondArm : record.FirstArm,
            SecondArm = staticSecond ? record.FirstArm : record.SecondArm,
            Normal = staticSecond ? Negate(record.Normal) : record.Normal,
            Elasticity = record.Elasticity,
            ConeCosine = cosine,
            ConeTangent = tangent,
            UsesAxis = axis.Uses,
            AxisTangent = axis.Tangent,
            Axis = axis.Axis,
        };

        solver.Solve(environment, cores, mayHoldBack: true, record.Impacts, pushOut);
        record.RelativeVelocity = solver.RecordRelative;

        return solver;
    }

    /// <summary>The cone along the materials' axes — <c>FUN_18008fe70(solver, cp)</c>.</summary>
    /// <remarks>
    /// <code>
    /// per synapse, first then second, when its material's +0xc is set:
    ///     axis = its object's +0xf0 core's matrix's first column, less its part along the record's normal (FUN_180070130)
    ///     ℓ = FUN_18006e120(axis);  p = the other material's slot 1 · this one's slot 2
    ///     if ℓ ≥ 1e-19:  axis scaled (FUN_18006dff0);  t = (√(double)e + 1.0)·(double)(float)((double)f − ((double)f − p)·ℓ)
    ///                   +0x100 = axis;  +0xf4 = the cone of t's tangent;  +0xf0 = 1          -- COMISD/JC: a NaN ℓ skips
    /// </code>
    /// **The second synapse's block overwrites the first's.** `e` is the record's elasticity and `f` the contact point's friction.
    /// </remarks>
    private static (bool Uses, float Tangent, (float X, float Y, float Z) Axis) MaterialAxes(
        IvpImpactEnvironment environment, IvpContactPoint point, IvpContactRecord record)
    {
        IIvpMaterial first = point.FirstMaterial(environment.Materials);
        IIvpMaterial second = point.SecondMaterial(environment.Materials);
        (bool Uses, float Tangent, (float X, float Y, float Z) Axis) result = default;

        AlongAxis(ref result, point, record, point.FirstObject, first, second);
        AlongAxis(ref result, point, record, point.SecondObject, second, first);

        return result;
    }

    private static void AlongAxis(
        ref (bool Uses, float Tangent, (float X, float Y, float Z) Axis) result,
        IvpContactPoint point,
        IvpContactRecord record,
        IvpCollisionObject owner,
        IIvpMaterial own,
        IIvpMaterial other)
    {
        if (!own.HasSecondFriction)
        {
            return;
        }

        IvpRigidBody frame = owner.FrameCore ??
            throw new InvalidOperationException("A material with an axis friction belongs to an object with no frame core.");
        IvpMatrix matrix = frame.CoreMatrix;
        (float X, float Y, float Z) axis = Across(((float)matrix.M0, (float)matrix.M4, (float)matrix.M8), record.Normal);
        double length = IvpVector.Length(axis);
        double friction = other.FrictionFactor * own.SecondFrictionFactor;

        if (!(length >= AxisLengthFloor))
        {
            return;
        }

        _ = IvpVector.TryScaleToUnitLength(ref axis);

        double pairFriction = point.Friction;
        double along = (Math.Sqrt(record.Elasticity) + 1d) * (float)(pairFriction - ((pairFriction - friction) * length));

        result = (true, Cone(along).Tangent, axis);
    }

    /// <summary>A friction cone from its tangent: the fourth-order cosine of <c>x = (float)atan(t)</c>, and that cosine times <c>t</c>.</summary>
    private static (float Cosine, float Tangent) Cone(double tangent)
    {
        float angle = (float)IvpMath.Atan(tangent);
        float squared = angle * angle;
        float cosine = (One - (squared * Half)) + (squared * TwentyFourth * squared);

        return (cosine, (float)(cosine * tangent));
    }

    private static IvpRigidBody CoreOf(IvpCollisionObject? owner) =>
        owner?.Core ?? throw new InvalidOperationException("The record's object has no core; the engine would read through a null one.");

    /// <summary>Solves one impact — <c>FUN_18008e290(solver, cores, p3, p4, p5)</c>.</summary>
    /// <param name="environment">The environment both cores are in.</param>
    /// <param name="cores">Two slots: the cores that took the impact, the heavier one nulled when it is held back.</param>
    /// <param name="mayHoldBack"><c>p3</c>, which the entry always passes as one.</param>
    /// <param name="impacts"><c>p4</c>: the impacts the contact record has counted, its signed word at <c>+0x72</c>.</param>
    /// <param name="pushOut"><c>p5</c>: the push-out estimate <c>FUN_18008fca0</c> gave.</param>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> or <paramref name="cores"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="cores"/> has fewer than two slots.</exception>
    /// <remarks>
    /// <code>
    /// e = 1f − (1f − elasticity)/((float)p4·0.5f + 1f);   +0x0 = (p5 + block[0x4a])·1.2f;   cores = (A, B);   env+0x94 += 1
    /// each side's velocity and spin = the core's plus its pending, in float
    /// c = the relative velocity;  the record's +0x30 = c
    /// mB = B.VirtualMass(armB, n);  mA = A.VirtualMass(armA, −n);  an immovable side takes the other's · 1e5, A first
    /// u = n·c;  k = ((−0.1/(mA + mB))·mA)·((mB + mB)·u)
    /// if !(u ≤ −1e-4f):  separate along −n, clamped;  p4 > 10 → +0x18 = 0
    /// else:  choose the push;  +0xe0 = 0;  s = −u;  b = √p4·0.01f + √e·s
    ///        while s > 0 and fewer than 100 pushes:  push by k;  c;  s = −(n·c);  choose the push
    ///        t = s + b;  +0xd0 = −n
    ///        if t > 0 and the loop was not capped:  push by 1;  c;  δ = n·c + s;  j = |δ| > 1e-4f ? t/δ : 0
    ///            undo the unit push on each movable side;  push by j
    ///        separate along +0xd0, unclamped
    /// the anomaly limits for A, then B;  commit;  either core's freeze bits → env+0xac += 1 and freeze both
    /// </code>
    /// </remarks>
    public void Solve(IvpImpactEnvironment environment, IvpRigidBody?[] cores, bool mayHoldBack, int impacts, float pushOut)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(cores);

        if (cores.Length < 2)
        {
            throw new ArgumentException("The solver writes both cores into the first two slots.", nameof(cores));
        }

        MayHoldBack = mayHoldBack;

        float restitution = One - ((One - Elasticity) / (((float)impacts * Half) + One));

        SeparationSpeed = (pushOut + IvpCollisionTolerance.TwiceToleranceMetres) * Restitution;

        cores[0] = First;
        cores[1] = Second;
        environment.Impacts++;

        _firstSpin = Sum(First.AngularVelocity, First.PendingAngularVelocity);
        _firstVelocity = Sum(First.Velocity, First.PendingVelocity);
        _secondSpin = Sum(Second.AngularVelocity, Second.PendingAngularVelocity);
        _secondVelocity = Sum(Second.Velocity, Second.PendingVelocity);

        MeasureRelative();
        RecordRelative = _relative;

        MeasureVirtualMasses();

        (float X, float Y, float Z) normal = Normal;
        float approach = IvpVector.Dot(normal, _relative);
        double first = FirstVirtualMass;
        double second = SecondVirtualMass;
        double share = ((Share / (first + second)) * first) * ((second + second) * approach);

        // COMISS/JBE: a NaN approach takes the approaching branch, so the test is `>`, not a negated `<=`.
        if (approach > Approaching)
        {
            Separate(Negate(normal), clamp: true);

            if (impacts > ImpactsBeforeRelease)
            {
                MayHoldBack = false;
            }
        }
        else
        {
            Approach(restitution, impacts, share);
        }

        CheckLimits(environment, First, ref _firstVelocity, ref _firstSpin);
        CheckLimits(environment, Second, ref _secondVelocity, ref _secondSpin);

        Commit(environment, cores);

        if (First.CollisionFreeze != 0 || Second.CollisionFreeze != 0)
        {
            environment.Frozen++;

            Freeze(First);
            cores[0] = First;

            Freeze(Second);
            cores[1] = Second;
        }
    }

    /// <summary>Each side's virtual mass along the normal, the second first, and the immovable sides' overrides.</summary>
    private void MeasureVirtualMasses()
    {
        (float X, float Y, float Z) normal = Normal;

        SecondVirtualMass = Second.VirtualMass(SecondArm, Second.CoreMatrix.RotateInverseNarrowed(normal), normal);

        (float X, float Y, float Z) reversed = Scale(normal, -1f);

        FirstVirtualMass = First.VirtualMass(FirstArm, First.CoreMatrix.RotateInverseNarrowed(reversed), reversed);

        if (First.Immovable)
        {
            FirstVirtualMass = SecondVirtualMass * StaticMass;
        }

        if (Second.Immovable)
        {
            SecondVirtualMass = FirstVirtualMass * StaticMass;
        }
    }

    /// <summary>The approaching branch: the push loop, the final correction, and the separation.</summary>
    private void Approach(float restitution, int impacts, double share)
    {
        (float X, float Y, float Z) normal = Normal;

        ChoosePush();
        _fallback = default;

        double speed = -IvpVector.Dot(normal, _relative);
        double allowance = (Math.Sqrt(impacts) * ImpactAllowance) + (Math.Sqrt(restitution) * speed);
        int pushes = 0;

        while (speed > 0d && pushes < MaximumPushes)
        {
            pushes++;
            PushAlong(share);
            MeasureRelative();
            speed = -IvpVector.Dot(normal, _relative);
            ChoosePush();
        }

        Pushes = pushes;

        double target = speed + allowance;

        _push = Negate(normal);

        if (target > 0d && pushes != MaximumPushes)
        {
            PushAlong(1d);
            MeasureRelative();

            double change = (double)IvpVector.Dot(_relative, normal) + speed;
            double scale = Math.Abs(change) > Negligible ? target / change : 0d;

            if (!First.Immovable)
            {
                _firstSpin = Difference(_firstSpin, _firstSpinChange);
                _firstVelocity = Difference(_firstVelocity, _firstVelocityChange);
            }

            if (!Second.Immovable)
            {
                _secondSpin = Difference(_secondSpin, _secondSpinChange);
                _secondVelocity = Difference(_secondVelocity, _secondVelocityChange);
            }

            PushAlong(scale);
        }

        Separate(_push, clamp: false);
    }

    /// <summary>The relative velocity at the contact from the working velocities — <c>FUN_18008fc00</c>.</summary>
    private void MeasureRelative()
    {
        (float X, float Y, float Z) first = First.PointVelocity(FirstArm, _firstVelocity, _firstSpin);
        (float X, float Y, float Z) second = Second.PointVelocity(SecondArm, _secondVelocity, _secondSpin);

        _relative = (second.X - first.X, second.Y - first.Y, second.Z - first.Z);
    }

    /// <summary>Which way to push — <c>FUN_180090240</c>.</summary>
    /// <remarks>
    /// <code>
    /// d = c scaled;  k = (double)(d·n)
    /// k > 0 → d = +0xe0 scaled;   the axis flag → FUN_1800904a0(k);   k > −cos → d's slide off n scaled, times the tangent,
    /// plus n·−cos, each lane in double and narrowed
    /// </code>
    /// </remarks>
    private void ChoosePush()
    {
        (float X, float Y, float Z) normal = Normal;

        _push = _relative;
        _ = IvpVector.TryScaleToUnitLength(ref _push);

        double along = IvpVector.Dot(_push, normal);

        if (along > 0d)
        {
            _push = _fallback;
            _ = IvpVector.TryScaleToUnitLength(ref _push);
            return;
        }

        if (UsesAxis)
        {
            HoldInCone(along);
            return;
        }

        if (!(along > (double)-ConeCosine))
        {
            return;
        }

        (float X, float Y, float Z) slide = Across(_push, normal, along);
        _ = IvpVector.TryScaleToUnitLength(ref slide);

        double tangent = ConeTangent;
        slide = ((float)(slide.X * tangent), (float)(slide.Y * tangent), (float)(slide.Z * tangent));

        double cosine = -ConeCosine;

        _push = (
            (float)((normal.X * cosine) + slide.X),
            (float)((normal.Y * cosine) + slide.Y),
            (float)((normal.Z * cosine) + slide.Z));
    }

    /// <summary>A push held inside a cone along a material's axis — <c>FUN_1800904a0</c>.</summary>
    /// <remarks>
    /// <code>
    /// v = the push's slide off n;  ℓ = (float)FUN_18006fc90(v), v scaled;  q = |v·axis|;  r = asinf(q)
    /// c = (1f − r²·0.5f) + (r²·(1/24f))·r²;  m = q²·(+0xf4)² + (+0x138)²·c²
    /// ℓ² > m → σ = √m;  r' = asinf(σ);  c' = (0.5f − r'²·(1/24f))·r'² − 1f;  d = (float)((double)v·σ + (double)(float)(n·c'))
    /// </code>
    /// </remarks>
    private void HoldInCone(double along)
    {
        (float X, float Y, float Z) normal = Normal;
        (float X, float Y, float Z) slide = Across(_push, normal, along);

        float length = (float)IvpVector.ScaleToUnitLength(ref slide);
        float across = MathF.Abs(IvpVector.Dot(slide, Axis));
        float angle = IvpMath.Asinf(across);
        float angleSquared = angle * angle;
        float cosine = (One - (angleSquared * Half)) + (angleSquared * TwentyFourth * angleSquared);
        float reach = (across * across * (AxisTangent * AxisTangent)) + (ConeTangent * ConeTangent * (cosine * cosine));

        if (!(length * length > reach))
        {
            return;
        }

        float sine = MathF.Sqrt(reach);
        float bend = IvpMath.Asinf(sine);
        float bendSquared = bend * bend;
        double lift = ((Half - (bendSquared * TwentyFourth)) * bendSquared) - One;
        double scale = sine;

        _push = (
            (float)((slide.X * scale) + (float)(normal.X * lift)),
            (float)((slide.Y * scale) + (float)(normal.Y * lift)),
            (float)((slide.Z * scale) + (float)(normal.Z * lift)));
    }

    /// <summary>A vector's part off a normal — <c>FUN_180070130</c>: each lane <c>(float)((double)n·−k + (double)v)</c>, <c>k = n·v</c> in float.</summary>
    private static (float X, float Y, float Z) Across((float X, float Y, float Z) vector, (float X, float Y, float Z) normal) =>
        Across(vector, normal, IvpVector.Dot(normal, vector));

    /// <summary>The same with <c>k</c> given — how the solver takes the push's part off the normal.</summary>
    private static (float X, float Y, float Z) Across(
        (float X, float Y, float Z) vector, (float X, float Y, float Z) normal, double along)
    {
        double reversed = -along;

        return (
            (float)((normal.X * reversed) + vector.X),
            (float)((normal.Y * reversed) + vector.Y),
            (float)((normal.Z * reversed) + vector.Z));
    }

    /// <summary>Pushes the pair along the push direction — <c>FUN_18008f1c0(solver, j)</c>.</summary>
    /// <remarks>
    /// `p = (float)((double)d·j)` per lane; a movable first core takes a unit push along `p`, turned into its frame inline as
    /// `FUN_180070620` turns, and a movable second one along `p·−1f`; each side's changes land at `+0x80..0xbb` and are added
    /// to its working spin and velocity in float.
    /// </remarks>
    private void PushAlong(double impulse)
    {
        (float X, float Y, float Z) push =
            ((float)(_push.X * impulse), (float)(_push.Y * impulse), (float)(_push.Z * impulse));

        if (!First.Immovable)
        {
            (_firstVelocityChange, _firstSpinChange) =
                First.UnitPush(FirstArm, First.CoreMatrix.RotateInverseNarrowed(push), push);

            _firstSpin = Sum(_firstSpin, _firstSpinChange);
            _firstVelocity = Sum(_firstVelocity, _firstVelocityChange);
        }

        if (!Second.Immovable)
        {
            (float X, float Y, float Z) reversed = Scale(push, -1f);

            (_secondVelocityChange, _secondSpinChange) =
                Second.UnitPush(SecondArm, Second.CoreMatrix.RotateInverseNarrowed(reversed), reversed);

            _secondSpin = Sum(_secondSpin, _secondSpinChange);
            _secondVelocity = Sum(_secondVelocity, _secondVelocityChange);
        }
    }

    /// <summary>Pushes the pair apart along a direction until it separates at <see cref="SeparationSpeed"/> — <c>FUN_18008f570</c>.</summary>
    /// <remarks>
    /// <code>
    /// a = (double)−((vB − vA)·d), MINSD 0 when clamped;  g = (double)+0x0 − a;  g &lt; 0 or NaN → return
    /// m = the second core's response along −d, then plus the first's along d;  j = g/(m + 1e-15f);  j &lt; 0 or NaN → return
    /// a movable first core is pushed along (float)((double)−d·−j), a movable second along (float)((double)−d·j)
    /// </code>
    /// **`FUN_180070b20` of each arm through its core's matrix is computed first and never read**, so it is not carried.
    /// </remarks>
    private void Separate((float X, float Y, float Z) direction, bool clamp)
    {
        (float X, float Y, float Z) first = First.PointVelocity(FirstArm, _firstVelocity, _firstSpin);
        (float X, float Y, float Z) second = Second.PointVelocity(SecondArm, _secondVelocity, _secondSpin);

        (float X, float Y, float Z) closing = (second.X - first.X, second.Y - first.Y, second.Z - first.Z);
        double apart = -IvpVector.Dot(closing, direction);

        if (clamp)
        {
            apart = apart < 0d ? apart : 0d;
        }

        double gap = SeparationSpeed - apart;

        if (!(gap >= 0d))
        {
            return;
        }

        (float X, float Y, float Z) reversed = Negate(direction);
        double response = 0d;

        if (!Second.Immovable)
        {
            response = Response(Second, SecondArm, reversed);
        }

        if (!First.Immovable)
        {
            response += Response(First, FirstArm, Scale(reversed, -1f));
        }

        double impulse = gap / (response + Stiffness);

        if (!(impulse >= 0d))
        {
            return;
        }

        if (!First.Immovable)
        {
            double against = -impulse;
            (float X, float Y, float Z) push =
                ((float)(reversed.X * against), (float)(reversed.Y * against), (float)(reversed.Z * against));

            (_firstVelocityChange, _firstSpinChange) =
                First.UnitPush(FirstArm, First.CoreMatrix.RotateInverseNarrowed(push), push);

            _firstSpin = Sum(_firstSpin, _firstSpinChange);
            _firstVelocity = Sum(_firstVelocity, _firstVelocityChange);
        }

        if (!Second.Immovable)
        {
            (float X, float Y, float Z) push =
                ((float)(reversed.X * impulse), (float)(reversed.Y * impulse), (float)(reversed.Z * impulse));

            (_secondVelocityChange, _secondSpinChange) =
                Second.UnitPush(SecondArm, Second.CoreMatrix.RotateInverseNarrowed(push), push);

            _secondSpin = Sum(_secondSpin, _secondSpinChange);
            _secondVelocity = Sum(_secondVelocity, _secondVelocityChange);
        }
    }

    /// <summary>How fast a core's contact point moves along a direction under a unit push along it.</summary>
    private static float Response(IvpRigidBody core, (float X, float Y, float Z) arm, (float X, float Y, float Z) direction)
    {
        ((float X, float Y, float Z) velocity, (float X, float Y, float Z) spin) =
            core.UnitPush(arm, core.CoreMatrix.RotateInverseNarrowed(direction), direction);

        return IvpVector.Dot(core.PointVelocity(arm, velocity, spin), direction);
    }

    /// <summary>The anomaly limits for one core's working velocity and spin — <c>FUN_18008dd00(core, v, ω)</c>.</summary>
    /// <remarks>
    /// <code>
    /// unless +0x58 is set and +0x8 is zero or NaN (UCOMISS/JZ):
    ///     (double)((ω.x² + ω.y²) + ω.z²) > ((double)((float)env+0x110 · +0x14))² → manager slot 1
    /// (double)((v.x² + v.y²) + v.z²) > ((double)+0xc)² → manager slot 0
    /// </code>
    /// </remarks>
    private static void CheckLimits(
        IvpImpactEnvironment environment,
        IvpRigidBody core,
        ref (float X, float Y, float Z) velocity,
        ref (float X, float Y, float Z) spin)
    {
        IvpAnomalyLimits limits = environment.Limits;

        // **Zero of either sign, or NaN**, which is what `UCOMISS` then `JZ` skips on — tested on the bits.
        bool zeroOrNaN = (BitConverter.SingleToInt32Bits(core.Offset08) & int.MaxValue) == 0 || float.IsNaN(core.Offset08);
        bool exempt = core.HasOffset58 && zeroOrNaN;

        if (!exempt)
        {
            float spinSquared = (spin.X * spin.X) + (spin.Y * spin.Y) + (spin.Z * spin.Z);
            double reach = (float)environment.InverseStep * limits.MaximumAngularVelocityPerPsi;

            if (spinSquared > reach * reach)
            {
                environment.Anomalies.MaximumAngularVelocityExceeded(limits, core, environment.InverseStep, ref spin);
            }
        }

        float speedSquared = (velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z);
        double limit = limits.MaximumVelocity;

        if (speedSquared > limit * limit)
        {
            environment.Anomalies.MaximumVelocityExceeded(limits, core, ref velocity);
        }
    }

    /// <summary>Commits the solve, or holds the heavier core back — <c>FUN_18008deb0(solver, cores)</c>.</summary>
    /// <remarks>
    /// <code>
    /// each movable core's +0x2 += 1
    /// if +0x18:  h = !(mA > mB) ? 1 : 0;  cores[h] not immovable:
    ///     w = FUN_180077d70(core h, the other, arm h, the other's arm, core h's own v and ω, the solver's v and ω of the other)
    ///     x = (double)(w·n), negated when h is 1;  !(x ≥ (double)(+0x0·−0.8333333f)) →
    ///         env+0xa4 += 1;  cores[h] = null;  both cores' pending zeroed;  commit the other
    ///         core h's pending = the solver's v and ω of h less its own;  +0x2 −= 1;  return
    /// both cores' pending zeroed;  commit A;  commit B
    /// </code>
    /// </remarks>
    private void Commit(IvpImpactEnvironment environment, IvpRigidBody?[] cores)
    {
        if (!First.Immovable)
        {
            First.Collisions++;
        }

        if (!Second.Immovable)
        {
            Second.Collisions++;
        }

        if (MayHoldBack && TryHoldBack(environment, cores))
        {
            return;
        }

        ClearPending(First);
        ClearPending(Second);
        CommitSide(environment, First, _firstVelocity, _firstSpin);
        CommitSide(environment, Second, _secondVelocity, _secondSpin);
    }

    /// <summary>The hold-back test and, when it holds, the hold.</summary>
    private bool TryHoldBack(IvpImpactEnvironment environment, IvpRigidBody?[] cores)
    {
        bool second = !(FirstVirtualMass > SecondVirtualMass);

        // The slot the solver itself filled, so the core is one of the pair.
        IvpRigidBody held = second ? Second : First;

        if (held.Immovable)
        {
            return false;
        }

        IvpRigidBody other = second ? First : Second;
        (float X, float Y, float Z) heldArm = second ? SecondArm : FirstArm;
        (float X, float Y, float Z) otherArm = second ? FirstArm : SecondArm;

        (float X, float Y, float Z) heldPoint = Moving(held, heldArm, held.Velocity, held.AngularVelocity);
        (float X, float Y, float Z) otherPoint = second
            ? Moving(other, otherArm, _firstVelocity, _firstSpin)
            : Moving(other, otherArm, _secondVelocity, _secondSpin);

        (float X, float Y, float Z) closing = (heldPoint.X - otherPoint.X, heldPoint.Y - otherPoint.Y, heldPoint.Z - otherPoint.Z);
        double along = IvpVector.Dot(closing, Normal);

        if (second)
        {
            along = -along;
        }

        HoldBackSpeed = along;

        if (along >= (double)(SeparationSpeed * HoldBackShare))
        {
            return false;
        }

        environment.HeldBack++;
        cores[second ? 1 : 0] = null;

        ClearPending(First);
        ClearPending(Second);

        if (second)
        {
            CommitSide(environment, First, _firstVelocity, _firstSpin);
            held.PendingVelocity = Difference(_secondVelocity, held.Velocity);
            held.PendingAngularVelocity = Difference(_secondSpin, held.AngularVelocity);
        }
        else
        {
            CommitSide(environment, Second, _secondVelocity, _secondSpin);
            held.PendingVelocity = Difference(_firstVelocity, held.Velocity);
            held.PendingAngularVelocity = Difference(_firstSpin, held.AngularVelocity);
        }

        held.Collisions--;

        return true;
    }

    /// <summary>A point's velocity, or zero for a core flagged <c>0x12</c> — each side of <c>FUN_180077d70</c>.</summary>
    private static (float X, float Y, float Z) Moving(
        IvpRigidBody core, (float X, float Y, float Z) arm, (float X, float Y, float Z) velocity, (float X, float Y, float Z) spin) =>
        core.Immovable || core.SkipsGravity ? default : core.PointVelocity(arm, velocity, spin);

    /// <summary>Hands one side's working velocities to its core — <c>FUN_18008ddf0(solver, i)</c>.</summary>
    /// <remarks>
    /// The velocity and spin are copied; then, when the core's `+0x2` exceeds the limits' `+0x10` as signed integers, the
    /// manager's slot 3 decides the core's freeze bits.
    /// </remarks>
    private static void CommitSide(
        IvpImpactEnvironment environment,
        IvpRigidBody core,
        (float X, float Y, float Z) velocity,
        (float X, float Y, float Z) spin)
    {
        core.Velocity = velocity;
        core.AngularVelocity = spin;

        if (core.Collisions > environment.Limits.MaximumCollisions)
        {
            core.CollisionFreeze =
                environment.Anomalies.MaximumCollisionsExceededCheckFreezing(environment.Limits, core) ? 1 : 0;
        }
    }

    /// <summary>Zeroes a core's pending velocity and spin, <c>+0x110..0x12b</c>.</summary>
    private static void ClearPending(IvpRigidBody core)
    {
        core.PendingAngularVelocity = default;
        core.PendingVelocity = default;
    }

    /// <summary>What the solver does to a core when either core's freeze bits are set — the tail of <c>FUN_18008e290</c>.</summary>
    /// <remarks>
    /// Bits 6–7 become one — zero for an immovable core — the pending velocity and spin take the velocity and spin added to
    /// them, and the velocity and spin are zeroed.
    /// </remarks>
    private static void Freeze(IvpRigidBody core)
    {
        core.CollisionFreeze = core.Immovable ? 0 : 1;
        core.PendingVelocity = Sum(core.Velocity, core.PendingVelocity);
        core.PendingAngularVelocity = Sum(core.AngularVelocity, core.PendingAngularVelocity);
        core.AngularVelocity = default;
        core.Velocity = default;
    }

    private static (float X, float Y, float Z) Sum((float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        (first.X + second.X, first.Y + second.Y, first.Z + second.Z);

    private static (float X, float Y, float Z) Difference((float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        (first.X - second.X, first.Y - second.Y, first.Z - second.Z);

    private static (float X, float Y, float Z) Negate((float X, float Y, float Z) vector) => (-vector.X, -vector.Y, -vector.Z);

    private static (float X, float Y, float Z) Scale((float X, float Y, float Z) vector, float factor) =>
        (vector.X * factor, vector.Y * factor, vector.Z * factor);
}
