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
    /// **`Lifetime Random` mirrored exactly** (`ParticleSystems`, `C_INIT_RandomLifeTime`): `lifetime_min` defaults to the
    /// lifetime as it stands and `lifetime_max` to `lifetime_min`, and the draw lies between the two either way round.
    /// </remarks>
    [Test]
    public void MaximumLife_LifetimeRandomDefaults_AreThoseTheInitializerReads()
    {
        ParticleEffect.MaximumLife(WithLifetime(("lifetime_min", 3d)), null).ShouldBe(2d + 3d + 8d, 1e-6, "no max: the max is the min");
        ParticleEffect.MaximumLife(WithLifetime(("lifetime_min", 4d), ("lifetime_max", 2d)), null)
            .ShouldBe(2d + 4d + 8d, 1e-6, "max below min: the larger bounds the draw");
        ParticleEffect.MaximumLife(WithLifetime(), null).ShouldBe(2d + 1d + 8d, 1e-6, "no keys: the default lifetime stands");
    }

    [Test]
    public void MaximumLife_LifetimeRandomWithNoKeysAfterLifetimeFromSequence_KeepsTheSequenceLife()
    {
        ParticleSystem flame = Timed(2d) with
        {
            Initializers =
            [
                new ParticleFunction("Lifetime From Sequence", "seq", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["Frames Per Second"] = new(DmxAttributeType.Real, 10d),
                }),
                new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)),
            ],
        };
        IReadOnlyList<SheetSequence> sheet = [new SheetSequence(0, false, 50f, [])];

        ParticleEffect.MaximumLife(flame, null, _ => sheet).ShouldBe(2d + 5d + 8d, 1e-6);
    }

    /// <remarks>
    /// **Every lifetime writer ParticleSystems runs is bounded or called unbounded** (review): `move particles between 2
    /// control points` sets the life to distance over speed, and `remap initial scalar` can write the life (field 1), so
    /// neither can be bounded from the definition. An initializer the viewer does not run writes no lifetime here.
    /// </remarks>
    [Test]
    public void MaximumLife_LifetimeWritersWithoutABound_AreUnboundedAndOthersAreNot()
    {
        ParticleEffect.MaximumLife(Initialized("move particles between 2 control points"), null).ShouldBe(double.PositiveInfinity);
        ParticleEffect.MaximumLife(Initialized("remap initial scalar", ("output field", 1d)), null).ShouldBe(double.PositiveInfinity);
        ParticleEffect.MaximumLife(Initialized("remap initial scalar", ("output field", 3d)), null)
            .ShouldBe(2d + 0.5d + 8d, 1e-6, "the control: remapping the radius leaves the life bounded");
    }

    private static ParticleSystem WithLifetime(params (string Key, double Value)[] keys)
    {
        Dictionary<string, DmxValue> parameters = new(StringComparer.Ordinal);

        foreach ((string key, double value) in keys)
        {
            parameters[key] = new DmxValue(DmxAttributeType.Real, value);
        }

        return Timed(2d) with { Initializers = [new ParticleFunction("Lifetime Random", "life", parameters)] };
    }

    private static ParticleSystem Initialized(string function, params (string Key, double Value)[] keys)
    {
        Dictionary<string, DmxValue> parameters = new(StringComparer.Ordinal);

        foreach ((string key, double value) in keys)
        {
            parameters[key] = new DmxValue(DmxAttributeType.Real, value);
        }

        return Timed(2d) with { Initializers = [.. Timed(2d).Initializers, new ParticleFunction(function, "it", parameters)] };
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
