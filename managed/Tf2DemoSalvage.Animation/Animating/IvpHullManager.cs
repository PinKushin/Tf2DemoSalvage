using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>What a hull manager files: a synapse record, told when its object's hull passes the key it was filed at.</summary>
/// <remarks>
/// The two slots of the records' listener table, <c>1800fdea0</c>, that a manager calls. **Slot 2, which deletes the mindist
/// when the manager is torn down (<c>FUN_180097580</c> from <c>FUN_180094420</c>), is not carried**: nothing here destroys an
/// object yet.
/// </remarks>
public interface IIvpHullSynapse
{
    /// <summary>The slot in the manager's list — the record's <c>+0x8</c> — or null when not filed.</summary>
    public int? HullSlot { get; set; }

    /// <summary>Slot 1: the hull has passed the key.</summary>
    /// <param name="manager">The manager telling it.</param>
    /// <param name="overshoot">The list's minimum less the next PSI's value, which is under zero.</param>
    public void HullPassed(IvpHullManager manager, float overshoot);

    /// <summary>Slot 3: the manager rebased.</summary>
    /// <param name="valueShift">Minus the value, taken off every key.</param>
    /// <param name="centerShift">Minus the center value.</param>
    public void Rebased(float valueShift, float centerShift);
}

/// <summary>
/// IVP's hull manager, at <c>object+0x80</c>: how far an object's surface may have traveled, as a value that grows along a
/// gradient, and the synapses to tell when it passes their keys (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*). The
/// value is the bound on the surface's travel, the center value the bound on the core's; both are floats taken relative to
/// the last rebase, which every step's pass does once ten seconds have gone since the last. **A far pair does not look again
/// on a clock**: its records are filed here and told when the hull passes them.
/// </remarks>
public sealed class IvpHullManager
{
    /// <summary><c>DAT_1800fdf8c</c>: the slack a core's speed bound is scaled by to make a gradient.</summary>
    public const float GradientSlack = 1.00001f;

    /// <summary><c>DAT_1800efe28</c>: the seconds between rebases.</summary>
    public const double ResetInterval = 10d;

    /// <summary>
    /// The telling budget in TF2's client: <c>physics_performanceparams_t::Defaults()</c>'s
    /// <c>maxCollisionChecksPerTimestep</c> (<c>vphysics/performance.h:33</c>), which vphysics writes to the anomaly limits'
    /// <c>+0x18</c> (<c>FUN_180015200</c>) and the client never changes.
    /// </summary>
    public const int DefaultCheckBudget = 250;

    /// <summary>The time the gradients were last taken, <c>+0x0</c>.</summary>
    public double Time { get; set; }

    /// <summary>The value's growth a second, <c>+0x8</c>.</summary>
    public float Gradient { get; set; }

    /// <summary>The center value's growth a second — the core's linear speed — <c>+0xc</c>.</summary>
    public float CenterGradient { get; set; }

    /// <summary>The value at <see cref="Time"/>, <c>+0x10</c>.</summary>
    public float Value { get; set; }

    /// <summary>The center value at <see cref="Time"/>, <c>+0x14</c>.</summary>
    public float CenterValue { get; set; }

    /// <summary>The value projected to the next PSI, <c>+0x18</c>.</summary>
    public float NextPsiValue { get; set; }

    /// <summary>The whole second after which the next pass rebases, <c>+0x1c</c>.</summary>
    public int NextReset { get; set; }

    /// <summary>The filed synapses, keyed by the value each is told at — the min-list at <c>+0x20</c>.</summary>
    public IvpMinList<IIvpHullSynapse> Synapses { get; } = new();

    /// <summary>A core's gradient: its surface speed bound plus its linear speed, times <see cref="GradientSlack"/>.</summary>
    /// <param name="surfaceSpeedBound">The core's <c>+0x254</c>.</param>
    /// <param name="linearSpeed">The core's <c>+0x1dc</c>.</param>
    /// <returns>The gradient, in float.</returns>
    public static float GradientFor(float surfaceSpeedBound, float linearSpeed) =>
        (surfaceSpeedBound + linearSpeed) * GradientSlack;

    /// <summary>Takes a step's gradients, as <c>FUN_180099a00</c> does for each of a core's objects.</summary>
    /// <param name="now">The environment's time.</param>
    /// <param name="step">The PSI step, narrowed.</param>
    /// <param name="gradient">The core's <see cref="GradientFor"/>.</param>
    /// <param name="linearSpeed">The core's linear speed, the new center gradient.</param>
    /// <returns>Whether a synapse is due, so the step pushes the manager for the pass.</returns>
    /// <remarks>
    /// **The values move along the OLD gradients before the new ones are taken**, over `(float)(now − time)`, all in float;
    /// the next PSI's value is then `gradient · step + value`.
    /// </remarks>
    public bool Advance(double now, float step, float gradient, float linearSpeed)
    {
        float elapsed = (float)(now - Time);
        float center = (elapsed * CenterGradient) + CenterValue;
        CenterGradient = linearSpeed;
        Time = now;
        float value = (elapsed * Gradient) + Value;
        Gradient = gradient;
        CenterValue = center;
        Value = value;
        NextPsiValue = (gradient * step) + value;
        return IsDue();
    }

    /// <summary>Folds the gradients in and rebases — a core coming to rest, <c>FUN_1800791a0</c> and <c>FUN_180078c90</c>.</summary>
    /// <param name="now">The environment's time.</param>
    /// <remarks>**The time is left where it was**, the gradients zeroed, and the rebase is unconditional.</remarks>
    public void Settle(double now)
    {
        float elapsed = (float)(now - Time);
        float center = (elapsed * CenterGradient) + CenterValue;
        float value = (elapsed * Gradient) + Value;
        Gradient = 0f;
        CenterGradient = 0f;
        CenterValue = center;
        Value = value;
        Rebase();
    }

    /// <summary>Files a synapse over now — <c>FUN_180097c40</c>.</summary>
    /// <param name="synapse">The record; its slot is written.</param>
    /// <param name="now">The environment's time.</param>
    /// <param name="allowance">How much further the hull may grow before the synapse is told.</param>
    /// <returns>How far the hull is past its center at now: `(gradient − center gradient) · t + (value − center value)`.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="synapse"/> is null.</exception>
    public double Install(IIvpHullSynapse synapse, double now, double allowance)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        float elapsed = (float)(now - Time);
        synapse.HullSlot = Synapses.Add(synapse, KeyAfter(elapsed, allowance));
        return ((Gradient - CenterGradient) * elapsed) + (Value - CenterValue);
    }

    /// <summary>Files a synapse over now with an allowance added in float — <c>FUN_1800b61a0</c>'s filing.</summary>
    /// <param name="synapse">The record; its slot is written.</param>
    /// <param name="now">The environment's time.</param>
    /// <param name="allowance">How much further the hull may grow before the synapse is told, added in float.</param>
    /// <exception cref="ArgumentNullException"><paramref name="synapse"/> is null.</exception>
    /// <remarks>
    /// `(float)(now − time)` `MULSS` the gradient, `ADDSS` the value, `ADDSS` the allowance, the running result the destination
    /// throughout — where <see cref="Install"/> adds its allowance in double.
    /// </remarks>
    public void InstallInFloat(IIvpHullSynapse synapse, double now, float allowance)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        float elapsed = (float)(now - Time);
        synapse.HullSlot = Synapses.Add(synapse, IvpMath.Addss(IvpMath.Addss(IvpMath.Mulss(elapsed, Gradient), Value), allowance));
    }

    /// <summary>Takes a filed synapse out and files it again — <c>FUN_180099970</c>.</summary>
    /// <param name="synapse">The record; its slot is rewritten.</param>
    /// <param name="time">The time the key is spread to.</param>
    /// <param name="allowance">How much further the hull may grow before the synapse is told.</param>
    /// <exception cref="ArgumentNullException"><paramref name="synapse"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The synapse is not filed, where the engine would unlink slot <c>0xffff</c>.</exception>
    public void Reinstall(IIvpHullSynapse synapse, double time, double allowance)
    {
        Remove(synapse);
        synapse.HullSlot = Synapses.Add(synapse, KeyAfter((float)(time - Time), allowance));
    }

    /// <summary>Files a synapse at the next PSI's value — one record of <c>FUN_180097e20</c>.</summary>
    /// <param name="synapse">The record; its slot is written.</param>
    /// <param name="allowance">How much further the hull may grow, added in float.</param>
    /// <exception cref="ArgumentNullException"><paramref name="synapse"/> is null.</exception>
    public void InstallAtNextPsi(IIvpHullSynapse synapse, float allowance)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        synapse.HullSlot = Synapses.Add(synapse, NextPsiValue + allowance);
    }

    /// <summary>Takes a filed synapse out — <c>FUN_1800ab1b0</c> on the record's slot.</summary>
    /// <param name="synapse">The record; its slot is cleared.</param>
    /// <exception cref="ArgumentNullException"><paramref name="synapse"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The synapse is not filed, where the engine would unlink slot <c>0xffff</c>.</exception>
    public void Remove(IIvpHullSynapse synapse)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        // Stryker disable once : a mutant that empties the guard body leaves 'slot'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (synapse.HullSlot is not int slot)
        {
            throw new InvalidOperationException("A synapse that is not filed is taken out of a hull manager.");
        }

        Synapses.Remove(slot);
        synapse.HullSlot = null;
    }

    /// <summary>Takes the value off every key — <c>FUN_180094490</c>.</summary>
    /// <remarks>
    /// Head first, each key moves and its synapse is told `(−value, −center)`; then the next PSI's value and the list's
    /// minimum move, **an empty list's included**, and the value and center are zeroed.
    /// </remarks>
    public void Rebase()
    {
        float valueShift = -Value;
        float centerShift = -CenterValue;
        Synapses.Offset(valueShift, synapse => synapse.Rebased(valueShift, centerShift));
        NextPsiValue = valueShift + NextPsiValue;
        Value = 0f;
        CenterValue = 0f;
    }

    /// <summary>Rebases once the reset time is past — the tail of each manager's turn in <c>FUN_18009a690</c>.</summary>
    /// <remarks>
    /// **`(double)` the reset time under the time (`COMISD`/`JNC`, so a NaN too)** rebases and sets the reset to
    /// `(int)(time + 10.0)`, truncated by `CVTTSD2SI`.
    /// </remarks>
    public void RebaseIfDue()
    {
        if ((double)NextReset >= Time)
        {
            return;
        }

        Rebase();
        NextReset = Truncate(Time + ResetInterval);
    }

    /// <summary>Tells the head synapse while the hull is past it — <c>FUN_18009a4f0</c>.</summary>
    /// <param name="budget">The anomaly limits' <c>+0x18</c>; <see cref="DefaultCheckBudget"/> in TF2's client.</param>
    /// <param name="additionalChecks">
    /// The anomaly manager's slot <c>+0x20</c>, handed the checks done and answering how many more — the collision solver's
    /// <c>AdditionalCollisionChecksThisTick</c>, which TF2's client answers <c>0</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="additionalChecks"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An empty list is under the next PSI's value, where the engine reads its head at index <c>0xffff</c>.
    /// </exception>
    /// <remarks>
    /// **The head is told while the minimum less the next PSI's value is under zero** (`COMISS`/`JNC`, so a NaN too). The
    /// budget counts down per telling; once it is under zero the extension is asked with the checks done and its answer
    /// added back — still under zero, the pass stops, and otherwise the checks done become `done + 1 + remaining`.
    /// </remarks>
    public void NotifyPassed(int budget, Func<int, int> additionalChecks)
    {
        ArgumentNullException.ThrowIfNull(additionalChecks);

        int remaining = budget;
        int done = budget;
        float overshoot = Synapses.Minimum - NextPsiValue;

        while (!(overshoot >= 0f))
        {
            if (!Synapses.TryFirst(out IIvpHullSynapse? head, out _))
            {
                throw new InvalidOperationException("An empty hull manager is under its next PSI's value.");
            }

            head.HullPassed(this, overshoot);
            remaining--;

            if (remaining < 0)
            {
                remaining += additionalChecks(done);

                if (remaining < 0)
                {
                    return;
                }

                done += 1 + remaining;
            }

            overshoot = Synapses.Minimum - NextPsiValue;
        }
    }

    /// <summary>The environment's pass over the managers a step pushed — <c>FUN_18009a690</c>.</summary>
    /// <param name="pushed">The managers, in the order the step pushed them.</param>
    /// <param name="budget">Each manager's telling budget.</param>
    /// <param name="additionalChecks">The budget's extension.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>**The last pushed goes first**: each is told, then rebased if its reset time is past.</remarks>
    public static void NotifyAll(IReadOnlyList<IvpHullManager> pushed, int budget, Func<int, int> additionalChecks)
    {
        ArgumentNullException.ThrowIfNull(pushed);
        ArgumentNullException.ThrowIfNull(additionalChecks);

        for (int index = pushed.Count - 1; index >= 0; index--)
        {
            IvpHullManager manager = pushed[index];
            manager.NotifyPassed(budget, additionalChecks);
            manager.RebaseIfDue();
        }
    }

    private static int Truncate(double seconds) =>
        seconds >= int.MinValue && seconds < 2147483648d ? (int)seconds : int.MinValue;

    private bool IsDue() => !(Synapses.Minimum - NextPsiValue >= 0f);

    /// <summary>The key over an elapsed time: the value spread in float, the allowance added in double, then narrowed.</summary>
    private float KeyAfter(float elapsed, double allowance) =>
        (float)((double)((elapsed * Gradient) + Value) + allowance);
}
