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
            int drawn = ParticleRandom.Whole(seed: 0, particle, offset: 0, least: 0, most: 3);

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
        int first = ParticleRandom.Whole(seed: 0, particle: 41, offset: 0, least: 0, most: 3);

        for (int again = 0; again < 5; again++)
        {
            ParticleRandom.Whole(seed: 0, particle: 41, offset: 0, least: 0, most: 3).ShouldBe(first);
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
            if (ParticleRandom.Whole(0, particle, 0, 0, 3) !=
                ParticleRandom.Whole(0, particle, 1024, 0, 3))
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
            float drawn = ParticleRandom.Between(0, particle, ParticleSystems.LifetimeDraw, 0.8f, 1.2f);

            drawn.ShouldBeInRange(0.8f, 1.2f);

            below |= drawn < 0.95f;
            above |= drawn > 1.05f;
        }

        below.ShouldBeTrue();
        above.ShouldBeTrue();
    }

    /// <remarks>
    /// **`RandomInt` has no early return and no clamp** (`particles.h:1779-1786`, B472): `(int)( r · ( nMax + 1 − nMin ) ) + nMin`.
    /// With 5 and 2 the span is −2, so a draw below one half truncates to 0 and gives 5, and one at or above it truncates
    /// to −1 and gives 4 — below the lower bound, as the engine's does. This test asserted 5 for both before B472.
    /// </remarks>
    [Test]
    public void Whole_AnUpperBoundBelowTheLower_ComputesTheEnginesDraw()
    {
        int low = FirstParticle(drawn => drawn < 0.5f);
        int high = FirstParticle(drawn => drawn >= 0.5f);

        (ParticleRandom.Whole(seed: 0, low, offset: 0, least: 5, most: 2), ParticleRandom.Whole(seed: 0, high, offset: 0, least: 5, most: 2))
            .ShouldBe((5, 4));
    }

    /// <remarks>
    /// **Every index is `( m_nRandomSeed + nRandomSampleId ) &amp; RANDOM_FLOAT_MASK`** (`particles.h:1782`, `:1791`, `:1800`;
    /// B469), so the collection's seed shifts the index exactly as the particle id and the offset do, and wraps with them.
    /// </remarks>
    [Test]
    public void Sample_ASeed_ShiftsTheIndexAsTheParticleAndOffsetDo()
    {
        ParticleRandom.Sample(seed: 7, particle: 3, offset: 11).ShouldBe(ParticleRandom.Sample(seed: 0, particle: 0, offset: 21));
        ParticleRandom.Sample(seed: 4095, particle: 1, offset: 0).ShouldBe(ParticleRandom.Sample(seed: 0, particle: 0, offset: 0), "4096 wraps to 0");
        ParticleRandom.Sample(seed: -1, particle: 1, offset: 0).ShouldBe(ParticleRandom.Sample(seed: 0, particle: 0, offset: 0), "a negative seed is two's complement under the mask");
        ParticleRandom.Sample(seed: 1, particle: 0, offset: 0).ShouldNotBe(ParticleRandom.Sample(seed: 0, particle: 0, offset: 0), "the control: a seed moves the draw");
    }

    /// <summary>The first particle whose seed-0, offset-0 draw satisfies <paramref name="wanted"/>.</summary>
    private static int FirstParticle(System.Func<float, bool> wanted)
    {
        for (int particle = 0; particle < ParticleRandom.Floats; particle++)
        {
            if (wanted(ParticleRandom.Sample(seed: 0, particle, offset: 0)))
            {
                return particle;
            }
        }

        throw new AssertionException("no table entry satisfies the condition");
    }

    [Test]
    public void Sample_EveryEntry_IsInsideZeroToOne()
    {
        // A table entry of exactly 1.0 would make `RandomInt` return `most + 1` — one past the end
        // of a sequence array, on one particle in four thousand.
        for (int particle = 0; particle < ParticleRandom.Floats; particle++)
        {
            float drawn = ParticleRandom.Sample(seed: 0, particle, offset: 0);

            drawn.ShouldBeGreaterThanOrEqualTo(0f);
            drawn.ShouldBeLessThan(1f);
        }
    }
}
