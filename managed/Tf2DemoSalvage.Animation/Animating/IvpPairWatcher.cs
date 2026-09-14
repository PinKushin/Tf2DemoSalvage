using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>IVP's default collision creator, <c>FUN_1800a0650</c> on table <c>1800fe6e8</c>: a watcher for each pair the broad phase finds (B369).</summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The watcher's three tables and the creator's* and *The rest of the watcher's
/// life*). Pinned by the `vphysics-pair-watcher` probe (`IvpPairWatcherConformanceTests`).
/// </remarks>
public sealed class IvpPairCreator : IIvpCollisionCreator
{
    /// <summary>Slot 0, <c>FUN_1800a0690</c>: a watcher going away comes off its first object's node, then its second's.</summary>
    /// <param name="collision">The watcher.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collision"/> is null.</exception>
    /// <exception cref="InvalidOperationException">An object has no node, where the engine reads its <c>+0xd8</c>.</exception>
    public void CollisionRemoved(IvpCollision collision)
    {
        ArgumentNullException.ThrowIfNull(collision);

        (IvpCollisionObject first, IvpCollisionObject second) = collision.Objects;
        IvpOvNode firstNode = NodeOf(first);
        IvpOvNode secondNode = NodeOf(second);

        firstNode.Unregister(collision);
        secondNode.Unregister(collision);
    }

    /// <summary>Slot 5, <c>FUN_1800a06f0</c>: a watcher made — its first refresh run — then registered on the first object's node and the second's.</summary>
    /// <param name="first">The object the broad phase ran for.</param>
    /// <param name="second">The object it found.</param>
    /// <returns>The watcher.</returns>
    /// <exception cref="ArgumentNullException">An object is null.</exception>
    /// <exception cref="InvalidOperationException">An object lacks what the watcher reads.</exception>
    public IvpCollision? Create(IvpCollisionObject first, IvpCollisionObject second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        IvpPairWatcher watcher = new(this, first, second);
        IvpOvNode firstNode = NodeOf(first);
        IvpOvNode secondNode = NodeOf(second);

        firstNode.Register(watcher);
        secondNode.Register(watcher);
        return watcher;
    }

    /// <summary>Slot 4, <c>FUN_1800a07a0</c>: every watcher on a leaving object's node deleted, last first, the count read once.</summary>
    /// <param name="removed">The object.</param>
    /// <exception cref="ArgumentNullException"><paramref name="removed"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The object has no node.</exception>
    public void ObjectRemoved(IvpCollisionObject removed)
    {
        ArgumentNullException.ThrowIfNull(removed);

        IReadOnlyList<IvpCollision> watchers = NodeOf(removed).Watchers;

        for (int index = watchers.Count - 1; index >= 0; index--)
        {
            watchers[index].Delete();
        }
    }

    private static IvpOvNode NodeOf(IvpCollisionObject collisionObject) =>
        collisionObject.Node ?? throw new InvalidOperationException("A pair watcher's object has no OV node.");
}

/// <summary>A broad-phase pair's watcher, the 0x78 bytes <c>FUN_1800b5dd0</c> builds: the pair's mindists, refreshed when a hull passes a record (B369).</summary>
/// <remarks>
/// **A pair is looked at again only when either object's hull passes the record filed for it**, at the range the range manager's slot 1
/// gave; the watcher holds the pair's mindists (<c>+0x68</c>) and is their delegator (<c>+0x20</c>, table <c>1800feb50</c>).
/// </remarks>
public sealed class IvpPairWatcher : IvpCollision, IIvpCollisionDelegator
{
    /// <summary><c>DAT_1800eb920</c>, <c>1e20f</c> by its bits: a new record's allowance, so it is refiled before it is ever told.</summary>
    private static readonly float NeverPassed = BitConverter.Int32BitsToSingle(0x60ad78ec);

    private readonly IIvpCollisionCreator _creator;
    private readonly IvpCollisionObject _first;
    private readonly IvpCollisionObject _second;
    private readonly List<IvpCollision> _pair = [];

    /// <summary>Builds a watcher — <c>FUN_1800b5dd0</c>: both records filed over now (<c>FUN_1800b61a0</c>), then the first refresh.</summary>
    internal IvpPairWatcher(IIvpCollisionCreator creator, IvpCollisionObject first, IvpCollisionObject second)
    {
        _creator = creator;
        _first = first;
        _second = second;
        FirstRecord = new IvpPairWatcherRecord(this);
        SecondRecord = new IvpPairWatcherRecord(this);
        first.Hull.InstallInFloat(FirstRecord, EnvironmentOf(first).Now, NeverPassed);
        second.Hull.InstallInFloat(SecondRecord, EnvironmentOf(second).Now, NeverPassed);
        Refresh();
    }

    /// <summary>The record filed in the first object's hull manager, <c>+0x28</c>.</summary>
    public IvpPairWatcherRecord FirstRecord { get; }

    /// <summary>The record filed in the second object's hull manager, <c>+0x48</c>.</summary>
    public IvpPairWatcherRecord SecondRecord { get; }

    /// <summary>The pair's mindists, <c>+0x68</c>, with their back-indices.</summary>
    public IReadOnlyList<IvpCollision> Pair => _pair;

    /// <inheritdoc/>
    public override (IvpCollisionObject First, IvpCollisionObject Second) Objects => (_first, _second);

    /// <summary>Refreshes the pair — <c>FUN_1800b6080</c>.</summary>
    /// <exception cref="InvalidOperationException">An object lacks what the refresh reads.</exception>
    /// <remarks>
    /// <code>
    /// env (first's) +0xbc += 1;  (rA, rB) = range slot 1(first, second);  FUN_180096680(first, second, rA + rB, pair, no ledges, this)
    /// first's record refiled over now with rA, then second's with rB (FUN_180099970)
    /// </code>
    /// </remarks>
    public void Refresh()
    {
        IvpCollisionEnvironment environment = EnvironmentOf(_first);

        environment.WatcherRefreshes++;

        (double firstRange, double secondRange) = IvpRangeManager.PairRange(
            IvpRangeManager.Bounds(CoreOf(_first)), IvpRangeManager.Bounds(CoreOf(_second)), environment.Step);

        IvpPairMindists.Refresh(_first, _second, IvpMath.Addsd(firstRange, secondRange), _pair, null, null, null, null, this);
        _first.Hull.Reinstall(FirstRecord, environment.Now, firstRange);
        _second.Hull.Reinstall(SecondRecord, environment.Now, secondRange);
    }

    /// <summary>The delegator's slot 0, <c>FUN_1800b5fd0</c>: a mindist out of the pair by its back-index.</summary>
    /// <param name="collision">The mindist.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collision"/> is null.</exception>
    public void CollisionRemoved(IvpCollision collision) => IvpCollisionList.Remove(_pair, collision);

    /// <summary>Deletes the watcher — <c>FUN_1800b5e80</c>.</summary>
    /// <remarks>
    /// Every mindist of the pair, last first, the count read once; the creator told (off both nodes); the second record out of its
    /// hull manager, then the first.
    /// </remarks>
    public override void Delete()
    {
        for (int index = _pair.Count - 1; index >= 0; index--)
        {
            _pair[index].Delete();
        }

        _creator.CollisionRemoved(this);
        _second.Hull.Remove(SecondRecord);
        _first.Hull.Remove(FirstRecord);
    }

    private static IvpCollisionEnvironment EnvironmentOf(IvpCollisionObject collisionObject) =>
        collisionObject.Environment ?? throw new InvalidOperationException("A pair watcher's object has no environment.");

    private static IvpRigidBody CoreOf(IvpCollisionObject collisionObject) =>
        collisionObject.Core ?? throw new InvalidOperationException("A pair watcher's object has no core.");
}

/// <summary>One of a watcher's two hull records, listener table <c>1800feb00</c> (B369).</summary>
/// <remarks>**Slot 2, which deletes the watcher when its manager goes away (<c>FUN_1800b6180</c>), is not carried**, as for a mindist's records.</remarks>
public sealed class IvpPairWatcherRecord : IIvpHullSynapse
{
    private readonly IvpPairWatcher _watcher;

    internal IvpPairWatcherRecord(IvpPairWatcher watcher) => _watcher = watcher;

    /// <inheritdoc/>
    public int? HullSlot { get; set; }

    /// <summary>Slot 1, <c>FUN_1800b6170</c>: the watcher refreshed.</summary>
    /// <param name="manager">Unread.</param>
    /// <param name="overshoot">Unread.</param>
    public void HullPassed(IvpHullManager manager, float overshoot) => _watcher.Refresh();

    /// <summary>Slot 3: nothing.</summary>
    /// <param name="valueShift">Unread.</param>
    /// <param name="centerShift">Unread.</param>
    public void Rebased(float valueShift, float centerShift)
    {
    }
}
