using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The systems a particle system runs alongside itself — its children (B373).
/// </summary>
/// <remarks>
/// **A rocket is three systems, not one.** `rockettrail` declares `rockettrail_burst` and
/// `rockettrail_fire`, both on additive `brightglow_y` materials, so without them a rocket has smoke
/// and no glow at all.
/// </remarks>
[TestFixture]
public sealed class ParticleChildConformanceTests
{
    [Test]
    public void Children_ANamedChild_IsResolvedAndSteppedWithTheParent()
    {
        // **Read from source:** `SetControlPoint` and `SetControlPointOrientation` each walk
        // `m_Children` and pass the same values down (`particles.h:1595`, `:1629`), so a child
        // follows the rocket exactly as the parent's own particles do.
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []),
            ["parent"] = Emitting("parent", ["child"]),
        };

        ParticleEffect effect = new(file["parent"], file);

        effect.Children.Count.ShouldBe(1);
        effect.Children[0].System.Name.ShouldBe("child");

        for (int tick = 0; tick < 20; tick++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 1f / 66f);
        }

        // Both emitted. The child having particles is what says it was STEPPED and not merely
        // constructed — an unstepped child resolves and stays empty for ever.
        effect.Particles.Count.ShouldBeGreaterThan(0);
        effect.Children[0].Particles.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Children_WithNoLookup_AreNotResolvedAtAll()
    {
        // **The control for the test above.** A caller with no file to resolve against gets the
        // trail alone, which is exactly what this project did before children existed — so the
        // assertion above is about resolution rather than about the fixture emitting.
        ParticleEffect effect = new(Emitting("parent", ["child"]));

        effect.Children.ShouldBeEmpty();
    }

    [Test]
    public void Empty_AParentWhoseChildStillHasParticles_IsNotFinished()
    {
        // *"make sure all children are finished"* — `particles.h:1630`. Dropping an effect on its
        // own count alone cuts a rocket's fire off the moment its smoke runs out.
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []),
            ["parent"] = Emitting("parent", ["child"]),
        };

        ParticleEffect effect = new(file["parent"], file);

        for (int tick = 0; tick < 20; tick++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 1f / 66f);
        }

        effect.Empty.ShouldBeFalse();

        // Reap the parent's particles only, leaving the child's alone: the parent is empty by its
        // own count and must still report unfinished.
        while (effect.Particles.Count > 0)
        {
            effect.Particles.Tick(10f);
            effect.Particles.Reap();
        }

        effect.Particles.Count.ShouldBe(0);
        effect.Children[0].Particles.Count.ShouldBeGreaterThan(0);

        effect.Empty.ShouldBeFalse();
    }

    [Test]
    public void Children_ASystemNamingItself_DoesNotRecurseForEver()
    {
        // A `.pcf` is input this project does not control, and a cycle here would recurse until the
        // stack ran out — at load, before anything drew. The guard is that a name is removed from
        // the lookup once used, so the cycle terminates rather than being detected.
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Emitting("a", ["b"]),
            ["b"] = Emitting("b", ["a"]),
        };

        ParticleEffect effect = new(file["a"], file);

        effect.Children.Count.ShouldBe(1);
        effect.Children[0].System.Name.ShouldBe("b");
        effect.Children[0].Children.ShouldBeEmpty();
    }

    [Test]
    public void Children_AgainstTheWholeGamesLookup_DoNotCopyIt()
    {
        // **Measured in the viewer (finding 80):** 342 MB of large-object allocations in twenty seconds of f12 were copies of
        // the 9,050-system lookup, one per child resolved, made to drop the parent's name. A copy of a 20,000-entry
        // dictionary is over 600 KB, so a child resolved against one must allocate far less than that; the grandchild
        // cycle must still end, as the test above holds.
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Emitting("a", ["b"]),
            ["b"] = Emitting("b", ["a"]),
        };

        for (int index = 0; index < 20_000; index++)
        {
            file["filler" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = file["b"];
        }

        _ = new ParticleEffect(file["a"], file);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        ParticleEffect effect = new(file["a"], file);
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        effect.Children[0].Children.ShouldBeEmpty();
        allocated.ShouldBeLessThan(100_000);
    }

    /// <summary>
    /// **A child's delay is a negative clock** (B564), read in the disassembly of `particles.lib`'s `particles.obj`:
    /// `CParticleCollection::Init( pDef, flDelay, seed )` stores `-flDelay` as the collection's times (+0x20 to +0x34),
    /// then makes each child with `m_Children[i].m_flDelay - m_flCurTime`, its own delay plus the parent's.
    /// </summary>
    [Test]
    public void Init_ADelayedChildOfADelayedChild_StartsItsClockAtMinusBothDelays()
    {
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["grandchild"] = Emitting("grandchild", []),
            ["child"] = Emitting("child", [new ParticleChild("grandchild", 0.25f)]),
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.5f)]),
        };

        ParticleEffect effect = new(file["parent"], file);

        (effect.Particles.Age, effect.Children[0].Particles.Age, effect.Children[0].Children[0].Particles.Age)
            .ShouldBe((0f, -0.5f, -0.75f));
    }

    /// <summary>
    /// **While its clock is negative a collection only advances it** (`CParticleCollection::Simulate`, `particles.obj`):
    /// no first frame, no emitter, no operator, and no child simulated, so a grandchild's clock stands while its parent waits.
    /// </summary>
    [Test]
    public void Step_ADelayedChildStillWaiting_EmitsNothingAndItsChildStands()
    {
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["grandchild"] = Emitting("grandchild", []),
            ["child"] = Emitting("child", [new ParticleChild("grandchild", 0.25f)]),
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.5f)]),
        };

        ParticleEffect effect = new(file["parent"], file);

        for (int step = 0; step < 4; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.0625f);
        }

        ParticleEffect child = effect.Children[0];

        effect.Particles.Count.ShouldBeGreaterThan(0);
        (child.Particles.Age, child.Particles.Count, child.Particles.Steps).ShouldBe((-0.25f, 0, 0));
        child.Children[0].Particles.Age.ShouldBe(-0.75f);
        child.Finished.ShouldBeFalse();
    }

    [Test]
    public void Step_ADelayedChildPastItsDelay_StartsOnTheStepAfterItsClockCrossesZero()
    {
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []),
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.5f)]),
        };

        ParticleEffect effect = new(file["parent"], file);
        ParticleEffect child = effect.Children[0];

        // Eight steps of 1/16 bring the clock to exactly 0, the last of them still a waiting one; the ninth simulates.
        for (int step = 0; step < 8; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.0625f);
        }

        (child.Particles.Age, child.Particles.Steps).ShouldBe((0f, 0));

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.0625f);

        (child.Particles.Age, child.Particles.Steps).ShouldBe((0.0625f, 1));
        child.Particles.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// **A waiting call keeps its `dt` as the previous one** (`UpdatePrevControlPoints` sets +0x40, `0x000e7540`), where a
    /// collection never called has `0x3d4ccccd`, 0.05.
    /// </summary>
    [Test]
    public void Step_ADelayedChildWaiting_KeepsTheCallsStepAsThePreviousStep()
    {
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []),
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.5f)]),
        };

        ParticleEffect effect = new(file["parent"], file);

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.125f);

        (effect.Children[0].Particles.Age, effect.Children[0].Particles.PreviousStep).ShouldBe((-0.375f, 0.125f));
    }

    /// <summary>The waiting branch does not read `updateBboxOnly`, so a delayed child's clock runs under a sleeping parent.</summary>
    [Test]
    public void SimulateBoundsOnly_ADelayedChildWaiting_AdvancesItsClock()
    {
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []),
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.5f)]),
        };

        ParticleEffect effect = new(file["parent"], file);

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.125f);
        effect.SimulateBoundsOnly(0.125f);

        effect.Children[0].Particles.Age.ShouldBe(-0.25f);
    }

    /// <summary>`Simulate` runs `SimulateFirstFrame` before it tests `updateBboxOnly`, so a just-started child gets its first frame.</summary>
    [Test]
    public void SimulateBoundsOnly_AChildWhoseClockHasReachedZero_MakesItsInitialParticles()
    {
        Dictionary<string, DmxValue> initial = new(System.StringComparer.Ordinal)
        {
            ["initial_particles"] = new DmxValue(DmxAttributeType.Whole, 3d),
        };
        Dictionary<string, ParticleSystem> file = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["child"] = Emitting("child", []) with { Parameters = initial },
            ["parent"] = Emitting("parent", [new ParticleChild("child", 0.125f)]),
        };

        ParticleEffect effect = new(file["parent"], file);

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), 0.125f);
        effect.Children[0].Particles.Count.ShouldBe(0);

        effect.SimulateBoundsOnly(0.125f);

        effect.Children[0].Particles.Count.ShouldBe(3);
    }

    /// <summary>A system that emits steadily, with the given children by name.</summary>
    private static ParticleSystem Emitting(string named, IReadOnlyList<ParticleChild> children) =>
        new(
            named,
            Emitters:
            [
                new ParticleFunction(
                    "emit_continuously",
                    "emit",
                    new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
                    {
                        ["emission_rate"] = new DmxValue(DmxAttributeType.Real, 128d),
                    }),
            ],
            Initializers:
            [
                new ParticleFunction(
                    "Lifetime Random",
                    "life",
                    new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 5d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 5d),
                    }),
            ],
            Operators: [],
            Renderers: [],
            Children: children,
            Parameters: new Dictionary<string, DmxValue>(System.StringComparer.Ordinal)
            {
                ["material"] = new DmxValue(
                    DmxAttributeType.Text, Text: $"effects\\{named}.vmt"),
            });
}
