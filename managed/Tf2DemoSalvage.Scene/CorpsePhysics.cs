using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>One corpse to bring up to a tick — what <see cref="CorpsePhysics.Advance"/> is handed for each corpse in the moment.</summary>
/// <param name="EntityIndex">The index the corpse is drawn under.</param>
/// <param name="Ragdoll">The model's bodies and joints.</param>
/// <param name="Entity">The animating entity whose bones the simulation will drive.</param>
/// <param name="BornAt">The tick this corpse died, or null when the timeline did not record it.</param>
/// <param name="Force">The killing blow — <c>m_vecForce</c>, an impulse in kg·in/s.</param>
/// <param name="ForceBone">Which body it landed on — <c>m_nForceBone</c>.</param>
/// <param name="Velocity">What the corpse was already carrying — <c>m_vecRagdollVelocity</c>; for a gib, its own throw.</param>
/// <param name="Gib">
/// Whether this is a gib — a physics prop made by <c>BreakModelCreateSingle</c> and thrown with <c>AddVelocity</c> — rather than a
/// corpse (B409).
/// </param>
/// <param name="Spin">A gib's angular impulse, degrees a second about its own axes; unused for a corpse.</param>
/// <param name="Spawn">
/// Where a gib is created — <c>CreateGibsFromList</c>'s position and angles, the corpse's origin and render yaw. A gib is a BAKED
/// model with no skeleton to seed from, so it starts here; unused for a corpse.
/// </param>
/// <remarks><paramref name="Entity"/> is null for a gib drawn as a baked model — it has no animating entity to hook a pose to.</remarks>
public readonly record struct CorpseRequest(
    int EntityIndex,
    RagdollBody Ragdoll,
    AnimatingEntity? Entity,
    int? BornAt,
    (float X, float Y, float Z)? Force,
    int? ForceBone,
    (float X, float Y, float Z)? Velocity,
    bool Gib = false,
    (float X, float Y, float Z)? Spin = null,
    (Vector3 Origin, float Yaw)? Spawn = null);

/// <summary>
/// The client's physics environment for corpses — one <see cref="IvpRagdollWorld"/> holding the map and every corpse, stepped by the
/// demo's clock (B58, D146, D172, D179).
/// </summary>
/// <remarks>
/// **One environment, as the engine keeps one** (`physenv`, `game/client/physics.cpp`): the map's collide and every client ragdoll in
/// it, stepped once per tick. Some of its state is environment-wide — the margin-decay look counter at `env+0x13c`, the random
/// stream, the time code — so a corpse's path depends on the others that shared the world with it, as the engine's does.
///
/// **A corpse is the one drawn thing whose pose depends on its own past**, so a stepped timeline and a freshly built one cannot agree
/// exactly. **The rule (D179, the owner's choice):** the world steps forward with the demo, and anything it cannot reach by stepping
/// forward — a seek backwards, or a corpse whose death is behind the world's tick — rebuilds the world and replays it from the
/// earliest death among the corpses in the moment. A corpse that has already gone is not replayed, so a rewind can differ from
/// straight-through play by what a departed corpse did to the shared state.
///
/// **Seeded from the ANIMATED pose, which is what the engine does.** `InitAsClientRagdoll` poses the entity from its death animation
/// and hands those bone matrices to the physics — `c_baseanimating.cpp:4931` copies the sequence and then zeroes the playback rate.
/// </remarks>
public sealed class CorpsePhysics
{
    private readonly Dictionary<int, (IvpRagdoll Ragdoll, int Born)> _running = [];
    private readonly Dictionary<int, Vector3> _roots = [];
    private readonly Dictionary<int, int> _contacts = [];
    private readonly Dictionary<int, int> _born = [];
    private readonly Dictionary<int, Vector3> _seeded = [];
    private readonly Dictionary<int, (float X, float Y, float Z)> _blows = [];
    private readonly Dictionary<int, (int Tick, int Contacts, (double X, double Y, double Z) At)> _fell = [];
    private readonly Dictionary<int, (double X, double Y, double Z)> _touched = [];
    private IvpRagdollWorld? _world;
    private int _worldTick;

    /// <summary>How far below the highest place it touched a corpse's root must be to have left the world, in Source units.</summary>
    /// <remarks>
    /// **Relative to what the corpse stood on, not a world height** (B306): a fixed height asks about the map. A body sixty-four units
    /// below the highest surface it touched has passed through it and is not coming back.
    /// </remarks>
    private const double FallenThrough = -64d;

    /// <summary>Builds the world a corpse falls onto, for a tick interval — the map's collide loaded into a fresh environment.</summary>
    /// <remarks>
    /// **Called on every rebuild**, so it must build the whole environment, map included. Null gives an empty world with
    /// <see cref="Surfaces"/> and <c>sv_gravity</c>'s default — no map, which is a working state for a viewer without one.
    /// </remarks>
    public Func<float, IvpRagdollWorld>? CreateWorld { get; set; }

    /// <summary>The game's surfaces, for the friction a corpse collides with, when <see cref="CreateWorld"/> is not set.</summary>
    public VphysicsSurfaceProps Surfaces { get; set; } = new([]);

    /// <summary>Every corpse simulated once in the background (D181), or null for none — read first, and the world runs only past it.</summary>
    public CorpseRecord? Record { get; set; }

    /// <summary>The environment, or null before a corpse needed one — for instruments.</summary>
    public IvpRagdollWorld? Physics => _world;

    /// <summary>How many corpses the environment holds.</summary>
    public int Count => _running.Count;

    /// <summary>How many times the environment was rebuilt because a tick could not be reached forwards.</summary>
    public int Rebuilds { get; private set; }

    /// <summary>How many ticks the environment has been stepped, across rebuilds.</summary>
    public int Steps { get; private set; }

    /// <summary>Stopwatch ticks spent stepping — the one unbounded cost here, since a rebuild replays every tick since a death.</summary>
    public long SteppingTicks { get; private set; }

    /// <summary>The same, in seconds.</summary>
    public double SteppingSeconds => SteppingTicks / (double)System.Diagnostics.Stopwatch.Frequency;

    /// <summary>Stopwatch ticks spent rebuilding — creating the world with its map and seeding the corpses born at its first tick.</summary>
    public long BuildingTicks { get; private set; }

    /// <summary>Where the simulation has put each corpse's root body, by entity index — carried out of the solver (B243).</summary>
    public IReadOnlyDictionary<int, Vector3> Roots => _roots;

    /// <summary>How many friction contacts hold each corpse, by entity index.</summary>
    public IReadOnlyDictionary<int, int> Contacts => _contacts;

    /// <summary>The tick each corpse was seeded at — its death, when the timeline records one.</summary>
    public IReadOnlyDictionary<int, int> Born => _born;

    /// <summary>Where each corpse's root body was placed when it was seeded.</summary>
    public IReadOnlyDictionary<int, Vector3> Seeded => _seeded;

    /// <summary>How hard each corpse was hit — <c>m_vecForce</c>, the vector the seed used.</summary>
    public IReadOnlyDictionary<int, (float X, float Y, float Z)> Blows => _blows;

    /// <summary>For each corpse that left the world, when, with how many contacts, and the highest place it had touched.</summary>
    public IReadOnlyDictionary<int, (int Tick, int Contacts, (double X, double Y, double Z) At)> Fell => _fell;

    /// <summary>Forgets the environment — a new demo, or a map change.</summary>
    public void Clear()
    {
        // A record was made against the old map and the old demo's corpses; kept, it would pose this one's from them.
        Record = null;
        _world = null;
        _running.Clear();
        _roots.Clear();
        _placements.Clear();
    }

    /// <summary>Brings the environment up to a tick, with every corpse in the moment in it, and attaches each one's pose.</summary>
    /// <param name="corpses">Every corpse the moment carries, seen or not.</param>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="seconds">Playback time, for the pose that seeds a new corpse.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corpses"/> is null.</exception>
    /// <remarks>
    /// **The step count comes from the TICK, never from how many times this was called** — `PhysicsLevelInit` sets the timestep to
    /// `gpGlobals->interval_per_tick`, and a frame rate is not a clock.
    /// </remarks>
    public void Advance(IReadOnlyList<CorpseRequest> corpses, int tick, float interval, double seconds)
    {
        ArgumentNullException.ThrowIfNull(corpses);

        if (corpses.Count == 0)
        {
            return;
        }

        if (Record is { } record && tick <= record.Reached && ShowRecorded(record, corpses, tick, seconds))
        {
            return;
        }

        bool unreachable = _world is null || tick < _worldTick;

        foreach (CorpseRequest corpse in corpses)
        {
            int birth = BirthOf(corpse, tick);
            unreachable |= birth < _worldTick && !IsRunning(corpse, birth);
        }

        if (unreachable)
        {
            _running.Clear();
        }

        // **Each birth decided once, after the rebuild is**: a corpse with no recorded death keeps its seed tick only while it is in the
        // world, so a rebuild seeds it now — the replay must start from the births the corpses will actually be given.
        int[] births = new int[corpses.Count];
        int earliest = int.MaxValue;

        for (int index = 0; index < births.Length; index++)
        {
            births[index] = BirthOf(corpses[index], tick);
            earliest = Math.Min(earliest, births[index]);
        }

        HashSet<int> seeded = [];

        if (unreachable)
        {
            long buildingFrom = System.Diagnostics.Stopwatch.GetTimestamp();

            _world = CreateWorld?.Invoke(interval) ??
                new IvpRagdollWorld(interval, new Vector3(0f, 0f, -PhysicsEnvironment.DefaultGravity), Surfaces);
            _touched.Clear();
            _worldTick = earliest;
            Rebuilds++;
            AddBornAt(corpses, births, _worldTick, tick, interval, seconds, seeded);
            BuildingTicks += System.Diagnostics.Stopwatch.GetTimestamp() - buildingFrom;
        }

        IvpRagdollWorld world = _world!;
        bool stepped = _worldTick < tick;
        long steppingFrom = System.Diagnostics.Stopwatch.GetTimestamp();

        while (_worldTick < tick)
        {
            world.Simulate(interval);
            _worldTick++;
            Steps++;

            foreach ((int entity, (IvpRagdoll ragdoll, _)) in _running)
            {
                // `CRagdoll::CheckSettleStationaryRagdoll` is a CORPSE's; a gib is a prop, and IVP's own rest check sleeps it.
                if (!ragdoll.IsDebris)
                {
                    ragdoll.CheckSettle(interval);
                }

                WatchForAFall(entity, ragdoll);
            }

            AddBornAt(corpses, births, _worldTick, tick, interval, seconds, seeded);
        }

        SteppingTicks += System.Diagnostics.Stopwatch.GetTimestamp() - steppingFrom;

        for (int index = 0; index < births.Length; index++)
        {
            CorpseRequest corpse = corpses[index];

            if (!IsRunning(corpse, births[index]))
            {
                continue;
            }

            IvpRagdoll ragdoll = _running[corpse.EntityIndex].Ragdoll;
            (Vector3 Position, Quaternion Orientation)[] state = ragdoll.State();

            _roots[corpse.EntityIndex] = state[0].Position;
            _contacts[corpse.EntityIndex] = ragdoll.Contacts;
            Place(corpse, state);

            if (corpse.Entity is not { } entity)
            {
                continue;
            }

            entity.Ragdoll = ragdoll.PoseIntoAccessor;

            // **`C_ClientRagdoll::LastBoneChangedTime()` returns the physics update time** (`c_baseanimating.cpp:587`), which
            // `CRagdoll::VPhysicsUpdate` advances only while the body moves — so a stepped corpse's pose is rebuilt, and one asked
            // for the same tick again is a cache hit, which is how the engine draws a pile of settled corpses for free.
            if (stepped)
            {
                entity.LastBoneChangedTime = seconds;
            }

            // **Seeding poses the entity out of band with the ragdoll detached**, which marks the frame built — so this frame's own
            // `SetupBones` would be a cache hit and the hook just attached would never run.
            if (seeded.Contains(corpse.EntityIndex))
            {
                entity.InvalidateBoneCache();
            }
        }
    }

    /// <summary>Where each gib is, as its entity's origin and angles — what a baked gib is drawn at (B409).</summary>
    /// <remarks>
    /// **`C_PhysPropClientside`'s origin and angles follow its physics object**: vphysics' `GetPosition( &amp;origin, &amp;angles )`
    /// makes a matrix from the core and reads the angles back with `MatrixAngles`. A gib has no skeleton — every TF2 gib model is
    /// baked — so this, not a bone pose, is what places it.
    /// </remarks>
    public IReadOnlyDictionary<int, (Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles)> Placements => _placements;

    private readonly Dictionary<int, (Vector3 Origin, (float Pitch, float Yaw, float Roll) Angles)> _placements = [];

    /// <summary>Records a gib's placement from its one body's state; a corpse is posed through its bones instead.</summary>
    private void Place(CorpseRequest corpse, (Vector3 Position, Quaternion Orientation)[] state)
    {
        if (!corpse.Gib || state.Length == 0)
        {
            return;
        }

        (Vector3 position, Quaternion orientation) = state[0];
        float[] matrix = StudioBones.FromQuaternion(
            (orientation.X, orientation.Y, orientation.Z, orientation.W), (position.X, position.Y, position.Z));

        _placements[corpse.EntityIndex] = (position, StudioBones.ToAngles(matrix));
    }

    /// <summary>Each corpse's pose as the record holds it at this tick, or false — changing nothing — when any corpse is missing from it.</summary>
    /// <remarks>
    /// **All or none**, so a moment never mixes a recorded corpse with a live one in two different environments. A pose that is a
    /// new array marks the bones changed, as a stepped corpse's are; the same array again is the settled corpse's cache hit.
    /// </remarks>
    private bool ShowRecorded(CorpseRecord record, IReadOnlyList<CorpseRequest> corpses, int tick, double seconds)
    {
        (Vector3 Position, Quaternion Orientation)[][] states = new (Vector3, Quaternion)[corpses.Count][];

        for (int index = 0; index < corpses.Count; index++)
        {
            CorpseRequest corpse = corpses[index];

            if (corpse.BornAt is not { } born || !record.TryGet(corpse.EntityIndex, born, tick, out (Vector3 Position, Quaternion Orientation)[]? state))
            {
                return false;
            }

            states[index] = state!;
        }

        for (int index = 0; index < corpses.Count; index++)
        {
            CorpseRequest corpse = corpses[index];
            (Vector3 Position, Quaternion Orientation)[] state = states[index];

            if (_shown.TryGetValue(corpse.EntityIndex, out (Vector3, Quaternion)[]? was) && ReferenceEquals(was, state) &&
                (corpse.Entity is null || corpse.Entity.Ragdoll is not null))
            {
                continue;
            }

            _shown[corpse.EntityIndex] = state;
            _roots[corpse.EntityIndex] = state.Length > 0 ? state[0].Position : Vector3.Zero;
            Place(corpse, state);

            if (corpse.Entity is { } entity)
            {
                RagdollBody body = corpse.Ragdoll;
                entity.Ragdoll = (into, written) => body.PoseInto(state, into, written);
                entity.LastBoneChangedTime = seconds;
            }
        }

        return true;
    }

    /// <summary>The pose each corpse was last shown from the record with, by entity index.</summary>
    private readonly Dictionary<int, (Vector3, Quaternion)[]> _shown = [];

    /// <summary>A running corpse's pose now, as <see cref="IvpRagdoll.State"/> reports it — what <see cref="CorpseRecord"/> stores.</summary>
    /// <param name="entity">The index the corpse is drawn under.</param>
    /// <returns>The pose, or null when that corpse is not in the world.</returns>
    internal (Vector3 Position, Quaternion Orientation)[]? StateOf(int entity) =>
        _running.TryGetValue(entity, out (IvpRagdoll Ragdoll, int) live) ? live.Ragdoll.State() : null;

    /// <summary>
    /// The tick a corpse is seeded at: its death when the timeline recorded one no later than now; else, for a corpse already in the
    /// world, the tick it was seeded at; else now.
    /// </summary>
    /// <remarks>
    /// **A corpse with no recorded death keeps the birth it was given**, or every tick would make it a new corpse: the first draw's
    /// tick is its birth from then on, as it was when each corpse had its own simulation.
    /// </remarks>
    private int BirthOf(CorpseRequest corpse, int tick)
    {
        if (corpse.BornAt is { } known && known <= tick)
        {
            return known;
        }

        return _running.TryGetValue(corpse.EntityIndex, out (IvpRagdoll, int Born) live) && corpse.BornAt is null ? live.Born : tick;
    }

    /// <summary>Whether this corpse — its entity at this birth — is the one in the world, rather than an earlier corpse whose index it reuses.</summary>
    private bool IsRunning(CorpseRequest corpse, int birth) =>
        _running.TryGetValue(corpse.EntityIndex, out (IvpRagdoll, int Born) live) && live.Born == birth;

    /// <summary>Puts every corpse that died at a tick into the world, seeded from its death pose.</summary>
    private void AddBornAt(
        IReadOnlyList<CorpseRequest> corpses, int[] births, int at, int tick, float interval, double seconds, HashSet<int> seeded)
    {
        for (int index = 0; index < births.Length; index++)
        {
            CorpseRequest corpse = corpses[index];
            int birth = births[index];

            if (birth != at || IsRunning(corpse, birth))
            {
                continue;
            }

            if (Seed(corpse, seconds - ((tick - birth) * interval)) is not { } start)
            {
                continue;
            }

            IvpRagdoll ragdoll;

            if (corpse.Gib)
            {
                // **A gib is a physics prop, thrown with `AddVelocity( rndVel, angVelocity )`** (`BreakModelCreateSingle`,
                // `physpropclientside.cpp:787-796`, B409) — not a ragdoll with a killing blow.
                ragdoll = IvpRagdoll.CreateProp(_world!, corpse.Ragdoll, start);
                (float X, float Y, float Z) throwAt = corpse.Velocity ?? (0f, 0f, 0f);
                (float X, float Y, float Z) spin = corpse.Spin ?? (0f, 0f, 0f);
                ragdoll.AddVelocity(new Vector3(throwAt.X, throwAt.Y, throwAt.Z), new Vector3(spin.X, spin.Y, spin.Z));
            }
            else
            {
                ragdoll = IvpRagdoll.Create(_world!, corpse.Ragdoll, start);

                // **The killing blow, applied at creation exactly as `RagdollCreate` does** (B58), staged so it lands on the first step.
                if (corpse.Velocity is { } inherited)
                {
                    ragdoll.Inherit(new Vector3(inherited.X, inherited.Y, inherited.Z));
                }

                if (corpse.Force is { } blow)
                {
                    ragdoll.Kill(new Vector3(blow.X, blow.Y, blow.Z), corpse.ForceBone ?? -1);
                    _blows[corpse.EntityIndex] = blow;
                }
            }

            _running[corpse.EntityIndex] = (ragdoll, birth);
            _born[corpse.EntityIndex] = birth;
            _seeded[corpse.EntityIndex] = start.Length > 0 ? start[0].Position : Vector3.Zero;
            seeded.Add(corpse.EntityIndex);
        }
    }

    /// <summary>Each element's starting state, read from the entity posed at its death with the ragdoll detached.</summary>
    /// <remarks>
    /// **Detached, and the cache blown first** — `ForceSetupBonesAtTime` opens with `InvalidateBoneCache(); // blow the cached prev
    /// bones` (`c_baseanimating.cpp:4763`). Attached, the seed would read the simulation's own previous output.
    /// </remarks>
    private static (Vector3 Position, Quaternion Orientation)[]? Seed(CorpseRequest corpse, double seconds)
    {
        // **A gib is created at its spawn transform, not from a pose**: `CreateGibsFromList` hands `BreakModelCreateSingle` a
        // position and `params.angles` — the player's render angles — and a gib's one body is its model's own transform (B409).
        if (corpse.Gib && corpse.Spawn is { } spawn)
        {
            (float x, float y, float z, float w) = StudioBones.FromAngles(0f, spawn.Yaw, 0f);
            return [.. System.Linq.Enumerable.Repeat((spawn.Origin, new Quaternion(x, y, z, w)), corpse.Ragdoll.Elements.Count)];
        }

        if (corpse.Entity is not { } entity)
        {
            return null;
        }

        entity.Ragdoll = null;
        entity.InvalidateBoneCache();

        if (!entity.SetupBones(StudioBoneFlags.UsedByAnything, seconds))
        {
            return null;
        }

        BoneAccessor posed = entity.Bones;
        (Vector3 Position, Quaternion Orientation)[] start = new (Vector3, Quaternion)[corpse.Ragdoll.Elements.Count];

        for (int element = 0; element < start.Length; element++)
        {
            int bone = corpse.Ragdoll.Elements[element].BoneIndex;

            if (bone < 0 || bone >= posed.Count)
            {
                start[element] = (Vector3.Zero, Quaternion.Identity);
                continue;
            }

            ReadOnlySpan<float> matrix = posed.Bone(bone);
            (float x, float y, float z, float w) = StudioBones.ToQuaternion(matrix);

            start[element] = (new Vector3(matrix[3], matrix[7], matrix[11]), new Quaternion(x, y, z, w));
        }

        return start;
    }

    /// <summary>Records the tick a corpse first drops out of the world, and the highest place it had touched (B58, B306).</summary>
    /// <remarks>
    /// **The highest place, not the most recent**: a body sinking through a surface keeps finding contacts the whole way down, so a
    /// most-recent reference follows it and the gap never grows.
    /// </remarks>
    private void WatchForAFall(int entity, IvpRagdoll ragdoll)
    {
        Vector3 root = ragdoll.State()[0].Position;
        (double X, double Y, double Z) at = (root.X, root.Y, root.Z);
        int contacts = ragdoll.Contacts;

        if (contacts > 0 && (!_touched.TryGetValue(entity, out (double X, double Y, double Z) touched) || at.Z > touched.Z))
        {
            _touched[entity] = at;
        }

        if (!_fell.ContainsKey(entity) && _touched.TryGetValue(entity, out (double X, double Y, double Z) last) && at.Z - last.Z < FallenThrough)
        {
            _fell[entity] = (_worldTick, contacts, last);
        }
    }
}
