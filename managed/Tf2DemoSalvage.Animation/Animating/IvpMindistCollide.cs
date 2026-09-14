using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A collided mindist's real response — <c>IvpMindist::Collide</c> (<c>18008ecb0</c>) and <c>FUN_18008ef60</c> under it
/// (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, both functions in full.** This is the `collide` argument
/// <see cref="IvpMindistFire.Handle"/> takes and does not itself port. Not yet pinned by an oracle probe against the
/// shipped binary; the retry loop above a mindist's own collision (<c>FUN_180090700</c>/<c>FUN_180090bd0</c>) and the
/// top-level PSI driver that would call this in a real running loop are not yet built — see `docs/HANDOFF.md`, item 3.
/// </remarks>
public static class IvpMindistCollide
{
    /// <summary>Resolves one mindist's collision into a linked contact, its record, and a solved impact.</summary>
    /// <param name="mindist">The mindist; its flags name synapse A.</param>
    /// <param name="firstObject">Synapse record 0's object.</param>
    /// <param name="firstSide">Synapse record 0's ledge side, freshly built for this collision.</param>
    /// <param name="secondObject">Synapse record 1's object.</param>
    /// <param name="secondSide">Synapse record 1's ledge side, freshly built for this collision.</param>
    /// <param name="environment">The impact environment.</param>
    /// <param name="materials">The material manager <see cref="IvpContactPoint.SetMaterials"/> reads.</param>
    /// <param name="now">The environment's time — this collision's own event time.</param>
    /// <returns>The solver, after its solve.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">An object has no core.</exception>
    /// <remarks>
    /// <code>
    /// each core not resting and not immovable: RebuildMatrixAtEventTime
    /// FindOrAllocate;  LinkContactByCore;  contact's last-measured time = now
    /// IvpContactRecord.Build;  SetMaterials;  PushOut;  IvpImpactSolver.Enter (runs the solve itself)
    /// </code>
    /// **The native's resting/immovable guard on the refinement is simplified to "movable"**: `core+1 &lt; 8 &amp;&amp;
    /// core+0x0 &amp; 0x10 == 0` reads a resting counter this port does not carry and one bit of the two
    /// <see cref="IvpRigidBody.Immovable"/> already collapses (documented there and in
    /// <see cref="IvpFrictionLinking.LinkContactByCore"/>). Refining a resting core's transform anyway costs a little
    /// extra work for the same geometry, never a different answer — the guard is a performance skip, not a branch in
    /// the physics. **The retry loop above this (`FUN_180090700`/`FUN_180090bd0`) and the environment's generation
    /// bump (`env+0x1a4`) are not included here** — they belong to the top-level PSI driver this call sits inside,
    /// not yet built.
    /// </remarks>
    public static IvpImpactSolver Collide(
        IvpMindist mindist,
        IvpCollisionObject firstObject,
        IvpLedgeSide firstSide,
        IvpCollisionObject secondObject,
        IvpLedgeSide secondSide,
        IvpImpactEnvironment environment,
        IIvpMaterialManager materials,
        double now)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(firstObject);
        ArgumentNullException.ThrowIfNull(firstSide);
        ArgumentNullException.ThrowIfNull(secondObject);
        ArgumentNullException.ThrowIfNull(secondSide);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(materials);

        IvpRigidBody firstCore = firstObject.Core ?? throw new InvalidOperationException("A collided mindist's first object has no core.");
        IvpRigidBody secondCore = secondObject.Core ?? throw new InvalidOperationException("A collided mindist's second object has no core.");

        if (!firstCore.Immovable)
        {
            firstCore.RebuildMatrixAtEventTime(now);
        }

        if (!secondCore.Immovable)
        {
            secondCore.RebuildMatrixAtEventTime(now);
        }

        IvpContactPoint contact = IvpFrictionLinking.FindOrAllocate(mindist, firstObject, firstSide, secondObject, secondSide, now);

        IvpFrictionLinking.LinkContactByCore(contact, firstCore, secondCore, environment);

        contact.LastMeasured = now;

        contact.Record = IvpContactRecord.Build(
            contact,
            new IvpContactBody(firstSide, firstCore, firstObject.ExtraRadius),
            new IvpContactBody(secondSide, secondCore, secondObject.ExtraRadius),
            now);

        contact.SetMaterials(materials);

        float pushOut = contact.PushOut(environment);

        return IvpImpactSolver.Enter(environment, contact, [firstCore, secondCore], pushOut);
    }
}
