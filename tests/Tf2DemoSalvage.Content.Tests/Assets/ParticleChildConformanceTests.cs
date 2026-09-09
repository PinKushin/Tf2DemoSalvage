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

    /// <summary>A system that emits steadily, with the given children by name.</summary>
    private static ParticleSystem Emitting(string named, IReadOnlyList<string> children) =>
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
