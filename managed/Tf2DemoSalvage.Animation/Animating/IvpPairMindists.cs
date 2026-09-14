using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An object's surface as the pair creation asks it — its manager at <c>object+0xc8</c> (B369).</summary>
public interface IIvpSurfaceManager
{
    /// <summary>Slot 4: every ledge within a radius of a point in the object's frame, appended in the manager's order.</summary>
    /// <param name="center">The point, in the object's frame.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="root">A hull ledge's node to query beneath, or null for the whole surface.</param>
    /// <param name="into">Where the ledges go.</param>
    public void LedgesWithin((double X, double Y, double Z) center, double radius, PhysicsLedgeTreeNode? root, ICollection<PhysicsLedgeTreeNode> into);
}

/// <summary>The polygon surface manager, table <c>1800eae60</c>, over a compact surface's ledge tree (B369).</summary>
/// <param name="Tree">The surface's ledge tree.</param>
public sealed record IvpPolygonSurfaceManager(PhysicsLedgeTree Tree) : IIvpSurfaceManager
{
    /// <inheritdoc/>
    public void LedgesWithin((double X, double Y, double Z) center, double radius, PhysicsLedgeTreeNode? root, ICollection<PhysicsLedgeTreeNode> into) =>
        IvpLedgeTree.LedgesWithin(Tree, root, center, radius, into);
}

/// <summary>What a collision's delegator is told — slot 0 of the table at a watcher's <c>+0x20</c> (B369).</summary>
public interface IIvpCollisionDelegator
{
    /// <summary>Slot 0: a collision it holds is going away.</summary>
    /// <param name="collision">The collision.</param>
    public void CollisionRemoved(IvpCollision collision);
}

/// <summary>A list of collisions kept with their back-indices, as IVP's node, pair and recursive vectors keep them (B369).</summary>
public static class IvpCollisionList
{
    /// <summary>Takes a collision out — <c>FUN_18009ef40</c>, and its twins <c>FUN_1800b5fd0</c> and <c>FUN_1800b2320</c>.</summary>
    /// <param name="list">The list.</param>
    /// <param name="collision">The collision.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Its place is its first index when that is in range and names it here, else its second; the last entry moves into the place,
    /// rewriting whichever of its indices named the last place; then the collision's index that names the place becomes −1, the second
    /// when the first does not.
    /// </remarks>
    public static void Remove(IList<IvpCollision> list, IvpCollision collision)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(collision);

        int place = collision.FirstIndex >= 0 && collision.FirstIndex < list.Count && ReferenceEquals(list[collision.FirstIndex], collision)
            ? collision.FirstIndex
            : collision.SecondIndex;
        int last = list.Count - 1;

        if (last > place)
        {
            IvpCollision moved = list[last];

            list[place] = moved;
            IvpBroadPhase.SetIndex(moved, last, place);
        }

        list.RemoveAt(last);

        if (collision.FirstIndex == place)
        {
            collision.FirstIndex = -1;
        }
        else
        {
            collision.SecondIndex = -1;
        }
    }

    /// <summary>Appends a collision — <c>FUN_18009de20</c>: its first index taken when free, else its second.</summary>
    /// <param name="list">The list.</param>
    /// <param name="collision">The collision.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Add(IList<IvpCollision> list, IvpCollision collision)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(collision);

        list.Add(collision);

        if (collision.FirstIndex == -1)
        {
            collision.FirstIndex = list.Count - 1;
        }
        else
        {
            collision.SecondIndex = list.Count - 1;
        }
    }
}

/// <summary>A pair's mindists kept, dropped and made — <c>FUN_180096680</c>, with the constructor <c>FUN_1800975d0</c> (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The pair's mindists, instruction by instruction*). Each side's ledges are those
/// of its object within the other object's extrapolated core sphere, grown by the gap; every pair of them keeps its mindist or gets a
/// new one; every mindist no pair named is deleted. Pinned by the `vphysics-pair-mindists` probe (`IvpPairMindistsConformanceTests`).
/// </remarks>
public static class IvpPairMindists
{
    private const int BaseFlags = 0x0fc00000;
    private const int PhantomClears = 0x2000;
    private const int PhantomSets = 0x1000;

    /// <summary>Refreshes a pair's mindists — <c>FUN_180096680</c>.</summary>
    /// <param name="first">The first object.</param>
    /// <param name="second">The second object.</param>
    /// <param name="gap">How far past the objects' surfaces a ledge may be.</param>
    /// <param name="pair">The pair's mindists, with their back-indices.</param>
    /// <param name="firstLedge">A ledge standing for the first side, or null to query its surface.</param>
    /// <param name="secondLedge">A ledge standing for the second side, or null to query its surface.</param>
    /// <param name="firstRoot">A hull ledge's node the first side is queried beneath, or null.</param>
    /// <param name="secondRoot">A hull ledge's node the second side is queried beneath, or null.</param>
    /// <param name="delegator">What each new mindist tells when it goes.</param>
    /// <exception cref="ArgumentNullException">An object or the pair is null.</exception>
    /// <exception cref="InvalidOperationException">An object lacks what the pair creation reads, or a ledge has children.</exception>
    /// <remarks>
    /// <code>
    /// side A:  a ledge → it alone;  else B's core moved to now over dt = (float)(now − +0x1d0), (double)v·(double)dt + p per lane;
    ///     r = (double)(A+0xe0 + coreB+0x4) + gap;  the point into A's frame through A's object cache;  A's slot 4(point, r, rootA)
    /// side B:  the same, the objects swapped
    /// A's ledges last first, B's last first:  a mindist naming both → into the kept prefix;  else a new one (FUN_1800975d0)
    /// every mindist past the kept prefix, last first, deleted;  every new one, last first, appended with its back-index
    /// </code>
    /// </remarks>
    public static void Refresh(
        IvpCollisionObject first, IvpCollisionObject second, double gap, IList<IvpCollision> pair,
        PhysicsLedgeTreeNode? firstLedge, PhysicsLedgeTreeNode? secondLedge, PhysicsLedgeTreeNode? firstRoot, PhysicsLedgeTreeNode? secondRoot,
        IIvpCollisionDelegator? delegator)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(pair);

        IvpCollisionEnvironment environment = first.Environment ?? throw new InvalidOperationException("A pair's first object has no environment.");
        List<PhysicsLedgeTreeNode> firstLedges = Side(environment, first, second, gap, firstLedge, firstRoot);
        List<PhysicsLedgeTreeNode> secondLedges = Side(environment, second, first, gap, secondLedge, secondRoot);
        List<IvpMindist> fresh = [];
        int kept = 0;

        for (int a = firstLedges.Count - 1; a >= 0; a--)
        {
            for (int b = secondLedges.Count - 1; b >= 0; b--)
            {
                if (!Keep(pair, firstLedges[a], secondLedges[b], ref kept))
                {
                    fresh.Add(Construct(environment, first, second, firstLedges[a], secondLedges[b], delegator));
                }
            }
        }

        for (int index = pair.Count - 1; index >= kept; index--)
        {
            pair[index].Delete();
        }

        for (int index = fresh.Count - 1; index >= 0; index--)
        {
            IvpCollisionList.Add(pair, fresh[index]);
        }
    }

    /// <summary>One side's ledges — the first half of <c>FUN_180096680</c>.</summary>
    private static List<PhysicsLedgeTreeNode> Side(
        IvpCollisionEnvironment environment, IvpCollisionObject self, IvpCollisionObject other, double gap, PhysicsLedgeTreeNode? ledge, PhysicsLedgeTreeNode? root)
    {
        if (ledge is not null)
        {
            return [ledge];
        }

        IvpRigidBody core = other.Core ?? throw new InvalidOperationException("A pair's object has no core.");
        IIvpSurfaceManager surface = self.Surface ?? throw new InvalidOperationException("A pair's object has no surface manager.");
        double elapsed = (float)(environment.Now - core.LastStepped);
        (double X, double Y, double Z) point = (
            IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.X, elapsed), core.Position.X),
            IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.Y, elapsed), core.Position.Y),
            IvpMath.Addsd(IvpMath.Mulsd(core.PreviousVelocity.Z, elapsed), core.Position.Z));
        double radius = IvpMath.Addsd(IvpMath.Addss(self.ExtraRadius, core.Radius), gap);
        IvpMatrix matrix = self.CacheFor(environment).Matrix;
        double x = point.X - matrix.Translation.X;
        double y = point.Y - matrix.Translation.Y;
        double z = point.Z - matrix.Translation.Z;
        (double X, double Y, double Z) local = (
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, matrix.M4), IvpMath.Mulsd(x, matrix.M0)), IvpMath.Mulsd(z, matrix.M8)),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, matrix.M5), IvpMath.Mulsd(x, matrix.M1)), IvpMath.Mulsd(z, matrix.M9)),
            IvpMath.Addsd(IvpMath.Addsd(IvpMath.Mulsd(y, matrix.M6), IvpMath.Mulsd(x, matrix.M2)), IvpMath.Mulsd(z, matrix.M10)));
        List<PhysicsLedgeTreeNode> ledges = [];

        surface.LedgesWithin(local, radius, root, ledges);
        return ledges;
    }

    /// <summary>The table's lookup — a mindist whose two ledges are these, swapped into the kept prefix.</summary>
    private static bool Keep(IList<IvpCollision> pair, PhysicsLedgeTreeNode first, PhysicsLedgeTreeNode second, ref int kept)
    {
        int found = -1;

        for (int index = pair.Count - 1; index >= 0; index--)
        {
            if (pair[index] is IvpMindist mindist && ReferenceEquals(mindist.Ledge(0), first) && ReferenceEquals(mindist.Ledge(1), second))
            {
                found = index;
                break;
            }
        }

        if (found < 0)
        {
            return false;
        }

        if (found > kept)
        {
            IvpCollision moved = pair[kept];
            IvpCollision keep = pair[found];

            pair[kept] = keep;
            pair[found] = moved;
            IvpBroadPhase.SetIndex(moved, kept, found);
            IvpBroadPhase.SetIndex(keep, found, kept);
        }

        kept++;
        return true;
    }

    /// <summary>A new plain mindist — the base constructor's writes, then <c>FUN_1800975d0</c>.</summary>
    private static IvpMindist Construct(
        IvpCollisionEnvironment environment, IvpCollisionObject first, IvpCollisionObject second, PhysicsLedgeTreeNode firstLedge,
        PhysicsLedgeTreeNode secondLedge, IIvpCollisionDelegator? delegator)
    {
        if ((firstLedge.LedgeChildren | secondLedge.LedgeChildren) != 0)
        {
            throw new InvalidOperationException("A ledge with children makes the larger mindist FUN_1800b21f0, which is not ported.");
        }

        IvpMindist mindist = new(
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
            IvpMath.Addss(first.ExtraRadius, second.ExtraRadius))
        {
            Flags = BaseFlags,
            Delegator = delegator,
        };

        environment.LiveMindists++;
        environment.CreatedMindists++;
        mindist.Attach(first, firstLedge, second, secondLedge);

        if (first.HasPhantom || second.HasPhantom)
        {
            mindist.Flags = (mindist.Flags & ~PhantomClears) | PhantomSets;
            environment.BecomePhantom?.Invoke(mindist);
        }
        else
        {
            environment.BecomeExact?.Invoke(mindist);
        }

        return mindist;
    }
}
