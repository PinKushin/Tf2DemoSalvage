using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// Finding or building the persistent friction contact a collided mindist joins, and the friction system it belongs to —
/// <c>IvpContactPoint::Allocate</c> (<c>18008c4b0</c>) and <c>IvpFrictionSystem::LinkContactByCore</c> (<c>180090e50</c>)
/// (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly, both functions in full.** Not yet pinned by an oracle probe — see
/// `docs/HANDOFF.md`, item 3, for what remains before this is trusted running-path code.
/// </remarks>
public static class IvpFrictionLinking
{
    /// <summary>Finds or builds the contact point for an exact mindist — <c>IvpContactPoint::Allocate</c>.</summary>
    /// <param name="mindist">The mindist; must be exact (flags bits 18–21 read <c>0xc0000</c>).</param>
    /// <param name="recordZeroObject">Synapse record 0's object.</param>
    /// <param name="recordZeroSide">Synapse record 0's ledge side, freshly built for this collision.</param>
    /// <param name="recordOneObject">Synapse record 1's object.</param>
    /// <param name="recordOneSide">Synapse record 1's ledge side, freshly built for this collision.</param>
    /// <param name="now">The environment's time.</param>
    /// <returns>An existing contact reused for its warm-started state, or a freshly built one.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The mindist is not exact.</exception>
    /// <remarks>
    /// **Searches synapse record 0's object's own <see cref="IvpCollisionObject.ContactPoints"/> list** — the native walks
    /// exactly that list, and the constructor already links a new point to both objects, so either would find it. A match
    /// requires both objects (either order) and both features (<see cref="FeatureMatches"/>).
    /// </remarks>
    public static IvpContactPoint FindOrAllocate(
        IvpMindist mindist,
        IvpCollisionObject recordZeroObject,
        IvpLedgeSide recordZeroSide,
        IvpCollisionObject recordOneObject,
        IvpLedgeSide recordOneSide,
        double now)
    {
        ArgumentNullException.ThrowIfNull(mindist);
        ArgumentNullException.ThrowIfNull(recordZeroObject);
        ArgumentNullException.ThrowIfNull(recordZeroSide);
        ArgumentNullException.ThrowIfNull(recordOneObject);
        ArgumentNullException.ThrowIfNull(recordOneSide);

        if ((mindist.Flags & 0x3c0000) != 0xc0000)
        {
            throw new InvalidOperationException("A contact point is found or allocated for a mindist that is not exact.");
        }

        for (LinkedListNode<IvpContactPoint>? node = recordZeroObject.ContactPoints.First; node is not null; node = node.Next)
        {
            if (Matches(node.Value, mindist))
            {
                return node.Value;
            }
        }

        return new IvpContactPoint(mindist, recordZeroObject, recordZeroSide, recordOneObject, recordOneSide, now);
    }

    /// <summary>Whether an existing contact already names this mindist's two objects and features, either order — <c>FUN_1800869a0</c>.</summary>
    private static bool Matches(IvpContactPoint candidate, IvpMindist mindist)
    {
        (IvpCollisionObject first, IvpCollisionObject second) = mindist.Objects;
        IvpSynapse mindistFirst = mindist.Synapse(0);
        IvpSynapse mindistSecond = mindist.Synapse(1);

        if (ReferenceEquals(candidate.FirstObject, first) && ReferenceEquals(candidate.SecondObject, second) &&
            FeatureMatches(candidate.First, mindistFirst) && FeatureMatches(candidate.Second, mindistSecond))
        {
            return true;
        }

        return ReferenceEquals(candidate.FirstObject, second) && ReferenceEquals(candidate.SecondObject, first) &&
            FeatureMatches(candidate.First, mindistSecond) && FeatureMatches(candidate.Second, mindistFirst);
    }

    /// <summary>Whether two synapses name the same feature — <c>FUN_180086a50</c>.</summary>
    /// <remarks>
    /// **Simplified from the native's masked pointer comparison to this port's stable identity.** The native compares
    /// pointers with the low bits masked off, normalising between two representations of the same triangle or edge; an
    /// <see cref="IvpLedgeEdge"/> here already names one triangle and one slot as a value, so the same-triangle test this
    /// project needs is plain equality on <see cref="IvpLedgeEdge.Triangle"/> for every kind but a point, which the native
    /// additionally requires an exact edge match for.
    /// </remarks>
    private static bool FeatureMatches(IvpSynapse a, IvpSynapse b)
    {
        if (a.Kind != b.Kind)
        {
            return false;
        }

        return a.Kind switch
        {
            IvpFeatureKind.Ball => true,
            IvpFeatureKind.Point => a.Feature == b.Feature,
            _ => a.Feature.Triangle == b.Feature.Triangle,
        };
    }

    /// <summary>
    /// Finds or creates the friction system a contact joins, files it and its cores' pair — <c>IvpFrictionSystem::LinkContactByCore</c>.
    /// </summary>
    /// <param name="contact">The contact point, already found or allocated.</param>
    /// <param name="firstCore">Synapse record 0's core.</param>
    /// <param name="secondCore">Synapse record 1's core.</param>
    /// <param name="environment">The impact environment a freshly built system needs.</param>
    /// <returns>The system the contact now belongs to.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NotSupportedException">
    /// Both cores are movable, or both immovable — <c>FUN_180086240</c>'s system merge, needed only for body-against-body
    /// contact, is not ported (this project collides a body only with the world, per <see cref="IvpEnvironment.LookAheadObject"/>).
    /// </exception>
    /// <remarks>
    /// **The ordering swap the native makes is approximated by <see cref="IvpRigidBody.Immovable"/>**, not the single bit it
    /// tests alone (<c>core+0x0 &amp; 2</c>) — a divergence only for a core that is constraint-immune (bit <c>0x10</c>)
    /// without being truly immovable, which no TF2 ragdoll element produces. <c>IvpFrictionPair::Build</c>'s extra
    /// fields (a relative-velocity direction and per-core response coefficients) are deliberately not computed: this path
    /// never calls it natively either, and <see cref="IvpFrictionSystem"/>'s heap solve does not read them.
    ///
    /// **Links the contact only when it is not already filed in this system.** A reused contact from
    /// <see cref="FindOrAllocate"/> is already on the system's list from an earlier collision; linking it again would set
    /// its own <see cref="IvpContactPoint.Next"/> to itself — a one-node cycle that hangs the first walk of the list.
    /// Found by a hanging test, not read from the native, which never reaches this call for an already-linked point.
    /// </remarks>
    public static IvpFrictionSystem LinkContactByCore(
        IvpContactPoint contact, IvpRigidBody firstCore, IvpRigidBody secondCore, IvpImpactEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(firstCore);
        ArgumentNullException.ThrowIfNull(secondCore);
        ArgumentNullException.ThrowIfNull(environment);

        if (firstCore.Immovable == secondCore.Immovable)
        {
            throw new NotSupportedException(
                "LinkContactByCore is ported for exactly one movable and one immovable core - a body against the world, "
                + "which is this project's only implemented collision. FUN_180086240's system merge, needed for "
                + "body-against-body contact, is not ported.");
        }

        IvpRigidBody movable = firstCore.Immovable ? secondCore : firstCore;
        IvpRigidBody other = ReferenceEquals(movable, firstCore) ? secondCore : firstCore;

        IvpFrictionSystem system = movable.FrictionInfo?.System ?? new IvpFrictionSystem(environment);

        if (movable.FrictionInfo is null)
        {
            system.AddCore(movable);
        }

        if (other.FrictionInfoIn(system) is null)
        {
            system.AddCore(other);
        }

        if (system.PairFor(movable, other) is null)
        {
            system.AddPair(new IvpFrictionPair(movable, other));
        }

        if (!ReferenceEquals(contact.FrictionSystem, system))
        {
            system.Link(contact);
        }

        return system;
    }
}
