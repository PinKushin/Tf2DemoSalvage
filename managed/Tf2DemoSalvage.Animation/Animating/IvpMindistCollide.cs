using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// A collided mindist's real response — <c>IvpMindist::Collide</c> (<c>18008ecb0</c>) and <c>FUN_18008ef60</c> under it
/// (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler, both functions in full** (`docs/findings/51`, *The collision around the impact loop*). This is the
/// `collide` argument <see cref="IvpMindistFire.Handle"/> takes and does not itself port. Not yet pinned by an oracle probe
/// against the shipped binary.
/// </remarks>
public static class IvpMindistCollide
{
    /// <summary>Resolves one mindist's collision: a linked contact, its solved impact, and the impact loop around it.</summary>
    /// <param name="mindist">The mindist; its flags name synapse A.</param>
    /// <param name="firstObject">Synapse record 0's object.</param>
    /// <param name="firstSide">Synapse record 0's ledge side, freshly built for this collision.</param>
    /// <param name="secondObject">Synapse record 1's object.</param>
    /// <param name="secondSide">Synapse record 1's ledge side, freshly built for this collision.</param>
    /// <param name="environment">The impact environment.</param>
    /// <param name="materials">The material manager <see cref="IvpContactPoint.SetMaterials"/> reads.</param>
    /// <param name="sides">Every other contact's two ledge sides now, for the impact loop's revalidations.</param>
    /// <param name="minimize">The minimize, for the tail's recheck.</param>
    /// <param name="reschedule">The scheduler in mode 2, for the tail's recheck.</param>
    /// <param name="now">The environment's time — this collision's own event time.</param>
    /// <returns>The first impact's solver, after its solve.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">An object has no core.</exception>
    /// <remarks>
    /// <code>
    /// each core: +0x1 &lt; 8 and not flags &amp; 0x10 → FUN_180078d60;  env+0x1a4 += 1
    /// cp = FUN_180090e50 (find or allocate, link, record, materials);  pair+0x28 = env+0x188
    /// FUN_18008ed60(record, cores, FUN_18008fca0(cp), cp)
    /// v = record+0x30, negated when B's core has flags &amp; 2;  FUN_180090700(block, mindist, system, pair, cp);  record+0x30 = v
    /// </code>
    /// *Not carried*: `FUN_180074360` on each object (state 8), the deferral count at `env+0xf8`, and the listeners. **Because
    /// the wake is not carried, a core at state 8 cannot collide here yet** — the engine wakes its unit first — so the `&lt; 8`
    /// bound has no reachable input until that lands.
    /// </remarks>
    public static IvpImpactSolver Collide(
        IvpMindist mindist,
        IvpCollisionObject firstObject,
        IvpLedgeSide firstSide,
        IvpCollisionObject secondObject,
        IvpLedgeSide secondSide,
        IvpImpactEnvironment environment,
        IIvpMaterialManager materials,
        Func<IvpContactPoint, (IvpLedgeSide First, IvpLedgeSide Second)> sides,
        Action<IvpMindist> minimize,
        Action<IvpMindist> reschedule,
        double now)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(firstObject);
        ArgumentNullException.ThrowIfNull(firstSide);
        ArgumentNullException.ThrowIfNull(secondObject);
        ArgumentNullException.ThrowIfNull(secondSide);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(sides);

        IvpRigidBody firstCore = firstObject.Core ?? throw new InvalidOperationException("A collided mindist's first object has no core.");
        IvpRigidBody secondCore = secondObject.Core ?? throw new InvalidOperationException("A collided mindist's second object has no core.");

        BringToEvent(firstCore, now);
        BringToEvent(secondCore, now);
        environment.ImpactGeneration++;

        IvpContactPoint contact = IvpFrictionLinking.FindOrAllocate(mindist, firstObject, firstSide, secondObject, secondSide, now);
        IvpFrictionSystem system = IvpFrictionLinking.LinkContactByCore(contact, firstCore, secondCore, environment);
        contact.LastMeasured = now;

        IvpContactRecord record = IvpContactRecord.Build(
            contact,
            new IvpContactBody(firstSide, firstCore, firstObject.ExtraRadius),
            new IvpContactBody(secondSide, secondCore, secondObject.ExtraRadius),
            now);
        contact.SetMaterials(materials);

        IvpFrictionPair pair = system.PairFor(firstCore, secondCore)
            ?? throw new InvalidOperationException("A contact linked by core has no pair for its cores.");
        pair.LastImpact = now;

        IvpImpactSolver solver = IvpImpactSolver.Enter(environment, contact, [firstCore, secondCore], contact.PushOut(environment));

        (float X, float Y, float Z) relative = record.RelativeVelocity;

        if (secondCore.Immovable)
        {
            relative = (-relative.X, -relative.Y, -relative.Z);
        }

        new IvpImpactIsland(system).Build(environment, pair, contact, sides, materials, minimize, reschedule, now);

        record.RelativeVelocity = relative;

        return solver;

        static void BringToEvent(IvpRigidBody core, double time)
        {
            if (core.UnitState < 8 && !core.SkipsGravity)
            {
                core.RebuildMatrixAtEventTime(time);
            }
        }
    }
}
