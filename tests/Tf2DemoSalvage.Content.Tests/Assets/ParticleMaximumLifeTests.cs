using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The longest a definition's effect can live, drawn throughout — what lets playing decline to rebuild one long finished
/// (finding 84): the last emission, plus the longest particle lifetime, plus the no-draw time.
/// </summary>
[TestFixture]
public sealed class ParticleMaximumLifeTests
{
    [Test]
    public void MaximumLife_AnEmitterForTwoSecondsWithHalfSecondParticles_IsTwoAndAHalfPlusTheSleep()
    {
        ParticleEffect.MaximumLife(Timed(2d), null).ShouldBe(2d + 0.5d + 8d, 1e-6);
    }

    [Test]
    public void MaximumLife_AnEmitterForEver_IsUnbounded()
    {
        ParticleEffect.MaximumLife(Timed(0d), null).ShouldBe(double.PositiveInfinity);
    }

    [Test]
    public void MaximumLife_AChild_BoundsByTheLongerAndAMissingChildIsUnbounded()
    {
        ParticleSystem child = Timed(5d) with { Name = "test_child" };
        ParticleSystem parent = Timed(2d) with { Children = ["test_child"] };

        ParticleEffect.MaximumLife(parent, new Dictionary<string, ParticleSystem>(StringComparer.Ordinal) { ["test_child"] = child })
            .ShouldBe(5d + 0.5d + 8d + 8d, 1e-6, "the child's own life and sleep, then the parent's sleep: an upper bound");
        ParticleEffect.MaximumLife(parent, null).ShouldBe(double.PositiveInfinity);
    }

    /// <remarks>
    /// **`Lifetime From Sequence` is bounded by the sheet** (`C_INIT_SequenceLifeTime`): a particle's life is its sequence's
    /// span over "Frames Per Second", or 1 when the span is zero. `rocketjump_flame` uses it, and treating it as unbounded
    /// kept every past rocket-jump smoke buildable — the stall stayed (finding 84). Without a sheet the initializer does
    /// nothing and the earlier lifetime stands.
    /// </remarks>
    [Test]
    public void MaximumLife_LifetimeFromSequence_IsTheLongestSpanOverTheRate()
    {
        ParticleSystem flame = Timed(2d) with
        {
            Initializers =
            [
                .. Timed(2d).Initializers,
                new ParticleFunction("Lifetime From Sequence", "seq", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["Frames Per Second"] = new(DmxAttributeType.Real, 10d),
                }),
            ],
        };
        IReadOnlyList<SheetSequence> sheet = [new SheetSequence(0, false, 30f, []), new SheetSequence(1, false, 12f, [])];

        ParticleEffect.MaximumLife(flame, null, _ => sheet).ShouldBe(2d + 3d + 8d, 1e-6, "30 frames at 10 a second");
        ParticleEffect.MaximumLife(flame, null, _ => null).ShouldBe(2d + 0.5d + 8d, 1e-6, "no sheet: the random lifetime stands");
    }

    private static ParticleSystem Timed(double duration) => new(
        Name: "test_timed",
        Emitters:
        [
            new ParticleFunction("emit_continuously", "emitter", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["emission_rate"] = new(DmxAttributeType.Real, 10d),
                ["emission_duration"] = new(DmxAttributeType.Real, duration),
            }),
        ],
        Initializers:
        [
            new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["lifetime_min"] = new(DmxAttributeType.Real, 0.25d),
                ["lifetime_max"] = new(DmxAttributeType.Real, 0.5d),
            }),
        ],
        Operators: [],
        Renderers: [],
        Children: [],
        Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));
}
