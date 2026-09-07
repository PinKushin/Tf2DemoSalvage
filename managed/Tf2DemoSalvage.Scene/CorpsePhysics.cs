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

    /// <summary>Stopwatch ticks spent stepping corpses forward.</summary>
    /// <remarks>
    /// **The one unbounded cost in this type**, and the only number that separates "a corpse is
    /// expensive" from "seeking is expensive": a seek replays every tick since a death, so the same
    /// per-tick cost that vanishes in a frame is minutes when six hundred run at once.
    /// </remarks>
    public long SteppingTicks { get; private set; }

    /// <summary>How many sub-intervals the last corpse's steps were walked in.</summary>
    public long Slices { get; private set; }

    /// <summary>
    /// **A corpse is only simulated while it is DRAWN, and that is a divergence** (B58).
    /// </summary>
    /// <remarks>
    /// **The engine keeps a ragdoll in the physics environment whether or not the view can see
    /// it.** `cl_ragdoll_physics_enable` decides whether one exists at all; after that it is
    /// `physenv`'s, and visibility governs drawing alone. Here the advance happens inside the loop
    /// over DRAWN props, so a corpse behind the camera stops dead and resumes when it comes back
    /// into view.
    ///
    /// **It was found by the instrument disagreeing with itself.** The same tick, from two
    /// cameras: from one, three corpses reported leaving the world; from a wider one that did not
    /// draw them, none did — because none of them had been stepped at all.
    ///
    /// **Fixing it is not a line.** This project's animation is draw-driven end to end — an
    /// `AnimatingEntity` exists because something posed it — so simulating an unseen corpse means
    /// giving it an entity nothing is drawing. Written down here rather than left as a surprise.
    /// </remarks>
    public static bool SimulatesOnlyWhatIsDrawn => true;

    /// <summary>For each corpse that left the world, when, where, and its contacts then.</summary>
    /// <remarks>
    /// **The position is the point of the record.** The tick says a corpse sank rather than
    /// started below; only the place says WHERE the world let it through, and the resting place
    /// cannot stand in for it — three corpses measured on `z1800` slid between three hundred and
    /// eight hundred units after crossing, so probing where they stopped asks about the wrong
    /// geometry.
    /// </remarks>
    public IReadOnlyDictionary<int, (int Tick, int Contacts, (double X, double Y, double Z) At)>
        Fell => _fell;

    private readonly Dictionary<int, (int Tick, int Contacts, (double X, double Y, double Z) At)>
        _fell = [];

    private readonly Dictionary<int, (double X, double Y, double Z)> _touched = [];

    /// <summary>Below this a body has left the playable world rather than sunk into a floor.</summary>
    private const double FallenThrough = -50d;

    /// <summary>The same, in seconds.</summary>
    public double SteppingSeconds =>
        SteppingTicks / (double)System.Diagnostics.Stopwatch.Frequency;

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

    /// <summary>How hard each corpse was hit — the magnitude of <c>m_vecForce</c>.</summary>
    /// <remarks>
    /// **Carried out of the seed that used it** (B243). A corpse that flies too far has two causes
    /// that look identical from outside — the wire's force being larger than expected, and this
    /// project applying it wrongly — and only the number the code actually used separates them.
    /// </remarks>
    public IReadOnlyDictionary<int, float> Blows => _blows;

    private readonly Dictionary<int, float> _blows = [];

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
    /// <param name="force">The killing blow — <c>m_vecForce</c>, an impulse in kg·in/s.</param>
    /// <param name="forceBone">Which body it landed on — <c>m_nForceBone</c>.</param>
    /// <param name="velocity">What the corpse was already carrying — <c>m_vecRagdollVelocity</c>.</param>
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
        int? bornAt = null,
        (float X, float Y, float Z)? force = null,
        int? forceBone = null,
        (float X, float Y, float Z)? velocity = null)
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
                force,
                forceBone,
                velocity,
                entityIndex,
                ragdoll,
                entity,
                birth,
                interval,
                seconds - ((tick - birth) * interval));

            if (live is null)
            {
                return false;
            }
        }

        // Behind the requested tick: catch up. Ahead of it — a backward seek within this corpse's
        // life — is handled above by rebuilding, so this only ever counts forward.
        bool stepped = live.SteppedTo < tick;

        long steppingFrom = System.Diagnostics.Stopwatch.GetTimestamp();

        while (live.SteppedTo < tick)
        {
            live.Simulation.Step();
            live.SteppedTo++;
            Steps++;

            // **The tick a corpse first drops out of the world, caught as it happens** (B58). The
            // resting place says only where it stopped; three corpses on `z1800` end at the same
            // three depths through every change, and what separates "it started there" from "it
            // fell through on the way" is WHEN — with the contact count at that moment beside it,
            // because a body falling with contacts is one the solve failed to hold and a body
            // falling without any is one nothing ever saw.
            if (live.Simulation.Environment.Bodies.Count == 0)
            {
                continue;
            }

            (double X, double Y, double Z) at = live.Simulation.Environment.Bodies[0].Position;

            // **Where it last touched anything, kept whether or not it ever falls.** The threshold
            // below fires long after the event — a corpse crossing a floor at z 200 is recorded at
            // −50, three hundred units and a second of sideways travel later, and probing THAT spot
            // asks about the wrong geometry. Two probes were spent on the wrong answer before this
            // existed. The last contact is the lip of the hole.
            if (live.Simulation.Environment.Contacts > 0)
            {
                _touched[entityIndex] = at;
            }

            if (!_fell.ContainsKey(entityIndex) && at.Z < FallenThrough)
            {
                _fell[entityIndex] = (
                    live.SteppedTo,
                    live.Simulation.Environment.Contacts,
                    _touched.TryGetValue(entityIndex, out (double X, double Y, double Z) last)
                        ? last
                        : at);
            }
        }

        // **Timed because catching a corpse up is the one unbounded thing here** (B58). A seek to a
        // tick long after a death replays every tick between, and a cost per tick that looks
        // trivial in a frame is minutes when six hundred of them run at once — which is what the
        // owner saw as a hang on seeking.
        SteppingTicks += System.Diagnostics.Stopwatch.GetTimestamp() - steppingFrom;

        Slices = live.Simulation.Environment.Slices;

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
        (float X, float Y, float Z)? force,
        int? forceBone,
        (float X, float Y, float Z)? velocity,
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

        // **The killing blow, applied at creation exactly as `RagdollCreate` does** (B58). It is
        // staged rather than set, so it lands on this corpse's first step — the engine's own
        // one-step lag, not a delay invented here.
        if (velocity is { } inherited)
        {
            simulation.Inherit(inherited);
        }

        if (force is { } blow)
        {
            simulation.Kill(blow, forceBone ?? -1);

            _blows[entityIndex] =
                MathF.Sqrt((blow.X * blow.X) + (blow.Y * blow.Y) + (blow.Z * blow.Z));
        }

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
