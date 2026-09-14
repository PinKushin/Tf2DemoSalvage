using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The performance settings a physics environment is given — <c>physics_performanceparams_t</c> (B369).</summary>
/// <param name="MaxCollisionsPerObjectPerTimestep"><i>"object will be frozen after this many collisions"</i>.</param>
/// <param name="MaxCollisionChecksPerTimestep"><i>"objects may penetrate after this many collision checks"</i>.</param>
/// <param name="MaxVelocity"><i>"limit world space linear velocity to this (in / s)"</i>.</param>
/// <param name="MaxAngularVelocity"><i>"limit world space angular velocity to this (degrees / s)"</i>.</param>
/// <param name="LookAheadTimeObjectsVsWorld"><i>"predict collisions this far (seconds) into the future"</i>.</param>
/// <param name="LookAheadTimeObjectsVsObject">The same, against another object.</param>
/// <param name="MinFrictionMass"><i>"min mass for friction solves"</i>.</param>
/// <param name="MaxFrictionMass">And the most.</param>
/// <remarks>
/// **Published** — `public/vphysics/performance.h:19-41`, each comment above quoted from it.
///
/// **The client never gives its environment any**: `PhysicsLevelInit` creates it and sets gravity, the step and the collision
/// handlers and nothing else (`game/client/physics.cpp:163-187`), where the server raises the collisions from 6 to 10 before
/// `SetPerformanceSettings` (`game/server/physics.cpp:222-226`). A corpse is simulated on the client, so it runs what the
/// environment constructor gives itself — `FUN_1800114f0` passes <see cref="Defaults"/>'s eight values to
/// `SetPerformanceSettings` (`FUN_180015200`) on its own.
/// </remarks>
public sealed record IvpPerformanceSettings(
    int MaxCollisionsPerObjectPerTimestep,
    int MaxCollisionChecksPerTimestep,
    float MaxVelocity,
    float MaxAngularVelocity,
    float LookAheadTimeObjectsVsWorld,
    float LookAheadTimeObjectsVsObject,
    float MinFrictionMass,
    float MaxFrictionMass)
{
    /// <summary><c>physics_performanceparams_t::Defaults()</c>, the values the environment constructor passes.</summary>
    /// <remarks>
    /// `6`, `250`, `k_flMaxVelocity = 2000.0f`, `k_flMaxAngularVelocity = 360.0f * 10.0f`, `1.0f`, `0.5f`,
    /// `DEFAULT_MIN_FRICTION_MASS = 10.0f` and `DEFAULT_MAX_FRICTION_MASS = 2500.0f` (`performance.h:14-18`, `:30-40`) — and the
    /// constructor's stack holds the same: `6`, `0xfa`, `{2000, 3600, 1, 0.5}`, `0x41200000`, `0x451c4000`.
    /// </remarks>
    public static IvpPerformanceSettings Defaults { get; } = new(6, 250, 2000f, 360f * 10f, 1f, 0.5f, 10f, 2500f);
}

/// <summary>The limits IVP's anomaly checks compare against — the object at <c>env+0x48</c> (B369).</summary>
/// <param name="MaximumVelocity"><c>+0xc</c>, in metres per second.</param>
/// <param name="MaximumCollisions"><c>+0x10</c>, the impacts a core may take before the freeze check.</param>
/// <param name="MaximumAngularVelocityPerPsi"><c>+0x14</c>, in radians per step of the PSI in force when the settings were given.</param>
/// <param name="MaximumCollisionChecks"><c>+0x18</c>.</param>
/// <param name="MinimumFrictionMass"><c>+0x1c</c>, clamped to one through fifty thousand.</param>
/// <param name="MaximumFrictionMass"><c>+0x20</c>, clamped the same.</param>
public sealed record IvpAnomalyLimits(
    float MaximumVelocity,
    int MaximumCollisions,
    float MaximumAngularVelocityPerPsi,
    int MaximumCollisionChecks,
    float MinimumFrictionMass,
    float MaximumFrictionMass)
{
    /// <summary><c>DAT_1800eb764</c>: degrees to radians, a float.</summary>
    private const float RadiansPerDegree = 0.017453292f;

    /// <summary>The <c>0x3f800000</c> the routine writes to its stack for <c>MAXSS</c>.</summary>
    private const float FrictionMassFloor = 1f;

    /// <summary>The <c>0x47435000</c> the routine writes to its stack for <c>MINSS</c>.</summary>
    private const float FrictionMassCeiling = 50000f;

    /// <summary>The limits <c>CPhysicsEnvironment::SetPerformanceSettings</c> writes — <c>FUN_180015200</c>.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="step">The environment's PSI step, <c>env+0x108</c>, at the moment the settings are given.</param>
    /// <returns>The limits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <remarks>
    /// <code>
    /// +0xc  = 0.0254f · maxVelocity                          +0x10 = maxCollisionsPerObjectPerTimestep
    /// +0x14 = (maxAngularVelocity · 0.017453292f) · (float)step   +0x18 = maxCollisionChecksPerTimestep
    /// +0x1c = MINSS(MAXSS(minFrictionMass, 1f), 50000f)       +0x20 = the same for maxFrictionMass
    /// </code>
    /// **The spin limit is taken per step of the PSI in force WHEN THE SETTINGS ARE GIVEN, and nothing takes it again**:
    /// `SetSimulationTimestep` (`1800152f0`) jumps to `FUN_180082470`, which writes the step, its reciprocal and a decay at
    /// `+0x1b0`, and not the limits. The check multiplies the limit by the CURRENT reciprocal (`FUN_18008dd00`), so an
    /// environment built at one step and run at another holds its spin to the ratio of the two. The routine also writes the
    /// two look-ahead times, widened, to the object at `env+0x38`; they are not limits and are not carried here.
    /// </remarks>
    public static IvpAnomalyLimits FromPerformanceSettings(IvpPerformanceSettings settings, double step)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new IvpAnomalyLimits(
            IvpTransform.MetresPerInch * settings.MaxVelocity,
            settings.MaxCollisionsPerObjectPerTimestep,
            settings.MaxAngularVelocity * RadiansPerDegree * (float)step,
            settings.MaxCollisionChecksPerTimestep,
            Clamp(settings.MinFrictionMass),
            Clamp(settings.MaxFrictionMass));
    }

    /// <summary><c>MAXSS</c> then <c>MINSS</c>, each answering its second operand for a NaN.</summary>
    private static float Clamp(float mass)
    {
        float floored = mass > FrictionMassFloor ? mass : FrictionMassFloor;

        return floored < FrictionMassCeiling ? floored : FrictionMassCeiling;
    }
}

/// <summary>What the game answers the physics environment — the part of <c>IPhysicsCollisionSolver</c> the collision path asks (B369).</summary>
/// <remarks>`public/vphysics_interface.h:500-516`. A game hands one over with `SetCollisionSolver`.</remarks>
public interface IPhysicsCollisionSolver
{
    /// <summary>Whether an object that has taken the most collisions a step allows should be frozen — <c>ShouldFreezeObject</c>.</summary>
    /// <param name="body">The object's core.</param>
    /// <returns>Whether to freeze it.</returns>
    /// <remarks>*"pObject has already done the max number of collisions this tick, should we freeze it to save CPU?"*</remarks>
    public bool ShouldFreezeObject(IvpRigidBody body);

    /// <summary>Whether a heap of objects with too many contacts should be frozen — <c>ShouldFreezeContacts</c>.</summary>
    /// <param name="objects">Each core's first object, named by its core.</param>
    /// <returns>Whether to freeze them.</returns>
    /// <remarks>*"The system has determined that these objects have too many contacts, should we freeze them?"* (`vphysics_interface.h:514`).</remarks>
    public bool ShouldFreezeContacts(IReadOnlyList<IvpRigidBody> objects);
}

/// <summary>The client's collision solver — <c>CCollisionEvent</c>, which <c>PhysicsLevelInit</c> hands the environment (B369).</summary>
/// <remarks>`physenv->SetCollisionSolver( &amp;g_Collisions )` (`game/client/physics.cpp:182`).</remarks>
public sealed class ClientCollisionEvent : IPhysicsCollisionSolver
{
    /// <inheritdoc />
    /// <remarks>`bool ShouldFreezeObject( IPhysicsObject *pObject ) { return true; }` (`game/client/physics.cpp:76`).</remarks>
    public bool ShouldFreezeObject(IvpRigidBody body) => true;

    /// <inheritdoc />
    /// <remarks>`bool ShouldFreezeContacts( IPhysicsObject **pObjectList, int objectCount ) { return true; }` (`game/client/physics.cpp:78`).</remarks>
    public bool ShouldFreezeContacts(IReadOnlyList<IvpRigidBody> objects) => true;
}

/// <summary>The three anomaly checks the impact solver makes — the slots of <c>IVP_Anomaly_Manager</c> it calls (B369).</summary>
public interface IIvpAnomalyManager
{
    /// <summary>Slot 0: a core's velocity is over the limit.</summary>
    /// <param name="limits">The environment's limits.</param>
    /// <param name="core">The core.</param>
    /// <param name="velocity">The velocity being checked, changed in place.</param>
    public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity);

    /// <summary>Slot 1: a core's angular velocity is over the limit.</summary>
    /// <param name="limits">The environment's limits.</param>
    /// <param name="core">The core.</param>
    /// <param name="inverseStep">The reciprocal of the PSI step, <c>env+0x110</c>, which the engine reaches through the core.</param>
    /// <param name="spin">The angular velocity being checked, changed in place.</param>
    public void MaximumAngularVelocityExceeded(
        IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin);

    /// <summary>Slot 3: a core has taken more impacts than the limit — should it freeze.</summary>
    /// <param name="limits">The environment's limits.</param>
    /// <param name="core">The core.</param>
    /// <returns>Whether to freeze it.</returns>
    public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core);

    /// <summary>Slot 5: a friction system holds more contacts than it solves — should its cores freeze.</summary>
    /// <param name="cores">The system's cores, <c>system+0x50</c>.</param>
    /// <returns>Whether to freeze them.</returns>
    public bool MaximumContactsExceeded(IReadOnlyList<IvpRigidBody> cores);
}

/// <summary>
/// The anomaly manager vphysics gives IVP — the <c>IVP_Anomaly_Manager</c> half of the object the environment constructor
/// allocates at <c>CPhysicsEnvironment+0xb0</c>, whose table is at <c>1800ebf90</c> (B369).
/// </summary>
/// <param name="collisionSolver">The game's solver, the manager's <c>+0x10</c>; null when none was handed over.</param>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The anomaly manager and the limits*):
///
/// <code>
/// slot 0  FUN_180017010   the core's first object's CPhysicsObject asked GetShadowController (IPhysicsObject slot 70);
///                         with none, the base's FUN_180089ae0
/// slot 1  FUN_180089a50   the base's, not overridden
/// slot 3  FUN_180016e80   ShouldFreezeObject of the game's solver (IPhysicsCollisionSolver slot 2), or one with none
/// </code>
///
/// **The shadow test is not carried, and it cannot change a ragdoll**: a shadow controller is made for an object by
/// `CreateShadowController` or `SetShadow`, and `ragdoll_shared.cpp`, which creates every ragdoll element, calls neither.
/// </remarks>
public sealed class VphysicsAnomalyManager(IPhysicsCollisionSolver? collisionSolver) : IIvpAnomalyManager
{
    /// <summary><c>DAT_1800fd728</c>: <c>0.99f</c> widened, the share of the limit a fast core is scaled to.</summary>
    private const double VelocityShare = 0.99f;

    /// <summary><c>DAT_1800fd588</c>: <c>0.9f</c> widened, the share of the limit a spinning core is scaled to.</summary>
    private const double AngularShare = 0.9f;

    /// <inheritdoc />
    /// <remarks>
    /// `FUN_180089ae0`: `s = ((double)+0xc · 0.99) / √(double)((x² + y²) + z²)`, the squares summed in float, and each lane
    /// `(float)((double)v · s)`.
    /// </remarks>
    public void MaximumVelocityExceeded(IvpAnomalyLimits limits, IvpRigidBody core, ref (float X, float Y, float Z) velocity)
    {
        ArgumentNullException.ThrowIfNull(limits);

        float squared = (velocity.X * velocity.X) + (velocity.Y * velocity.Y) + (velocity.Z * velocity.Z);
        double scale = ((double)limits.MaximumVelocity * VelocityShare) / Math.Sqrt(squared);

        velocity = ((float)(velocity.X * scale), (float)(velocity.Y * scale), (float)(velocity.Z * scale));
    }

    /// <inheritdoc />
    /// <remarks>
    /// `FUN_180089a50`: `s = ((double)((float)env+0x110 · +0x14) · 0.9) / √(double)((x² + y²) + z²)`, the product and the squares
    /// in float, and each lane `(float)((double)ω · s)`.
    /// </remarks>
    public void MaximumAngularVelocityExceeded(
        IvpAnomalyLimits limits, IvpRigidBody core, double inverseStep, ref (float X, float Y, float Z) spin)
    {
        ArgumentNullException.ThrowIfNull(limits);

        float squared = (spin.X * spin.X) + (spin.Y * spin.Y) + (spin.Z * spin.Z);
        double reach = (float)inverseStep * limits.MaximumAngularVelocityPerPsi;
        double scale = (reach * AngularShare) / Math.Sqrt(squared);

        spin = ((float)(spin.X * scale), (float)(spin.Y * scale), (float)(spin.Z * scale));
    }

    /// <inheritdoc />
    /// <remarks>`FUN_180016e80`: `SETNZ` of the game's answer, or `1` when the manager's `+0x10` is null.</remarks>
    public bool MaximumCollisionsExceededCheckFreezing(IvpAnomalyLimits limits, IvpRigidBody core) =>
        collisionSolver?.ShouldFreezeObject(core) ?? true;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No game solver was handed over, where the binary dereferences the null.</exception>
    /// <remarks>
    /// `FUN_180016ec0`: each core's first object's `CPhysicsObject` (`[core+0x70]` → `+0x100`) appended to a list, in order, and the
    /// list handed to `ShouldFreezeContacts` (`IPhysicsCollisionSolver` slot 4) — `SETNZ` of the answer. **Unlike slot 3 it does not
    /// check for a solver**: `MOV RCX,[RCX+0x10]; MOV RAX,[RCX]` with nothing between.
    /// </remarks>
    public bool MaximumContactsExceeded(IReadOnlyList<IvpRigidBody> cores) =>
        (collisionSolver ?? throw new InvalidOperationException("vphysics asks a game solver that was never handed over."))
            .ShouldFreezeContacts(cores);
}
