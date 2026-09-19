using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The simulation's heartbeat — the PSI event <c>FUN_18008a020</c>, which rebases the queue, runs the pipeline and requeues
/// itself (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_18008a020` is the PSI EVENT*). The engine's simulation is an event
/// that reschedules itself one step later in the time manager's ONE queue — the queue every pair's event sits in too — and the
/// rebase happens on every one of them, so no queued time is ever far from its base.
/// </remarks>
public sealed class IvpPsiEvent : IIvpTimeEvent
{
    private readonly IvpImpactEnvironment _environment;
    private readonly IvpTimeManager<IIvpTimeEvent> _time;
    private readonly IvpUnitManager _units;
    private readonly IvpMindistManager _mindists;
    private readonly Action<IvpMindist> _minimize;
    private readonly Action<IvpMindist> _recheckInvalidMinimize;
    private readonly Action<IvpMindist> _examine;
    private readonly Action<IvpSimulationUnit> _wake;
    private readonly Func<float> _random;

    private IvpPsiEvent(
        IvpImpactEnvironment environment,
        IvpTimeManager<IIvpTimeEvent> time,
        IvpUnitManager units,
        IvpMindistManager mindists,
        Action<IvpMindist> minimize,
        Action<IvpMindist> recheckInvalidMinimize,
        Action<IvpMindist> examine,
        Action<IvpSimulationUnit> wake,
        Func<float> random)
    {
        _environment = environment;
        _time = time;
        _units = units;
        _mindists = mindists;
        _minimize = minimize;
        _recheckInvalidMinimize = recheckInvalidMinimize;
        _examine = examine;
        _wake = wake;
        _random = random;
    }

    /// <inheritdoc/>
    public int? QueueSlot { get; set; }

    /// <summary>Queues the first PSI event, due at the base.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="time">The time manager whose queue the event lives in — the one every pair's event is queued in.</param>
    /// <param name="units">The unit lists.</param>
    /// <param name="mindists">The mindist manager the pipeline walks.</param>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="recheckInvalidMinimize">The minimize with no step budget, <c>FUN_180095ad0</c> — see <see cref="IvpPhysicsPipeline.Psi"/>.</param>
    /// <param name="examine">The scheduler in mode 1, <c>FUN_180099380(mindist, 1, 1)</c>.</param>
    /// <param name="wake">The unit wake, <c>FUN_1800758e0</c> — see <see cref="IvpPhysicsPipeline.Psi"/>.</param>
    /// <param name="random">The jitter the rest check's cadence takes.</param>
    /// <returns>The event, queued.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IvpPsiEvent Start(
        IvpImpactEnvironment environment,
        IvpTimeManager<IIvpTimeEvent> time,
        IvpUnitManager units,
        IvpMindistManager mindists,
        Action<IvpMindist> minimize,
        Action<IvpMindist> recheckInvalidMinimize,
        Action<IvpMindist> examine,
        Action<IvpSimulationUnit> wake,
        Func<float> random)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(mindists);
        ArgumentNullException.ThrowIfNull(minimize);
        ArgumentNullException.ThrowIfNull(recheckInvalidMinimize);
        ArgumentNullException.ThrowIfNull(examine);
        ArgumentNullException.ThrowIfNull(wake);
        ArgumentNullException.ThrowIfNull(random);

        IvpPsiEvent psi = new(environment, time, units, mindists, minimize, recheckInvalidMinimize, examine, wake, random);
        psi.QueueSlot = time.Queue.Add(psi, 0f);
        return psi;
    }

    /// <summary>
    /// One PSI: the rebase, the pipeline, and the next event — <c>FUN_18008a020</c>, the event's slot 1, IVP's
    /// <c>simulate_time_event</c>, which the time manager calls.
    /// </summary>
    /// <param name="now">The time the event fired at, <c>env+0x188</c>.</param>
    /// <remarks>
    /// <code>
    /// env+0x198 = env+0x188;  env+0x190 = (float)env+0x108 + env+0x188      -- the base, then the next PSI
    /// every queued entry's time, and the minimum, −= (float)now;  tm+0x28 = env+0x198;  tm+0x20 = 0
    /// FUN_180082560(env)                                                     -- the pipeline
    /// requeue this event at (float)(env+0x190 − tm+0x28)
    /// </code>
    /// **The step is added as a float** — `(float)env+0x108 + env+0x188` — so the next PSI's time carries the float's error, not
    /// the double's.
    /// </remarks>
    public void SimulateTimeEvent(double now)
    {
        _environment.Now = now;
        _environment.RebaseBase = now;
        _environment.PsiEnd = (float)_environment.Step + now;

        _time.Rebase(now);

        IvpPhysicsPipeline.Psi(
            _environment, _units, _mindists, _time.Queue, _minimize, _recheckInvalidMinimize, _examine, _wake, _random, now);

        QueueSlot = _time.Queue.Add(this, (float)(_environment.PsiEnd - _time.Base));
    }
}
