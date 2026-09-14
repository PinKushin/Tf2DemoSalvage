using System.Collections.Generic;
using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// What a queued mindist does when its event fires — <c>FUN_1800992e0</c>, slot 1 of both mindist vtables (B369).
/// </summary>
/// <remarks>
/// **Read from the disassembly** (`docs/findings/51`, *When a queued mindist fires*, and *The event queue, and where the
/// far branch hands a pair off*). The minimize runs first; flags `&amp; 0xc000` end it; a kind with low bits set reschedules in
/// mode 1; otherwise `(float)(0.1·d + margin)` over the length collides through the mindist's `+0x40`, and anything else —
/// a NaN included — reschedules in mode 2.
/// </remarks>
public sealed class IvpMindistFireConformanceTests
{
    private static readonly float Threshold = IvpCollisionTolerance.EdgeTargetScale + IvpCollisionTolerance.MarginFor(0);

    /// <remarks>
    /// **The minimize runs before anything is decided, and its flags decide**: a minimize that leaves `0xc000` set ends the
    /// fire with nothing rescheduled and no impact.
    /// </remarks>
    [Test]
    public void Handle_AMinimizeThatLeavesTheFrozenBits_EndsTheFire()
    {
        Fixture fixture = new(0x20, 0.1f) { MinimizeSets = 0xc000 };

        fixture.Handle().ShouldBe(IvpFireOutcome.Frozen);

        fixture.Calls.ShouldBe(["minimize"]);
    }

    /// <remarks>**A kind with low bits set is a feature change**: rescheduled in mode 1 however short the length.</remarks>
    [Test]
    public void Handle_AFeatureChangeKind_ReschedulesInModeOne()
    {
        Fixture fixture = new(0x21, 0.01f);

        fixture.Handle().ShouldBe(IvpFireOutcome.Rescheduled);

        fixture.Calls.ShouldBe(["minimize", "reschedule AfterFeatureChange"]);
    }

    /// <remarks>
    /// **A collision kind whose re-minimized length is under `0.1·d + margin` collides.** In metres that threshold is
    /// about `0.00698`, so `0.005f` is under it.
    /// </remarks>
    [Test]
    public void Handle_ACollisionKindUnderTheThreshold_Collides()
    {
        Fixture fixture = new(0x20, 0.005f);

        fixture.Handle().ShouldBe(IvpFireOutcome.Collided);

        fixture.Calls.ShouldBe(["minimize", "impact"]);
    }

    /// <remarks>
    /// **At or over the threshold, or NaN, it is rescheduled in mode 2** — `COMISS` then `JBE` takes both a tie and an
    /// unordered compare.
    /// </remarks>
    [TestCase(0.3f)]
    [TestCase(float.NaN)]
    public void Handle_ACollisionKindAtOrOverTheThreshold_ReschedulesInModeTwo(float length)
    {
        Fixture fixture = new(0x20, length);

        fixture.Handle().ShouldBe(IvpFireOutcome.Rescheduled);

        fixture.Calls.ShouldBe(["minimize", "reschedule AfterMiss"]);
    }

    /// <remarks>**A length exactly at the threshold is not under it**, and is rescheduled.</remarks>
    [Test]
    public void Handle_ACollisionKindExactlyAtTheThreshold_Reschedules()
    {
        Fixture fixture = new(0x20, Threshold);

        fixture.Handle().ShouldBe(IvpFireOutcome.Rescheduled);
    }

    private sealed class Fixture
    {
        private readonly IvpMindist _mindist;

        public Fixture(int kind, float length) =>
            _mindist = new IvpMindist(
                new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Point),
                new IvpSynapse(new IvpLedgeEdge(0, 0), IvpFeatureKind.Triangle),
                extraRadius: 0f)
            {
                Flags = kind,
                Length = length,
            };

        public int MinimizeSets { get; init; }

        public List<string> Calls { get; } = [];

        public IvpFireOutcome Handle() =>
            IvpMindistFire.Handle(
                _mindist,
                mindist =>
                {
                    Calls.Add("minimize");
                    mindist.Flags |= MinimizeSets;
                },
                recheck => Calls.Add($"reschedule {recheck}"),
                _ => Calls.Add("impact"));
    }
}
