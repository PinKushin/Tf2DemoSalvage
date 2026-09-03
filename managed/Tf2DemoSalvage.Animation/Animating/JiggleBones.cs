using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// One jiggle bone's simulation state — Valve's <c>JiggleData</c>.
/// </summary>
/// <remarks>
/// **A class rather than a struct, because the simulation mutates it in place** across a dozen
/// steps and every one of them writes back. Valve keeps these in a linked list per entity, searched
/// linearly by bone index (<c>jigglebones.cpp:30</c>); a dictionary is the same lookup with the same
/// lifetime.
/// </remarks>
internal sealed class JiggleBoneState
{
    /// <summary>When this bone was last simulated, on the same clock the caller passes.</summary>
    public float LastUpdate;

    /// <summary>Where the base of the bone sits, which moves only with a base spring.</summary>
    public Vector3 BasePosition;

    /// <summary>Where it sat last step, used to recompute the base velocity after clamping.</summary>
    public Vector3 BaseLastPosition;

    /// <summary>The base's velocity.</summary>
    public Vector3 BaseVelocity;

    /// <summary>Accelerations accumulated this step, cleared after integrating.</summary>
    public Vector3 BaseAcceleration;

    /// <summary>Where the tip of the bone has swung to.</summary>
    public Vector3 TipPosition;

    /// <summary>The tip's velocity.</summary>
    public Vector3 TipVelocity;

    /// <summary>Accelerations accumulated this step, cleared after integrating.</summary>
    public Vector3 TipAcceleration;

    /// <summary>The previous left axis, seeded once and used only by Valve's debug overlay.</summary>
    public Vector3 LastLeft;

    /// <summary>Where the base was last step, for the boing effect's velocity estimate.</summary>
    public Vector3 LastBoingPosition;

    /// <summary>Which way the current boing is squashing.</summary>
    public Vector3 BoingDirection = new(0f, 0f, 1f);

    /// <summary>The unit velocity the boing test compares against.</summary>
    public Vector3 BoingVelocityDirection;

    /// <summary>How fast the base was moving, for the impact test.</summary>
    public float BoingSpeed;

    /// <summary>How long the current boing has been running.</summary>
    public float BoingTime;

    /// <summary>Frames left that must use the goal matrix rather than the simulation.</summary>
    public int UseGoalMatrixCount;

    /// <summary>Frames left that must use the simulation rather than the goal matrix.</summary>
    /// <remarks>
    /// **Sixteen on creation, thirty-two afterwards, and both numbers are Valve's.** A new bone gets
    /// `useJiggleBoneCount = 16` (`jigglebones.cpp:45`) and a bone that comes back above the
    /// framerate cutoff gets 32 (`:117`). The pair exist so the simulation cannot flicker on and off
    /// frame by frame around the cutoff, which would look far worse than either state.
    /// </remarks>
    public int UseJiggleBoneCount = 16;

    /// <summary>Re-seeds every field, as <c>JiggleData::Init</c> does.</summary>
    /// <param name="currentTime">Now.</param>
    /// <param name="basePosition">Where the bone's base is.</param>
    /// <param name="tipPosition">Where its tip would be at rest.</param>
    /// <remarks>
    /// **`Init` does NOT reset the two frame counters** — `GetJiggleData` sets them beside its own
    /// call, and the re-init inside `BuildJiggleTransformations` leaves whatever they held. Copied
    /// as written: resetting them here would restart the anti-flicker window every time a bone came
    /// back after being off screen.
    /// </remarks>
    public void Reset(float currentTime, Vector3 basePosition, Vector3 tipPosition)
    {
        LastUpdate = currentTime;

        BasePosition = basePosition;
        BaseLastPosition = basePosition;
        BaseVelocity = default;
        BaseAcceleration = default;

        TipPosition = tipPosition;
        TipVelocity = default;
        TipAcceleration = default;

        LastLeft = default;

        LastBoingPosition = basePosition;
        BoingDirection = new Vector3(0f, 0f, 1f);
        BoingVelocityDirection = default;
        BoingSpeed = 0f;
        BoingTime = 0f;
    }
}

/// <summary>
/// The spring simulation behind <c>STUDIO_PROC_JIGGLE</c> — Valve's <c>CJiggleBones</c>.
/// </summary>
/// <remarks>
/// **This is the only procedural bone rule TF2 uses.** Measured across two demos: every bone with a
/// <c>proctype</c> is a jiggle bone, and none of the four rules <c>CalcProceduralBone</c> implements
/// appears at all. Earbud cords, weapon chains, cosmetic tassels — 22 of 379 bones on
/// `koth_harvest_final`.
///
/// **Ported from <c>jigglebones.cpp:60</c> branch for branch**, including the parts that look like
/// they could be simplified:
///
/// - **the two frame counters**, which stop the simulation flickering on and off around the
///   framerate cutoff;
/// - **`deltaT` clamped up to a thousandth rather than skipped**, so a backwards seek takes a tiny
///   forward step instead of integrating a negative time;
/// - **the velocity zeroed at a constraint instead of reflected**, with Valve's own comment saying
///   the friction and bounce fields were removed for causing blowups — which is why four fields of
///   `mstudiojigglebone_t` are parsed and never read.
///
/// **What is NOT ported: the debug overlays.** Six `cl_jiggle_bone_debug*` blocks draw lines through
/// `debugoverlay`, which has no implementation in the SDK at all and no equivalent here.
/// </remarks>
public sealed class JiggleBones
{
    /// <summary>Below this many frames a second the simulation is skipped.</summary>
    /// <remarks>
    /// **`cl_jiggle_bone_framerate_cutoff`, default 20** (<c>jigglebones.cpp:25</c>). Not a cheat
    /// cvar, so a player can raise it. The reason it exists is in its own help string: below the
    /// cutoff a frame's movement is too large for Euler integration and the spring explodes.
    /// </remarks>
    public const float FramerateCutoff = 20f;

    /// <summary>A step this small or smaller is treated as this small.</summary>
    public const float SmallestStep = 0.001f;

    /// <summary>How long a gap counts as the bone having been away and needing a re-seed.</summary>
    public const float RestartAfter = 0.5f;

    /// <summary>Per-bone state, kept for the life of the entity.</summary>
    private readonly Dictionary<int, JiggleBoneState> _state = [];

    /// <summary>How many bones this has simulated at least once.</summary>
    /// <remarks>
    /// **For a diagnostic to report, and it reports what the code USED** (B243). A viewer line
    /// saying "12 jiggle bones" built from the model's flags would be a second reading of the
    /// model; this counts the ones that actually reached the simulation.
    /// </remarks>
    public int Simulated => _state.Count;

    /// <summary>Where a bone's tip has swung to, in world space.</summary>
    /// <param name="bone">Which bone.</param>
    /// <param name="tip">Its tip, when this bone has been simulated.</param>
    /// <returns>Whether the bone has any state yet.</returns>
    /// <remarks>
    /// **The simulation's real variable, and nothing else can see it.** The matrix this writes
    /// carries only the NORMALISED direction from base to tip and the base position, so the tip's
    /// distance — the whole subject of `JIGGLE_HAS_LENGTH_CONSTRAINT` — is invisible downstream.
    ///
    /// **Added because a test could not fail without it.** `Build_WithALengthConstraint_
    /// HoldsTheTipOneLengthOut` asserted on the matrix's forward axis, which is normalised one line
    /// BEFORE the constraint runs and is therefore a unit vector either way: deleting the
    /// constraint's own reprojection changed nothing the suite could see. Measuring a proxy that is
    /// unfaithful to the variable is the first entry in
    /// `docs/memory/instrument-bugs-outnumber-decoder-bugs.md`, and this is that entry exactly.
    ///
    /// **Valve exposes the same value for the same reason** — `cl_jiggle_bone_debug` draws a line
    /// from the base to `data->tipPos` and prints it (`jigglebones.cpp:780`).
    /// </remarks>
    public bool TipOf(int bone, out Vector3 tip)
    {
        if (_state.TryGetValue(bone, out JiggleBoneState? data))
        {
            tip = data.TipPosition;
            return true;
        }

        tip = default;
        return false;
    }

    /// <summary>Runs one bone's spring physics and writes its matrix.</summary>
    /// <param name="bone">Which bone, for keying the state.</param>
    /// <param name="currentTime">Now, on a clock that only moves forward between frames.</param>
    /// <param name="jiggle">The bone's authored parameters.</param>
    /// <param name="goal">Where the animation put the bone: a row-major 3x4 of twelve floats.</param>
    /// <param name="into">Where to write the result, also twelve floats. May alias <paramref name="goal"/>.</param>
    /// <param name="flipped">Whether the coordinate system is mirrored, as for a left-handed viewmodel.</param>
    /// <exception cref="ArgumentException">Either matrix is not twelve floats.</exception>
    /// <remarks>
    /// **`goal` is Valve's `goalMX`** — the bone's local transform already concatenated onto its
    /// parent, which is what `BuildTransformations` has in hand at the point it branches
    /// (<c>c_baseanimating.cpp:1557</c>). The simulation reads the goal's axes and position out of
    /// it and never touches the skeleton itself.
    /// </remarks>
    public void Build(
        int bone,
        float currentTime,
        in StudioJiggleBone jiggle,
        ReadOnlySpan<float> goal,
        Span<float> into,
        bool flipped)
    {
        if (goal.Length != 12 || into.Length != 12)
        {
            throw new ArgumentException(
                "A jiggle bone takes and writes a matrix3x4_t of twelve floats.");
        }

        // MatrixPosition is column three; MatrixGetColumn 0, 1 and 2 are left, up and forward.
        Vector3 basePosition = new(goal[3], goal[7], goal[11]);
        Vector3 left = new(goal[0], goal[4], goal[8]);
        Vector3 up = new(goal[1], goal[5], goal[9]);
        Vector3 forward = new(goal[2], goal[6], goal[10]);

        Vector3 goalTip = basePosition + (jiggle.Length * forward);

        if (!_state.TryGetValue(bone, out JiggleBoneState? data))
        {
            data = new JiggleBoneState();
            data.Reset(currentTime, basePosition, goalTip);
            _state[bone] = data;
        }

        // "if frames have been skipped since our last update, we were likely disabled and
        // re-enabled, so re-init".
        if (currentTime - data.LastUpdate > RestartAfter)
        {
            data.Reset(currentTime, basePosition, goalTip);
        }

        if (data.LastLeft == Vector3.Zero)
        {
            data.LastLeft = left;
        }

        float step = currentTime - data.LastUpdate;

        bool tiny = step < SmallestStep;
        bool useGoal = FramerateCutoff <= 0f || step > 1f / FramerateCutoff;

        if (useGoal)
        {
            data.UseGoalMatrixCount = 32;
        }
        else if (data.UseGoalMatrixCount > 0)
        {
            useGoal = true;
            data.UseGoalMatrixCount--;
        }
        else
        {
            data.UseJiggleBoneCount = 32;
        }

        if (data.UseJiggleBoneCount > 0)
        {
            data.UseJiggleBoneCount--;
            data.UseGoalMatrixCount = 0;
            useGoal = false;
        }

        if (tiny)
        {
            // **Clamped UP, not skipped, and the order matters.** A step of zero or a backwards seek
            // both land here and take a thousandth of a second forward; the alternative — returning
            // early — would leave the bone on last frame's matrix while the model moved.
            step = SmallestStep;
        }
        else if (useGoal)
        {
            goal.CopyTo(into);
            return;
        }

        // "we want lastUpdate here, so if jigglebones were skipped they get reinitialized if they
        // turn back on".
        data.LastUpdate = currentTime;

        if (jiggle.HasTipFlex)
        {
            TipFlex(data, jiggle, step, basePosition, goalTip, left, up, forward, goal, into, flipped);
        }

        if (jiggle.HasBaseSpring)
        {
            BaseSpring(data, jiggle, step, basePosition, left, up, forward, goal, into);
        }
        else if (jiggle.IsBoing)
        {
            Boing(data, jiggle, step, basePosition, left, up, forward, goal, into);
        }
        else if (!jiggle.HasTipFlex)
        {
            // "no flex at all - just use goal matrix".
            goal.CopyTo(into);
        }
    }

    /// <summary>The tip's spring, its constraints, and the matrix built from where it ended up.</summary>
    private static void TipFlex(
        JiggleBoneState data,
        in StudioJiggleBone jiggle,
        float step,
        Vector3 basePosition,
        Vector3 goalTip,
        Vector3 left,
        Vector3 up,
        Vector3 forward,
        ReadOnlySpan<float> goal,
        Span<float> into,
        bool flipped)
    {
        // Gravity, in GLOBAL space — the only term that does not go through the local axes.
        data.TipAcceleration.Z -= jiggle.TipMass;

        if (jiggle.IsFlexible)
        {
            Vector3 error = goalTip - data.TipPosition;

            Vector3 localError = new(
                Vector3.Dot(left, error), Vector3.Dot(up, error), Vector3.Dot(forward, error));

            Vector3 localVelocity = new(
                Vector3.Dot(left, data.TipVelocity), Vector3.Dot(up, data.TipVelocity), 0f);

            float yaw = (jiggle.YawStiffness * localError.X) - (jiggle.YawDamping * localVelocity.X);
            float pitch =
                (jiggle.PitchStiffness * localError.Y) - (jiggle.PitchDamping * localVelocity.Y);

            if (jiggle.HasLengthConstraint)
            {
                data.TipAcceleration += (yaw * left) + (pitch * up);
            }
            else
            {
                // Only reached without a length constraint, which is why the third dot product is
                // inside this arm rather than beside the other two.
                float alongVelocity = Vector3.Dot(forward, data.TipVelocity);

                float along =
                    (jiggle.AlongStiffness * localError.Z) - (jiggle.AlongDamping * alongVelocity);

                data.TipAcceleration += (yaw * left) + (pitch * up) + (along * forward);
            }
        }

        // Simple Euler integration, and Valve says so in the comment.
        data.TipVelocity += data.TipAcceleration * step;
        data.TipPosition += data.TipVelocity * step;
        data.TipAcceleration = default;

        if (jiggle.HasYawConstraint || jiggle.HasPitchConstraint)
        {
            Constrain(data, jiggle, basePosition, left, up, forward, goal);
        }

        Vector3 tip = Vector3.Normalize(data.TipPosition - basePosition);

        if (jiggle.HasAngleConstraint)
        {
            // **Valve's own angle arithmetic, including the branch that looks wrong.** `acos`
            // already returns the angle in [0, pi], so `2*pi - angleBetween` for a negative dot
            // takes the REFLEX angle — always greater than pi, so any negative dot exceeds any
            // authored limit and the clamp always fires. Reproduced rather than corrected: a bone
            // bent more than ninety degrees off its goal is exactly the case the limit is for.
            float dot = Vector3.Dot(tip, forward);
            float between = MathF.Acos(Math.Clamp(dot, -1f, 1f));

            if (dot < 0f)
            {
                between = (2f * MathF.PI) - between;
            }

            if (between > jiggle.AngleLimit)
            {
                float widest = jiggle.Length * MathF.Sin(jiggle.AngleLimit);

                Vector3 delta = Vector3.Normalize(goalTip - data.TipPosition);

                data.TipPosition = goalTip - (widest * delta);

                tip = Vector3.Normalize(data.TipPosition - basePosition);
            }
        }

        if (jiggle.HasLengthConstraint)
        {
            data.TipPosition = basePosition + (jiggle.Length * tip);

            // Zero the velocity along the bone, since it can no longer travel that way.
            data.TipVelocity -= Vector3.Dot(data.TipVelocity, tip) * tip;
        }

        // **The left-handed arm is for a mirrored viewmodel** and is not a tidy-up of the other:
        // crossing the same pair in the other order gives the opposite vector, which is exactly what
        // a flipped coordinate system needs.
        Vector3 side;
        Vector3 above;

        if (flipped)
        {
            side = Vector3.Normalize(Vector3.Cross(tip, up));
            above = Vector3.Cross(side, tip);
        }
        else
        {
            side = Vector3.Normalize(Vector3.Cross(up, tip));
            above = Vector3.Cross(tip, side);
        }

        into[0] = side.X;
        into[4] = side.Y;
        into[8] = side.Z;
        into[1] = above.X;
        into[5] = above.Y;
        into[9] = above.Z;
        into[2] = tip.X;
        into[6] = tip.Y;
        into[10] = tip.Z;

        into[3] = basePosition.X;
        into[7] = basePosition.Y;
        into[11] = basePosition.Z;
    }

    /// <summary>Clamps the tip's yaw and pitch to their authored ranges.</summary>
    /// <remarks>
    /// **Each limit is applied by rebuilding the goal matrix rotated to the limit and projecting
    /// onto it**, rather than by clamping an angle — so the tip lands exactly on the limit plane in
    /// world space. The yaw pass then recomputes its local vectors, because the pitch pass reads
    /// them and a stale set would clamp against where the tip used to be.
    /// </remarks>
    private static void Constrain(
        JiggleBoneState data,
        in StudioJiggleBone jiggle,
        Vector3 basePosition,
        Vector3 left,
        Vector3 up,
        Vector3 forward,
        ReadOnlySpan<float> goal)
    {
        Vector3 along = data.TipPosition - basePosition;

        Vector3 localAlong = new(
            Vector3.Dot(left, along), Vector3.Dot(up, along), Vector3.Dot(forward, along));

        if (jiggle.HasYawConstraint)
        {
            float error = MathF.Atan2(localAlong.X, localAlong.Z);
            float limit = 0f;
            bool atLimit = false;

            if (error < jiggle.MinYaw)
            {
                atLimit = true;
                limit = jiggle.MinYaw;
            }
            else if (error > jiggle.MaxYaw)
            {
                atLimit = true;
                limit = jiggle.MaxYaw;
            }

            if (atLimit)
            {
                float sine = MathF.Sin(limit);
                float cosine = MathF.Cos(limit);

                // A yaw about the up axis, written out because Valve writes it out: the entries are
                // cy, 0, sy / 0, 1, 0 / -sy, 0, cy in column-major reading.
                Span<float> rotation =
                [
                    cosine, 0f, sine, 0f,
                    0f, 1f, 0f, 0f,
                    -sine, 0f, cosine, 0f,
                ];

                Project(data, goal, rotation, basePosition, along, yaw: true);

                along = data.TipPosition - basePosition;
                localAlong = new Vector3(
                    Vector3.Dot(left, along), Vector3.Dot(up, along), Vector3.Dot(forward, along));
            }
        }

        if (!jiggle.HasPitchConstraint)
        {
            return;
        }

        float pitchError = MathF.Atan2(localAlong.Y, localAlong.Z);
        float pitchLimit = 0f;
        bool atPitchLimit = false;

        if (pitchError < jiggle.MinPitch)
        {
            atPitchLimit = true;
            pitchLimit = jiggle.MinPitch;
        }
        else if (pitchError > jiggle.MaxPitch)
        {
            atPitchLimit = true;
            pitchLimit = jiggle.MaxPitch;
        }

        if (!atPitchLimit)
        {
            return;
        }

        float pitchSine = MathF.Sin(pitchLimit);
        float pitchCosine = MathF.Cos(pitchLimit);

        Span<float> pitchRotation =
        [
            1f, 0f, 0f, 0f,
            0f, pitchCosine, pitchSine, 0f,
            0f, -pitchSine, pitchCosine, 0f,
        ];

        Project(data, goal, pitchRotation, basePosition, along, yaw: false);
    }

    /// <summary>Puts the tip on the limit plane and stops it dead.</summary>
    /// <remarks>
    /// **The two limits keep DIFFERENT components, and swapping them is the plausible bug.** A yaw
    /// limit keeps the up and forward parts (`limitAlong.y * limitUp + limitAlong.z * limitForward`)
    /// and a pitch limit keeps the left and forward parts. Each drops the component the limit
    /// constrains.
    ///
    /// **The velocity is zeroed rather than reflected**, and Valve's comment at both sites says why:
    /// *"removed friction and velocity clipping against constraint - was causing simulation blowups
    /// (MSB 12/9/2010)"*. That is why `yawFriction`, `yawBounce`, `pitchFriction` and `pitchBounce`
    /// are read out of the model and used by nothing.
    /// </remarks>
    private static void Project(
        JiggleBoneState data,
        ReadOnlySpan<float> goal,
        ReadOnlySpan<float> rotation,
        Vector3 basePosition,
        Vector3 along,
        bool yaw)
    {
        Span<float> limit = stackalloc float[12];

        StudioBones.Concatenate(goal, rotation, limit);

        Vector3 limitLeft = new(limit[0], limit[4], limit[8]);
        Vector3 limitUp = new(limit[1], limit[5], limit[9]);
        Vector3 limitForward = new(limit[2], limit[6], limit[10]);

        Vector3 limitAlong = new(
            Vector3.Dot(limitLeft, along),
            Vector3.Dot(limitUp, along),
            Vector3.Dot(limitForward, along));

        data.TipPosition = yaw
            ? basePosition + (limitAlong.Y * limitUp) + (limitAlong.Z * limitForward)
            : basePosition + (limitAlong.X * limitLeft) + (limitAlong.Z * limitForward);

        data.TipVelocity = default;
    }

    /// <summary>The base's own spring, its travel limits, and the friction at them.</summary>
    private static void BaseSpring(
        JiggleBoneState data,
        in StudioJiggleBone jiggle,
        float step,
        Vector3 basePosition,
        Vector3 left,
        Vector3 up,
        Vector3 forward,
        ReadOnlySpan<float> goal,
        Span<float> into)
    {
        data.BaseAcceleration.Z -= jiggle.BaseMass;

        Vector3 error = basePosition - data.BasePosition;

        data.BaseAcceleration +=
            (jiggle.BaseStiffness * error) - (jiggle.BaseDamping * data.BaseVelocity);

        data.BaseVelocity += data.BaseAcceleration * step;
        data.BasePosition += data.BaseVelocity * step;
        data.BaseAcceleration = default;

        error = data.BasePosition - basePosition;

        Vector3 localError = new(
            Vector3.Dot(left, error), Vector3.Dot(up, error), Vector3.Dot(forward, error));

        Vector3 localVelocity = new(
            Vector3.Dot(left, data.BaseVelocity),
            Vector3.Dot(up, data.BaseVelocity),
            Vector3.Dot(forward, data.BaseVelocity));

        // **Friction is applied to the OTHER two axes**, not to the one at its limit — a base
        // pressed against its left stop is slowed in up and forward, which is what friction on a
        // surface does.
        if (localError.X < jiggle.BaseMinLeft)
        {
            localError.X = jiggle.BaseMinLeft;
            data.BaseAcceleration -= jiggle.BaseLeftFriction *
                ((localVelocity.Y * up) + (localVelocity.Z * forward));
        }
        else if (localError.X > jiggle.BaseMaxLeft)
        {
            localError.X = jiggle.BaseMaxLeft;
            data.BaseAcceleration -= jiggle.BaseLeftFriction *
                ((localVelocity.Y * up) + (localVelocity.Z * forward));
        }

        if (localError.Y < jiggle.BaseMinUp)
        {
            localError.Y = jiggle.BaseMinUp;
            data.BaseAcceleration -= jiggle.BaseUpFriction *
                ((localVelocity.X * left) + (localVelocity.Z * forward));
        }
        else if (localError.Y > jiggle.BaseMaxUp)
        {
            localError.Y = jiggle.BaseMaxUp;
            data.BaseAcceleration -= jiggle.BaseUpFriction *
                ((localVelocity.X * left) + (localVelocity.Z * forward));
        }

        if (localError.Z < jiggle.BaseMinForward)
        {
            localError.Z = jiggle.BaseMinForward;
            data.BaseAcceleration -= jiggle.BaseForwardFriction *
                ((localVelocity.X * left) + (localVelocity.Y * up));
        }
        else if (localError.Z > jiggle.BaseMaxForward)
        {
            localError.Z = jiggle.BaseMaxForward;
            data.BaseAcceleration -= jiggle.BaseForwardFriction *
                ((localVelocity.X * left) + (localVelocity.Y * up));
        }

        data.BasePosition = basePosition +
            (localError.X * left) + (localError.Y * up) + (localError.Z * forward);

        // Recomputed from the clamped position rather than kept, so a base held at a stop reports
        // no velocity into that stop next step.
        data.BaseVelocity = (data.BasePosition - data.BaseLastPosition) / step;
        data.BaseLastPosition = data.BasePosition;

        if (!jiggle.HasTipFlex)
        {
            // "no tip flex - use bone's goal orientation".
            goal.CopyTo(into);
        }

        into[3] = data.BasePosition.X;
        into[7] = data.BasePosition.Y;
        into[11] = data.BasePosition.Z;
    }

    /// <summary>The squash-and-stretch impact effect — <c>JIGGLE_IS_BOING</c>.</summary>
    /// <remarks>
    /// **Reached only when there is NO base spring**, because it is the `else` of that test. A bone
    /// declaring both flags gets the base spring and never boings, which is Valve's structure rather
    /// than an oversight to fix.
    /// </remarks>
    private static void Boing(
        JiggleBoneState data,
        in StudioJiggleBone jiggle,
        float step,
        Vector3 basePosition,
        Vector3 left,
        Vector3 up,
        Vector3 forward,
        ReadOnlySpan<float> goal,
        Span<float> into)
    {
        const float MinimumSpeed = 5f;
        const float MinimumInterval = 0.5f;

        Vector3 velocity = basePosition - data.LastBoingPosition;

        data.LastBoingPosition = basePosition;

        float speed = velocity.Length();

        if (speed < 0.00001f)
        {
            velocity = new Vector3(0f, 0f, 1f);
            speed = 0f;
        }
        else
        {
            velocity /= speed;
            speed /= step;
        }

        data.BoingTime += step;

        if ((speed > MinimumSpeed || data.BoingSpeed > MinimumSpeed) &&
            data.BoingTime > MinimumInterval &&
            (MathF.Abs(data.BoingSpeed - speed) > jiggle.BoingImpactSpeed ||
             Vector3.Dot(velocity, data.BoingVelocityDirection) < jiggle.BoingImpactAngle))
        {
            data.BoingTime = 0f;
            data.BoingDirection = -velocity;
        }

        data.BoingVelocityDirection = velocity;
        data.BoingSpeed = speed;

        float damping = 1f - (jiggle.BoingDampingRate * data.BoingTime);

        if (damping < 0.01f)
        {
            goal.CopyTo(into);
            return;
        }

        damping *= damping;
        damping *= damping;

        float flex = jiggle.BoingAmplitude *
            MathF.Cos(jiggle.BoingFrequency * data.BoingTime) * damping;

        float squash = 1f + flex;
        float stretch = 1f - flex;

        // The goal's rotation with NO translation, which the squash is applied to before the
        // position is put back at the end.
        into[0] = left.X;
        into[4] = left.Y;
        into[8] = left.Z;
        into[1] = up.X;
        into[5] = up.Y;
        into[9] = up.Z;
        into[2] = forward.X;
        into[6] = forward.Y;
        into[10] = forward.Z;
        into[3] = 0f;
        into[7] = 0f;
        into[11] = 0f;

        // A basis whose Z is the boing direction. The 0.9 test picks whichever world axis is least
        // parallel to it, so the cross product never degenerates.
        Vector3 side = MathF.Abs(data.BoingDirection.X) < 0.9f
            ? Vector3.Cross(data.BoingDirection, new Vector3(1f, 0f, 0f))
            : Vector3.Cross(data.BoingDirection, new Vector3(0f, 0f, 1f));

        side = Vector3.Normalize(side);

        Vector3 other = Vector3.Cross(data.BoingDirection, side);

        Span<float> toBoing =
        [
            side.X, side.Y, side.Z, 0f,
            other.X, other.Y, other.Z, 0f,
            data.BoingDirection.X, data.BoingDirection.Y, data.BoingDirection.Z, 0f,
        ];

        Span<float> squashed =
        [
            squash, 0f, 0f, 0f,
            0f, squash, 0f, 0f,
            0f, 0f, stretch, 0f,
        ];

        // The inverse is the transpose, the basis being orthonormal.
        Span<float> fromBoing =
        [
            toBoing[0], toBoing[4], toBoing[8], 0f,
            toBoing[1], toBoing[5], toBoing[9], 0f,
            toBoing[2], toBoing[6], toBoing[10], 0f,
        ];

        // **`Concatenate`, which is Valve's `MatrixMultiply` for a 3x4.** Three distinct buffers
        // because it may not alias its inputs, where Valve's writes into one of its own
        // (`MatrixMultiply( xfrmMX, xfrmFromBoingCoordsMX, xfrmMX )`).
        Span<float> combined = stackalloc float[12];
        Span<float> whole = stackalloc float[12];

        StudioBones.Concatenate(toBoing, squashed, combined);
        StudioBones.Concatenate(combined, fromBoing, whole);
        StudioBones.Concatenate(into, whole, combined);

        combined.CopyTo(into);

        into[3] = basePosition.X;
        into[7] = basePosition.Y;
        into[11] = basePosition.Z;
    }
}
