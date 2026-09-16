using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One controller of a unit — its slot <c>+0x20</c> is the PSI's work and its slot <c>+0x28</c> the priority.</summary>
/// <remarks>
/// **The priorities are read** (`docs/findings/51`): friction `2000`, gravity `1000`, friction `600`, the constraints `405`,
/// friction `0`. A unit's entries are sorted ascending and walked last first, so the PSI runs them highest priority first.
/// </remarks>
public interface IIvpUnitController
{
    /// <summary>The controller's priority — its slot <c>+0x28</c>, which sorts the unit's entries.</summary>
    public int Priority { get; }

    /// <summary>Runs this controller over the cores of its entry for one PSI — the slot <c>+0x20</c>.</summary>
    /// <param name="unit">The unit the entry belongs to, <c>frame+0x10</c>.</param>
    /// <param name="cores">The entry's cores, <c>entry+0x8</c>.</param>
    /// <param name="psiStep">The PSI's step, <c>frame+0x0</c>.</param>
    public void Advance(IvpSimulationUnit unit, IReadOnlyList<IvpRigidBody> cores, float psiStep);
}

/// <summary>One controller and the cores of this unit it drives — the <c>0x28</c> bytes of an entry at <c>unit+0x10</c>.</summary>
/// <param name="controller">The controller, <c>+0x0</c>.</param>
public sealed class IvpUnitControllerEntry(IIvpUnitController controller)
{
    /// <summary>The controller — <c>+0x0</c>.</summary>
    public IIvpUnitController Controller { get; } = controller ?? throw new ArgumentNullException(nameof(controller));

    /// <summary>Its cores in this unit — the vector at <c>+0x8</c>, count <c>+0xa</c>, elements <c>+0x10</c>.</summary>
    internal List<IvpRigidBody> Cores { get; } = [];
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
    /// <remarks>*What sets the <c>0x300</c> pair is not read yet* — the PSI only clears it once it has rebuilt.</remarks>
    public int Flags { get; internal set; }

    /// <summary>The cores — <c>+0x8</c>, count <c>+0x1a</c>.</summary>
    internal List<IvpRigidBody> Cores { get; } = [];

    /// <summary>The controller entries — <c>+0x10</c>, count <c>+0x3a</c>, sorted ascending by priority.</summary>
    internal List<IvpUnitControllerEntry> Entries { get; } = [];

    /// <summary>
    /// Appends an entry for a controller and sorts the entries again — <c>FUN_180074820</c> then <c>FUN_180075990</c>.
    /// </summary>
    /// <param name="controller">The controller.</param>
    /// <returns>The entry, whose cores the caller fills.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="controller"/> is null.</exception>
    /// <remarks>
    /// **The sort is an insertion sort that moves a later entry down while its neighbour's priority is strictly greater**, so
    /// entries of equal priority keep the order they were added — which decides which friction controller runs first.
    /// </remarks>
    internal IvpUnitControllerEntry AddController(IIvpUnitController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        IvpUnitControllerEntry entry = new(controller);
        Entries.Add(entry);
        Sort();

        return entry;
    }

    /// <summary>Rebuilds every entry from the cores' own controllers — <c>FUN_180074ba0</c> then <c>FUN_180075470</c>.</summary>
    /// <remarks>
    /// **Every core, last first, and every controller of that core, last first**: the controller's entry is searched for from
    /// the last, made when missing, and gains the core. The entries are sorted once at the end. *The third routine the unit's
    /// <c>0x300</c> bits also call, `FUN_180074e80`, is unread.*
    /// </remarks>
    internal void RebuildEntries()
    {
        Entries.Clear();

        for (int index = Cores.Count - 1; index >= 0; index--)
        {
            IvpRigidBody core = Cores[index];

            for (int at = core.Controllers.Count - 1; at >= 0; at--)
            {
                IIvpUnitController controller = core.Controllers[at];
                IvpUnitControllerEntry? found = null;

                for (int scan = Entries.Count - 1; scan >= 0; scan--)
                {
                    if (ReferenceEquals(Entries[scan].Controller, controller))
                    {
                        found = Entries[scan];
                        break;
                    }
                }

                if (found is null)
                {
                    found = new IvpUnitControllerEntry(controller);
                    Entries.Add(found);
                }

                found.Cores.Add(core);
            }
        }

        Sort();
    }

    /// <summary>The entries insertion-sorted ascending by priority — <c>FUN_180075990</c>.</summary>
    private void Sort()
    {
        for (int index = 1; index < Entries.Count; index++)
        {
            IvpUnitControllerEntry moving = Entries[index];
            int at = index;

            while (at > 0 && Entries[at - 1].Controller.Priority > moving.Controller.Priority)
            {
                Entries[at] = Entries[at - 1];
                at--;
            }

            Entries[at] = moving;
        }
    }

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
    /// *Not carried*: `FUN_180074e80`, the third routine the <c>0x300</c> bits call, and the sleep listeners
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

        if ((Flags & 0x300) != 0)
        {
            RebuildEntries();
            Flags &= ~0x300;
        }

        for (int index = Entries.Count - 1; index >= 0; index--)
        {
            IvpUnitControllerEntry entry = Entries[index];
            entry.Controller.Advance(this, entry.Cores, step);
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
