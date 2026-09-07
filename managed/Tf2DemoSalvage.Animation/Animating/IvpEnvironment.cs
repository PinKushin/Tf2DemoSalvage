using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// An IVP simulation: a clock, a gravity vector, and the bodies it steps (B58, D142, D146).
/// </summary>
/// <remarks>
/// **The assembly of pieces transcribed separately.** Each stage is read out of `vphysics.dll` on
/// its own — <see cref="IvpGravity"/> from the controller at `env+0x0`, <see cref="IvpIntegrator"/>
/// from `FUN_180099a00` — and this is the order they run in:
///
/// <code>
///   FUN_18008a020    the PSI event fires
///     FUN_180082560  the pipeline
///       FUN_180090700   islands assembled
///         FUN_1800909d0 every awake core in the island integrated
///           FUN_180099a00
/// </code>
///
/// with the gravity controller accumulating into velocity before that, and the constraint group's
/// two relaxation sweeps between the two.
///
/// **The step is the demo's TICK interval, not the frame time.** `PhysicsLevelInit` sets it with
/// `physenv->SetSimulationTimestep( gpGlobals->interval_per_tick )` under Valve's own comment —
/// *"Always run client physics at this rate - helps keep ragdolls stable"* (`physics.cpp:177-180`).
/// A viewer drawing at three hundred frames a second must not step physics three hundred times a
/// second; getting that wrong does not fail, it produces a corpse that settles differently at every
/// frame rate.
///
/// **The constraint solve runs between gravity and integration**, as
/// <see cref="IvpConstraintGroup"/> — two relaxation sweeps per step, each walking the joint list
/// descending then ascending. It corrects the velocity gravity just changed, and the integrator
/// reads the result, which is why the order in <see cref="Simulate"/> is not rearrangeable.
///
/// **One term in it is still zero for want of a reading**: the rate gain, which would let a limit
/// clamp the PREDICTED deflection rather than the current one. At zero a joint resists a limit it
/// has already broken instead of stopping short of it — a real difference, and a smaller one than
/// inventing the number would be. `docs/findings/51` names where it arrives.
/// </remarks>
public sealed class IvpEnvironment
{
    private readonly List<IvpRigidBody> _bodies = [];

    /// <summary>Creates an environment.</summary>
    /// <param name="step">The simulation timestep — the demo's tick interval.</param>
    /// <param name="gravity">
    /// Acceleration, defaulting to <c>sv_gravity</c>'s 800 down Z as
    /// <c>Vector( 0, 0, -GetCurrentGravity() )</c> gives it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The step is not positive.</exception>
    public IvpEnvironment(float step, (float X, float Y, float Z)? gravity = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        Step = step;
        Gravity = gravity ?? (0f, 0f, -PhysicsEnvironment.DefaultGravity);
    }

    /// <summary>The fixed simulation step — <c>env+0x108</c>.</summary>
    public float Step { get; }

    /// <summary>Acceleration applied to every body that takes it.</summary>
    public (float X, float Y, float Z) Gravity { get; }

    /// <summary>The second acceleration, for bodies that ask for it.</summary>
    /// <remarks>
    /// **IVP holds two and picks per body**, at `controller+0x10` and `controller+0x20`. Nothing in
    /// TF2 sets it; it is carried because the engine has it.
    /// </remarks>
    public (float X, float Y, float Z)? AlternateGravity { get; set; }

    /// <summary>Absolute simulation time — <c>env+0x188</c>.</summary>
    public double Now { get; private set; }

    /// <summary>The bodies this environment steps.</summary>
    public IReadOnlyList<IvpRigidBody> Bodies => _bodies;

    /// <summary>The joints solved between gravity and integration.</summary>
    /// <remarks>
    /// **One group per environment, which is what the engine has.** `CreateConstraintGroup` is a
    /// slot on the environment (23, a thunk at `0x180012a40` loading `environment+0x8`), and a
    /// ragdoll's joints all go into one — so a corpse's limbs relax against each other in the same
    /// sweep rather than in separate passes.
    /// </remarks>
    public IvpConstraintGroup Constraints { get; } = new();

    /// <summary>The fastest a body may travel — <c>k_flMaxVelocity</c>, in units per second.</summary>
    /// <remarks>
    /// **Valve's own number, published**: `const float k_flMaxVelocity = 2000.0f;`
    /// (`public/vphysics/performance.h:14`), set into every environment by
    /// `physics_performanceparams_t::Defaults()` and applied through
    /// `physenv->SetPerformanceSettings( &amp;params )` (`physics.cpp:226`). The field's own comment
    /// is *"limit world space linear velocity to this (in / s)"*.
    ///
    /// **It matters for a corpse specifically.** A killing blow of 24,000 through a ten-kilo bone
    /// is 2,400 units a second, and this project had no clamp at all — measured on `z1800`, where
    /// four of eight corpses left the map entirely once the death force was applied.
    /// </remarks>
    public float MaximumVelocity { get; set; } = 2000f;

    /// <summary>And the fastest it may spin — <c>k_flMaxAngularVelocity</c>, in radians.</summary>
    /// <remarks>
    /// **`360.0f * 10.0f` DEGREES per second** (`performance.h:15`), which is 3,600°/s or about
    /// 62.83 rad/s. The engine states it in degrees because that is what the field says; this
    /// simulation carries angular velocity in radians, so the conversion happens once, here.
    /// </remarks>
    public float MaximumAngularVelocity { get; set; } = 3600f * (MathF.PI / 180f);

    /// <summary>How far ahead a collision with the world is predicted, in seconds.</summary>
    /// <remarks>
    /// **`lookAheadTimeObjectsVsWorld = 1.0f`, and its own comment is "predict collisions this far
    /// (seconds) into the future"** (`public/vphysics/performance.h:37`). A full second, where this
    /// project looked ahead by a single step — about thirty milliseconds, or sixty units at the
    /// speed clamp, which a thrown corpse crosses a wall inside of.
    ///
    /// **A TIME and not a distance**, which is the part that makes it work at any speed: the
    /// faster a body travels the further ahead it looks, automatically.
    /// </remarks>
    public float LookAheadWorld { get; set; } = 1f;

    /// <summary>And against another object — <c>lookAheadTimeObjectsVsObject = 0.5f</c>.</summary>
    /// <remarks>
    /// **Carried because the engine has it, and unused because this project collides a corpse only
    /// with the world.** Body-against-body contact is not implemented; a corpse passes through
    /// another corpse. Stated here rather than left as a silent absence.
    /// </remarks>
    public float LookAheadObject { get; set; } = 0.5f;

    /// <summary>How many collisions one body may take in a step before the engine freezes it.</summary>
    /// <remarks>
    /// **The game raises Valve's own default from 6 to 10** — `params.maxCollisionsPerObjectPerTimestep
    /// = 10` immediately after `params.Defaults()` (`physics.cpp:224`), which is the value TF2
    /// actually runs. The field's comment: *"object will be frozen after this many collisions
    /// (visual hitching vs. CPU cost)"*.
    ///
    /// **Carried and not yet enforced**, so a body here is never frozen for taking too many.
    /// </remarks>
    public int MaximumCollisionsPerBody { get; set; } = 10;

    /// <summary>And the whole step's budget — <c>maxCollisionChecksPerTimestep = 250</c>.</summary>
    /// <remarks>
    /// *"objects may penetrate after this many collision checks"* — so the engine's own answer to
    /// running out of budget is to let a body through, which is worth knowing when one does.
    /// Carried and not yet enforced.
    /// </remarks>
    public int MaximumCollisionChecks { get; set; } = 250;

    /// <summary>The impact solver's own loop bound — <c>FUN_18008e290</c>, <c>iVar15 &lt; 100</c>.</summary>
    /// <remarks>
    /// **A bound, not a count.** The loop's real exit is the contact no longer approaching, so a
    /// resting body costs one test; reaching a hundred means the engine gave up, and it checks for
    /// exactly that afterwards — `if ((0.0 &lt; dVar18) &amp;&amp; (iVar15 != 100))` skips the final
    /// calibrated impulse when the loop ran out.
    /// </remarks>
    public const int MaximumImpulsePasses = 100;

    /// <summary>The static world these bodies collide with, or null when there is none.</summary>
    /// <remarks>
    /// **Null is a legitimate state and not a missing input.** Every test of the solve that predates
    /// collision runs without a world, and a ragdoll in mid-air genuinely has nothing to touch.
    /// </remarks>
    public IvpWorldCollision? World { get; set; }

    /// <summary>This step's contacts, rebuilt each time rather than carried.</summary>
    /// <remarks>
    /// **Not persistent, and the engine's ARE** — `FUN_18008d0c0` caches its record at
    /// `mindist+0x70` and keeps it while the pair stays close. That persistence is what lets an
    /// accumulated impulse warm-start the next step, so a stack settles in fewer iterations; here
    /// each step starts from zero, which costs iterations rather than correctness. Filed as a
    /// departure rather than left to look deliberate.
    /// </remarks>
    private readonly List<IvpContact> _contacts = [];

    /// <summary>How many contacts the last step found.</summary>
    /// <remarks>
    /// **Carried out of the step that used them, never recounted** (B243). "The corpse is still
    /// falling" has two causes that look identical from outside — no contact was found, or one was
    /// found and did not hold — and only this number separates them.
    /// </remarks>
    public int Contacts => _contacts.Count;

    /// <summary>The deepest penetration the last step found.</summary>
    /// <remarks>
    /// **It separates "started inside" from "tunnelled in", which look identical afterwards.** A
    /// body seeded from a death pose whose feet are already below the floor is penetrating on its
    /// FIRST step and can only be pushed out; one that entered at speed penetrates later and only
    /// once. Both end up under the map.
    /// </remarks>
    public float DeepestContact
    {
        get
        {
            float deepest = 0f;

            foreach (IvpContact contact in _contacts)
            {
                deepest = MathF.Max(deepest, contact.Depth);
            }

            return deepest;
        }
    }

    /// <summary>Adds a body, starting its clock at the current time.</summary>
    /// <param name="body">The body.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <remarks>
    /// **Its clock starts NOW rather than at zero**, and that is not tidiness. The integrator takes
    /// its position delta from `env+0x188 − core+0x1d0`, so a body joining a running environment
    /// with a zeroed stamp would integrate its first step across the environment's whole lifetime
    /// and be flung across the map. A corpse is created mid-demo every time.
    /// </remarks>
    public void Add(IvpRigidBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        body.LastStepped = Now;

        _bodies.Add(body);
    }

    /// <summary>Runs one physics step.</summary>
    /// <remarks>
    /// **Gravity, then the constraint solve, then integration** — the order the pipeline runs them
    /// in, and it matters: the solve corrects the velocity gravity just changed, and the integrator
    /// reads the result.
    ///
    /// **The clock advances FIRST**, because the integrator derives its own position delta from the
    /// difference between the environment's time and the body's — which is how a body that missed
    /// steps catches up in one longer move rather than losing the distance.
    ///
    /// **An immovable body is skipped entirely**, exactly as `FUN_1800909d0` skips a core carrying
    /// the bit.
    /// </remarks>
    public void Simulate()
    {
        // **Damping, then the staged pushes, then gravity — the order `FUN_180074c80` calls them
        // in**, and each position in it is load-bearing:
        //
        //     FUN_180078250(core, dt);   // damping        — IvpDamping
        //     FUN_180077950(core);       // flush the push — IvpPush.Flush
        //     … v += g * dt …            // gravity        — IvpGravity
        //
        // Damping runs BEFORE the flush, so an impulse staged last step is not damped on the step
        // it lands; and gravity runs after both, so the step's own acceleration is never damped
        // either. Reordering any pair changes a corpse's launch.
        float before = Energy();

        IvpDamping.Apply(_bodies, Step);

        IvpPush.Flush(_bodies);

        IvpGravity.Apply(_bodies, Gravity, Step, AlternateGravity);

        float afterGravity = Energy();

        // **Contacts are found against the CURRENT positions and solved beside the joints**, which
        // is the order the engine runs: the mindist system re-checks its pairs, the contact records
        // exist before the solve, and the integrator reads whatever the solve left. Finding them
        // after integration instead would resolve last step's penetration one step late, and a
        // corpse would sink a little further into the floor every step before being pushed back.
        Constraints.Solve();

        float afterJoints = Energy();

        Gained = (afterGravity - before, afterJoints - afterGravity, afterJoints);

        // **The step is SUBDIVIDED at each collision, which is the engine's own shape and was the
        // last thing making a thrown corpse leave the map.** `FUN_180099380` gates a pair on
        //
        //     (float)(env[0x190] - env[0x188]) * closingSpeed + margin <= distance   →   ignore
        //
        // where `env+0x188` is the CURRENT time and `env+0x190` the end of this PSI. The engine
        // asks about the time REMAINING, which only means anything if the current time advances
        // inside the step — so IVP walks the interval event by event, advancing to each impact and
        // resolving it there rather than integrating the whole step and correcting afterwards.
        //
        // **The margin in that gate is zero.** `DAT_18012d548` reads `0.0` for these materials and
        // its base `DAT_18012d664` dumps `0.0` too, so nothing is added; the epsilon in the speed
        // bound beside it is `DAT_1800ea938`, `1.0E-10`.
        //
        // **`maxCollisionChecksPerTimestep` bounds the walk**, and the engine's own comment says
        // what running out costs: *"objects may penetrate after this many collision checks"*.
        // **The collision count is per STEP**, so it is cleared here and not inside the walk.
        for (int index = 0; index < _bodies.Count; index++)
        {
            _bodies[index].Collisions = 0;
            _bodies[index].Frozen = false;
        }

        float remaining = Step;
        int checks = 0;
        int walked = 0;

        Passes = 0;
        Split = (0f, 0f, 0f);

        // **The walk is bounded by the number of SLICES as well as by the check count, and that
        // second bound is not a belt-and-braces addition — without it this loop does not
        // terminate.** `checks` counts sweeps, and a sweep only happens for a body that is not
        // already touching something; a body resting in contact raises its contacts through
        // `Penetration` and spends no budget at all. So a step in which every body is already
        // touching can hand back an impact time that is small, positive, and never large enough to
        // finish the interval, and the loop spins on it forever with the budget untouched.
        //
        // **Measured, and by the owner rather than by a test**: *"it hung"* — six ticks of a single
        // corpse ran past four hundred seconds once the ball-and-socket started holding bodies
        // against surfaces they were resting on. The loop was already unbounded before that; what
        // changed was that something finally produced the input.
        //
        // **The bound is `MaximumCollisionChecks` because that is the engine's answer to the same
        // question** — *"objects may penetrate after this many collision checks"* — and running out
        // costs exactly what running out of checks costs: the rest of the interval is taken in one
        // move, with this slice's contacts already solved.
        while (remaining > TimeEpsilon && walked < MaximumCollisionChecks)
        {
            remaining -= Advance(remaining, ref checks);

            Slices++;
            walked++;
        }

        if (remaining > TimeEpsilon)
        {
            Move(remaining);
        }

        // **The friction system is solved ONCE per PSI**, after the interval has been walked —
        // `FUN_1800836b0` walks each system exactly once, where `FUN_18008e290` fires per event
        // inside it. Running this per slice gave it several bites at one tick and left the slip it
        // carries forward several corrections stale.
        _slice = Step;

        for (int index = 0; index < _contacts.Count; index++)
        {
            _contacts[index].Rubbed = false;
        }

        Rub();

        Gained = (Gained.Gravity, Gained.Joints, Energy() - Gained.Contacts);
    }

    /// <summary>Kinetic energy added by each stage of the last step — gravity, joints, contacts.</summary>
    /// <remarks>
    /// **Written because disabling passes one at a time stopped answering.** Turning a stage off
    /// says whether the system is stable without it, which is not the same question as which stage
    /// puts energy in — a stage can be indispensable AND be the pump, and the contact solve is
    /// exactly that: without it a corpse falls through the map, with it a corpse never stops
    /// moving. This measures the thing directly.
    ///
    /// **Carried out of the stages that ran, not recomputed afterwards** (B243).
    /// </remarks>
    public (float Gravity, float Joints, float Contacts) Gained { get; private set; }

    /// <summary>And the contact solve's own three passes, split the same way.</summary>
    public (float Oppose, float Separate, float Rub) Split { get; private set; }

    /// <summary>Total kinetic energy of every body, linear and angular.</summary>
    private float Energy()
    {
        float total = 0f;

        for (int index = 0; index < _bodies.Count; index++)
        {
            IvpRigidBody body = _bodies[index];

            if (body.InverseMass <= 0f)
            {
                continue;
            }

            float mass = 1f / body.InverseMass;

            total += 0.5f * mass * (
                (body.Velocity.X * body.Velocity.X) +
                (body.Velocity.Y * body.Velocity.Y) +
                (body.Velocity.Z * body.Velocity.Z));

            total += 0.5f * (
                (body.AngularVelocity.X * body.AngularVelocity.X / Spin(body.InverseInertia.X)) +
                (body.AngularVelocity.Y * body.AngularVelocity.Y / Spin(body.InverseInertia.Y)) +
                (body.AngularVelocity.Z * body.AngularVelocity.Z / Spin(body.InverseInertia.Z)));
        }

        return total;
    }

    private static float Spin(float inverse) => inverse > 0f ? inverse : 1f;

    /// <summary>How many sub-intervals the steps so far have been walked in.</summary>
    /// <remarks>
    /// **One slice per step means the walk is not walking**, which is a state this has been in
    /// twice: a resting point reporting an impact at nearly zero collapsed every step to a single
    /// move, and the collision limit above never fired because nothing was being sliced.
    /// </remarks>
    public long Slices { get; private set; }

    /// <summary>Finds the next impact, moves to it and resolves it; returns the time consumed.</summary>
    /// <remarks>
    /// **One event, which is what makes the remaining-time gate mean something.** A contact found
    /// at a fraction of the interval is resolved AT that fraction, so a body travelling faster than
    /// a wall is thick still meets the wall.
    /// </remarks>
    private float Advance(float remaining, ref int checks)
    {
        _contacts.Clear();

        float soonest = remaining;

        for (int index = 0; index < _bodies.Count; index++)
        {
            IvpRigidBody body = _bodies[index];

            int before = _contacts.Count;

            float when = IvpContact.Find(
                body, World, _contacts, remaining, LookAheadWorld, ref checks);

            // **One collision for a body that found any contact this slice**, which is what the
            // engine counts: an object's collisions in a timestep, not a contact per point.
            if (_contacts.Count > before)
            {
                body.Collisions++;
            }

            // **Frozen at the limit, which is the engine's own word and its own number.** A body
            // thrown hard enough to collide ten times inside one step is one that would otherwise
            // grind through the surface it keeps hitting — measured on `z1800`, where corpses
            // launched by a rocket ended below a floor that has a hull and a normal of `0 0 1`.
            if (body.Collisions >= MaximumCollisionsPerBody)
            {
                body.Frozen = true;

                body.Velocity = (0f, 0f, 0f);
                body.PreviousVelocity = (0f, 0f, 0f);
                body.AngularVelocity = (0f, 0f, 0f);
            }

            if (when < soonest)
            {
                soonest = when;
            }
        }

        // **Only the ARRIVALS are solved per slice, because only they are events.** IVP's PSI
        // walks the interval firing mindist events, and an impact is one; the friction system is
        // solved ONCE per PSI by `FUN_1800836b0` and not once per event. Running the resting solve
        // every slice gave it several bites at the same tick, and the state it carries forward went
        // stale between them: the stored slip a warm start reads back was several corrections
        // behind the live one, which measured as friction acting as a motor.
        _slice = remaining;

        Arrive();

        // **Two ways a slice must become the WHOLE remainder, and both were escapes before.**
        //
        // An impact at essentially zero means a body is already touching what it is about to hit.
        // Advancing by an epsilon there makes no progress and spends the check budget, and the
        // interval left over was then taken in one unresisted move — a corpse thrown at a wall
        // went through it. The contact for that surface has just been solved, so the rest of the
        // interval is taken with the body already resisted.
        //
        // **And when the budget runs out the move still happens WITH its contacts solved.** The
        // engine's own answer to running out is that *"objects may penetrate after this many
        // collision checks"* — penetrate, which is a body pressed into a surface, not a body
        // teleported past one. Stopping the search is not the same as stopping the collision.
        bool exhausted = checks >= MaximumCollisionChecks;

        float slice = soonest <= TimeEpsilon || exhausted ? remaining : soonest;

        Move(slice);

        return slice;
    }

    /// <summary>How many impulse passes the last slice's contacts took between them.</summary>
    /// <remarks>
    /// **Carried out of the loop that ran it, not recomputed** (B243). It is the number that says
    /// whether the solve is converging or thrashing, and reconstructing it from contact counts
    /// afterwards would be a second route free to be wrong.
    /// </remarks>
    public int Passes { get; private set; }

    /// <summary>Solves this slice's contacts the way the engine's impact solver does.</summary>
    /// <remarks>
    /// **The hundred-pass bound belongs to ONE contact, and putting it here instead was a
    /// mis-transcription that cost an afternoon.** `FUN_18008e290` solves a single mindist — one
    /// pair of objects — and iterates that pair's approach to zero. This method used to wrap the
    /// same bound around a sweep over every contact a ragdoll has, which re-solves settled contacts
    /// a hundred times per slice and lets the joint solve re-supply the approach in between.
    /// Measured that way, six ticks of one corpse ran past four hundred seconds.
    ///
    /// **The cost was the evidence, and the owner named it**: *"if this is partiy it shouldnt be
    /// doing this"*. The engine simulates a server full of ragdolls at sixty-six ticks a second, so
    /// a transcription that cannot keep up is not slow, it is wrong — and in this project doing
    /// what the engine does has been faster every time.
    ///
    /// **And before that it borrowed the joint group's two iterations, which was also a guess.**
    /// `FUN_18008e290` — the consumer of the contact record, reached through `FUN_18008ed60` — runs
    /// its own loop, and the number in it is not two:
    ///
    /// <code>
    /// for (; (0.0 &lt; dVar24 &amp;&amp; (iVar15 &lt; 100)); iVar15 = iVar15 + 1) {
    ///     FUN_18008f1c0(param_1, dVar12 * dVar19 * (dVar18 + dVar18) * (double)fVar17);
    ///     FUN_18008fc00(param_1);                       // recompute the relative velocity
    ///     dVar24 = -(dot of it with the direction);
    ///     FUN_180090240(param_1);                       // re-choose the direction
    /// }
    /// </code>
    ///
    /// **The loop ends on a CONDITION and the hundred is only a bound.** A contact that is already
    /// resting exits on the first test, so the cost of raising two to a hundred is paid by bodies
    /// that are genuinely still moving into something — which is exactly the case two iterations
    /// could not settle, and the one where three corpses were measured sinking through a floor they
    /// were touching.
    ///
    /// **The separation term is outside it**, because the engine's post-loop addition is applied
    /// once — see `IvpContact.Separate`, which is this project's and not Valve's.
    /// </remarks>
    /// <summary>Records which sub-interval a depth is spread over — see the remarks above.</summary>
    /// <param name="slice">The sub-interval this walk is about to take.</param>
    public void Resolve(float slice)
    {
        // **The whole solve runs once per contact MANIFOLD, not once per touching vertex**, which
        // is the divergence four separate measurements pointed at before it was fixed.
        //
        // **A face resting on a face is ONE contact to IVP.** Its narrow phase returns a
        // closest-feature pair, so a body lying on the ground is a single mindist carrying a single
        // arrival impulse, a single separation and a single friction solve. This project tests hull
        // VERTICES, so the same body arrived as sixteen to thirty-six contacts and every pass ran
        // that many times — sixteen arrivals, sixteen separations at up to
        // `IvpContact.MaximumRecovery` each, sixteen friction solves each retuning the spin the
        // next one measures.
        //
        // **Traced on a real scout ragdoll dropped on `koth_harvest_final`** — the `corpse-drop`
        // probe, which runs this path with no viewer in the way — it produced a permanent limit
        // cycle: 200 to 350 units a second for ten seconds, the root bouncing between z 2 and z 12,
        // never decaying and never settling. Not a blow-up, which would have been obvious; a
        // corpse vibrating on the floor for ever.
        //
        // **The three earlier symptoms were all this.** Friction that made a slide FASTER, a
        // hundred-pass loop that hung the viewer, and one-contact-per-body making penetration
        // worse: the quantities were the engine's and the set they ran over was ours.
        _slice = slice;
    }

    private float _slice;

    /// <summary>Solves friction once per contact MANIFOLD rather than once per hull point.</summary>
    /// <remarks>
    /// **A face resting on a face is ONE contact to IVP**, and that is the whole of this. The
    /// engine's narrow phase returns a closest-feature pair — vertex-face, edge-edge and the rest —
    /// so a box on a floor is a single mindist carrying a single friction contact. This project
    /// tests hull VERTICES, so the same box arrives as eight, and solving eight slips in sequence
    /// is a different operator: each cancellation retunes the body's spin and the next vertex
    /// cancels a slip the previous one just made.
    ///
    /// **Measured, and it is why this exists.** With friction applied per vertex and the warm start
    /// switched off — so each contact did nothing but drive its own slip to zero — a sliding body
    /// SPED UP, from 11.98 units a second to 21.3. Friction is dissipative and no arrangement of
    /// the arithmetic makes it add energy, so the fault could only be the set it ran over.
    ///
    /// **Grouped by body and by normal**, which is the observable a shared feature leaves behind:
    /// every vertex resting on one face reports that face's normal. The group is solved at its
    /// CENTROID against its SUMMED normal impulse, which is the arm and the force a single
    /// face-face contact would have had.
    ///
    /// **This is not the feature dispatch itself.** A real narrow phase would produce the manifold
    /// rather than infer it, and would distinguish an edge resting on a face from a vertex on one.
    /// What this does is give the friction solve the cardinality the engine's has, which is the
    /// part the measurement showed was wrong.
    /// </remarks>
    /// <summary>Kills the approach of every contact that is arriving — the per-EVENT half.</summary>
    /// <remarks>
    /// **An impact is an event and the friction system is not.** IVP fires a mindist event when a
    /// pair arrives and solves that pair there (`FUN_18008e290`), and separately walks every
    /// friction system exactly once per PSI (`FUN_1800836b0`). This is the first of those, so it
    /// runs inside the interval walk where arrivals happen; <see cref="Rub"/> is the second and
    /// runs once.
    /// </remarks>
    private void Arrive() => Manifolds(arriving: true);

    private void Rub() => Manifolds(arriving: false);

    private void Manifolds(bool arriving)
    {
        for (int index = 0; index < _contacts.Count; index++)
        {
            IvpContact contact = _contacts[index];

            if (contact.Rubbed)
            {
                continue;
            }

            (float X, float Y, float Z) centre = contact.Arm;
            float deepest = contact.Depth;
            int count = 1;

            for (int other = index + 1; other < _contacts.Count; other++)
            {
                IvpContact beside = _contacts[other];

                if (beside.Rubbed || !ReferenceEquals(beside.Body, contact.Body) ||
                    Facing(beside.Normal, contact.Normal) < Shared)
                {
                    continue;
                }

                beside.Rubbed = true;
                deepest = MathF.Max(deepest, beside.Depth);
                centre = (centre.X + beside.Arm.X, centre.Y + beside.Arm.Y, centre.Z + beside.Arm.Z);
                count++;
            }

            contact.Rubbed = true;

            // **Held steady between steps, because the set this averages reshuffles.** A vertex
            // inside last step can be outside this one, so a freshly measured centroid jumps and
            // the retained impulse would be applied somewhere new every tick. See
            // `IvpContact.Steady` — this is the body half of the closest-feature pair.
            (float X, float Y, float Z) arm = contact.Steady(
                (centre.X / count, centre.Y / count, centre.Z / count));

            // **The arrival is an EVENT and the resting solve is not**, so the two halves run at
            // different rates: `FUN_18008e290` fires per mindist event inside the PSI, and
            // `FUN_1800836b0` walks every friction system exactly once per PSI. Running both every
            // slice gave the resting half several bites at one tick and left the state it carries
            // forward several corrections behind — measured as friction acting as a motor.
            if (arriving)
            {
                contact.Begin();

                float before = Energy();

                Passes += contact.Oppose(arm);

                Split = (Split.Oppose + (Energy() - before), Split.Separate, Split.Rub);

                continue;
            }

            float opposed = Energy();

            // **This is the ONLY thing holding a resting corpse up, and that is the divergence.**
            // Switching it off drops the same ragdoll straight through the map to −1267 at five to
            // eight hundred units a second, with contacts found the whole way: `Oppose` fires only
            // for a contact that is APPROACHING — the engine's own `fVar17 <= _DAT_1800ee398` gate
            // — so nothing else in this solve supports a body that has already landed.
            //
            // **The engine holds it with a persistent contact instead.** A friction-system contact
            // carries its normal term between PSIs (`contact+0x78`, half of the Coulomb product
            // `FUN_1800857c0` clamps against), so a resting body is held continuously. Ours is
            // ejected whenever it is found inside something, which is a different mechanism with a
            // characteristic signature: sink, shove, fly, fall, sink — the limit cycle traced at
            // 77 to 262 units a second that never decays.
            //
            // **So the remaining energy is not a term to tune, it is a missing state.** The warm
            // start built for the tangential pair is the same structure the normal needs.
            contact.Separate(_slice, arm, deepest);

            float separated = Energy();

            contact.Rub(IvpConstraintGroup.Relaxation, arm, contact.Accumulated);

            Split = (
                Split.Oppose,
                Split.Separate + (separated - opposed),
                Split.Rub + (Energy() - separated));
        }
    }

    private static float Facing(
        (float X, float Y, float Z) left, (float X, float Y, float Z) right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    /// <summary>How closely two contacts must agree on a normal to be one feature.</summary>
    /// <remarks>
    /// **About eight degrees**, which separates the faces of a convex hull without splitting the
    /// vertices of one face. A displacement's triangles meet at shallower angles than that and are
    /// deliberately merged: they are one surface, and the engine's virtual terrain presents them to
    /// the solver as one object.
    /// </remarks>
    private const float Shared = 0.99f;

    /// <summary>Integrates every body over one slice of the step.</summary>
    private void Move(float slice)
    {
        Now += slice;

        for (int index = 0; index < _bodies.Count; index++)
        {
            IvpRigidBody body = _bodies[index];

            if (body.Immovable)
            {
                continue;
            }

            IvpIntegrator.Step(body, Now - body.LastStepped, slice);

            // **The environment's own speed limits, which are Valve's published numbers.** See
            // `MaximumVelocity`. **Where in the step the engine clamps is INFERRED** — the
            // parameters and their meaning are published, the site is not — so it is done after
            // integration, which is the last moment a velocity can be wrong before it is read.
            Limit(body);

            body.LastStepped = Now;
        }
    }

    /// <summary>Below this a slice is not worth taking, and the walk would not terminate.</summary>
    private const float TimeEpsilon = 1e-6f;

    /// <summary>Holds one body inside the environment's speed limits.</summary>
    /// <remarks>
    /// **Scaled, not clipped per axis.** A per-component clamp would turn a fast diagonal into a
    /// slower one pointing somewhere else, and the field's comment says *"limit world space linear
    /// velocity"* — a magnitude.
    /// </remarks>
    private void Limit(IvpRigidBody body)
    {
        float speed = MathF.Sqrt(
            (body.Velocity.X * body.Velocity.X) +
            (body.Velocity.Y * body.Velocity.Y) +
            (body.Velocity.Z * body.Velocity.Z));

        if (speed > MaximumVelocity && speed > 0f)
        {
            float scale = MaximumVelocity / speed;

            body.Velocity = (
                body.Velocity.X * scale, body.Velocity.Y * scale, body.Velocity.Z * scale);
        }

        float spin = MathF.Sqrt(
            (body.AngularVelocity.X * body.AngularVelocity.X) +
            (body.AngularVelocity.Y * body.AngularVelocity.Y) +
            (body.AngularVelocity.Z * body.AngularVelocity.Z));

        if (spin > MaximumAngularVelocity && spin > 0f)
        {
            float scale = MaximumAngularVelocity / spin;

            body.AngularVelocity = (
                body.AngularVelocity.X * scale,
                body.AngularVelocity.Y * scale,
                body.AngularVelocity.Z * scale);
        }
    }
}
