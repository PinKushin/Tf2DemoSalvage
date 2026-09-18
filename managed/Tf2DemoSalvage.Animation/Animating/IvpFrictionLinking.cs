using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

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
        PhysicsLedge? firstLedge = mindist.Ledge(0)?.Ledge;
        PhysicsLedge? secondLedge = mindist.Ledge(1)?.Ledge;

        if (ReferenceEquals(candidate.FirstObject, first) && ReferenceEquals(candidate.SecondObject, second) &&
            FeatureMatches(candidate.First, candidate.FirstLedge, candidate.FirstTopology, mindistFirst, firstLedge) &&
            FeatureMatches(candidate.Second, candidate.SecondLedge, candidate.SecondTopology, mindistSecond, secondLedge))
        {
            return true;
        }

        return ReferenceEquals(candidate.FirstObject, second) && ReferenceEquals(candidate.SecondObject, first) &&
            FeatureMatches(candidate.Second, candidate.SecondLedge, candidate.SecondTopology, mindistFirst, firstLedge) &&
            FeatureMatches(candidate.First, candidate.FirstLedge, candidate.FirstTopology, mindistSecond, secondLedge);
    }

    /// <summary>Whether a contact's synapse names a mindist's feature — <c>FUN_180086a50(cp synapse, mindist synapse)</c>.</summary>
    /// <remarks>
    /// <code>
    /// kinds differ → 0
    /// 0, a point:     the same ledge, and the two edge words' low sixteen bits — the point they start at — equal
    /// 1, an edge:     the same edge, or the contact's edge's twin (cp + ((int)(*cp · 2) &gt;&gt; 17) words) is the mindist's
    /// 2, a triangle:  the two edges' addresses equal but for their low four bits — the same triangle
    /// 3, a ball:      1;   anything else asserts (ivp_friction.cxx:0x90a)
    /// </code>
    /// **A same-triangle test stood here for an edge, and an exact edge for a point.** The first joined the two corners of a
    /// face–face overlap — edge pairs on the same two triangles in other slots — into one contact, so the second corner was solved
    /// at the first one's arms and two boxes driven together passed through each other (B369). The binary makes two.
    /// </remarks>
    private static bool FeatureMatches(
        IvpSynapse contact, PhysicsLedge? contactLedge, IvpLedgeTopology topology, IvpSynapse mindist, PhysicsLedge? mindistLedge)
    {
        if (contact.Kind != mindist.Kind)
        {
            return false;
        }

        if (contact.Kind == IvpFeatureKind.Ball)
        {
            return true;
        }

        if (!Equals(contactLedge, mindistLedge))
        {
            return false;
        }

        return contact.Kind switch
        {
            IvpFeatureKind.Point => topology.Start(contact.Feature) == topology.Start(mindist.Feature),
            IvpFeatureKind.Edge => contact.Feature == mindist.Feature || topology.Hop(contact.Feature) == mindist.Feature,
            IvpFeatureKind.Triangle => contact.Feature.Triangle == mindist.Feature.Triangle,
            _ => throw new InvalidOperationException("A contact names a feature kind the engine asserts on (ivp_friction.cxx:0x90a)."),
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
    /// <exception cref="NotSupportedException">Both cores are immovable, a pair the engine's own filter never collides.</exception>
    /// <remarks>
    /// <code>
    /// M = the core not unmovable, the first when both or neither;  S = the other
    /// M has a system T:   S's share in T → file;  S movable in a system of its own → merge it into T → file;  else S joins T
    /// M has none:         S movable in a system T → M joins T;  else a new T, M joins, S joins
    /// file:  cp into T and its pair;  cp onto both shares;  unless either is unmovable or they share one, their units merge
    /// </code>
    /// </remarks>
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

        if (firstCore.Immovable && secondCore.Immovable)
        {
            throw new NotSupportedException("Two immovable cores are filed into a friction system, which the engine's filter never collides.");
        }

        IvpRigidBody movable = firstCore.Immovable ? secondCore : firstCore;
        IvpRigidBody other = ReferenceEquals(movable, firstCore) ? secondCore : firstCore;

        IvpFrictionSystem system;

        if (movable.FrictionInfo is { } info)
        {
            system = info.System;

            if (other.FrictionInfoIn(system) is null)
            {
                if (!other.Immovable && other.FrictionInfo is { } own)
                {
                    system.Merge(own.System);
                }
                else
                {
                    system.AddCore(other);
                }
            }
        }
        else if (!other.Immovable && other.FrictionInfo is { } theirs)
        {
            system = theirs.System;
            system.AddCore(movable);
        }
        else
        {
            system = new IvpFrictionSystem(environment);
            system.AddCore(movable);
            system.AddCore(other);
        }

        system.FileInPair(contact, movable, other);

        // **Onto each core's own share too** — `FUN_180054640`, "cp onto both records' contact vectors" (findings 51,
        // *Filing a contact into a friction system*). This is what `FUN_180083e40`'s per-core removal (`FUN_180075130`)
        // is the inverse of; without it a core's share never held the contacts the removal walks. Guarded like the pair,
        // for a reused contact re-filed on a later collision.
        FileOnCore(movable.FrictionInfoIn(system), contact);
        FileOnCore(other.FrictionInfoIn(system), contact);

        if (!ReferenceEquals(contact.FrictionSystem, system))
        {
            system.Link(contact);
        }

        // `FUN_180074e40`: two movable cores in one friction system are simulated as one unit.
        if (!movable.Immovable && !other.Immovable && !ReferenceEquals(movable.Unit, other.Unit))
        {
            environment.MergeUnits?.Invoke(movable, other);
        }

        return system;
    }

    /// <summary>Files a contact onto one core's share of the system, once — half of <c>FUN_180054640</c>.</summary>
    private static void FileOnCore(IvpFrictionInfo? share, IvpContactPoint contact)
    {
        if (share is { } info && !info.Contacts.Contains(contact))
        {
            info.Contacts.Add(contact);
        }
    }
}
