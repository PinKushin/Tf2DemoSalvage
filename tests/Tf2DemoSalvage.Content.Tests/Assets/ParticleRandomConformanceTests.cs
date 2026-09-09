using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// The draws a particle initializer makes — <c>CParticleCollection::RandomInt</c> (B373).
/// </summary>
/// <remarks>
/// **What is asserted here is the ARITHMETIC and the properties, not the numbers**, because the
/// engine's table ships only in a binary and this project's is its own. What the engine's scheme
/// guarantees — an inclusive upper bound, a value keyed on the particle rather than on call order,
/// and independence between two draws for one particle — is exactly what these test.
/// </remarks>
[TestFixture]
public sealed class ParticleRandomConformanceTests
{
    [Test]
    public void Whole_TheUpperBound_IsIncluded()
    {
        // **`nMax + 1 - nMin`, `particles.h:1783`.** `rockettrail` declares `sequence_max = 3`
        // against a texture with four sequences, so an exclusive bound would leave a quarter of the
        // sheet unreachable — and everything would still draw, which is why this needs a test.
        bool sawMost = false;
        bool sawLeast = false;

        for (int particle = 0; particle < ParticleRandom.Floats; particle++)
        {
            int drawn = ParticleRandom.Whole(particle, offset: 0, least: 0, most: 3);

            drawn.ShouldBeInRange(0, 3);

            sawMost |= drawn == 3;
            sawLeast |= drawn == 0;
        }

        sawMost.ShouldBeTrue();
        sawLeast.ShouldBeTrue();
    }

    [Test]
    public void Whole_OneParticle_DrawsTheSameValueEveryTime()
    {
        // **The property that makes a replay reproducible without a seed** (D136): the draw is a
        // function of the particle's id, not of how many draws came before it. A generator with
        // internal state would fail this on the second call.
        int first = ParticleRandom.Whole(particle: 41, offset: 0, least: 0, most: 3);

        for (int again = 0; again < 5; again++)
        {
            ParticleRandom.Whole(particle: 41, offset: 0, least: 0, most: 3).ShouldBe(first);
        }
    }

    [Test]
    public void Whole_TwoDrawsForOneParticle_AreIndependent()
    {
        // Without the offset, `Lifetime Random` and `Sequence Random` would read one table entry for
        // one particle and the longest-lived puffs would all play the same animation. Over a full
        // table of particles the two draws must disagree far more often than they agree.
        int differed = 0;

        for (int particle = 0; particle < 512; particle++)
        {
            if (ParticleRandom.Whole(particle, 0, 0, 3) !=
                ParticleRandom.Whole(particle, 1024, 0, 3))
            {
                differed++;
            }
        }

        // Four outcomes, so two independent draws agree about a quarter of the time. Anything above
        // 320 of 512 is far outside that, and a correlated pair would score zero.
        differed.ShouldBeGreaterThan(320);
    }

    [Test]
    public void Between_ATrailsDeclaredBounds_SpreadAcrossThemRatherThanSittingAtTheMidpoint()
    {
        // **The divergence this replaced.** `rockettrail` declares `lifetime_min 0.8` and
        // `lifetime_max 1.2`, and the code before this took the midpoint — so every particle in a
        // trail lived exactly 1.0 seconds and the plume died all at once. The assertion is that
        // values appear in both halves of the band, which a midpoint cannot produce.
        bool below = false;
        bool above = false;

        for (int particle = 0; particle < 512; particle++)
        {
            float drawn = ParticleRandom.Between(particle, ParticleSystems.LifetimeDraw, 0.8f, 1.2f);

            drawn.ShouldBeInRange(0.8f, 1.2f);

            below |= drawn < 0.95f;
            above |= drawn > 1.05f;
        }

        below.ShouldBeTrue();
        above.ShouldBeTrue();
    }

    [Test]
    public void Whole_AnUpperBoundBelowTheLower_ReturnsTheLower()
    {
        // A definition can declare `sequence_min` above `sequence_max`; the engine's arithmetic
        // would give a negative span and an index below the array. Refusing is the only answer that
        // cannot index out of a sequence list.
        ParticleRandom.Whole(particle: 7, offset: 0, least: 5, most: 2).ShouldBe(5);
    }

    [Test]
    public void Sample_EveryEntry_IsInsideZeroToOne()
    {
        // A table entry of exactly 1.0 would make `RandomInt` return `most + 1` — one past the end
        // of a sequence array, on one particle in four thousand.
        for (int particle = 0; particle < ParticleRandom.Floats; particle++)
        {
            float drawn = ParticleRandom.Sample(particle, offset: 0);

            drawn.ShouldBeGreaterThanOrEqualTo(0f);
            drawn.ShouldBeLessThan(1f);
        }
    }
}
