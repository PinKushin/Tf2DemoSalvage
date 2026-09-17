using System;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>
/// The simulation's heartbeat — the PSI event <c>FUN_18008a020</c>, which rebases the queue, runs the pipeline and requeues
/// itself (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *`FUN_18008a020` is the PSI EVENT*). The engine's simulation is an event
/// that reschedules itself one step later, and the rebase happens on every one of them, so no queued time is ever more than a
/// step old.
/// </remarks>
public static class IvpPsiEvent
{
    /// <summary>Queues the first PSI event, due at once.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="time">The time manager whose queue the event lives in.</param>
    /// <param name="units">The unit lists.</param>
    /// <param name="mindists">The mindist manager the pipeline walks.</param>
    /// <param name="queue">The mindist queue the pipeline's two walks take.</param>
    /// <param name="minimize">The minimize, <c>FUN_180095cb0</c>.</param>
    /// <param name="recheckInvalidMinimize">The minimize with no step budget, <c>FUN_180095ad0</c> — see <see cref="IvpPhysicsPipeline.Psi"/>.</param>
    /// <param name="examine">The scheduler in mode 1, <c>FUN_180099380(mindist, 1, 1)</c>.</param>
    /// <param name="random">The jitter the rest check's cadence takes.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Start(
        IvpImpactEnvironment environment,
        PhysicsTimeManager time,
        IvpUnitManager units,
        IvpMindistManager mindists,
        IvpMinList<IvpMindist> queue,
        Action<IvpMindist> minimize,
        Action<IvpMindist> recheckInvalidMinimize,
        Action<IvpMindist> examine,
        Func<float> random)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);

        time.Add(new PhysicsEvent(
            0f, now => RunPsi(environment, time, units, mindists, queue, minimize, recheckInvalidMinimize, examine, random, now)));
    }

    /// <summary>One PSI: the rebase, the pipeline, and the next event — <c>FUN_18008a020</c>.</summary>
    /// <param name="environment">The environment.</param>
    /// <param name="time">The time manager.</param>
    /// <param name="units">The unit lists.</param>
    /// <param name="mindists">The mindist manager.</param>
    /// <param name="queue">The mindist queue.</param>
    /// <param name="minimize">The minimize.</param>
    /// <param name="recheckInvalidMinimize">The minimize with no step budget — see <see cref="IvpPhysicsPipeline.Psi"/>.</param>
    /// <param name="examine">The scheduler in mode 1.</param>
    /// <param name="random">The rest check's jitter.</param>
    /// <param name="now">The time the event fired at, <c>env+0x188</c>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// env+0x198 = env+0x188;  env+0x190 = (float)env+0x108 + env+0x188      -- the base, then the next PSI
    /// every queued entry's time −= (float)now;  tm+0x28 = env+0x198;  tm+0x20 = 0
    /// FUN_180082560(env)                                                     -- the pipeline
    /// requeue this event at (float)(env+0x190 − tm+0x28)
    /// </code>
    /// **The step is added as a float** — `(float)env+0x108 + env+0x188` — so the next PSI's time carries the float's error, not
    /// the double's.
    /// </remarks>
    public static void RunPsi(
        IvpImpactEnvironment environment,
        PhysicsTimeManager time,
        IvpUnitManager units,
        IvpMindistManager mindists,
        IvpMinList<IvpMindist> queue,
        Action<IvpMindist> minimize,
        Action<IvpMindist> recheckInvalidMinimize,
        Action<IvpMindist> examine,
        Func<float> random,
        double now)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);

        environment.Now = now;
        environment.RebaseBase = now;
        environment.PsiEnd = (float)environment.Step + now;

        time.Rebase(now);
        time.ZeroClock();

        IvpPhysicsPipeline.Psi(environment, units, mindists, queue, minimize, recheckInvalidMinimize, examine, random, now);

        time.Add(new PhysicsEvent(
            (float)(environment.PsiEnd - time.Base),
            later => RunPsi(environment, time, units, mindists, queue, minimize, recheckInvalidMinimize, examine, random, later)));
    }
}
