using System;
using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's hull manager — the per-object record at <c>object+0x80</c> that tells a far pair's synapses when the object may
/// have moved far enough to reach its partner (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *The hull manager, and how a far pair is told to look again*): the
/// constructor `FUN_1800943d0`, the step `FUN_180099a00`, the pass `FUN_18009a690`, the rebase `FUN_180094490`, the settle
/// in `FUN_1800791a0`, and the filings `FUN_180097c40`, `FUN_180099970` and `FUN_180097e20`. Units are inches and seconds.
/// </remarks>
public sealed class IvpHullManagerConformanceTests
{
    private const float Step = 0.015f;

    /// <remarks>**The gradient is the surface bound plus the linear speed, times `1.00001f`** — `DAT_1800fdf8c`.</remarks>
    [Test]
    public void GradientFor_ASurfaceBoundAndALinearSpeed_IsTheirSumTimesOnePointZeroZeroZeroZeroOne() =>
        IvpHullManager.GradientFor(6f, 2f).ShouldBe(8f * 1.00001f);

    /// <remarks>
    /// **A manager starts at zero** and its first step takes the gradients it is handed, the value staying zero: the next
    /// PSI's value is `gradient · step`. An empty list's `1e10f` is not under it, so nothing is due.
    /// </remarks>
    [Test]
    public void Advance_FromRest_TakesTheGradientsAndProjectsTheNextPsi()
    {
        IvpHullManager manager = new();

        bool due = manager.Advance(1d, Step, gradient: 8f, linearSpeed: 2f);

        due.ShouldBeFalse();
        manager.Time.ShouldBe(1d);
        manager.Gradient.ShouldBe(8f);
        manager.CenterGradient.ShouldBe(2f);
        manager.Value.ShouldBe(0f);
        manager.CenterValue.ShouldBe(0f);
        manager.NextPsiValue.ShouldBe(8f * Step);
    }

    /// <remarks>
    /// **The values move along the OLD gradients before the new ones are taken**: half a second at `8` and `2` makes the
    /// value `4` and the center `1`, and the next PSI projects the new gradient from there.
    /// </remarks>
    [Test]
    public void Advance_HalfASecondLater_MovesTheValuesAlongTheOldGradients()
    {
        IvpHullManager manager = new();
        manager.Advance(1d, Step, gradient: 8f, linearSpeed: 2f);

        manager.Advance(1.5d, Step, gradient: 12f, linearSpeed: 4f);

        manager.Time.ShouldBe(1.5d);
        manager.Value.ShouldBe(4f);
        manager.CenterValue.ShouldBe(1f);
        manager.Gradient.ShouldBe(12f);
        manager.CenterGradient.ShouldBe(4f);
        manager.NextPsiValue.ShouldBe((12f * Step) + 4f);
    }

    /// <remarks>
    /// **A synapse is due when the list's minimum less the next PSI's value is under zero** (`COMISS` then `JNC`), so a key
    /// equal to it is not: the next PSI's value here is `8 · 0.015f`, which is `0.12f` exactly.
    /// </remarks>
    [TestCase(0.11f, true)]
    [TestCase(0.12f, false)]
    [TestCase(0.13f, false)]
    public void Advance_ASynapseKeyedAgainstTheNextPsiValue_IsDueOnlyUnderIt(float key, bool due)
    {
        IvpHullManager manager = new();
        manager.Synapses.Add(new Recorder("a", []), key);

        manager.Advance(1d, Step, gradient: 8f, linearSpeed: 2f).ShouldBe(due);
    }

    /// <remarks>
    /// **Settling folds the gradients into the values, zeroes them, keeps the time, and rebases unconditionally**: half a
    /// second at `4` and `1` from `3` and `1` is `5` and `1.5`, which the rebase takes off every key and the next PSI.
    /// </remarks>
    [Test]
    public void Settle_AMovingManager_FoldsItsGradientsIntoItsValuesAndRebases()
    {
        IvpHullManager manager = new() { Time = 1d, Gradient = 4f, CenterGradient = 1f, Value = 3f, CenterValue = 1f, NextPsiValue = 7f };
        Recorder synapse = new("a", []);
        manager.Synapses.Add(synapse, 9f);

        manager.Settle(1.5d);

        manager.Time.ShouldBe(1d);
        manager.Gradient.ShouldBe(0f);
        manager.CenterGradient.ShouldBe(0f);
        manager.Value.ShouldBe(0f);
        manager.CenterValue.ShouldBe(0f);
        manager.NextPsiValue.ShouldBe(2f);
        manager.Synapses.Minimum.ShouldBe(4f);
        synapse.Shifts.ShouldBe([(-5f, -1.5f)]);
    }

    /// <remarks>
    /// **A rebase takes the value off every key, the next PSI and the minimum**, telling each synapse head first the value
    /// and center shifts, and zeroes the value and center.
    /// </remarks>
    [Test]
    public void Rebase_AManagerWithTwoSynapses_ShiftsEveryKeyByMinusTheValue()
    {
        List<string> told = [];
        IvpHullManager manager = new() { Value = 5f, CenterValue = 2f, NextPsiValue = 7f };
        Recorder later = new("later", told);
        Recorder first = new("first", told);
        manager.Synapses.Add(later, 11f);
        manager.Synapses.Add(first, 9f);

        manager.Rebase();

        told.ShouldBe(["first", "later"]);
        first.Shifts.ShouldBe([(-5f, -2f)]);
        later.Shifts.ShouldBe([(-5f, -2f)]);
        manager.Value.ShouldBe(0f);
        manager.CenterValue.ShouldBe(0f);
        manager.NextPsiValue.ShouldBe(2f);
        manager.Synapses.Minimum.ShouldBe(4f);
        manager.Synapses.TryFirst(out _, out int slot).ShouldBeTrue();
        manager.Synapses.Remove(slot);
        manager.Synapses.Minimum.ShouldBe(6f);
    }

    /// <remarks>
    /// **The minimum is shifted even when nothing is filed**: `1e10f` less a thousand is `9999998976f`, the float nearest,
    /// and the list keeps it until something is added or removed.
    /// </remarks>
    [Test]
    public void Rebase_AnEmptyManager_ShiftsTheEmptyMinimumToo()
    {
        IvpHullManager manager = new() { Value = 1000f };

        manager.Rebase();

        manager.Synapses.Minimum.ShouldBe(9999998976f);
    }

    /// <remarks>
    /// **Filing keys a synapse at the value spread to now plus the allowance, and returns how far the hull is past its
    /// center**: `(float)(0.5 · 4 + 3) + 0.25` is `5.25`, and `(4 − 1) · 0.5 + (3 − 1)` is `3.5`.
    /// </remarks>
    [Test]
    public void Install_AtHalfASecond_KeysTheSpreadValuePlusTheAllowanceAndReturnsTheHullPastTheCentre()
    {
        IvpHullManager manager = new() { Time = 1d, Gradient = 4f, CenterGradient = 1f, Value = 3f, CenterValue = 1f };
        Recorder synapse = new("a", []);

        double past = manager.Install(synapse, now: 1.5d, allowance: 0.25d);

        past.ShouldBe(3.5d);
        manager.Synapses.Minimum.ShouldBe(5.25f);
        synapse.HullSlot.ShouldBe(0);
    }

    /// <remarks>
    /// **The allowance is added in double before the key is narrowed** (`CVTPS2PD`, `ADDSD`, `CVTPD2PS`): `2^24 +
    /// 1.00000005` is just over the tie between `2^24` and `2^24 + 2` and narrows up, where a float sum — the allowance
    /// narrowed first, to exactly `1` — lands on the tie and rounds to even, staying at `2^24`. *An allowance of `1.0000001`
    /// could not tell the two apart: it narrows to `1.00000012`, over the tie either way.*
    /// </remarks>
    [Test]
    public void Install_AnAllowanceJustOverATie_IsAddedInDoubleBeforeNarrowing()
    {
        IvpHullManager manager = new() { Value = 16777216f };

        manager.Install(new Recorder("a", []), now: 0d, allowance: 1.00000005d);

        manager.Synapses.Minimum.ShouldBe(16777218f);
    }

    /// <remarks>**Refiling takes the synapse out first**, so it holds one entry at its new key in the slot it freed.</remarks>
    [Test]
    public void Reinstall_ASynapseAlreadyFiled_TakesItOutFirst()
    {
        IvpHullManager manager = new() { Value = 3f };
        Recorder synapse = new("a", []);
        manager.Install(synapse, now: 0d, allowance: 2d);

        manager.Reinstall(synapse, time: 0d, allowance: -1d);

        manager.Synapses.Count.ShouldBe(1);
        manager.Synapses.Minimum.ShouldBe(2f);
        synapse.HullSlot.ShouldBe(0);
    }

    /// <remarks>**Filing at the next PSI keys on the next PSI's value plus the allowance**, ignoring the time and gradients.</remarks>
    [Test]
    public void InstallAtNextPsi_AnyGradient_KeysOnTheNextPsiValuePlusTheAllowance()
    {
        IvpHullManager manager = new() { Time = 0d, Gradient = 100f, Value = 100f, NextPsiValue = 7f };
        Recorder synapse = new("a", []);

        manager.InstallAtNextPsi(synapse, 0.5f);

        manager.Synapses.Minimum.ShouldBe(7.5f);
        synapse.HullSlot.ShouldBe(0);
    }

    /// <remarks>A removed synapse leaves the list and forgets its slot.</remarks>
    [Test]
    public void Remove_AFiledSynapse_LeavesTheListAndForgetsItsSlot()
    {
        IvpHullManager manager = new();
        Recorder synapse = new("a", []);
        manager.Install(synapse, now: 0d, allowance: 1d);

        manager.Remove(synapse);

        manager.Synapses.Count.ShouldBe(0);
        synapse.HullSlot.ShouldBeNull();
    }

    /// <remarks>
    /// **The head is told while the minimum is under the next PSI's value**, handed the minimum less that value: a synapse
    /// that files itself onward is told once, and the next head is told its own shortfall.
    /// </remarks>
    [Test]
    public void NotifyPassed_SynapsesThatFileThemselvesOnward_AreEachToldOnceHeadFirst()
    {
        List<string> told = [];
        IvpHullManager manager = new() { NextPsiValue = 10f };
        Recorder second = new("second", told) { OnPassed = (hull, self) => hull.Reinstall(self, 0d, 20d) };
        Recorder first = new("first", told) { OnPassed = (hull, self) => hull.Reinstall(self, 0d, 20d) };
        manager.Install(second, 0d, 2d);
        manager.Install(first, 0d, 1d);

        manager.NotifyPassed(budget: IvpHullManager.DefaultCheckBudget, additionalChecks: _ => 0);

        told.ShouldBe(["first", "second"]);
        first.Overshoots.ShouldBe([-9f]);
        second.Overshoots.ShouldBe([-8f]);
    }

    /// <remarks>
    /// **The budget counts down per telling, and once it is spent the extension is asked with the checks done so far**:
    /// with a budget of one, the second telling asks with `1` and is granted two, the fourth asks with `1 + 1 + 1 = 3` and is
    /// granted none, and the pass stops — a synapse that never moves is told four times.
    /// </remarks>
    [Test]
    public void NotifyPassed_ASynapseThatNeverMoves_StopsWhenTheBudgetAndItsExtensionRunOut()
    {
        IvpHullManager manager = new() { NextPsiValue = 10f };
        Recorder stuck = new("stuck", []);
        manager.Install(stuck, 0d, 1d);
        List<int> asked = [];

        manager.NotifyPassed(budget: 1, additionalChecks: done =>
        {
            asked.Add(done);
            return asked.Count == 1 ? 2 : 0;
        });

        stuck.Overshoots.Count.ShouldBe(4);
        asked.ShouldBe([1, 3]);
    }

    /// <remarks>
    /// **The TF2 client's budget is `physics_performanceparams_t::Defaults()`'s 250 checks and its extension is zero**
    /// (`vphysics/performance.h:33`, `game/client/physics.cpp:77`), so a synapse that never moves is told 251 times.
    /// </remarks>
    [Test]
    public void NotifyPassed_TheClientsDefaults_TellAStuckSynapseTwoHundredFiftyOneTimes()
    {
        IvpHullManager manager = new() { NextPsiValue = 10f };
        Recorder stuck = new("stuck", []);
        manager.Install(stuck, 0d, 1d);

        manager.NotifyPassed(budget: IvpHullManager.DefaultCheckBudget, additionalChecks: _ => 0);

        stuck.Overshoots.Count.ShouldBe(251);
    }

    /// <remarks>
    /// **A pass rebases a manager whose reset time is under its time**, compared in double, and sets the next reset to the
    /// time plus ten truncated: a reset of `10` at `10` is not under it.
    /// </remarks>
    [TestCase(0, 0.5d, true, 10)]
    [TestCase(10, 10d, false, 10)]
    [TestCase(10, 10.25d, true, 20)]
    public void RebaseIfDue_AResetTimeAgainstTheTime_RebasesOnlyWhenUnderIt(int reset, double time, bool rebased, int nextReset)
    {
        IvpHullManager manager = new() { NextReset = reset, Time = time, Value = 1f };

        manager.RebaseIfDue();

        manager.Value.ShouldBe(rebased ? 0f : 1f);
        manager.NextReset.ShouldBe(nextReset);
    }

    /// <remarks>
    /// **The environment's pass walks the managers the step pushed from the last to the first**, telling and then rebasing
    /// each: four managers pushed in order are told fourth first, and each at half a second rebases to a reset of ten.
    /// </remarks>
    [TestCase(1)]
    [TestCase(4)]
    public void NotifyAll_ManagersInPushedOrder_AreToldLastFirst(int count)
    {
        List<string> told = [];
        List<IvpHullManager> pushed = [];
        List<string> expected = [];

        for (int index = 0; index < count; index++)
        {
            IvpHullManager manager = new() { Time = 0.5d, NextPsiValue = 10f };
            manager.Install(new Recorder($"m{index}", told) { OnPassed = (hull, self) => hull.Remove(self) }, 0.5d, 1d);
            pushed.Add(manager);
            expected.Insert(0, $"m{index}");
        }

        IvpHullManager.NotifyAll(pushed, IvpHullManager.DefaultCheckBudget, _ => 0);

        told.ShouldBe(expected);
        pushed.ShouldAllBe(manager => manager.NextReset == 10);
    }

    /// <summary>A synapse that records what it is told.</summary>
    private sealed class Recorder(string name, List<string> told) : IIvpHullSynapse
    {
        public int? HullSlot { get; set; }

        public List<float> Overshoots { get; } = [];

        public List<(float Value, float Center)> Shifts { get; } = [];

        public Action<IvpHullManager, Recorder>? OnPassed { get; init; }

        public void HullPassed(IvpHullManager manager, float overshoot)
        {
            told.Add(name);
            Overshoots.Add(overshoot);
            OnPassed?.Invoke(manager, this);
        }

        public void Rebased(float valueShift, float centerShift)
        {
            told.Add(name);
            Shifts.Add((valueShift, centerShift));
        }
    }
}
