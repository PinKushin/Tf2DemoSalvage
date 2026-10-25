using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One running instance of a particle system — emit, operate, reap (B373).
/// </summary>
/// <remarks>
/// **This is the order the engine runs and the order matters.** A particle born this step must be
/// operated on this step or it appears at its spawn state for one frame, and reaping last means a
/// particle that died this step is not drawn. `CParticleCollection::Simulate` is the shape being
/// followed; the operators themselves are `ParticleOperators`.
///
/// **The emitter is `emit_continuously` and its rate is a FRACTION per step**, which is why the
/// engine keeps a running total and emits its floor less what it has emitted. At the shipped
/// `emission_rate 128` and a 66-tick clock that is 1.94 particles a step — truncating each step
/// would emit one and lose a third of the trail, and rounding would emit two and inflate it.
///
/// **A particle is born at a TIME inside the step, and placed where its control point was then**
/// (B470). The collection keeps each control point's position at the end of the last step beside
/// the current one, and an initializer reads the lerp between them at the particle's
/// `CREATION_TIME` — so the two particles a rocket's trail emits in a step are spread along the
/// rocket's path rather than stacked on its newest position. Read in the disassembly of the SDK's
/// `particles.lib`: `Simulate`, `UpdatePrevControlPoints`, `GetControlPointAtTime` and the two
/// emitters' `Emit`; the arithmetic is quoted where each is reproduced below.
///
/// <code>
/// emit_continuously   emission_rate 128   emission_start_time 0   emission_duration 0
/// </code>
///
/// **`emission_duration` of zero means FOREVER**, not "never" — the one place in this file where a
/// zero is a sentinel rather than a quantity, and reading it the other way emits nothing at all
/// (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
/// </remarks>
public sealed class ParticleEffect
{
    /// <summary>How long a particle lives when its system declares no <c>Lifetime Random</c>.</summary>
    /// <remarks>
    /// **A fallback and not the answer.** `ParticleSystems.Spawn` runs the initializer where one
    /// exists and overwrites this; a system with none has nothing else to say, and one second is the
    /// same number the code that read the initializer here used to return.
    /// </remarks>
    private const float DefaultLifetime = 1f;

    /// <summary>Particles born, by system name, by every effect in this process — an instrument, read by `--measure` (finding 82, B557).</summary>
    /// <remarks>
    /// **Counted at the birth**, the one place a particle is made, so it is what was emitted and not a second reckoning
    /// of what should have been. Process-wide, so concurrent: production steps effects on the render thread only, but
    /// `Content.Tests` steps them from fixtures running in parallel.
    /// </remarks>
    public static ConcurrentDictionary<string, long> BirthsBySystem { get; } = new(StringComparer.Ordinal);

    private static int _systemsBorn;

    /// <summary>How many distinct systems are in <see cref="BirthsBySystem"/>, kept beside it so a per-frame read takes no bucket lock (B595 review).</summary>
    public static int SystemsBorn => global::System.Threading.Volatile.Read(ref _systemsBorn);

    /// <summary>The definition this is an instance of.</summary>
    public ParticleSystem System { get; }

    /// <summary>Its live particles, and the collection's seed.</summary>
    public ParticleStore Particles { get; }

    /// <summary>The operators this run can apply, by name.</summary>
    private readonly IReadOnlyDictionary<string, IParticleOperator> _operators;

    /// <summary>The sheet this system's material carries, or null.</summary>
    private readonly IReadOnlyList<SheetSequence>? _sheet;

    /// <summary>
    /// Each continuous emitter's context — `m_flTotalActualParticlesSoFar` and how many it has emitted — keyed by the
    /// emitter, because the engine gives every emitter its own.
    /// </summary>
    private readonly Dictionary<ParticleFunction, (float Total, int Emitted)> _continuous = [];

    /// <summary>Whether this effect and every child of it has run out of particles.</summary>
    /// <remarks>
    /// **A parent is not finished while a child still has particles**, which the engine states in
    /// as many words — *"make sure all children are finished"* (`particles.h:1630`). Dropping an
    /// effect on its own emptiness would cut a rocket's fire off the moment its smoke ran out.
    /// </remarks>
    public bool Empty
    {
        get
        {
            if (Particles.Count > 0)
            {
                return false;
            }

            foreach (ParticleEffect child in Children)
            {
                if (!child.Empty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Whether the owner is dormant: `CParticleCollection::SetDormant` (`0xe5c60`) writes +0x68, which `IsFinished` reads.</summary>
    /// <remarks>Set by whoever steps the effect; it does not stop emission itself, which the caller's `Fade` does.</remarks>
    public bool Dormant { get; set; }

    /// <summary>Whether this effect has run out of particles AND will make no more.</summary>
    /// <remarks>
    /// **Emptiness alone is not finishedness, and the engine says so in the declaration itself**:
    /// *"IsFinished returns true when a system has no particles and won't be creating any more"*
    /// (`particles.h:1119`). The second half is what <see cref="Empty"/> cannot answer.
    ///
    /// **It matters the moment an emitter has a start time.** `emit_continuously` carries
    /// `emission_start_time`, so a system that waits before its first particle is empty and unfinished — and a
    /// caller that dropped it on emptiness would throw it away before it ever emitted, which looks exactly like an
    /// effect that does not exist.
    ///
    /// **Zero duration is forever**, the sentinel <see cref="Step"/> already reads: such a system is never
    /// finished on its own and is stopped from outside, which is what <see cref="Fade"/> is for.
    /// </remarks>
    public bool Finished
    {
        get
        {
            // `CParticleCollection::IsFinished` (`0xe3c60`), in its order: the first-frame flag, the particles, the dormant
            // flag, then the emitters and the children. Each can only say "not finished".
            if (!_previousSet)
            {
                return false;
            }

            if (!Empty)
            {
                return false;
            }

            if (Dormant)
            {
                return false;
            }

            // **A burst that has not fired yet is not finished**, which is the same rule as a steady emitter
            // with a start time: both are empty and both will emit.
            if (!BurstsSpent)
            {
                return false;
            }

            foreach (ParticleFunction emitter in System.Emitters)
            {
                if (!string.Equals(emitter.Function, ContinuousEmitter, StringComparison.Ordinal))
                {
                    continue;
                }

                // `C_OP_ContinuousEmitter::MayCreateMoreParticles` (`0x00067430`): a rate not above 0 makes no more.
                if (emitter.Number("emission_rate", DefaultEmissionRate) <= 0d)
                {
                    continue;
                }

                float duration = (float)emitter.Number("emission_duration", 0d);

                if (duration <= 0f ||
                    Particles.Age <= (float)emitter.Number("emission_start_time", 0d) + duration)
                {
                    return false;
                }
            }

            foreach (ParticleEffect child in Children)
            {
                if (!child.Finished)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>`emission_rate`'s unpack default, `"100"` (`DmxElementUnpackInit&lt;C_OP_ContinuousEmitter&gt;`, +0x40).</summary>
    private const double DefaultEmissionRate = 100d;

    /// <summary>The steady emitter, named once.</summary>
    private const string ContinuousEmitter = "emit_continuously";

    /// <summary>The one-shot emitter — what an explosion is made of.</summary>
    private const string BurstEmitter = "emit_instantaneously";

    /// <summary>The systems this one runs alongside itself.</summary>
    /// <remarks>
    /// **A child is a full collection, not a decoration.** `rockettrail` declares two —
    /// `rockettrail_burst` and `rockettrail_fire` — each with its own emitters, operators and
    /// MATERIAL, which is why drawing them needs a draw per material rather than one more batch.
    /// Without them a rocket has smoke and no glow.
    /// </remarks>
    public IReadOnlyList<ParticleEffect> Children { get; }

    /// <summary>The effect's world box: its particles and its children's, each grown by its radius, holding control point 0.</summary>
    /// <returns>The box's two corners; with no particles anywhere, control point 0 alone.</returns>
    /// <remarks>
    /// What the leaf system files the effect by (finding 84, D1). `BloatBoundsUsingControlPoint` keeps control point 0
    /// inside the box, and a collection with no particles is its sort origin (particles_new.cpp:351-361). **The radius is
    /// interpolated**: the per-particle growth happens inside the closed `particles.lib`.
    /// </remarks>
    public (Vector3 Min, Vector3 Max) Bounds()
    {
        Vector3 point = ControlPoint(0).At;
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        Grow(this, ref min, ref max);

        return min.X > max.X ? (point, point) : (Vector3.Min(min, point), Vector3.Max(max, point));
    }

    private static void Grow(ParticleEffect effect, ref Vector3 min, ref Vector3 max)
    {
        ParticleStore store = effect.Particles;

        for (int index = 0; index < store.Count; index++)
        {
            Vector3 reach = new(store.Radius[index]);

            min = Vector3.Min(min, store.Position[index] - reach);
            max = Vector3.Max(max, store.Position[index] + reach);
        }

        foreach (ParticleEffect child in effect.Children)
        {
            Grow(child, ref min, ref max);
        }
    }

    /// <summary>Starts an instance of one system.</summary>
    /// <param name="system">The definition.</param>
    /// <param name="others">
    /// Every system that could be a child, by name, or null for an instance with none. A child is
    /// referred to BY NAME and may live in another file, so the caller resolves rather than this.
    /// </param>
    /// <param name="sheets">
    /// The sheet each system's material carries — the collection's `m_Sheet`, which `Lifetime From Sequence` reads — or
    /// null when none is to hand.
    /// </param>
    /// <param name="seed">
    /// The collection's <c>m_nRandomSeed</c> (B469) — every random draw's index starts from it. Non-zero is the engine's
    /// seeded ("scrubbable") collection, which a caller that can seek must use; see the remarks.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="system"/> is null.</exception>
    /// <remarks>
    /// **A child that cannot be resolved is skipped rather than throwing**, because a `.pcf` names
    /// children across files and this project reads one. Skipping loses a glow; refusing loses the
    /// trail as well.
    ///
    /// **A system is never its own child**, and the guard is not paranoia: a cycle here would
    /// recurse until the stack ran out, at load, on a file this project does not control.
    ///
    /// **The seed, read in the disassembly of `CParticleCollection::Init( pDef, flDelay, nRandomSeed )`** in the SDK's
    /// `particles.lib`: <c>m_bIsScrubbable = nRandomSeed != 0</c>; a non-zero seed is kept, and a zero one becomes
    /// <c>(int)this + Plat_MSTime()</c>. The child loop sets each child's seed at the top of every pass —
    /// <c>lea eax,[r15+0x81]; test r15d,r15d; cmovz eax,r15d; mov r15d,eax</c> — so entry k of the definition's
    /// `children` gets the seed plus 129 × k, unresolved entries counted, and an unseeded parent's children are unseeded.
    /// The TF2 client creates every effect unseeded, so its seeds are a pointer and a clock — an input no demo carries.
    /// **D136's adaptation:** a caller that can seek passes a seed fixed by the effect's identity, which is the engine's
    /// own seeded path; a zero seed here stays zero rather than becoming a pointer and a clock, so a test is reproducible.
    /// The HUD's panels pass a distinct seed per collection instead (`ParticleEffects.NextCreatedSeed`, B490).
    /// </remarks>
    public ParticleEffect(
        ParticleSystem system,
        IReadOnlyDictionary<string, ParticleSystem>? others = null,
        Func<ParticleSystem, IReadOnlyList<SheetSequence>?>? sheets = null,
        int seed = 0)
        : this(system, others, sheets, seed, ancestors: null)
    {
    }

    /// <summary>The names above this collection, nearest first: a child naming one of them is the cycle the guard ends.</summary>
    private sealed record Ancestry(string Name, Ancestry? Parent)
    {
        public bool Contains(string named) =>
            string.Equals(Name, named, StringComparison.OrdinalIgnoreCase) || (Parent?.Contains(named) ?? false);
    }

    private ParticleEffect(
        ParticleSystem system,
        IReadOnlyDictionary<string, ParticleSystem>? others,
        Func<ParticleSystem, IReadOnlyList<SheetSequence>?>? sheets,
        int seed,
        Ancestry? ancestors)
    {
        ArgumentNullException.ThrowIfNull(system);

        System = system;
        Particles = new ParticleStore { Seed = seed };
        _operators = ParticleOperators.All();
        _sheet = sheets?.Invoke(system);

        bool killsUnimplemented = false;
        bool killsImplemented = false;

        foreach (ParticleFunction one in system.Operators)
        {
            if (Killers.Contains(one.Function))
            {
                killsImplemented |= _operators.ContainsKey(one.Function);
                killsUnimplemented |= !_operators.ContainsKey(one.Function);
            }

            killsUnimplemented |= NotInTheSdkLibrary.Contains(one.Function);
        }

        _reapsForAKillerNotImplemented = killsUnimplemented && !killsImplemented;

        // **`InitializeContextData`, at `Init`, before any particle** (B472, B495): a burst's count is drawn from the
        // collection's query stream when the collection is made. A minimum of −1 means the count is exact, and it is what
        // every emitter in the explosion path declares; any minimum of 0 or more draws `RandomInt` in
        // `[minimum, num_to_emit]`, with no special case for a minimum at or above the count.
        foreach (ParticleFunction emitter in system.Emitters)
        {
            if (string.Equals(emitter.Function, BurstEmitter, StringComparison.Ordinal))
            {
                int count = (int)emitter.Number("num_to_emit", 0d);
                int least = (int)emitter.Number("num_to_emit_minimum", -1d);

                _bursts[emitter] = least < 0 ? count : ParticleRandom.Whole(seed, Particles.Query(), 0, least, count);
                _burstCounts[emitter] = _bursts[emitter];
            }
        }

        List<ParticleEffect> children = [];
        int running = seed;

        // **The cycle guard is the chain of names above, not a copy of the lookup without them.** The copy was the
        // whole game's 9,050 systems once per child: 342 MB of large-object allocations in twenty seconds of f12
        // (finding 80). Same rule: a child naming this system or any ancestor is not resolved.
        Ancestry path = new(system.Name, ancestors);

        foreach (string named in system.Children)
        {
            running = running == 0 ? 0 : unchecked(running + ChildSeedStep);

            if (others is not null &&
                !path.Contains(named) &&
                others.TryGetValue(named, out ParticleSystem? child))
            {
                children.Add(new ParticleEffect(child, others, sheets, running, path));
            }
        }

        Children = children;
    }

    /// <summary>What each entry of a seeded definition's `children` adds to the seed — the <c>0x81</c> in its <c>lea</c>.</summary>
    private const int ChildSeedStep = 0x81;

    /// <summary>How many of this system's operators this project implements.</summary>
    /// <returns>The count, against <see cref="ParticleSystem.Operators"/>.</returns>
    /// <remarks>
    /// **Reported rather than silently skipped**, because a missing operator is invisible in the
    /// result: the effect still draws, just wrongly. `rockettrail` is 4 of 4; a system reaching for
    /// something unimplemented should be able to say so.
    /// </remarks>
    public int Implemented()
    {
        int known = 0;

        foreach (ParticleFunction one in System.Operators)
        {
            if (_operators.ContainsKey(one.Function))
            {
                known++;
            }
        }

        return known;
    }

    /// <summary>Every control point set on this effect by number — <c>m_Position</c> and the basis; slot 0 is set by each <see cref="Step"/>.</summary>
    private readonly List<ParticleControlPoint> _points = [];

    /// <summary>Each control point's position at the end of the last simulated step — <c>m_PrevPosition</c> (B470).</summary>
    private readonly List<Vector3> _previous = [];

    /// <summary>Whether <see cref="_previous"/> has been seeded — <c>PCFLAGS_PREV_CONTROL_POINTS_INITIALIZED</c>.</summary>
    private bool _previousSet;

    /// <summary>The control points at one particle's creation time, reused.</summary>
    private readonly List<ParticleControlPoint> _spawnPoints = [];

    /// <summary>Sets one control point — <c>SetControlPoint</c> and <c>SetControlPointOrientation</c> together.</summary>
    /// <param name="number">Which one; a tracer's end is 1.</param>
    /// <param name="point">Where it is, and which way it faces.</param>
    /// <remarks>
    /// **Passed down to every child**, as the engine's own walks `m_Children` (`particles.h:1595`). Points between
    /// the last one set and this one are <see cref="ParticleControlPoint.Unset"/> — the origin with zero axes, previous
    /// position the origin too — as `CParticleCollection`'s constructor leaves every one (B496).
    ///
    /// **The position always lands; the orientation only when it is a frame** (B471). The engine sets them in two
    /// calls, and <c>SetControlPointOrientation</c> applies forward, right and up only when <c>|forward·up|</c>,
    /// <c>|forward·right|</c> and <c>|right·up|</c> are each at most 0.1 — otherwise it warns "Attempt to set particle
    /// collection %s to invalid orientation matrix" and keeps the old one (`particles.h:1616-1640`). Each child makes
    /// the same test. The warning is not reproduced.
    /// </remarks>
    public void SetControlPoint(int number, ParticleControlPoint point)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);

        while (_points.Count <= number)
        {
            _points.Add(ParticleControlPoint.Unset);
            _previous.Add(Vector3.Zero);
        }

        _points[number] = IsFrame(point) ? point : _points[number] with { At = point.At };

        foreach (ParticleEffect child in Children)
        {
            child.SetControlPoint(number, point);
        }
    }

    /// <summary>`SetControlPointOrientation`'s test: every pair of the three axes within a tenth of perpendicular.</summary>
    private static bool IsFrame(ParticleControlPoint point) =>
        MathF.Abs(Dot(point.Forward, point.Up)) <= PerpendicularTolerance
        && MathF.Abs(Dot(point.Forward, point.Right)) <= PerpendicularTolerance
        && MathF.Abs(Dot(point.Right, point.Up)) <= PerpendicularTolerance;

    /// <summary>The `0.1f` in `SetControlPointOrientation`.</summary>
    private const float PerpendicularTolerance = 0.1f;

    /// <summary>`DotProduct`, in its own order of operations.</summary>
    private static float Dot(Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>`GetControlPointAtCurrentTime( number )`: the point last set there, the origin when unset.</summary>
    /// <param name="number">Which one.</param>
    /// <returns>The control point.</returns>
    public ParticleControlPoint ControlPoint(int number) =>
        number >= 0 && number < _points.Count ? _points[number] : ParticleControlPoint.Unset;

    /// <summary>Advances the effect one step, emitting at the declared rate.</summary>
    /// <param name="at">Where the emitter is — the rocket's own position — which is control point 0.</param>
    /// <param name="seconds">How long the step is.</param>
    /// <remarks>
    /// **A child gets the parent's control point, position AND orientation**, because
    /// <see cref="SetControlPoint"/> walks `m_Children` as the engine's does (`particles.h:1595`, `:1629`). So a rocket's
    /// fire and burst follow the rocket exactly as its smoke does, rather than being placed once where it spawned.
    /// </remarks>
    public void Step(ParticleControlPoint at, float seconds)
    {
        SetControlPoint(0, at);
        Simulate(seconds, emit: true);
    }

    /// <summary>Advances without emitting, for an effect whose emitter is gone.</summary>
    /// <param name="seconds">How long the step is.</param>
    /// <remarks>
    /// **A rocket explodes and its trail hangs in the air**, fading on the particles' own schedule
    /// rather than vanishing with the blast. So the effect outlives the entity, and this is what it
    /// does in the meantime: operate and reap, emit nothing — the engine's `Simulate` with emission stopped.
    ///
    /// **Not `Step` with the last position**, which is the bug this replaced — that keeps emitting
    /// at wherever the trail happens to be and grows it for ever.
    /// </remarks>
    public void Fade(float seconds) => Simulate(seconds, emit: false);

    /// <summary>
    /// `CParticleCollection::StartEmission(false)`, which waking from dormancy calls: each continuous emitter's
    /// `StartEmission` calls `SkipToTime( curtime )` (vtable slot +0xe8, read from the RTTI-anchored table in
    /// `builtin_particle_emitters.obj`), and children follow.
    /// </summary>
    /// <remarks>
    /// `C_OP_ContinuousEmitter::SkipToTime` (`0x000670f0`, read in the disassembly):
    /// <code>
    /// if curtime &lt;= start time: return
    /// from = duration != 0 ? start time : 0;   span = min( curtime − from, 4 )
    /// total = span · rate;   emitted = 0          (mov dword [ctx+4], 0)
    /// n = min( floor( total ), room ), born evenly over ( curtime − span, curtime ]
    /// unless g_bDontMakeSkipToTimeTakeForever: curtime = max( curtime − no-draw time, curtime − span ),
    ///     then Simulate( 0.1 ) until it is reached again
    /// </code>
    /// **The duration does not cap the span, and `emitted = 0` makes the next `Emit` owe the same particles again**:
    /// the re-simulation's own `Emit` calls do exactly that. Both are Valve's code as compiled, not a reading of a
    /// decompile. TF leaves the flag false (its line in `CParticleMgr::UpdateNewEffects` is commented out,
    /// particlemgr.cpp:1826-1829).
    /// </remarks>
    public void StartEmission()
    {
        foreach (ParticleFunction emitter in System.Emitters)
        {
            if (string.Equals(emitter.Function, ContinuousEmitter, StringComparison.Ordinal))
            {
                SkipToTime(emitter);
            }
            else if (string.Equals(emitter.Function, BurstEmitter, StringComparison.Ordinal))
            {
                // `C_OP_InstantaneousEmitter::StartEmission` (`0x00066af0`): owed = the drawn count again, then its own
                // `SkipToTime( curtime )` (`0x00066b20`, slot +0xe8 of its table at `0x00065388`) cancels it once curtime
                // is past start time + 2.0 (`ADDSS` against `0x000658a8`, 2.0f). Woken within two seconds of its start,
                // a burst fires again.
                float start = (float)emitter.Number("emission_start_time", 0d);

                _bursts[emitter] = Particles.Age > start + BurstRefireWindow ? 0 : _burstCounts[emitter];
            }
        }

        foreach (ParticleEffect child in Children)
        {
            child.StartEmission();
        }
    }

    /// <summary>`C_OP_InstantaneousEmitter::SkipToTime`'s window past the start time, 2.0 (`0x000658a8`).</summary>
    private const float BurstRefireWindow = 2f;

    /// <summary>`C_OP_ContinuousEmitter::SkipToTime( curtime )`; see <see cref="StartEmission"/>.</summary>
    private void SkipToTime(ParticleFunction emitter)
    {
        float rate = (float)emitter.Number("emission_rate", DefaultEmissionRate);
        float duration = (float)emitter.Number("emission_duration", 0d);
        float from = (float)emitter.Number("emission_start_time", 0d);
        float now = Particles.Age;

        if (now <= from)
        {
            return;
        }

#pragma warning disable S1244 // Floating point equality — the engine's own `m_flEmissionDuration != 0.0` test
        float lower = duration != 0f && 0f < from ? from : 0f;
#pragma warning restore S1244
        float span = MathF.Min(now - lower, MaximumSkip);
        float start = now - span;
        float total = span * rate;

        _continuous[emitter] = (total, 0);

        int count = Math.Min((int)MathF.Floor(total), ParticleSystems.MaxParticles(System) - Particles.Count);

        if (count > 0)
        {
            float step = span / count;
            float born = step + start;
            int first = Particles.Count;

            for (int made = 0; made < count; made++)
            {
                born = MathF.Min(born, now);
                BirthAt(born);
                born += step;
            }

            InitializeFrom(first);
        }

        float back = MathF.Max(now - (float)SleepAfter(System), start);

        Particles.RewindTo(back);

        // The counter is a float stepped through a double (`CVTSS2SD`, `ADDSD`, `CVTPD2PS`), as compiled.
        for (float at = back; at < now; at = (float)(at + SkipStep))
        {
            Simulate(SkipStepSeconds, emit: true);
        }
    }

    /// <summary>`SkipToTime`'s cap on the span it emits, 4.0 (`MINSS` against `0x000658ac`).</summary>
    private const float MaximumSkip = 4f;

    /// <summary>`SkipToTime`'s re-simulation step, 0.1 as a double for the loop and a float for the call.</summary>
    private const double SkipStep = 0.1d;

    /// <summary>The same step as the float `Simulate` takes.</summary>
    private const float SkipStepSeconds = 0.1f;

    /// <summary>
    /// Seconds undrawn before a system sleeps: `time to sleep when not drawn`, default "8"
    /// (`DmxElementUnpackInit&lt;CParticleSystemDefinition&gt;`, particles.obj, read in Ghidra), at definition offset 0x234.
    /// </summary>
    /// <param name="system">The definition.</param>
    /// <returns>The no-draw time, in seconds.</returns>
    public static double SleepAfter(ParticleSystem system) =>
        ParticleSystems.Declared(system, "time to sleep when not drawn", 8d);

    /// <summary>
    /// The longest an effect of this definition can be alive, drawn throughout: its last emission, plus the longest particle
    /// lifetime, plus the no-draw time — the quantities <see cref="Finished"/> and <see cref="SleepAfter"/> read.
    /// </summary>
    /// <param name="system">The definition.</param>
    /// <param name="others">Every system that could be a child, by name; null for none.</param>
    /// <returns>Seconds, or <see cref="double.PositiveInfinity"/> when any part is forever or cannot be bounded here.</returns>
    /// <remarks>
    /// **An upper bound, and an unknown is forever**: a continuous emitter with no duration emits for ever (the sentinel
    /// <see cref="Finished"/> reads); an emitter this does not bound, a lifetime written from positions or remapped, and a
    /// child not supplied make the life unbounded. An effect older than this has finished, so a caller may decline to build
    /// it (finding 84); one asleep is not covered, since its clock stands.
    /// </remarks>
    /// <param name="sheets">The sheet each system's material carries, as <see cref="ParticleEffect"/> takes it; null for none.</param>
    public static double MaximumLife(
        ParticleSystem system,
        IReadOnlyDictionary<string, ParticleSystem>? others,
        Func<ParticleSystem, IReadOnlyList<SheetSequence>?>? sheets = null)
    {
        ArgumentNullException.ThrowIfNull(system);

        return Lived(system, others, sheets, depth: 0) + SleepAfter(system);
    }

    /// <summary>How deep a child chain is followed before it is called unbounded — a cycle in the data would never end.</summary>
    private const int DeepestChild = 16;

    /// <summary>The last emission plus the longest particle life — drawn throughout, the root's sleep apart; a child's own sleep is added.</summary>
    private static double Lived(
        ParticleSystem system,
        IReadOnlyDictionary<string, ParticleSystem>? others,
        Func<ParticleSystem, IReadOnlyList<SheetSequence>?>? sheets,
        int depth)
    {
        if (depth > DeepestChild)
        {
            return double.PositiveInfinity;
        }

        double emitted = 0d;

        foreach (ParticleFunction emitter in system.Emitters)
        {
            double start = emitter.Number("emission_start_time", 0d);

            if (string.Equals(emitter.Function, ContinuousEmitter, StringComparison.Ordinal))
            {
                double duration = emitter.Number("emission_duration", 0d);

                if (duration <= 0d)
                {
                    return double.PositiveInfinity;
                }

                emitted = Math.Max(emitted, start + duration);
            }
            else if (string.Equals(emitter.Function, BurstEmitter, StringComparison.Ordinal))
            {
                emitted = Math.Max(emitted, start);
            }
            else
            {
                return double.PositiveInfinity;
            }
        }

        double lifetime = DefaultLifetime;

        // **Every lifetime writer ParticleSystems runs, and no other** (review): an initializer the viewer does not run writes
        // no lifetime here, so the bound is the viewer's.
        foreach (ParticleFunction initializer in system.Initializers)
        {
            if (string.Equals(initializer.Function, "Lifetime Random", StringComparison.Ordinal))
            {
                // `C_INIT_RandomLifeTime` as ParticleSystems reads it: `lifetime_min` defaults to the life as it stands,
                // `lifetime_max` to `lifetime_min`, and the draw lies between them either way round.
                double least = initializer.Number("lifetime_min", lifetime);

                lifetime = Math.Max(least, initializer.Number("lifetime_max", least));
            }
            else if (string.Equals(initializer.Function, "move particles between 2 control points", StringComparison.Ordinal) ||
                (string.Equals(initializer.Function, "remap initial scalar", StringComparison.Ordinal) &&
                    (int)initializer.Number("output field", 3d) == LifeDurationField))
            {
                // Distance over speed, and a remap onto the life: neither has a bound in the definition.
                return double.PositiveInfinity;
            }
            else if (string.Equals(initializer.Function, "Lifetime From Sequence", StringComparison.Ordinal))
            {
                // `C_INIT_SequenceLifeTime`, as ParticleSystems runs it: with a rate and a sheet, a sequence's span over the
                // rate, or 1 for a zero span; otherwise nothing, and the earlier lifetime stands.
                double rate = initializer.Number("Frames Per Second", 30d);

                if (rate != 0d && sheets?.Invoke(system) is { } sheet)
                {
                    double longest = 1d;

                    foreach (SheetSequence sequence in sheet)
                    {
                        longest = Math.Max(longest, sequence.TotalTime / rate);
                    }

                    lifetime = Math.Max(lifetime, longest);
                }
            }
        }

        double life = emitted + lifetime;

        foreach (string name in system.Children)
        {
            if (others is null || !others.TryGetValue(name, out ParticleSystem? child))
            {
                return double.PositiveInfinity;
            }

            life = Math.Max(life, Lived(child, others, sheets, depth + 1) + SleepAfter(child));
        }

        return life;
    }

    /// <summary>`PARTICLE_ATTRIBUTE_LIFE_DURATION`, the life's field number (`particles.h:63`), as a remap names it.</summary>
    private const int LifeDurationField = 1;

    /// <summary>The shortest step `CParticleCollection::Simulate` simulates — its <c>dt &gt;= 1e-22</c>.</summary>
    private const double ShortestStep = 1e-22;

    /// <summary>`CParticleCollection::Simulate`, read in the disassembly of `particles.lib`'s `particles.obj`.</summary>
    /// <remarks>
    /// <code>
    /// first frame:  m_PrevPosition = m_Position for every control point (SimulateFirstFrame)
    /// if ( dt &gt;= 1e-22 ):
    ///     m_flDt = dt;  m_flCurTime += dt;  emit, operate
    ///     every child: Simulate( dt )
    ///     m_PrevPosition = m_Position for every control point (UpdatePrevControlPoints)
    /// </code>
    /// **Emit, operate, reap.** A particle born this step is operated on this step, so it never appears at its raw spawn
    /// state; reaping last means one that died this step is gone before anything draws it. **A paused step simulates
    /// nothing**, not even the previous positions, so the step after it lerps from where the last real one ended.
    ///
    /// **One call is several sub-steps** (B492), each its own `m_flDt`, read in the same function:
    /// <code>
    /// step = "maximum time step" &gt; 0 ? it : 0.1
    /// if ( "maximum sim tick rate" != 0 and frames ≤ "minimum rendered frames" ):
    ///     if ( curtime + dt &gt; maximum ):  dt = max( maximum − curtime, "minimum sim tick rate" );   frames++
    /// left = min( dt, step · 10 );  while ( left &gt; 0 ):  m_flDt = min( left, step );  left −= m_flDt;  emit, operate
    /// </code>
    /// The previous control points are not updated between sub-steps, only after the call, so a sub-step lerps from the
    /// call's start across a window one sub-step wide — the engine's arithmetic, reproduced rather than tidied. Children
    /// are given the whole step and cut it themselves.
    /// </remarks>
    private void Simulate(float seconds, bool emit)
    {
        if (!_previousSet)
        {
            RememberPoints();
            _previousSet = true;
            CreateInitial();
        }

        if (seconds < ShortestStep)
        {
            return;
        }

        float step = ParticleSystems.Declared(System, "maximum time step", 0d) is var declared and > 0d
            ? (float)declared
            : DefaultMaximumStep;
        float span = Clamped(seconds);
        float left = MathF.Min(span, step * MaximumSteps);

        while (left > 0f)
        {
            float sub = MathF.Min(left, step);

            left -= sub;
            SubStep(sub, emit);
        }

        foreach (ParticleEffect child in Children)
        {
            child.Simulate(seconds, emit);
        }

        Particles.EndCall(seconds);
        RememberPoints();
    }

    /// <summary>`SimulateFirstFrame`'s particles: `min( initial_particles, m_nMaxAllowedParticles )`, every initializer run (B491).</summary>
    /// <remarks>
    /// Created before the first step with `m_flDt` still 0, so `GetControlPointAtTime` gives each the point's present
    /// position, and dated to the collection's present, 0. *Not built:* the operators `SimulateFirstFrame` runs first,
    /// those that ask to run before the emitters (`ShouldRunBeforeEmitters`); no operator this project implements does.
    /// </remarks>
    private void CreateInitial()
    {
        int count = Math.Min(
            (int)ParticleSystems.Declared(System, "initial_particles", 0d),
            ParticleSystems.MaxParticles(System) - Particles.Count);

        int first = Particles.Count;

        for (int made = 0; made < count; made++)
        {
            BirthAt(Particles.Age);
        }

        InitializeFrom(first);
    }

    /// <summary>`m_flMaximumTimeStep`'s default, `"0.1"` in the unpack table and `0x3dcccccd` in `Simulate`.</summary>
    private const float DefaultMaximumStep = 0.1f;

    /// <summary>The `10.0f` a call's simulated span is capped at, in maximum steps.</summary>
    private const float MaximumSteps = 10f;

    /// <summary>`m_nSimulatedFrames`: the calls the maximum sim time has clamped.</summary>
    private int _simulatedFrames;

    /// <summary>The first frames' clamp to `maximum sim tick rate` (`m_flMaximumSimTime`), as `Simulate` applies it.</summary>
    private float Clamped(float seconds)
    {
        float maximum = (float)ParticleSystems.Declared(System, "maximum sim tick rate", 0d);

#pragma warning disable S1244 // Floating point equality — the engine's own `m_flMaximumSimTime != 0` test
        if (maximum == 0f || _simulatedFrames > (int)ParticleSystems.Declared(System, "minimum rendered frames", 0d))
#pragma warning restore S1244
        {
            return seconds;
        }

        _simulatedFrames++;

        return maximum < Particles.Age + seconds
            ? MathF.Max(maximum - Particles.Age, (float)ParticleSystems.Declared(System, "minimum sim tick rate", 0d))
            : seconds;
    }

    /// <summary>One sub-step: the clock, emission, every operator, the reap.</summary>
    private void SubStep(float seconds, bool emit)
    {
        Particles.Tick(seconds);

        if (emit)
        {
            Emit(seconds);
        }

        foreach (ParticleFunction one in System.Operators)
        {
            if (_operators.TryGetValue(one.Function, out IParticleOperator? run))
            {
                run.Operate(Particles, one, seconds);

                if (run is MovementBasic)
                {
                    ApplyConstraints(one);
                }
            }
            else if (string.Equals(one.Function, MovementLock.Named, StringComparison.Ordinal))
            {
                if (!_locks.TryGetValue(one, out MovementLock? locked))
                {
                    locked = new MovementLock();
                    _locks[one] = locked;
                }

                int number = (int)one.Number("control_point_number", 0d);

                locked.Operate(Particles, one, seconds, ControlPoint(number));
            }
            else if (string.Equals(one.Function, LockToBone.Named, StringComparison.Ordinal))
            {
                // `InitParams` clamps the number to the control points there are, 0..63.
                (IReadOnlyList<ModelHitbox> now, IReadOnlyList<ModelHitbox> before) =
                    HitboxesOf(Math.Clamp((int)one.Number("control_point_number", 0d), 0, 63));

                LockToBone.Operate(Particles, one, now, before);
            }
        }

        // The next simulation's `UpdateHitBoxInfo` finds this one's boxes as its previous ones, so a second sub-step, or
        // a step nobody posed the model for, moves nothing twice.
        if (_hitboxes.Count > 0)
        {
            foreach (int number in _hitboxes.Keys)
            {
                _settled.Add(number);
            }

            foreach (int number in _settled)
            {
                _hitboxes[number] = (_hitboxes[number].Now, _hitboxes[number].Now);
            }

            _settled.Clear();
        }

        // **No age kill of our own** (B557 row 3): `CParticleCollection::Simulate` has none, and a particle dies only by an
        // operator that writes the kill list — the implemented ones reap inside `Operate`. A system whose only killers are
        // ones not implemented here is reaped at the lifetime instead, a STAND-IN so our gap does not make them immortal.
        if (_reapsForAKillerNotImplemented)
        {
            Particles.Reap();
        }
    }

    /// <summary>
    /// The operators that can end a particle — the six in `builtin_particle_ops.obj` that write the kill list (+0x276c):
    /// `C_OP_Decay`, `C_OP_VelocityDecay`, `C_OP_Cull`, `C_OP_PlaneCull`, `C_OP_ModelCull`, `C_OP_FadeAndKill`.
    /// </summary>
    public static IReadOnlySet<string> Killers { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Lifespan Decay", "Lifespan Minimum Velocity Decay", "Cull Random", "Cull when crossing plane",
        "Cull relative to model", "Alpha Fade and Decay",
    };

    /// <summary>
    /// Operator names shipped `.pcf` files declare that the SDK's 2013 `particles.lib` does not define, though TF2's own
    /// `client.dll` carries them — so whether they kill is unread, and a system declaring one keeps the stand-in reap.
    /// </summary>
    /// <remarks>Found by `particles killers` over the systems with no known killer; absent from `builtin_particle_ops.obj`'s strings.</remarks>
    public static IReadOnlySet<string> NotInTheSdkLibrary { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "alpha_fade", "alpha_fade_in_random", "alpha_fade_out_random", "basic_movement", "radius_scale",
        "oscillate_vector", "oscillate_scalar", "postion_lock_to_controlpoint", "rotation_spin", "rotation_spin yaw",
        "lifespan_decay",
    };

    /// <summary>Whether this system kills only through operators this project does not implement; see <see cref="SubStep"/>.</summary>
    private readonly bool _reapsForAKillerNotImplemented;

    /// <summary>`CParticleCollection::HasMoved` (`0x000e1410`): a control point away from its previous position, or a child's.</summary>
    /// <returns>Whether this effect or any child moved since its last simulation.</returns>
    /// <remarks>
    /// The engine asks only the points the definition reads (its mask at definition +0x20); this project keeps no such
    /// mask, so every point set is asked — an APPROXIMATION, wider than the engine's.
    /// </remarks>
    public bool HasMoved()
    {
        for (int number = 0; number < _points.Count; number++)
        {
            if (_points[number].At != _previous[number])
            {
                return true;
            }
        }

        foreach (ParticleEffect child in Children)
        {
            if (child.HasMoved())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>`Simulate( dt, true )`: an asleep effect that moved — no time, no emission, no operator (B557 row 9).</summary>
    /// <param name="seconds">The frame's time.</param>
    /// <remarks>
    /// Read in `particles.obj`: with `updateBboxOnly` the sub-step loop is skipped, and still every child is given the
    /// same call, `m_flPreviousDt` is set to `dt` and the previous control points to the present ones. The bounds are not
    /// kept here.
    /// </remarks>
    public void SimulateBoundsOnly(float seconds)
    {
        if (seconds < ShortestStep || !_previousSet)
        {
            return;
        }

        foreach (ParticleEffect child in Children)
        {
            child.SimulateBoundsOnly(seconds);
        }

        Particles.EndCall(seconds);
        RememberPoints();
    }

    /// <summary>`UpdatePrevControlPoints`: every control point's position, kept as its previous one.</summary>
    private void RememberPoints()
    {
        for (int number = 0; number < _points.Count; number++)
        {
            _previous[number] = _points[number].At;
        }
    }

    /// <summary>
    /// The definition's constraints, as `C_OP_BasicMovement::Operate` runs them after integrating (B396).
    /// </summary>
    /// <remarks>
    /// <code>
    /// for pass in 0 .. "max constraint passes" (3):
    ///     for each constraint not yet satisfied this round:  mark it;  if it moved anything, un-mark every other
    /// </code>
    /// *Not built:* the "final" constraints the engine runs once after the passes, and every constraint but the path's.
    /// </remarks>
    private void ApplyConstraints(ParticleFunction movement)
    {
        IReadOnlyList<ParticleFunction> constraints = System.Constraints;

        if (constraints.Count == 0)
        {
            return;
        }

        int passes = (int)movement.Number("max constraint passes", 3d);
        Span<bool> satisfied = stackalloc bool[constraints.Count];

        for (int pass = 0; pass < passes; pass++)
        {
            for (int index = 0; index < constraints.Count; index++)
            {
                if (satisfied[index])
                {
                    continue;
                }

                satisfied[index] = true;

                if (Enforce(constraints[index], _points))
                {
                    satisfied.Clear();
                    satisfied[index] = true;
                }
            }
        }
    }

    /// <summary>One constraint's `EnforceConstraint`; false for one this project does not implement.</summary>
    private bool Enforce(ParticleFunction constraint, IReadOnlyList<ParticleControlPoint> points) =>
        string.Equals(constraint.Function, PathConstraint.Named, StringComparison.Ordinal) &&
        PathConstraint.Enforce(Particles, constraint, points);

    /// <summary>Each declared `Movement Lock to Control Point` with its own context, as the engine gives each operator one.</summary>
    private readonly Dictionary<ParticleFunction, MovementLock> _locks = new(ReferenceEqualityComparer.Instance);

    /// <summary>Runs every emitter the definition declares.</summary>
    private void Emit(float seconds)
    {
        foreach (ParticleFunction emitter in System.Emitters)
        {
            if (string.Equals(emitter.Function, BurstEmitter, StringComparison.Ordinal))
            {
                EmitBurst(emitter);
            }
            else if (string.Equals(emitter.Function, ContinuousEmitter, StringComparison.Ordinal))
            {
                EmitContinuously(emitter, seconds);
            }
        }
    }

    /// <summary>`C_OP_ContinuousEmitter::Emit`, read in the disassembly of `builtin_particle_emitters.obj` (B470).</summary>
    /// <remarks>
    /// <code>
    /// if rate &gt; 0 and ( duration == 0 or curtime − dt ≤ start time + duration ) and start time ≤ curtime:
    ///     start = curtime − dt;  end = curtime
    ///     if duration != 0:  start = max( start, start time );  end = min( end, duration + start time )
    ///     total += ( end − start ) · rate;  n = floor( total ) − emitted;  emitted += n
    ///     n = min( n, room );  step = ( end − start ) / n
    ///     CREATION_TIME of the k-th = min( start + k · step, end )
    /// </code>
    /// **Zero duration is FOREVER**, the sentinel this class's remarks name, and then the window is the whole step even
    /// in the step the start time falls in. **A particle the cap refuses is not owed**: `emitted` counts it anyway.
    /// </remarks>
    private void EmitContinuously(ParticleFunction emitter, float seconds)
    {
        float rate = (float)emitter.Number("emission_rate", DefaultEmissionRate);
        float duration = (float)emitter.Number("emission_duration", 0d);
        float from = (float)emitter.Number("emission_start_time", 0d);
        float now = Particles.Age;

#pragma warning disable S1244 // Floating point equality — the engine's own `m_flEmissionDuration == 0.0` sentinel
        bool forever = duration == 0f;
#pragma warning restore S1244

        // The engine's gate as written. Its first two refusals change nothing a test can see — past the window the
        // clamped span is negative and a rate of 0 adds nothing, so neither would emit — and a mutant on them is
        // equivalent; the start-time refusal is the one that matters.
        if (rate <= 0f || (!forever && now - seconds > from + duration) || from > now)
        {
            return;
        }

        float start = now - seconds;
        float end = now;

        if (!forever)
        {
            start = MathF.Max(start, from);
            end = MathF.Min(end, duration + from);
        }

        (float total, int emitted) = _continuous.GetValueOrDefault(emitter);

        total = ((end - start) * rate) + total;

        int owed = (int)Math.Floor(total) - emitted;

        _continuous[emitter] = (total, emitted + owed);

        int count = Math.Min(owed, ParticleSystems.MaxParticles(System) - Particles.Count);

        if (count <= 0)
        {
            return;
        }

        float step = (end - start) / count;
        float born = step + start;
        int first = Particles.Count;

        for (int made = 0; made < count; made++)
        {
            born = MathF.Min(born, end);
            BirthAt(born);
            born += step;
        }

        InitializeFrom(first);
    }

    /// <summary>Every control point as it was at a creation time, into a reused list; point 0 is never missing.</summary>
    /// <remarks>
    /// `GetControlPointAtTime( cp, t )`, read in `particles.obj`, which every initializer here that reads a point's
    /// position uses at `CREATION_TIME` (`C_INIT_CreateWithinSphere`, `C_INIT_PositionOffset`, `C_INIT_MoveBetweenPoints`,
    /// and `CalculatePathValues` for `C_INIT_CreateAlongPath`):
    /// <code>
    /// dt = m_flDt;  dt == 0 → m_Position
    /// f = ( dt − ( curtime − t ) ) / dt,  0 when that is ≤ 0, and not clamped above
    /// ( m_Position − m_PrevPosition ) · f + m_PrevPosition
    /// </code>
    /// The `dt == 0` arm serves `SimulateFirstFrame`'s initial particles (B491), created before any step; every other
    /// emission happens inside a step <see cref="Simulate"/> let through, so `dt` is positive. The orientation is the
    /// current one: `GetControlPointTransformAtTime` puts the lerped position under the point's present axes.
    /// </remarks>
    private List<ParticleControlPoint> PointsAt(float born)
    {
        float dt = Particles.LastStep;

        // The arm's VALUE is unobservable: on the first frame the previous points were just set to the present ones. What
        // it prevents is 0 / 0, a NaN position.
#pragma warning disable S1244 // Floating point equality — the engine's own `m_flDt == 0` arm
        float along = dt == 0f ? 1f : MathF.Max((dt - (Particles.Age - born)) / dt, 0f);
#pragma warning restore S1244

        _spawnPoints.Clear();

        for (int number = 0; number < _points.Count; number++)
        {
            ParticleControlPoint now = _points[number];
            Vector3 then = _previous[number];

            _spawnPoints.Add(now with { At = ((now.At - then) * along) + then });
        }

        if (_spawnPoints.Count == 0)
        {
            _spawnPoints.Add(ParticleControlPoint.Unset);
        }

        return _spawnPoints;
    }

    /// <summary>Adds one particle at its creation time, on control point 0 as it was then; no initializer has run.</summary>
    private void BirthAt(float born)
    {
        ParticleSystems.Birth(System, Particles, PointsAt(born)[0].At, DefaultLifetime, born);
        // TryAdd is atomic, so exactly one caller sees a system's first birth and counts it.
        if (BirthsBySystem.TryAdd(System.Name, 1L))
        {
            global::System.Threading.Interlocked.Increment(ref _systemsBorn);
        }
        else
        {
            BirthsBySystem.AddOrUpdate(System.Name, 1L, static (_, born) => born + 1);
        }
    }

    /// <summary>`InitializeNewParticles( first, count )`: every initializer over the particles born since <paramref name="first"/>.</summary>
    /// <remarks>
    /// **One batch per emitter call, initializer-major** (B495): the engine calls it once an emitter's `Emit` returns, for
    /// all the particles that call added, and a seeded collection runs each initializer over every one of them before the
    /// next. Each particle reads the control points at its own creation time. The velocity initializers scale by
    /// `m_flPreviousDt`, not by this sub-step (B494).
    /// </remarks>
    private void InitializeFrom(int first)
    {
        int count = Particles.Count - first;

        if (count <= 0)
        {
            return;
        }

        ParticleSystems.Initialize(
            System,
            Particles,
            first,
            count,
            index =>
            {
                List<ParticleControlPoint> points = PointsAt(Particles.Born[index]);

                return (points[0], points);
            },
            Particles.PreviousStep,
            _sheet,
            number => (ControlPoint(number), HitboxesOf(number).Now));
    }

    /// <summary>Each control point's entity's posed hitboxes, this step's and the last one's — <c>m_ControlPointHitBoxes</c>.</summary>
    private readonly Dictionary<int, (IReadOnlyList<ModelHitbox> Now, IReadOnlyList<ModelHitbox> Before)> _hitboxes = [];

    /// <summary>Gives a control point its entity's hitboxes as they are posed now — <c>UpdateHitBoxInfo</c> (B538).</summary>
    /// <param name="number">Which control point.</param>
    /// <param name="boxes">The entity's hitboxes, in set order; empty when the point follows no model.</param>
    /// <remarks>
    /// **Call once before each <see cref="Step"/>**: the engine swaps its two buffers the first time a function asks
    /// in a simulation (`m_flLastUpdateTime` against `m_flCurTime`), so "before" is what the last simulation saw.
    /// Passed down to every child, each of which keeps its own in the engine.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="boxes"/> is null.</exception>
    public void SetHitboxes(int number, IReadOnlyList<ModelHitbox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);

        _hitboxes[number] = (boxes, HitboxesOf(number).Now);

        foreach (ParticleEffect child in Children)
        {
            child.SetHitboxes(number, boxes);
        }
    }

    /// <summary>The control points whose boxes a sub-step settles, reused.</summary>
    private readonly List<int> _settled = [];

    /// <summary>A control point's hitboxes; none for a point never given any.</summary>
    private (IReadOnlyList<ModelHitbox> Now, IReadOnlyList<ModelHitbox> Before) HitboxesOf(int number) =>
        _hitboxes.TryGetValue(number, out (IReadOnlyList<ModelHitbox> Now, IReadOnlyList<ModelHitbox> Before) found) ? found : ([], []);

    /// <summary>Emits a burst's particles once, at its own start time — <c>emit_instantaneously</c>.</summary>
    /// <remarks>
    /// **An explosion is a burst, and this is most of one.** Six of `ExplosionCore_Wall`'s eight children
    /// declare this emitter; with only `emit_continuously` implemented, exactly one part of an explosion drew —
    /// the debris chunks — and a blast on a wall in `cp_process_f12` came out as fifteen hard orange polygons.
    /// **Seen before it was diagnosed** (B415).
    ///
    /// **Its parameters are read from the shipped `.pcf`**, which states what a closed emitter is parameterised
    /// by (`docs/memory/nothing-is-closed.md`). `Explosion_Smoke_1` declares:
    ///
    /// <code>
    ///   num_to_emit = 8            num_to_emit_minimum = -1
    ///   emission_start_time = 0    maximum emission per frame = 100
    /// </code>
    ///
    /// **`num_to_emit_minimum` of −1 is "no range", not "emit none"** — the count is exactly `num_to_emit`.
    /// A non-negative value makes the count a random one in `[minimum, num_to_emit]`, which is why the sentinel
    /// cannot be read as a bound (`docs/memory/sentinels-conflate-unknown-with-answer.md`).
    ///
    /// **The per-frame cap is honoured rather than ignored because TF2's own values are under it.** A system
    /// asking for more than it may emit in one step carries the rest to the next, so the cap delays a burst
    /// instead of truncating it. Nothing in the explosion path reaches 100; implementing what the parameter says
    /// costs three lines and not implementing it is a divergence waiting for a bigger effect.
    ///
    /// **`C_OP_InstantaneousEmitter::Emit`, read in the disassembly of `builtin_particle_emitters.obj` (B470)**: every
    /// particle's `CREATION_TIME` is the start time — the context's start is 0 — however many steps the per-frame cap
    /// spreads the burst over. At `max_particles` only the refused part of the step's share is dropped, and the rest of
    /// the count stays owed: `share = min( owed, per frame )`, `emitted = min( share, room )`, `owed −= share` (B493).
    /// </remarks>
    private void EmitBurst(ParticleFunction emitter)
    {
        float start = (float)emitter.Number("emission_start_time", 0d);

        if (Particles.Age < start)
        {
            return;
        }

        int owed = _bursts[emitter];

        // `C_OP_InstantaneousEmitter::Emit` (B493): the step's share is taken off what is owed whether or not it fits;
        // only the part of it the cap refuses is lost, and the rest stays owed.
        int share = Math.Min(owed, (int)emitter.Number("maximum emission per frame", int.MaxValue));
        int fits = Math.Min(share, ParticleSystems.MaxParticles(System) - Particles.Count);
        int first = Particles.Count;

        for (int born = 0; born < fits; born++)
        {
            BirthAt(start);
        }

        InitializeFrom(first);

        _bursts[emitter] = owed - Math.Max(share, 0);
    }

    /// <summary>What each burst emitter still owes, so it fires once rather than every step.</summary>
    /// <remarks>
    /// **Keyed by the emitter, because a system may declare several** and each has its own start time and count.
    /// A single flag would make the second one silently follow the first.
    /// </remarks>
    private readonly Dictionary<ParticleFunction, int> _bursts = [];

    /// <summary>Each burst emitter's drawn count, the context's second word, which `StartEmission` re-arms from.</summary>
    private readonly Dictionary<ParticleFunction, int> _burstCounts = [];

    /// <summary>Whether every burst emitter has fired and has nothing left owing.</summary>
    internal bool BurstsSpent
    {
        get
        {
            foreach (ParticleFunction emitter in System.Emitters)
            {
                // Stryker disable all : 'owed' is declared by the TryGetValue and read after the '||', so the
                // Logical mutator's switch declares it twice or leaves it unassigned (CS0128/CS0165) — B410.
                if (string.Equals(emitter.Function, BurstEmitter, StringComparison.Ordinal) &&
                    (!_bursts.TryGetValue(emitter, out int owed) || owed > 0))
                {
                    return false;
                }

                // Stryker restore all
            }

            return true;
        }
    }
}
