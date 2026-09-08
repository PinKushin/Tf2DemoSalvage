using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One rigid body's state, as <c>IVP_Core</c> holds it (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Field for field from the decompiled binary**, with each offset recorded so a later reader can
/// check it rather than trust this list:
///
/// | offset | field |
/// |---|---|
/// | `+0x20/0x24/0x28` | inertia about each axis |
/// | `+0x40/0x44/0x48` | its reciprocals (INFERRED — the division was not found) |
/// | `+0x4c` | inverse mass |
/// | `+0x130/0x134/0x138` | angular velocity |
/// | `+0x140/0x144/0x148` | linear velocity |
/// | `+0x150/0x158/0x160` | position, as DOUBLES |
/// | `+0x170/0x174/0x178` | the previous step's linear velocity |
/// | `+0x180..0x198` | the orientation everything outside reads |
/// | `+0x1a0..0x1b8` | the orientation the integrator works in |
/// | `+0x1d0` | the absolute time this body was last stepped |
///
/// **Position is double and velocity is float, and that is not an oversight to tidy up.** A corpse
/// accumulates position over thousands of steps and its velocity is rewritten every one, so the
/// engine spends the precision where it accumulates. Flattening both to float drifts; both to
/// double diverges from the engine's own rounding.
///
/// **Two of these fields exist so that what is READ lags what is COMPUTED**, by exactly one step —
/// see <see cref="IvpIntegrator.Step"/>.
/// </remarks>
public sealed class IvpRigidBody
{
    /// <summary>Where the body is — <c>core+0x150/0x158/0x160</c>, in doubles.</summary>
    public (double X, double Y, double Z) Position { get; set; }

    /// <summary>How fast it is moving — <c>core+0x140/0x144/0x148</c>.</summary>
    public (float X, float Y, float Z) Velocity { get; set; }

    /// <summary>What <see cref="Velocity"/> was last step — <c>core+0x170/0x174/0x178</c>.</summary>
    /// <remarks>
    /// **This is what moves the body, not <see cref="Velocity"/>**, which is why it is a field
    /// rather than a local. See <see cref="IvpIntegrator.Step"/>.
    /// </remarks>
    public (float X, float Y, float Z) PreviousVelocity { get; set; }

    /// <summary>How fast it is turning — <c>core+0x130/0x134/0x138</c>, radians per second.</summary>
    public (float X, float Y, float Z) AngularVelocity { get; set; }

    /// <summary>The orientation everything outside the solver reads — <c>core+0x180</c>.</summary>
    /// <remarks>**One step behind <see cref="WorkingOrientation"/>, deliberately.**</remarks>
    public (float X, float Y, float Z, float W) Orientation { get; set; } = (0f, 0f, 0f, 1f);

    /// <summary>The orientation the integrator advances — <c>core+0x1a0</c>.</summary>
    public (float X, float Y, float Z, float W) WorkingOrientation { get; set; } = (0f, 0f, 0f, 1f);

    /// <summary>Rotational inertia about each axis — <c>core+0x20/0x24/0x28</c>.</summary>
    public (float X, float Y, float Z) Inertia { get; set; } = (1f, 1f, 1f);

    /// <summary>Its reciprocal — <c>core+0x40/0x44/0x48</c>.</summary>
    /// <remarks>
    /// **That these hold reciprocals is INFERRED**, not read: it is what makes the free-rotation
    /// expression equal Euler's torque-free equations, and it matches the inverse mass living at
    /// `+0x4c` in the same block — but the division that produces them was not found in the binary.
    /// </remarks>
    public (float X, float Y, float Z) InverseInertia { get; set; } = (1f, 1f, 1f);

    /// <summary>The reciprocal of this body's mass — <c>core+0x4c</c>.</summary>
    /// <remarks>
    /// **Named by the contact builder, which is the only traced site that reads it.**
    /// `FUN_18008d0c0` computes a contact's effective inverse mass as
    /// `rx*rx*core[+0x44] + ry*ry*core[+0x40] + rz*rz*core[+0x48] + core[+0x4c]` — three rotational
    /// terms and one that carries no lever arm, which is what a linear inverse mass is.
    ///
    /// **Zero for an immovable body**, because the same builder zeroes the whole term rather than
    /// dividing: static map geometry has infinite mass by having none to contribute.
    ///
    /// **The default is 1 rather than 0** so a body built without one is light rather than
    /// immovable — a zero default would make every un-massed body silently unpushable, which is the
    /// failure that looks like working collision.
    /// </remarks>
    public float InverseMass { get; set; } = 1f;

    /// <summary>Velocity waiting to be added at the next step — <c>core+0x120/0x124/0x128</c>.</summary>
    /// <remarks>
    /// **A push does not reach a body's velocity where it is applied; it is STAGED.**
    /// `FUN_180077950` drains this pair into the real velocities and zeroes it, once per body per
    /// step, from inside the same gate as gravity:
    ///
    /// <code>
    /// *(float *)(core + 0x130) = *(float *)(core + 0x110) + *(float *)(core + 0x130);
    /// *(float *)(core + 0x140) = *(float *)(core + 0x120) + *(float *)(core + 0x140);
    /// ...
    /// *(undefined8 *)(core + 0x124) = 0;   *(undefined4 *)(core + 0x120) = 0;
    /// *(undefined8 *)(core + 0x114) = 0;   *(undefined4 *)(core + 0x110) = 0;
    /// </code>
    ///
    /// **So an impulse applied between steps lands on the NEXT one**, which is the same one-step
    /// lag the integrator has by moving on the previous velocity. A ragdoll's creation force is
    /// applied through exactly this — `ApplyForceCenter` and `AddVelocity` stage, they do not set.
    /// </remarks>
    public (float X, float Y, float Z) PendingVelocity { get; set; }

    /// <summary>Angular velocity waiting the same way — <c>core+0x110/0x114/0x118</c>.</summary>
    public (float X, float Y, float Z) PendingAngularVelocity { get; set; }

    /// <summary>How fast this body loses speed — <c>core+0x50</c>, the <c>.phy</c>'s <c>damping</c>.</summary>
    /// <remarks>
    /// **Zero on every element of every TF2 ragdoll**, measured — so this is the term that does
    /// nothing for a corpse and everything for a prop. Carried because the engine has it.
    /// </remarks>
    public float Damping { get; set; }

    /// <summary>How fast it loses spin — <c>core+0x30/0x34/0x38</c>, the <c>rotdamping</c>.</summary>
    /// <remarks>
    /// **IVP holds three of these and Valve supplies one.** `core+0x30` is a per-axis vector and
    /// `objectparams_t::rotdamping` is a scalar, so the engine writes the same number into all
    /// three lanes; a scalar here is that, not a simplification of it. It matters for a corpse:
    /// rotational damping runs 4 to 16 per joint across the game's ragdolls.
    /// </remarks>
    public float RotationDamping { get; set; }

    /// <summary>How many collisions this body has taken in the current step.</summary>
    /// <remarks>
    /// **`maxCollisionsPerObjectPerTimestep`, whose own comment says what happens at the limit** —
    /// *"object will be frozen after this many collisions (visual hitching vs. CPU cost)"*
    /// (`performance.h:21`). TF2 sets it to 10, raising Valve's default of 6 immediately after
    /// `Defaults()` (`physics.cpp:224`).
    ///
    /// **It is the engine's safety net for exactly the body this project could not hold**: one
    /// thrown hard enough to collide again and again inside a single step, which without a limit
    /// grinds its way through the surface it is hitting.
    /// </remarks>
    public int Collisions { get; set; }


    /// <summary>Whether this body has been frozen for the rest of the step.</summary>
    /// <remarks>
    /// **Frozen, not asleep.** It lasts until the step ends and the count is cleared; the engine's
    /// word for it is the same one its comment uses, and the cost it trades against is *"visual
    /// hitching"*.
    /// </remarks>
    public bool Frozen { get; set; }

    /// <summary>This body's coefficient of friction — its surface's, from the game's own table.</summary>
    /// <remarks>
    /// **`surfacephysicsparams_t::friction`**, looked up by the `surfaceprop` its `.phy` solid
    /// names (`ragdoll_shared.cpp:194`). One rather than zero by default, which is
    /// `g_PhysDefaultObjectParams` — a zero default would make every body frictionless the moment
    /// somebody forgot to set one, and that failure looks like working physics until a corpse
    /// slides off the map.
    /// </remarks>
    public float Friction { get; set; } = 1f;

    /// <summary>The hull this body collides with, in its own space and in SOURCE units.</summary>
    /// <remarks>
    /// **Empty for a body that is not collided**, which is every body this project had until
    /// contacts existed. The points are the ledge geometry read out of the model's `.phy` — see
    /// `PhysicsHull` — converted at the seam, because this simulation runs in Source units where
    /// IVP's own runs in metres.
    /// </remarks>
    public IReadOnlyList<(float X, float Y, float Z)> Hull
    {
        get => _hull;

        set => _hull = value ?? [];
    }

    /// <summary>Each contact FEATURE's tangential slip, carried between steps, keyed by normal.</summary>
    /// <remarks>
    /// **A friction contact in IVP SURVIVES between PSIs, and this is what that survival needs.**
    /// `FUN_1800857c0` builds its right-hand side as `weight × stored − current velocity`, reading
    /// the tangential pair at `contact+0x68`/`+0x6c` that its own previous solve wrote back. The
    /// contact object is persistent — cached on the mindist, split and merged by `FUN_180086e80` as
    /// an object's contact list changes — so friction converges across steps instead of being
    /// rediscovered from nothing in each one.
    ///
    /// **Keyed by the manifold's NORMAL, and the key is the whole of it.** This was keyed by hull
    /// point first, which looks equivalent and is not: a manifold's representative is whichever of
    /// its vertices comes first in the contact list, and that changes as a body rocks — so each
    /// step's warm start read a slot the previous step had not written. Measured, the difference is
    /// total. Keyed by point, a body on a one-in-ten slope ACCELERATED, 8.2 units a second at six
    /// seconds and 18.5 at twelve. With the warm start switched off entirely it came to rest.
    /// A normal is stable for exactly as long as the feature is, which is what the mindist's own
    /// identity means.
    ///
    /// **Warm starting is not an optimisation here; it is where a resting body's holding force
    /// comes from** — a stateless pass can only react to the slide it can already see.
    /// </remarks>
    public IList<(
        (float X, float Y, float Z) Normal,
        float Holding,
        float First,
        float Second,
        (float X, float Y, float Z) Local)> Sliding { get; } = [];


    private IReadOnlyList<(float X, float Y, float Z)> _hull = [];

    /// <summary>When this body was last stepped — <c>core+0x1d0</c>, absolute.</summary>
    public double LastStepped { get; set; }

    /// <summary>Whether gravity passes this body by — bit <c>0x10</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// **The whole gravity step is inside `if ((*pbVar4 &amp; 0x10) == 0)`**, so a body carrying this
    /// bit skips the two helper calls as well as the acceleration.
    /// </remarks>
    public bool SkipsGravity { get; set; }

    /// <summary>Whether this body takes the environment's SECOND acceleration — bit <c>0x20</c>.</summary>
    /// <remarks>
    /// **IVP holds two gravity vectors and picks between them per body**, at
    /// `controller+0x10/0x14/0x18` and `controller+0x20/0x24/0x28`. Nothing in TF2 sets it, and it
    /// is carried anyway because the engine has it — a transcription with one global vector is
    /// right for this game and wrong for the engine.
    /// </remarks>
    public bool UsesAlternateGravity { get; set; }

    /// <summary>Whether this body is immovable — bits <c>0x2</c> and <c>0x10</c> of <c>core+0x0</c>.</summary>
    /// <remarks>
    /// **Read from two independent sites that never shared a session**, which is what makes it
    /// trustworthy: the contact builder zeroes a body's mass and inertia contribution on
    /// `core+0x0 &amp; 2`, and the island driver skips integrating a core on the same bit. The
    /// constraint solver's cache builder tests `&amp; 0x12` — two bits — so there is a second bit,
    /// `0x10`, that stops a body being pushed by a constraint while still letting it integrate.
    ///
    /// **This is how STATIC MAP GEOMETRY is represented**: an ordinary body with the bit set, not a
    /// separate type. `CreatePolyObjectStatic` takes the identical `CPhysCollide *` as the moving
    /// variant, so there is no separate world path at the API boundary either.
    /// </remarks>
    public bool Immovable { get; set; }
}

/// <summary>
/// IVP's per-core integration step — <c>FUN_180099a00</c> (B58, D142, D146).
/// </summary>
/// <remarks>
/// **Transcribed from the decompiled body, not designed.** `src/vphysics` ships no source, so this
/// is the one part of a corpse that had to be read out of `vphysics.dll` rather than out of the SDK
/// — and the reason to transcribe rather than write a plausible Euler integrator is that a
/// plausible one produces a corpse that settles somewhere else, silently.
///
/// **The chain above it:** the PSI event `FUN_18008a020` runs the pipeline `FUN_180082560`, which
/// assembles islands in `FUN_180090700`, and `FUN_1800909d0` calls this once per awake core in each.
///
/// **Everything a viewer reads is one step old, and it is old in the same way twice.** Position
/// integrates against the PREVIOUS step's velocity, and the visible orientation is the PREVIOUS
/// step's working one. Get either backwards and the corpse draws a step ahead of TF2.
/// </remarks>
public static class IvpIntegrator
{
    /// <summary>Advances one body by one step.</summary>
    /// <param name="body">The body.</param>
    /// <param name="positionDelta">
    /// Seconds since this body was last stepped — <c>env+0x188 − core+0x1d0</c>, which is NOT the
    /// same number as <paramref name="orientationDelta"/>.
    /// </param>
    /// <param name="orientationDelta">
    /// The island's nominal step — <c>env+0x190 − env+0x188</c>, shared by every core in it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **The order is the finding.** Verbatim, and every line of it matters:
    ///
    /// <code>
    /// dVar9 = env[0x188] - core[0x1d0];  core[0x1d0] = env[0x188];        // per-core dt
    /// core[0x150] += (double)core[0x170] * dVar9;                          // move by LAST velocity
    /// core[0x160] += (double)core[0x178] * dVar9;
    /// core[0x158] += (double)core[0x174] * dVar9;
    /// core[0x170] = core[0x140];  core[0x174] = core[0x144];  core[0x178] = core[0x148];
    /// core[0x180..0x198] = core[0x1a0..0x1b8];                             // commit, THEN integrate
    /// FUN_180070d60(core+0x1a0, core+0x1a0, delta);
    /// FUN_180070c60(core+0x1a0);
    /// </code>
    ///
    /// - **A body does not move on the step its velocity was first set.** A ragdoll handed a
    ///   velocity from its death animation stands still for one step and then goes.
    /// - **The visible orientation is committed BEFORE the working one advances.** An earlier note
    ///   in `docs/findings/51` had this the other way round; it would draw a corpse a step ahead.
    /// - **Two different clocks.** A body that has been asleep catches its POSITION up in one long
    ///   step while its ORIENTATION advances by one nominal step, because one dt is per core and the
    ///   other is per island.
    /// - **The per-core dt is narrowed to `float` before use** — `(double)(float)(dVar9 - dVar2)` —
    ///   so the arithmetic runs at single precision even though both operands are doubles.
    /// </remarks>
    public static void Step(IvpRigidBody body, double positionDelta, float orientationDelta)
    {
        ArgumentNullException.ThrowIfNull(body);

        // `(double)(float)(env[0x188] - core[0x1d0])` — the difference is taken in double and then
        // narrowed, so a long catch-up carries single-precision error exactly as the engine's does.
        double delta = (float)positionDelta;

        body.Position = (
            body.Position.X + (body.PreviousVelocity.X * delta),
            body.Position.Y + (body.PreviousVelocity.Y * delta),
            body.Position.Z + (body.PreviousVelocity.Z * delta));

        body.PreviousVelocity = body.Velocity;

        // **Commit first.** What the rest of the engine reads is last step's working orientation.
        body.Orientation = body.WorkingOrientation;

        (float X, float Y, float Z, float W) turn = Rotate(body, orientationDelta);

        body.WorkingOrientation =
            IvpQuaternion.Normalise(IvpQuaternion.Product(body.WorkingOrientation, turn));
    }

    /// <summary>Builds a step's rotation and advances the angular velocity — <c>FUN_180099fc0</c>.</summary>
    /// <param name="body">The body, whose angular velocity this updates.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The rotation to apply to the orientation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Sub-steps when the turn is large**, then applies Euler's torque-free equations between each
    /// one. The sub-step count and the free rotation are separate enough to test on their own — see
    /// <see cref="SubSteps"/> and <see cref="FreeRotation"/>.
    /// </remarks>
    public static (float X, float Y, float Z, float W) Rotate(IvpRigidBody body, float delta)
    {
        ArgumentNullException.ThrowIfNull(body);

        int steps = SubSteps(body.AngularVelocity, delta);

        float step = (float)((double)delta / steps);

        (float X, float Y, float Z, float W) turn = IvpQuaternion.Delta(body.AngularVelocity, step);

        body.AngularVelocity =
            FreeRotation(body.AngularVelocity, body.Inertia, body.InverseInertia, step);

        for (int index = 1; index < steps; index++)
        {
            turn = IvpQuaternion.Product(
                turn, IvpQuaternion.Delta(body.AngularVelocity, step));

            body.AngularVelocity =
                FreeRotation(body.AngularVelocity, body.Inertia, body.InverseInertia, step);
        }

        return turn;
    }

    /// <summary>How many sub-steps a step's rotation is broken into.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>At least one.</returns>
    /// <remarks>
    /// **Both constants were dumped rather than inferred from the shape of the arithmetic:**
    ///
    /// <code>
    /// dVar10 = (double)(wy*wy + wx*wx + wz*wz) * dt * dt;
    /// if (_DAT_1800fdf90 &lt; dVar10) {                       // DAT_1800fdf90 = 0.027777777777777776
    ///     auVar13 = sqrtpd(dVar10 * _DAT_1800fdfa0, …);     // DAT_1800fdfa0 = 144.0
    ///     iVar6 = (int)auVar13._0_8_ + 1;
    ///     dVar11 = dVar11 / (double)iVar6;
    /// }
    /// </code>
    ///
    /// `0.0277…` is 1/36 and 144 is 12², so the test is `|ω|·dt &gt; 1/6` radian — about 9.55 degrees
    /// of turn in one step — and the count is `floor( 12·|ω|·dt ) + 1`.
    ///
    /// **The comparison is strictly less-than**, so a turn of exactly 1/6 radian does not sub-step.
    ///
    /// **A transcription that stepped rotation once per PSI is right for a settling corpse and wrong
    /// for a limb that is whipping** — which is the frame anyone watching a demo is looking at.
    /// </remarks>
    public static int SubSteps((float X, float Y, float Z) angularVelocity, float delta)
    {
        double square =
            ((double)angularVelocity.X * angularVelocity.X) +
            ((double)angularVelocity.Y * angularVelocity.Y) +
            ((double)angularVelocity.Z * angularVelocity.Z);

        double turn = square * delta * delta;

        return SubStepThreshold < turn ? (int)Math.Sqrt(turn * SubStepScale) + 1 : 1;
    }

    /// <summary>Euler's torque-free equations for one step.</summary>
    /// <param name="angularVelocity">Radians per second about each axis.</param>
    /// <param name="inertia">Rotational inertia about each axis.</param>
    /// <param name="inverseInertia">Its reciprocal.</param>
    /// <param name="delta">The step, in seconds.</param>
    /// <returns>The angular velocity after the step.</returns>
    /// <remarks>
    /// **All three axes read the OLD angular velocity**, which the decompiled body arranges by
    /// precomputing the cross products it needs before writing any of them back:
    ///
    /// <code>
    /// fVar17 = local_110 * local_118;    // wz * wx, the OLD wx
    /// fVar18 = local_114 * local_118;    // wy * wx, the OLD wx
    /// local_118 = (local_110 * local_114 * fVar9) * dVar11 + local_118;
    /// local_114 = (fVar17 * fVar16) * dVar11 + local_114;
    /// local_110 = (fVar18 * fVar15) * dVar11 + local_110;
    /// </code>
    ///
    /// **It is a simultaneous update.** Writing the three in sequence — which is what anyone would
    /// do — feeds the new `ωx` into `ωy` and both into `ωz`, and the error grows with the timestep.
    ///
    /// **The coefficients pair each difference with the reciprocal of the axis being written:**
    /// `(Iy − Iz)·(1/Ix)`, `(Iz − Ix)·(1/Iy)`, `(Ix − Iy)·(1/Iz)`.
    /// </remarks>
    public static (float X, float Y, float Z) FreeRotation(
        (float X, float Y, float Z) angularVelocity,
        (float X, float Y, float Z) inertia,
        (float X, float Y, float Z) inverseInertia,
        float delta)
    {
        float aboutX = (inertia.Y - inertia.Z) * inverseInertia.X;
        float aboutY = (inertia.Z - inertia.X) * inverseInertia.Y;
        float aboutZ = (inertia.X - inertia.Y) * inverseInertia.Z;

        // Every product is taken from the values passed in, never from a partly updated triple.
        float yz = angularVelocity.Z * angularVelocity.Y;
        float zx = angularVelocity.Z * angularVelocity.X;
        float yx = angularVelocity.Y * angularVelocity.X;

        return (
            (float)(((double)(yz * aboutX) * delta) + angularVelocity.X),
            (float)(((double)(zx * aboutY) * delta) + angularVelocity.Y),
            (float)(((double)(yx * aboutZ) * delta) + angularVelocity.Z));
    }

    /// <summary><c>DAT_1800fdf90</c>, dumped — 1/36, so the test is on 1/6 radian.</summary>
    private const double SubStepThreshold = 0.027777777777777776d;

    /// <summary><c>DAT_1800fdfa0</c>, dumped — 144, which is 12 squared.</summary>
    private const double SubStepScale = 144d;
}
