using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>An IVP collision — what an OV node's watcher list and a pair's vector hold (B369).</summary>
/// <remarks>
/// **The engine's `IVP_Collision` shape**: slot 0 deletes it, slot 2 names its two objects, and `+0x18`/`+0x1c` are the places it
/// holds in the two lists it is filed in, the first registration's in `+0x18` (`FUN_18009de20`). A broad-phase pair watcher and a
/// mindist both derive from it in the engine.
/// </remarks>
public abstract class IvpCollision
{
    /// <summary>Its place in the first list it was filed in, <c>+0x18</c>; −1 when none.</summary>
    public int FirstIndex { get; internal set; } = -1;

    /// <summary>Its place in the second list it was filed in, <c>+0x1c</c>; −1 when none.</summary>
    public int SecondIndex { get; internal set; } = -1;

    /// <summary>Its two objects — slot 2.</summary>
    public abstract (IvpCollisionObject First, IvpCollisionObject Second) Objects { get; }

    /// <summary>Deletes it — slot 0 with 1.</summary>
    public abstract void Delete();
}

/// <summary>A collision creator — an entry of <c>env+0x1d0</c>, the default one <c>FUN_1800a0650</c> (B369).</summary>
public interface IIvpCollisionCreator
{
    /// <summary>Slot 0: a collision it made is going away.</summary>
    /// <param name="collision">The collision.</param>
    public void CollisionRemoved(IvpCollision collision);

    /// <summary>Slot 5: a collision for a pair the broad phase found new, or null to leave it to the next creator.</summary>
    /// <param name="first">The object the broad phase ran for.</param>
    /// <param name="second">The object it found.</param>
    /// <returns>The collision made, or null.</returns>
    public IvpCollision? Create(IvpCollisionObject first, IvpCollisionObject second);

    /// <summary>Slot 4: an object's node is going away, so its collisions go too.</summary>
    /// <param name="removed">The object.</param>
    public void ObjectRemoved(IvpCollisionObject removed);
}

/// <summary>The fields of an <c>IVP_Environment</c> the broad phase reads and writes (B369).</summary>
/// <remarks>
/// **Named by the engine's offsets** (`docs/findings/51`, *What the environment's construction installs*). vphysics leaves
/// <see cref="RangeCallback"/> null and always installs a <see cref="Filter"/>.
/// </remarks>
public sealed class IvpCollisionEnvironment
{
    /// <summary>The mindist manager, <c>+0x20</c>, whose <c>+0x0</c> flag marks a pair creation running.</summary>
    public IvpMindistManager MindistManager { get; } = new();

    /// <summary>The OV tree, <c>+0x28</c>.</summary>
    public IvpOvTree OvTree { get; } = new();

    /// <summary>The pair filter, <c>+0x30</c>'s slot 0: whether two objects may collide.</summary>
    public required Func<IvpCollisionObject, IvpCollisionObject, bool> Filter { get; init; }

    /// <summary><c>+0x58</c>'s slot 0, handed an object, its node's centre and its range; null in vphysics.</summary>
    public Action<IvpCollisionObject, (float X, float Y, float Z), double>? RangeCallback { get; init; }

    /// <summary>The collision creators, <c>+0x1d0</c> (count <c>+0x1ca</c>), asked last first.</summary>
    public IList<IIvpCollisionCreator> Creators { get; } = [];

    /// <summary>How many times a pair watcher or an opened mindist has asked its pair's ranges again, <c>+0xbc</c>.</summary>
    public int WatcherRefreshes { get; set; }

    /// <summary>How many times the broad phase has run, <c>+0xc0</c>.</summary>
    public int BroadPhaseRuns { get; set; }

    /// <summary>The PSI step, <c>+0x108</c>.</summary>
    public double Step { get; set; }

    /// <summary>The environment's time, <c>+0x188</c>.</summary>
    public double Now { get; set; }

    /// <summary>The PSI count, <c>+0x1a0</c>, which an object cache is refreshed against.</summary>
    public int Psi { get; set; }

    /// <summary>How many mindists are alive, <c>+0xb0</c>.</summary>
    public int LiveMindists { get; set; }

    /// <summary>How many mindists have been made, <c>+0xb4</c>.</summary>
    public int CreatedMindists { get; set; }

    /// <summary>How many mindists have been deleted, <c>+0xb8</c>.</summary>
    public int DeletedMindists { get; set; }

    /// <summary>The time manager's queue, which an exact mindist leaves when it is unlinked.</summary>
    public IvpMinList<IvpMindist> EventQueue { get; } = new();

    /// <summary>What a new mindist of two objects without phantoms becomes — <c>FUN_1800977f0(env+0x20, m)</c>.</summary>
    public Action<IvpMindist>? BecomeExact { get; init; }

    /// <summary>What a new mindist with a phantom object becomes — <c>FUN_180097940(env+0x20, m)</c>.</summary>
    public Action<IvpMindist>? BecomePhantom { get; init; }
}

/// <summary>IVP's broad phase: an object's sphere refiled in the OV tree, and its collisions kept, dropped and made (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The broad phase*): `FUN_180098880`, its inline copy `FUN_180096eb0`, the
/// partner table `FUN_1800962c0`, the node's destructor `FUN_18009dae0` and the creators' removal notice `FUN_1800821d0`. Pinned by
/// the `vphysics-broad-phase` probe (`IvpBroadPhaseConformanceTests`).
/// </remarks>
public static class IvpBroadPhase
{
    private const int StateBits = 7;
    private const int SkipsOthers = 0x200;
    private const int SkipsFilterWith = 0x400;

    /// <summary><c>DAT_1800f4f20</c>, the gap a node is filed with while a pair creation runs: <c>0x3bfd83c94fb6d2ac</c>.</summary>
    private static readonly double CreatingGap = BitConverter.Int64BitsToDouble(0x3bfd83c94fb6d2ac);

    /// <summary>Refiles an object and its collisions — <c>FUN_180098880(env+0x20, object)</c>.</summary>
    /// <param name="environment">The object's environment.</param>
    /// <param name="collisionObject">The object; one without a node is left alone.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidOperationException">The object has a node but no core, or a found node has no object.</exception>
    /// <remarks>
    /// <code>
    /// node out of the tree;  env+0xc0 += 1;  centre = (float)core+0xf0..0x100
    /// a pair creation running → insert with (double)core+0x4 twice and no list;  file the node with 1e-19;  return
    /// r = (double)core+0x4 + range slot 2(object);  env+0x58 and object+0x78 &amp; 7 → the flag set around its slot 0(object, &amp;centre, r)
    /// g = insert(node, r, r, found);  file the node with g − (double)core+0x4
    /// every found node, last first — other = its object:
    ///     both states &amp; 7 clear, the same friction core, both cores &amp; 0x12, or object bit 9 with other bit 9 → skip
    ///     object bit 10 without 9 and other bit 9, or object bit 9 and other bit 10 → kept without the filter; else the filter
    ///     a collision on the node naming other → swapped into the kept prefix;  none → other is new
    /// every collision past the kept prefix, last first: deleted;  every new other, last first: creators, last first, until one makes one
    /// </code>
    /// </remarks>
    public static void Refile(IvpCollisionEnvironment environment, IvpCollisionObject collisionObject)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(collisionObject);

        if (collisionObject.Node is not { } node)
        {
            return;
        }

        IvpRigidBody core = collisionObject.Core ?? throw new InvalidOperationException("An object with a node has no core.");

        environment.OvTree.Remove(node);
        environment.BroadPhaseRuns++;

        (double X, double Y, double Z) position = core.CoreMatrix.Translation;
        node.Center = ((float)position.X, (float)position.Y, (float)position.Z);

        double radius = core.Radius;

        if (environment.MindistManager.CreatingPairs)
        {
            environment.OvTree.Insert(node, radius, radius, null);
            node.File(collisionObject.Hull, environment.Now, CreatingGap);
            return;
        }

        double range = IvpMath.Addsd(
            radius, IvpRangeManager.ObjectRange(IvpRangeManager.Bounds(core), environment.Step));

        if (environment.RangeCallback is { } callback && (collisionObject.MovementState & StateBits) != 0)
        {
            environment.MindistManager.CreatingPairs = true;
            callback(collisionObject, node.Center, range);
            environment.MindistManager.CreatingPairs = false;
        }

        List<IvpOvNode> found = [];
        double used = environment.OvTree.Insert(node, range, range, found);

        node.File(collisionObject.Hull, environment.Now, used - radius);

        int flags = collisionObject.MovementState;
        bool skipsOthers = (flags & SkipsOthers) != 0;
        bool filterWithOthers = !skipsOthers && (flags & SkipsFilterWith) != 0;
        bool fixedCore = Fixed(core);
        int kept = 0;
        List<IvpCollisionObject> fresh = [];

        for (int index = found.Count - 1; index >= 0; index--)
        {
            IvpCollisionObject other = found[index].Owner ?? throw new InvalidOperationException("An OV node without an object was found.");
            int otherFlags = other.MovementState;

            if (((flags & StateBits) == 0 && (otherFlags & StateBits) == 0) ||
                ReferenceEquals(collisionObject.FrictionCore, other.FrictionCore) ||
                (fixedCore && other.Core is { } otherCore && Fixed(otherCore)) ||
                (skipsOthers && (otherFlags & SkipsOthers) != 0))
            {
                continue;
            }

            bool unfiltered = (filterWithOthers && (otherFlags & SkipsOthers) != 0) || (skipsOthers && (otherFlags & SkipsFilterWith) != 0);

            if (!unfiltered && !environment.Filter(collisionObject, other))
            {
                continue;
            }

            if (!Keep(node, other, ref kept))
            {
                fresh.Add(other);
            }
        }

        for (int index = node.Watchers.Count - 1; index >= kept; index--)
        {
            node.Watchers[index].Delete();
        }

        for (int index = fresh.Count - 1; index >= 0; index--)
        {
            for (int creator = environment.Creators.Count - 1; creator >= 0; creator--)
            {
                if (environment.Creators[creator].Create(collisionObject, fresh[index]) is not null)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Gives an object a new node and files it — <c>FUN_180096eb0(env+0x20, object)</c>.</summary>
    /// <param name="environment">The object's environment.</param>
    /// <param name="collisionObject">The object; its old node, if any, is deleted first.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Rebuild(IvpCollisionEnvironment environment, IvpCollisionObject collisionObject)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(collisionObject);

        if (collisionObject.Node is { } old)
        {
            Delete(environment, old);
        }

        collisionObject.Node = new IvpOvNode(collisionObject);
        Refile(environment, collisionObject);
    }

    /// <summary>Deletes a node — its slot 4, the destructor <c>FUN_18009dae0</c>.</summary>
    /// <param name="environment">Its object's environment.</param>
    /// <param name="node">The node.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// Out of its hull manager; every creator, last first, told its object is going (`FUN_1800821d0`); out of the tree with every
    /// cell it empties; its watcher list emptied.
    /// </remarks>
    public static void Delete(IvpCollisionEnvironment environment, IvpOvNode node)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(node);

        node.Unfile();

        if (node.Owner is { } owner)
        {
            for (int creator = environment.Creators.Count - 1; creator >= 0; creator--)
            {
                environment.Creators[creator].ObjectRemoved(owner);
            }

            if (ReferenceEquals(owner.Node, node))
            {
                owner.Node = null;
            }
        }

        environment.OvTree.Remove(node);
        node.ClearWatchers();
    }

    /// <summary>Rewrites whichever of a collision's two indices is <paramref name="from"/> — the first when it is.</summary>
    internal static void SetIndex(IvpCollision collision, int from, int to)
    {
        if (collision.FirstIndex == from)
        {
            collision.FirstIndex = to;
        }
        else
        {
            collision.SecondIndex = to;
        }
    }

    /// <summary><c>core+0x0 &amp; 0x12</c>.</summary>
    private static bool Fixed(IvpRigidBody core) => core.Immovable || core.SkipsGravity;

    /// <summary>
    /// The partner table's lookup — <c>FUN_1800962c0</c>: a collision on the node naming the other object, swapped into the kept prefix.
    /// </summary>
    /// <remarks>
    /// The table is keyed by the other object and holds each collision once, first inserted from the list's end, so its lookup finds
    /// the collision at the highest place that names the other. **A found collision past the kept count swaps with the one at it**,
    /// each rewriting whichever of its two indices named its old place.
    /// </remarks>
    private static bool Keep(IvpOvNode node, IvpCollisionObject other, ref int kept)
    {
        IList<IvpCollision> watchers = node.WatcherList;
        int found = -1;

        for (int index = watchers.Count - 1; index >= 0; index--)
        {
            (IvpCollisionObject first, IvpCollisionObject second) = watchers[index].Objects;

            if (ReferenceEquals(first, other) || ReferenceEquals(second, other))
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
            IvpCollision moved = watchers[kept];
            IvpCollision keep = watchers[found];

            watchers[kept] = keep;
            watchers[found] = moved;
            SetIndex(moved, kept, found);
            SetIndex(keep, found, kept);
        }

        kept++;
        return true;
    }
}
