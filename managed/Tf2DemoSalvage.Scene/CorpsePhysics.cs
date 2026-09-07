using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// One running simulation per corpse, stepped by the demo's clock (B58, D146).
/// </summary>
/// <remarks>
/// **A corpse is the one drawn thing whose pose depends on its own past.** Every other entity in
/// this project is a pure function of the tick — ask for tick N and you get the same answer whether
/// you arrived forwards, backwards, or by seeking. A ragdoll is not: it is where it is because of
/// every step since it died, which is why this type exists at all rather than the pose being
/// computed inline like everything else.
///
/// **So it follows D131's persistent sample, and it has the same hazard**: a stepped timeline must
/// agree with a freshly built one. It cannot agree exactly here — a physics simulation is not
/// reversible — so the rule is the weaker one that is actually achievable: **a corpse is simulated
/// from its death forward, and any request that cannot be reached by stepping forward rebuilds from
/// death.** Seeking backwards, or to a tick before this corpse existed, throws the simulation away.
///
/// **Seeded from the ANIMATED pose, which is what the engine does.** `InitAsClientRagdoll` poses the
/// entity from its death animation and hands those bone matrices to the physics —
/// `c_baseanimating.cpp:4931` copies the sequence and then zeroes the playback rate, so what the
/// solver starts from is a real pose rather than the bind pose. Here that means: build the bones
/// once with no ragdoll attached, read each element's bone out of the accessor, and start there.
/// </remarks>
public sealed class CorpsePhysics
{
    private readonly Dictionary<int, Running> _running = [];

    /// <summary>How many corpses are being simulated.</summary>
    public int Count => _running.Count;

    /// <summary>The map every corpse falls onto, or null before one is loaded.</summary>
    /// <remarks>
    /// **Set when the map changes, and a simulation reads it at seed time.** A corpse created
    /// before the world arrives would fall through it for its whole life, so
    /// <see cref="Clear"/> — which a map change calls anyway — is what makes that impossible.
    /// </remarks>
    public IvpWorldCollision? World { get; set; }

    /// <summary>The game's surface table, for the friction that stops a corpse sliding.</summary>
    /// <remarks>
    /// **Empty is a working state and not a missing input**, which is what makes the game folder
    /// optional here: every surface then falls back to `g_PhysDefaultObjectParams`' friction of 1,
    /// which is the engine's own answer for an unknown surface.
    /// </remarks>
    public SurfaceTable Surfaces { get; set; } = SurfaceTable.Empty;

    /// <summary>How many were rebuilt from death because the tick could not be reached forwards.</summary>
    /// <remarks>
    /// **Counted because a seek is the expensive case and nothing else would say it happened.**
    /// A rebuild replays every tick since the corpse died; a match seeked around in heavily is the
    /// shape that would make this dominate a frame, and the number is what a later measurement
    /// would start from.
    /// </remarks>
    public int Rebuilds { get; private set; }

    /// <summary>How many steps have been run, across all corpses.</summary>
    public int Steps { get; private set; }

    /// <summary>Where the simulation has put each corpse's root body, by entity index.</summary>
    /// <remarks>
    /// **Carried out of the solver rather than recomputed** (B243). A corpse's simulated position is
    /// not its wire position — that is the entire point of simulating it — so pointing a camera at
    /// what the demo said, which is what `docs/memory/point-the-camera-from-the-data.md` otherwise
    /// prescribes, aims at where the body was when it died and not at where it is.
    /// </remarks>
    public IReadOnlyDictionary<int, Vector3> Roots => _roots;

    private readonly Dictionary<int, Vector3> _roots = [];

    /// <summary>How many contacts each corpse's last step found, by entity index.</summary>
    /// <remarks>
    /// **"Still falling" has two causes that look the same from outside** — no contact found, or
    /// one found that did not hold — and only this separates them. Beside it, the root body's hull
    /// point count, because a body with no hull cannot generate a contact at all and that is the
    /// third cause.
    /// </remarks>
    public IReadOnlyDictionary<int, int> Contacts => _contacts;

    /// <summary>The deepest penetration each corpse's last step found, in whole Source units.</summary>
    public IReadOnlyDictionary<int, int> Deepest => _deepest;

    private readonly Dictionary<int, int> _contacts = [];

    private readonly Dictionary<int, int> _deepest = [];

    /// <summary>The tick each corpse was seeded at — its death, when the timeline records one.</summary>
    public IReadOnlyDictionary<int, int> Born => _born;

    private readonly Dictionary<int, int> _born = [];

    /// <summary>Where each corpse's root body was placed when it was seeded.</summary>
    /// <remarks>
    /// **The seed and the settled position answer different questions.** A body that ends up far
    /// below the map either started somewhere wrong or fell from somewhere right, and only the pair
    /// tells those apart.
    /// </remarks>
    public IReadOnlyDictionary<int, Vector3> Seeded => _seeded;

    private readonly Dictionary<int, Vector3> _seeded = [];

    /// <summary>Forgets every simulation — a new demo, or a map change.</summary>
    public void Clear()
    {
        _running.Clear();
        _roots.Clear();
    }

    /// <summary>Brings one corpse's simulation up to a tick, creating it if needed.</summary>
    /// <param name="entityIndex">The index the corpse is drawn under.</param>
    /// <param name="ragdoll">The model's bodies and joints.</param>
    /// <param name="entity">The animating entity whose bones the simulation will drive.</param>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="seconds">Playback time, for the pose that seeds a new simulation.</param>
    /// <param name="bornAt">
    /// The tick this corpse died, so a seek simulates it forward from there rather than seeding it
    /// standing at whatever tick it was first drawn at. Null falls back to that drawn tick.
    /// </param>
    /// <returns><c>true</c> when the entity now has a simulation attached.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The step count comes from the TICK, never from how many times this was called.** A frame
    /// rate is not a clock: at 300 frames a second a corpse asked to step once per rebuild would
    /// fall four times as fast as one drawn at 66, and the pose would look plausible at every rate
    /// while matching TF2 at none. `PhysicsLevelInit` sets the timestep to
    /// `gpGlobals->interval_per_tick` for exactly this reason.
    /// </remarks>
    public bool Advance(
        int entityIndex,
        RagdollBody ragdoll,
        AnimatingEntity entity,
        int tick,
        float interval,
        double seconds,
        int? bornAt = null)
    {
        ArgumentNullException.ThrowIfNull(ragdoll);
        ArgumentNullException.ThrowIfNull(entity);

        bool seeded = false;

        if (!_running.TryGetValue(entityIndex, out Running? live) || tick < live.BornAt)
        {
            seeded = true;

            // **A tick before this corpse existed cannot be reached by stepping forward**, so the
            // simulation is discarded rather than run backwards. The same branch covers a seek to
            // a different part of the demo entirely.
            // **Seeded at DEATH, not at the tick being drawn, and that is the whole of seeking.**
            // A corpse opened at tick 14,270 having died at 14,100 is not a corpse standing in its
            // death pose; it is one that has had 170 ticks of physics. Falling back to the drawn
            // tick keeps a corpse whose birth the timeline did not record from being seeded in the
            // past, which would replay the whole demo's worth of steps.
            int birth = bornAt is { } known && known <= tick ? known : tick;

            live = Seed(
                entityIndex, ragdoll, entity, birth, interval, seconds - ((tick - birth) * interval));

            if (live is null)
            {
                return false;
            }
        }

        // Behind the requested tick: catch up. Ahead of it — a backward seek within this corpse's
        // life — is handled above by rebuilding, so this only ever counts forward.
        bool stepped = live.SteppedTo < tick;

        while (live.SteppedTo < tick)
        {
            live.Simulation.Step();
            live.SteppedTo++;
            Steps++;
        }

        entity.Ragdoll = live.Write;

        if (live.Simulation.Environment.Bodies.Count > 0)
        {
            (double x, double y, double z) = live.Simulation.Environment.Bodies[0].Position;

            _roots[entityIndex] = new Vector3((float)x, (float)y, (float)z);

            _contacts[entityIndex] = live.Simulation.Environment.Contacts;
            _deepest[entityIndex] = (int)live.Simulation.Environment.DeepestContact;
            _born[entityIndex] = live.BornAt;
        }

        // **`C_ClientRagdoll::LastBoneChangedTime()` returns the physics update time**
        // (`c_baseanimating.cpp:587`), which `CRagdoll::VPhysicsUpdate` advances only while the
        // body is awake (`ragdoll.cpp:191-193`). So a corpse that stepped has changed and its pose
        // must be rebuilt; a corpse asked for the same tick again has not, and
        // `SetupBones`' own guard then declines to invalidate it. **That is how the engine draws a
        // pile of settled corpses for free**, and it is why this is a time rather than a flag.
        if (stepped)
        {
            entity.LastBoneChangedTime = seconds;
        }

        // **Seeding is the case the time cannot cover.** It poses the entity out of band with the
        // ragdoll DETACHED, which marks the frame built — so the draw's own `SetupBones`, same
        // frame and same time, would be a cache hit and the hook just attached would never run.
        // Measured: a corpse drew standing in its death animation while every part of the physics
        // path passed its own tests.
        if (seeded)
        {
            entity.InvalidateBoneCache();
        }

        return true;
    }

    /// <summary>Builds a simulation seeded from the entity's animated pose.</summary>
    /// <remarks>
    /// **The seed pose is built with the ragdoll DETACHED**, which is not a subtlety to optimise
    /// away: attaching first would seed the simulation from its own previous output, and the corpse
    /// would drift a little further from the animation every time it was rebuilt.
    /// </remarks>
    private Running? Seed(
        int entityIndex,
        RagdollBody ragdoll,
        AnimatingEntity entity,
        int tick,
        float interval,
        double seconds)
    {
        entity.Ragdoll = null;

        // **`ForceSetupBonesAtTime` opens exactly this way** — `InvalidateBoneCache(); // blow the
        // cached prev bones` (`c_baseanimating.cpp:4763`), the routine the engine uses to pose an
        // entity out of band, and `GetRagdollInitBoneArrays` right beneath it is what seeds a
        // ragdoll from those bones. Without it the seed reads whatever this frame already built,
        // which for a corpse rebuilt after a seek is its own previous output.
        entity.InvalidateBoneCache();

        if (!entity.SetupBones(StudioBoneFlags.UsedByAnything, seconds))
        {
            return null;
        }

        BoneAccessor posed = entity.Bones;

        (Vector3 Position, Quaternion Orientation)[] start =
            new (Vector3, Quaternion)[ragdoll.Elements.Count];

        for (int element = 0; element < start.Length; element++)
        {
            int bone = ragdoll.Elements[element].BoneIndex;

            if (bone < 0 || bone >= posed.Count)
            {
                start[element] = (Vector3.Zero, Quaternion.Identity);
                continue;
            }

            ReadOnlySpan<float> matrix = posed.Bone(bone);

            (float x, float y, float z, float w) = StudioBones.ToQuaternion(matrix);

            start[element] = (
                new Vector3(matrix[3], matrix[7], matrix[11]), new Quaternion(x, y, z, w));
        }

        RagdollSimulation simulation = RagdollSimulation.Create(
            ragdoll, interval, start, Surfaces);

        simulation.Environment.World = World;

        Running live = new(simulation, tick, tick);

        _seeded[entityIndex] = start.Length > 0 ? start[0].Position : Vector3.Zero;

        _running[entityIndex] = live;
        Rebuilds++;

        return live;
    }

    /// <summary>One corpse's simulation and how far it has been stepped.</summary>
    private sealed class Running(RagdollSimulation simulation, int bornAt, int steppedTo)
    {
        public RagdollSimulation Simulation { get; } = simulation;

        /// <summary>The tick this was seeded at; anything earlier needs a rebuild.</summary>
        public int BornAt { get; } = bornAt;

        /// <summary>The tick it has been stepped up to.</summary>
        public int SteppedTo { get; set; } = steppedTo;

        /// <summary>The delegate handed to the entity, allocated once rather than per frame.</summary>
        public void Write(BoneAccessor into, BoneBitList written) =>
            Simulation.PoseIntoAccessor(into, written);
    }
}
