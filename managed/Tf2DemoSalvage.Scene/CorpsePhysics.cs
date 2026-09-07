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

    /// <summary>Forgets every simulation — a new demo, or a map change.</summary>
    public void Clear() => _running.Clear();

    /// <summary>Brings one corpse's simulation up to a tick, creating it if needed.</summary>
    /// <param name="entityIndex">The index the corpse is drawn under.</param>
    /// <param name="ragdoll">The model's bodies and joints.</param>
    /// <param name="entity">The animating entity whose bones the simulation will drive.</param>
    /// <param name="tick">The tick being drawn.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <param name="seconds">Playback time, for the pose that seeds a new simulation.</param>
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
        double seconds)
    {
        ArgumentNullException.ThrowIfNull(ragdoll);
        ArgumentNullException.ThrowIfNull(entity);

        if (!_running.TryGetValue(entityIndex, out Running? live) || tick < live.BornAt)
        {
            // **A tick before this corpse existed cannot be reached by stepping forward**, so the
            // simulation is discarded rather than run backwards. The same branch covers a seek to
            // a different part of the demo entirely.
            live = Seed(entityIndex, ragdoll, entity, tick, interval, seconds);

            if (live is null)
            {
                return false;
            }
        }

        // Behind the requested tick: catch up. Ahead of it — a backward seek within this corpse's
        // life — is handled above by rebuilding, so this only ever counts forward.
        while (live.SteppedTo < tick)
        {
            live.Simulation.Step();
            live.SteppedTo++;
            Steps++;
        }

        entity.Ragdoll = live.Write;

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

        Running live = new(
            RagdollSimulation.Create(ragdoll, interval, start), tick, tick);

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
