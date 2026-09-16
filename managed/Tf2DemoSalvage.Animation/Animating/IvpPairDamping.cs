using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The work a friction system's normal pushes did, banked per pair and paid back as damping of the pair's relative motion — the
/// priority-2000 controller's many-contact branch, <c>FUN_180088ae0</c> and <c>FUN_180086b40</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler and settled in the disassembly** (`docs/findings/51`, *The priority-2000 routines*), every product
/// and sum in the order and width the binary gives it.
/// </remarks>
internal static class IvpPairDamping
{
    /// <summary>
    /// <c>DAT_1800fd4e0</c>, near <c>ln 0.9</c> but not its closest double — which <c>SetSimulationTimestep</c> scales by the step
    /// before <c>exp</c>. Carried as its bits; with them, <see cref="Decay"/> at <c>1/66</c> is the constructor's own
    /// <c>0x3feff2eed61b4202</c>.
    /// </summary>
    private static readonly double LogDecayPerSecond = BitConverter.Int64BitsToDouble(unchecked((long)0xbfbaf8e892d15de8UL));

    /// <summary><c>DAT_1800f4f20</c>.</summary>
    private const double Floor = 1e-19;

    /// <summary><c>DAT_1800ee388</c>.</summary>
    private const double Half = 0.5;

    /// <summary><c>DAT_1800fd578</c>: <c>0.1f</c> widened, the share of a pair's reducible energy one PSI may take.</summary>
    private const double Share = 0.1f;

    /// <summary><c>DAT_1800fb0b8</c>: what an unmovable side's mass and inertia are, times the other side's.</summary>
    private const double UnmovableMass = 10000d;

    /// <summary><c>DAT_1800ec278</c>: what an unmovable side's inverse mass and inertia are, times the other side's.</summary>
    private const double UnmovableInverse = 1e-4;

    /// <summary>The pair energy's decay per PSI — <c>env+0x1b0 = FUN_1800d3cf0(step · ln 0.9)</c>, written by <c>FUN_180082470</c>.</summary>
    /// <param name="step">The PSI step, <c>env+0x108</c>.</param>
    /// <returns><c>0.9</c> to the power of the step, through the engine's own <c>exp</c>.</returns>
    internal static double Decay(double step) => IvpMath.Exp(step * LogDecayPerSecond);

    /// <summary>Every pair's records rebuilt, and the work the pushes did banked — <c>FUN_180088ae0(system)</c>.</summary>
    /// <param name="system">The system.</param>
    /// <param name="rebuild">A contact's record rebuilt, <c>FUN_18008d0c0(cp, env)</c>.</param>
    /// <remarks>
    /// <code>
    /// every pair, last first:  acc = 0f;  every contact, last first:  g = cp+0x8c;  FUN_18008d0c0(cp, env);  acc = acc + (g − cp+0x8c)·cp+0x88
    ///     acc > 0 → pair+0x30 = acc + pair+0x30                                       -- all float
    /// </code>
    /// </remarks>
    internal static void Bank(IvpFrictionSystem system, Action<IvpContactPoint> rebuild)
    {
        for (int pairIndex = system.Pairs.Count - 1; pairIndex >= 0; pairIndex--)
        {
            IvpFrictionPair pair = system.Pairs[pairIndex];
            float work = 0f;

            for (int index = pair.Contacts.Count - 1; index >= 0; index--)
            {
                IvpContactPoint contact = pair.Contacts[index];
                float before = contact.Gap;
                rebuild(contact);
                work = IvpMath.Addss(work, IvpMath.Mulss(before - contact.Gap, contact.NormalPush));
            }

            if (work > 0f)
            {
                pair.StoredEnergy = IvpMath.Addss(work, pair.StoredEnergy);
            }
        }
    }

    /// <summary>Each pair's banked work decayed and paid back out of its relative motion — <c>FUN_180086b40(system)</c>.</summary>
    /// <param name="system">The system.</param>
    /// <remarks>
    /// <code>
    /// every pair, last first:  e = (float)((double)pair+0x30·(double)env+0x1b0);  pair+0x30 = e
    ///     e &lt; 0 (NaN too), or either core's +0x58 set → next
    ///     L = FUN_180086670(pair);  E₁ from the spin, E₂ from the velocity, each MAXSD(…, 0)·0.5
    ///     x = MINSD((E₂ + E₁)·0.1, e);  total &lt; 1e-19 (NaN too) → x = 0  else  FUN_180083f70(L, x / total)
    ///     env+0x78 = x + env+0x78;  pair+0x30 = (float)((double)pair+0x30 − x)
    /// </code>
    /// </remarks>
    internal static void PayBack(IvpFrictionSystem system)
    {
        IvpImpactEnvironment environment = system.Environment;
        double decay = Decay(environment.Step);

        for (int pairIndex = system.Pairs.Count - 1; pairIndex >= 0; pairIndex--)
        {
            IvpFrictionPair pair = system.Pairs[pairIndex];
            float energy = (float)((double)pair.StoredEnergy * decay);
            pair.StoredEnergy = energy;

            if (!(energy >= 0f) || pair.FirstCore.HasOffset58 || pair.SecondCore.HasOffset58)
            {
                continue;
            }

            RelativeMotion motion = RelativeMotion.Between(pair.FirstCore, pair.SecondCore);

            motion.RotationalEnergy = Reducible(
                motion.AngularSpeed, motion.FirstInverseInertia, motion.SecondInverseInertia, motion.SecondInertia, motion.FirstInertia);
            motion.LinearEnergy = Reducible(
                motion.LinearSpeed, motion.FirstInverseMass, motion.SecondInverseMass, motion.SecondMass, motion.FirstMass);

            double total = motion.LinearEnergy + motion.RotationalEnergy;
            double proposed = total * Share;

            // MINSD: the second operand unless the first is strictly less.
            double taken = proposed < energy ? proposed : energy;

            if (!(total >= Floor))
            {
                taken = 0d;
            }
            else
            {
                motion.Damp(taken / total);
            }

            environment.DampedEnergy = taken + environment.DampedEnergy;
            pair.StoredEnergy = (float)((double)pair.StoredEnergy - taken);
        }
    }

    /// <summary>What an inelastic meeting of the two sides would take out — one of <c>FUN_180086b40</c>'s two blocks.</summary>
    /// <remarks>`t = a/(b + c);  r = a − c·t;  MAXSD(((d·a)·a + 1e-19) − (((f·(b·t))·(b·t)) + ((d·r)·r)), 0)·0.5`.</remarks>
    private static double Reducible(double speed, double firstInverse, double secondInverse, double secondMass, double firstMass)
    {
        double t = speed / (firstInverse + secondInverse);
        double firstShare = firstInverse * t;
        double rest = speed - (secondInverse * t);
        double before = (secondMass * speed * speed) + Floor;
        double after = (firstMass * firstShare * firstShare) + (secondMass * rest * rest);
        double reducible = before - after;

        // MAXSD: the second operand unless the first is strictly greater, so a NaN is zero.
        return (reducible > 0d ? reducible : 0d) * Half;
    }

    /// <summary>The pair's relative motion — the stack block <c>FUN_180086670(L, A, B)</c> fills.</summary>
    private sealed class RelativeMotion
    {
        /// <summary>The first side, <c>L+0x90</c>: the unmovable core when the second was one.</summary>
        public required IvpRigidBody First { get; init; }

        /// <summary>The second side, <c>L+0x98</c>.</summary>
        public required IvpRigidBody Second { get; init; }

        /// <summary>The second's velocity less the first's, scaled to unit length — <c>L+0x0</c>.</summary>
        public required (float X, float Y, float Z) Direction { get; set; }

        /// <summary>The spin difference turned into the first's frame — <c>L+0x10</c>.</summary>
        public required (float X, float Y, float Z) FirstAxis { get; init; }

        /// <summary>Its negation turned into the second's frame — <c>L+0x20</c>.</summary>
        public required (float X, float Y, float Z) SecondAxis { get; init; }

        /// <summary><c>L+0x30</c>.</summary>
        public required double LinearSpeed { get; init; }

        /// <summary><c>L+0x38</c>.</summary>
        public required double FirstMass { get; init; }

        /// <summary><c>L+0x40</c>.</summary>
        public required double SecondMass { get; init; }

        /// <summary><c>L+0x48</c>.</summary>
        public required double FirstInverseMass { get; init; }

        /// <summary><c>L+0x50</c>.</summary>
        public required double SecondInverseMass { get; init; }

        /// <summary><c>L+0x58</c>.</summary>
        public required double AngularSpeed { get; init; }

        /// <summary><c>L+0x60</c>.</summary>
        public required double FirstInertia { get; init; }

        /// <summary><c>L+0x68</c>.</summary>
        public required double SecondInertia { get; init; }

        /// <summary><c>L+0x70</c>.</summary>
        public required double FirstInverseInertia { get; init; }

        /// <summary><c>L+0x78</c>.</summary>
        public required double SecondInverseInertia { get; init; }

        /// <summary><c>L+0x80</c>, which <c>FUN_180086b40</c> leaves for <c>FUN_180083f70</c>.</summary>
        public double RotationalEnergy { get; set; }

        /// <summary><c>L+0x88</c>.</summary>
        public double LinearEnergy { get; set; }

        /// <summary>
        /// <code>
        /// B flagged 0x12 → P = B, Q = A;  else P = A, Q = B
        /// L+0x0 = Q.v − P.v (float);  L+0x30 = FUN_18006fc90(&amp;L+0x0)
        /// Δω = FUN_180070950(Q+0x90, Q.ω) − FUN_180070950(P+0x90, P.ω) (float);  L+0x58 = FUN_18006fc90(&amp;Δω)
        /// L+0x10 = FUN_180070620(P+0x90, Δω);  L+0x20 = FUN_180070620(Q+0x90, Δω·−1f)
        /// FUN_180086440(P, L+0x10, L+0x60, L+0x70);  FUN_180086440(Q, L+0x20, L+0x68, L+0x78)
        /// L+0x40 = Q+0x2c;  L+0x50 = Q+0x4c;  P not flagged 0x12 → L+0x38 = P+0x2c, L+0x48 = P+0x4c
        /// otherwise L+0x38 = L+0x40·10000;  L+0x60 = L+0x68·10000;  L+0x48 = L+0x50·1e-4;  L+0x70 = L+0x78·1e-4
        /// </code>
        /// </summary>
        public static RelativeMotion Between(IvpRigidBody first, IvpRigidBody second)
        {
            (IvpRigidBody p, IvpRigidBody q) = Unmovable(second) ? (second, first) : (first, second);

            (float X, float Y, float Z) direction = (q.Velocity.X - p.Velocity.X, q.Velocity.Y - p.Velocity.Y, q.Velocity.Z - p.Velocity.Z);
            double linearSpeed = ScaleToUnitLength(ref direction);

            (float X, float Y, float Z) qSpin = Turned(q);
            (float X, float Y, float Z) pSpin = Turned(p);
            (float X, float Y, float Z) spin = (qSpin.X - pSpin.X, qSpin.Y - pSpin.Y, qSpin.Z - pSpin.Z);
            double angularSpeed = ScaleToUnitLength(ref spin);

            (float X, float Y, float Z) firstAxis = p.CoreMatrix.RotateInverseNarrowed(spin);
            (float X, float Y, float Z) secondAxis = q.CoreMatrix.RotateInverseNarrowed((spin.X * -1f, spin.Y * -1f, spin.Z * -1f));

            (double pInertia, double pInverseInertia) = AlongAxis(p, firstAxis);
            (double qInertia, double qInverseInertia) = AlongAxis(q, secondAxis);

            double qMass = q.Mass;
            double qInverseMass = q.InverseMass;
            bool pUnmovable = Unmovable(p);

            return new RelativeMotion
            {
                First = p,
                Second = q,
                Direction = direction,
                FirstAxis = firstAxis,
                SecondAxis = secondAxis,
                LinearSpeed = linearSpeed,
                AngularSpeed = angularSpeed,
                SecondMass = qMass,
                SecondInverseMass = qInverseMass,
                SecondInertia = qInertia,
                SecondInverseInertia = qInverseInertia,
                FirstMass = pUnmovable ? qMass * UnmovableMass : p.Mass,
                FirstInverseMass = pUnmovable ? qInverseMass * UnmovableInverse : p.InverseMass,
                FirstInertia = pUnmovable ? qInertia * UnmovableMass : pInertia,
                FirstInverseInertia = pUnmovable ? qInverseInertia * UnmovableInverse : pInverseInertia,
            };
        }

        /// <summary>
        /// The relative motion damped by a fraction of its energy — <c>FUN_180083f70(L, f)</c>, into both cores' staged changes.
        /// </summary>
        /// <remarks>
        /// <code>
        /// jω = (L+0x58 − √|L+0x58² − (f·L+0x80 + f·L+0x80)·(L+0x78 + L+0x70)|) / (L+0x78 + L+0x70)
        /// jv = (L+0x30 − √|L+0x30² − (f·L+0x88 + f·L+0x88)·(L+0x48 + L+0x50)|) / (L+0x48 + L+0x50)
        /// P not flagged 2 nor 0x10:  P+0x120 += L+0x0·(L+0x48·jv);  P+0x110 += L+0x10·(jω·L+0x70)     -- each lane (f)((d)·k + (d))
        /// L+0x0 = −L+0x0;  Q+0x120 += L+0x0·(jv·L+0x50);  Q+0x110 += L+0x20·(jω·L+0x78)
        /// </code>
        /// </remarks>
        public void Damp(double fraction)
        {
            double inverseInertia = SecondInverseInertia + FirstInverseInertia;
            double spinSquared = (AngularSpeed * AngularSpeed)
                - (((fraction * RotationalEnergy) + (fraction * RotationalEnergy)) * inverseInertia);
            double spinImpulse = (AngularSpeed - Math.Sqrt(Math.Abs(spinSquared))) / inverseInertia;

            double inverseMass = FirstInverseMass + SecondInverseMass;
            double speedSquared = (LinearSpeed * LinearSpeed) - (((fraction * LinearEnergy) + (fraction * LinearEnergy)) * inverseMass);
            double speedImpulse = (LinearSpeed - Math.Sqrt(Math.Abs(speedSquared))) / inverseMass;

            if (!First.Immovable && !First.SkipsGravity)
            {
                First.PendingVelocity = Staged(First.PendingVelocity, Direction, FirstInverseMass * speedImpulse);
                First.PendingAngularVelocity = Staged(First.PendingAngularVelocity, FirstAxis, spinImpulse * FirstInverseInertia);
            }

            Direction = (-Direction.X, -Direction.Y, -Direction.Z);
            Second.PendingVelocity = Staged(Second.PendingVelocity, Direction, speedImpulse * SecondInverseMass);
            Second.PendingAngularVelocity = Staged(Second.PendingAngularVelocity, SecondAxis, spinImpulse * SecondInverseInertia);
        }

        /// <summary>A float vector scaled to unit length, and the length it had — <c>FUN_18006fc90</c>.</summary>
        /// <remarks>
        /// `((x² + y²) + z²)` in float, widened; under `1e-19` (NaN too) the vector is left and zero answered; else five Newton steps
        /// (`FUN_18006edd0`), each lane `(float)((double)v·r)`, and `r·d` answered.
        /// </remarks>
        private static double ScaleToUnitLength(ref (float X, float Y, float Z) vector)
        {
            double squared = IvpMath.Addss(IvpMath.Addss(vector.X * vector.X, vector.Y * vector.Y), vector.Z * vector.Z);

            if (!(squared >= Floor))
            {
                return 0d;
            }

            double scale = IvpVector.ReciprocalSquareRoot(squared, IvpVector.FiveSteps);
            vector = ((float)(vector.X * scale), (float)(vector.Y * scale), (float)(vector.Z * scale));

            return scale * squared;
        }

        private static (float X, float Y, float Z) Staged((float X, float Y, float Z) staged, (float X, float Y, float Z) along, double k) =>
            ((float)(((double)along.X * k) + staged.X),
             (float)(((double)along.Y * k) + staged.Y),
             (float)(((double)along.Z * k) + staged.Z));

        /// <summary>A core's spin turned into the world, narrowed — <c>FUN_180070950(core+0x90, core+0x130)</c>.</summary>
        private static (float X, float Y, float Z) Turned(IvpRigidBody core)
        {
            (double X, double Y, double Z) world =
                core.CoreMatrix.Rotate((core.AngularVelocity.X, core.AngularVelocity.Y, core.AngularVelocity.Z));

            return ((float)world.X, (float)world.Y, (float)world.Z);
        }

        /// <summary>
        /// A core's inertia along an axis and its reciprocal — <c>FUN_180086440</c>: <c>I = FUN_18006e120(I⊙u)</c> in float; under
        /// <c>1e-19</c> (NaN too) both are one, else the reciprocal is <c>1.0 / I</c>.
        /// </summary>
        private static (double Inertia, double Inverse) AlongAxis(IvpRigidBody core, (float X, float Y, float Z) axis)
        {
            double inertia = IvpVector.Length((core.Inertia.X * axis.X, core.Inertia.Y * axis.Y, core.Inertia.Z * axis.Z));

            return !(inertia >= Floor) ? (1d, 1d) : (inertia, 1d / inertia);
        }

        /// <summary>The flags' <c>0x12</c>: unmovable, or kept out of gravity.</summary>
        private static bool Unmovable(IvpRigidBody core) => core.Immovable || core.SkipsGravity;
    }
}
