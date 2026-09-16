using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One controller of a unit — the slot <c>+0x20</c> the PSI calls per entry of <c>unit+0x10</c>.</summary>
public interface IIvpUnitController
{
    /// <summary>Runs this controller over its unit for one PSI.</summary>
    /// <param name="unit">The unit, <c>frame+0x10</c>.</param>
    /// <param name="psiStep">The PSI's step, <c>frame+0x0</c>.</param>
    public void Advance(IvpSimulationUnit unit, float psiStep);
}

/// <summary>
/// A unit of cores simulated together, and its PSI — <c>FUN_180075c80(unit, frame, &amp;pushed)</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Read from the decompiler** (`docs/findings/51`, *The PSI per unit*). The unit is what the time manager's active list holds:
/// the cores at <c>+0x8</c> (count <c>+0x1a</c>), the controllers at <c>+0x10</c> (count <c>+0x3a</c>), its flags at
/// <c>+0x0</c> and its state in that word's low byte, <c>8</c> being asleep.
/// </remarks>
public sealed class IvpSimulationUnit
{
    /// <summary><c>DAT_1800ea988</c>: <c>1.0f</c>, the spin squared a unit is called fast above.</summary>
    private const float FastSpin = 1f;

    /// <summary>The state byte, <c>unit+0x0</c>'s low byte: <c>8</c> once the unit is asleep.</summary>
    public int State { get; private set; }

    /// <summary>The flags word, <c>unit+0x0</c>, whose <c>0x400</c>/<c>0x3000</c> bits carry a fast spin into the next PSI.</summary>
    public int Flags { get; private set; }

    /// <summary>The cores — <c>+0x8</c>, count <c>+0x1a</c>.</summary>
    internal List<IvpRigidBody> Cores { get; } = [];

    /// <summary>The controllers — <c>+0x10</c>, count <c>+0x3a</c>.</summary>
    internal List<IIvpUnitController> Controllers { get; } = [];

    /// <summary>Runs one PSI for this unit — <c>FUN_180075c80</c>.</summary>
    /// <param name="environment">The environment: its time, rest delay and rest-check countdown.</param>
    /// <param name="now">The environment's time, <c>env+0x188</c>.</param>
    /// <param name="step">The PSI's step, <c>env+0x108</c> narrowed.</param>
    /// <param name="pushed">The cores to step, pushed last to first.</param>
    /// <param name="random">The jitter the rest check's cadence takes, <c>FUN_18007d5c0</c>.</param>
    /// <returns><c>true</c> when the unit fell asleep, so the driver takes it off the active list.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// <code>
    /// every core, last first:  dt = (float)(env+0x188 − core+0x1d0)
    ///     FUN_180071330(core+0x1a0, core+0x90);  core+0xf0 = core+0x150 + core+0x170·dt
    ///     FUN_180077950(core);  core+0x0 &amp;= 0xff3f;  core+0x2 = 0;  remember (1.0 − |ω|²) &lt; 0
    /// any fast:  flags = (flags &amp; ~0x800) | 0x400
    /// else:      flags = (flags·4 ^ flags) &amp; 0xffffcfff ^ flags·4;  flags &amp; 0x3000 → every core's anchors reset;  flags &amp;= ~0xc00
    /// env+0x1a8 −= 1;  zero → env+0x1a8 = 0xf − (short)(random · DAT_1800ee1c8)
    /// every controller, last first:  its slot +0x20;  every core, last first:  pushed
    /// the countdown was zero:  every core's +0x1 = FUN_180077220, ANDed with 3;  all 3 → the cores settle and the unit sleeps
    /// </code>
    /// *Not carried*: the unit's own <c>0x300</c> bits and the three routines behind them, and the sleep listeners
    /// (<c>FUN_180088930</c> keeps only its hull settle here).
    /// </remarks>
    internal bool Psi(
        IvpImpactEnvironment environment, double now, float step, List<IvpRigidBody> pushed, Func<float> random)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(pushed);
        ArgumentNullException.ThrowIfNull(random);

        bool fast = false;

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];
            float elapsed = (float)(now - core.LastStepped);

            core.CoreMatrix = IvpMatrix.FromRotation(core.WorkingOrientation, core.CoreMatrix.Translation);
            core.EventPosition = (
                core.Position.X + ((double)core.PreviousVelocity.X * elapsed),
                core.Position.Y + ((double)core.PreviousVelocity.Y * elapsed),
                core.Position.Z + ((double)core.PreviousVelocity.Z * elapsed));

            IvpPush.Flush(core);

            core.CollisionFreeze = 0;
            core.Collisions = 0;

            (float X, float Y, float Z) spin = core.AngularVelocity;

            fast |= FastSpin - ((spin.X * spin.X) + (spin.Y * spin.Y) + (spin.Z * spin.Z)) < 0f;
        }

        if (fast)
        {
            Flags = (Flags & ~0x800) | 0x400;
        }
        else
        {
            Flags = ((Flags * 4) ^ Flags) & ~0x3000 ^ (Flags * 4);

            if ((Flags & 0x3000) != 0)
            {
                for (int index = Cores.Count - 1; index >= 0; index--)
                {
                    IvpRigidBody core = Cores[index];
                    core.RestAnchorTime = now;
                    core.SettleAnchorTime = now;
                }
            }

            Flags &= ~0xc00;
        }

        bool restCheckDue = --environment.RestCheckCountdown == 0;

        if (restCheckDue)
        {
            environment.RestCheckCountdown = (short)(0xf - (short)(random() * RestCheckJitter));
        }

        for (int index = Controllers.Count - 1; index >= 0; index--)
        {
            Controllers[index].Advance(this, step);
        }

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            pushed.Add(Cores[index]);
        }

        if (!restCheckDue)
        {
            return false;
        }

        int motion = (int)IvpCoreMotion.Resting;

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];
            IvpCoreMotion answer = core.TestRest(now, environment.RestDelay);

            core.UnitState = (int)answer;
            motion &= (int)answer;
        }

        if (motion != (int)IvpCoreMotion.Resting)
        {
            return false;
        }

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];

            for (int at = core.Objects.Count - 1; at >= 0; at--)
            {
                core.Objects[at].Hull.Settle(now);
            }
        }

        State = 8;
        return true;
    }

    /// <summary><c>_DAT_1800ee1c8</c>, dumped as <c>-5.0f</c>: the jitter the rest check's countdown is scaled by, so it lands in 15..20.</summary>
    private const float RestCheckJitter = -5f;
}
