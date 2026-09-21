using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Every bullet impact's effect that should be running at a tick, stepped by ticks (B415).</summary>
/// <remarks>
/// **The same lifecycle as <see cref="ParticleEffects.Bursts"/>**: an effect's state is a function of how many ticks
/// have passed since its bullet landed, never of how many frames were drawn; one met part way through catches up; a
/// backward seek rebuilds; and a finished effect is kept while it is still offered, so it is not rebuilt and replayed
/// every frame. *Stepped per tick rather than per frame* — the engine steps by frame time — which is what makes a seek
/// land on the same picture however it was reached.
/// </remarks>
public sealed class ImpactEffectRunner
{
    private readonly Dictionary<int, Running> _running = [];
    private readonly HashSet<int> _offered = [];
    private readonly List<int> _retiring = [];
    private int _tick = int.MinValue;

    /// <summary>The events the <see cref="ShotImpact"/> overload hands on, reused.</summary>
    private readonly List<(int Index, int Tick, int Seed)> _events = [];

    /// <summary>Each offered impact by index, for the duration of one call.</summary>
    private readonly Dictionary<int, ShotImpact> _impacts = [];

    /// <summary>How many effects are held, finished or not.</summary>
    public int Count => _running.Count;

    /// <summary>How many ticks an effect has been stepped, or −1 when it is not held.</summary>
    /// <param name="index">The impact's index.</param>
    /// <returns>The steps.</returns>
    public int Steps(int index) => _running.TryGetValue(index, out Running running) ? running.Stepped : -1;

    /// <summary>Starts, steps and retires effects so each is where it should be at this tick.</summary>
    /// <param name="live">The impacts whose effects should be running, by their index in the map's list.</param>
    /// <param name="tick">The tick shown.</param>
    /// <param name="seconds">How long one tick is.</param>
    /// <param name="spawn">An impact's effect, from its own draws; null for one that throws nothing.</param>
    /// <param name="trace">The world trace, for flecks and sparks.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Advance(
        IReadOnlyList<(int Index, ShotImpact Impact)> live,
        int tick,
        float seconds,
        Func<int, ShotImpact, Func<float, float, float>, ImpactEffect?> spawn,
        Func<Vector3, Vector3, BspTrace> trace)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(spawn);

        _events.Clear();

        foreach ((int index, ShotImpact impact) in live)
        {
            _events.Add((index, impact.Tick, SeededDraw.Of(impact.Shot, impact.Bullet, salt: 1)));
            _impacts[index] = impact;
        }

        Advance(_events, tick, seconds, (index, random) => spawn(index, _impacts[index], random), trace);
        _impacts.Clear();
    }

    /// <summary>Starts, steps and retires effects so each is where it should be at this tick — any event with a tick.</summary>
    /// <param name="live">The events whose effects should be running: an index unique to this runner, the tick, and the seed of their draws.</param>
    /// <param name="tick">The tick shown.</param>
    /// <param name="seconds">How long one tick is.</param>
    /// <param name="spawn">An event's effect, from its own draws; null for one that throws nothing.</param>
    /// <param name="trace">The world trace, for flecks and sparks.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Advance(
        IReadOnlyList<(int Index, int Tick, int Seed)> live,
        int tick,
        float seconds,
        Func<int, Func<float, float, float>, ImpactEffect?> spawn,
        Func<Vector3, Vector3, BspTrace> trace)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(trace);

        if (tick < _tick)
        {
            _running.Clear();
        }

        _tick = tick;
        _offered.Clear();

        foreach ((int index, int at, int seed) in live)
        {
            int wanted = tick - at;

            if (wanted < 0)
            {
                continue;
            }

            _offered.Add(index);

            // Stryker disable all : 'running' is declared by the TryGetValue before a '||' and read after it, so the
            // Logical mutator's switch leaves it unassigned (CS0165) and Safe Mode drops the method — B410.
            if (!_running.TryGetValue(index, out Running running) || running.Stepped > wanted)
            {
                Func<float, float, float> random = SeededDraw.For(seed);

                running = new Running(spawn(index, random), 0, random);
            }

            // Stryker restore all

            int taken = running.Stepped;

            while (taken < wanted)
            {
                running.Effect?.Step(seconds, trace, running.Random);
                taken++;
            }

            _running[index] = running with { Stepped = taken };
        }

        _retiring.Clear();

        foreach ((int index, Running running) in _running)
        {
            if (!_offered.Contains(index) && (running.Effect?.Finished ?? true))
            {
                _retiring.Add(index);
            }
        }

        foreach (int index in _retiring)
        {
            _running.Remove(index);
        }
    }

    /// <summary>Adds every held effect's particles and quads to their materials' corners.</summary>
    /// <param name="eye">The camera's position.</param>
    /// <param name="forward">Its forward.</param>
    /// <param name="right">Its right.</param>
    /// <param name="up">Its up.</param>
    /// <param name="materialAlpha">A material's own `$alpha`.</param>
    /// <param name="into">Corners by material, added to.</param>
    public void Build(
        Vector3 eye,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        Func<string, float> materialAlpha,
        IDictionary<string, List<DetailSpriteVertex>> into)
    {
        foreach (Running running in _running.Values)
        {
            if (running.Effect is { } effect)
            {
                ImpactDraw.Build(effect, eye, forward, right, up, materialAlpha, into);
            }
        }
    }

    /// <summary>Forgets every effect — a map or demo change.</summary>
    public void Clear()
    {
        _running.Clear();
        _tick = int.MinValue;
    }

    /// <summary>One impact's effect, how far it has been stepped, and its draws.</summary>
    private readonly record struct Running(ImpactEffect? Effect, int Stepped, Func<float, float, float> Random);
}
