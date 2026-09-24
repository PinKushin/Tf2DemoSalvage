using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

/// <summary>One corpse for the record: the prop the viewer draws it as, and the last tick the demo describes it.</summary>
/// <param name="Corpse">The corpse as <see cref="RagdollProps.Fill"/> makes it, <see cref="SceneProp.FirstTick"/> its death.</param>
/// <param name="LastTick">The last tick its entity exists — the window fade can only shorten.</param>
public readonly record struct RecordedCorpse(SceneProp Corpse, int LastTick);

/// <summary>
/// Every corpse in a demo simulated once, straight through, in one environment — and each one's pose per tick, for a seek to read
/// (D181).
/// </summary>
/// <remarks>
/// **Through the production path, not beside it.** <see cref="Run"/> drives a PRIVATE <see cref="EntityModelSet"/> — its own
/// entities, bone clock and <see cref="CorpsePhysics"/>, sharing only the map's read-only model frames — over the corpses alone,
/// tick by tick from the first death to the last corpse's end, through <see cref="EntityModelSet.AdvanceCorpsesAlone"/>. So a
/// corpse is seeded and stepped by the same code the live viewer runs, and a record that disagreed with straight-through play
/// would be a defect in the one path both use.
///
/// **One writer, many readers.** Each corpse's track is sized to its window when the record is made, a tick's poses are written
/// before <see cref="Reached"/> moves past it, and a reader asks only below <see cref="Reached"/> — so the render thread never
/// reads a slot the pass is writing.
///
/// **A pose is stored once while it holds**: a tick whose pose equals the one before shares its array, so a settled corpse costs
/// a reference a tick.
/// </remarks>
public sealed class CorpseRecord
{
    private readonly RecordedCorpse[] _corpses;
    private readonly Dictionary<(int Entity, int Born), (Vector3 Position, Quaternion Orientation)[]?[]> _tracks = [];
    private int _reached = int.MinValue;

    /// <summary>Makes an empty record for these corpses.</summary>
    /// <param name="corpses">Every corpse in the demo.</param>
    /// <exception cref="ArgumentNullException"><paramref name="corpses"/> is null.</exception>
    /// <exception cref="ArgumentException">A corpse has no death tick, or ends before it dies.</exception>
    public CorpseRecord(IReadOnlyList<RecordedCorpse> corpses)
    {
        ArgumentNullException.ThrowIfNull(corpses);

        _corpses = [.. corpses];

        foreach (RecordedCorpse recorded in _corpses)
        {
            // Stryker disable once : a mutant that empties the guard body leaves 'born'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (recorded.Corpse.FirstTick is not { } born || recorded.LastTick < born)
            {
                throw new ArgumentException("A recorded corpse needs its death tick, and must end after it.", nameof(corpses));
            }

            _tracks[(recorded.Corpse.EntityIndex, born)] = new (Vector3, Quaternion)[]?[recorded.LastTick - born + 1];
        }
    }

    /// <summary>Each tick's physics impact sounds, as the corpses' frames played them (B172); written by the pass, read by the renderer.</summary>
    private readonly ConcurrentDictionary<int, PhysicsImpactSound[]> _impactSounds = [];

    /// <summary>The impact sounds the corpses' physics frame at a tick played — `PlayImpactSounds`' list for that frame.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The sounds, empty past <see cref="Reached"/> or for a quiet tick.</returns>
    public IReadOnlyList<PhysicsImpactSound> ImpactSoundsAt(int tick) =>
        tick <= Reached && _impactSounds.TryGetValue(tick, out PhysicsImpactSound[]? heard) ? heard : [];

    /// <summary>The last tick every corpse's pose has been recorded for, or <see cref="int.MinValue"/> before the first.</summary>
    public int Reached => Volatile.Read(ref _reached);

    /// <summary>A corpse's recorded pose at a tick.</summary>
    /// <param name="entity">The index the corpse is drawn under.</param>
    /// <param name="born">Its death tick.</param>
    /// <param name="tick">The tick asked for.</param>
    /// <param name="state">Each element's position and orientation, as <see cref="IvpRagdoll.State"/> reported it.</param>
    /// <returns>Whether the record holds it — false past <see cref="Reached"/>, outside the corpse's window, or for a stranger.</returns>
    public bool TryGet(int entity, int born, int tick, out (Vector3 Position, Quaternion Orientation)[]? state)
    {
        state = null;

        if (tick > Reached || tick < born || !_tracks.TryGetValue((entity, born), out (Vector3, Quaternion)[]?[]? track) ||
            tick - born >= track.Length)
        {
            return false;
        }

        state = track[tick - born];
        return state is not null;
    }

    /// <summary>Simulates every corpse straight through, recording as it goes — the background pass.</summary>
    /// <param name="geometry">The map's loaded model frames, by path — read only.</param>
    /// <param name="createWorld">The environment with the map loaded, as the live viewer builds it; null for none.</param>
    /// <param name="surfaces">The game's surfaces, when <paramref name="createWorld"/> is null.</param>
    /// <param name="intervalPerTick">The demo's seconds per tick.</param>
    /// <param name="token">Stops the pass between ticks.</param>
    /// <exception cref="OperationCanceledException"><paramref name="token"/> was cancelled.</exception>
    public void Run(
        Func<string, PropModels.ModelFrames?> geometry,
        Func<float, IvpRagdollWorld>? createWorld,
        VphysicsSurfaceProps surfaces,
        float intervalPerTick,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(surfaces);

        if (_corpses.Length == 0)
        {
            return;
        }

        EntityModelSet models = new() { Geometry = geometry, IntervalPerTick = intervalPerTick };
        models.Corpses.CreateWorld = createWorld;
        models.Corpses.Surfaces = surfaces;

        // Gathered per tick and published whole, before `Reached` passes it, like the poses.
        Dictionary<int, List<PhysicsImpactSound>> pending = [];

        models.Corpses.ImpactHeard = (tick, sound) =>
        {
            if (!pending.TryGetValue(tick, out List<PhysicsImpactSound>? heard))
            {
                pending[tick] = heard = [];
            }

            heard.Add(sound);
        };

        int first = int.MaxValue;
        int last = int.MinValue;
        List<SceneProp> all = [];

        foreach (RecordedCorpse recorded in _corpses)
        {
            all.Add(recorded.Corpse);
            first = Math.Min(first, recorded.Corpse.FirstTick!.Value);
            last = Math.Max(last, recorded.LastTick);
        }

        models.Add(all, geometry);

        List<SceneProp> alive = [];

        for (int tick = first; tick <= last; tick++)
        {
            token.ThrowIfCancellationRequested();

            alive.Clear();

            foreach (RecordedCorpse recorded in _corpses)
            {
                if (recorded.Corpse.FirstTick <= tick && tick <= recorded.LastTick)
                {
                    alive.Add(recorded.Corpse);
                }
            }

            // **The live path's own clock**: a moment's seconds are its absolute tick times the interval (`MomentInfo.Seconds`).
            models.CurrentTick = tick;
            models.AdvanceCorpsesAlone(alive, tick * (double)intervalPerTick);

            foreach (SceneProp corpse in alive)
            {
                Store(models.Corpses, corpse, tick);
            }

            foreach ((int heardAt, List<PhysicsImpactSound> heard) in pending)
            {
                _impactSounds[heardAt] = [.. heard];
            }

            pending.Clear();
            Volatile.Write(ref _reached, tick);
        }
    }

    /// <summary>Writes one corpse's pose at a tick into its track, sharing the previous tick's array when nothing moved.</summary>
    private void Store(CorpsePhysics corpses, SceneProp corpse, int tick)
    {
        int born = corpse.FirstTick!.Value;

        // Stryker disable once : a mutant that empties the guard body leaves 'state' and 'track'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (corpses.StateOf(corpse.EntityIndex) is not { } state || !_tracks.TryGetValue((corpse.EntityIndex, born), out (Vector3 Position, Quaternion Orientation)[]?[]? track))
        {
            return;
        }

        int at = tick - born;

        track[at] = at > 0 && track[at - 1] is { } before && before.AsSpan().SequenceEqual(state) ? before : state;
    }
}
