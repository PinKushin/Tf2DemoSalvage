using System;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Core.Tests.Primitives;

/// <summary>Valve's own uniform RNG, <c>CUniformRandomStream</c> (B415, `docs/findings/57`).</summary>
/// <remarks>
/// **A hitscan shot is a seed, so this has to match bit for bit.** `DT_TEFireBullets` sends no bullet paths at all —
/// the client rebuilds every pellet's direction from `m_iSeed` through <c>RandomSeed</c>/<c>RandomFloat</c>. A generator
/// that is merely a good `ran1` would put every tracer somewhere plausible and wrong.
///
/// **Every constant here was read out of `vstdlib.dll`, not out of Numerical Recipes**, because `random.cpp` ships
/// closed and the header declares `m_idum`, `m_iy`, `m_iv[NTAB]` with no values. The algorithm's own assert names its
/// source file, `src\vstdlib\random.cpp`.
///
/// <code>
/// GenerateRandomNumber  0x18000f110   idum*0x41a7 + (idum/0x1f31d)*-0x7fffffff;  j = iy >> 0x1a
/// SetSeed               0x18000fb10   m_iy = 0;  m_idum = -|seed|
/// RandomFloat           0x18000f9b0   (float)(n * 4.656612875245797E-10), clamped, then scaled
/// </code>
/// </remarks>
public sealed class UniformRandomStreamConformanceTests
{
    /// <remarks>
    /// <c>SetSeed</c> is two writes and no generation: <c>m_iy = 0</c> and <c>m_idum = -|seed|</c>. The negative idum is
    /// what makes the next call take the warm-up branch, so seeding must NOT produce a number by itself.
    /// </remarks>
    [Test]
    public void SetSeed_ANegativeSeed_IsTheSameStreamAsItsPositive()
    {
        UniformRandomStream positive = new();
        UniformRandomStream negative = new();

        positive.SetSeed(1337);
        negative.SetSeed(-1337);

        negative.RandomInt(0, int.MaxValue - 1)
            .ShouldBe(positive.RandomInt(0, int.MaxValue - 1), "m_idum is -|seed|, so the sign cannot matter");
    }

    /// <remarks>
    /// **The determinism the whole feature rests on.** `FX_FireBullets` reseeds with `iSeed` at the top of every bullet
    /// and increments it afterwards, so a shotgun's pellets are a seed RUN — two streams seeded alike must agree for as
    /// long as they are asked.
    /// </remarks>
    [Test]
    public void RandomFloat_TwoStreamsOnOneSeed_AgreeForEveryDraw()
    {
        UniformRandomStream first = new();
        UniformRandomStream second = new();

        first.SetSeed(20260920);
        second.SetSeed(20260920);

        for (int draw = 0; draw < 200; draw++)
        {
            second.RandomFloat(-0.5f, 0.5f).ShouldBe(first.RandomFloat(-0.5f, 0.5f));
        }
    }

    /// <remarks>
    /// A different seed must give a different stream, or the test above would pass on a generator that returns a
    /// constant — the control without which "deterministic" and "broken" look identical.
    /// </remarks>
    [Test]
    public void RandomInt_TwoStreamsOnDifferentSeeds_Diverge()
    {
        UniformRandomStream first = new();
        UniformRandomStream second = new();

        first.SetSeed(1);
        second.SetSeed(2);

        bool differed = false;

        // Compared as integers rather than floats: the raw stream is what differs, and an exact
        // comparison on it is meaningful where one on a scaled float would not be.
        for (int draw = 0; draw < 32 && !differed; draw++)
        {
            differed = first.RandomInt(0, int.MaxValue - 1) != second.RandomInt(0, int.MaxValue - 1);
        }

        differed.ShouldBeTrue("two seeds must not give one stream");
    }

    /// <remarks>
    /// The range mapping is <c>(max - min) * t + min</c> over a <c>t</c> the engine clamps to <c>0.9999999f</c>, so a
    /// draw can reach the bottom of the range and never quite its top.
    /// </remarks>
    [Test]
    public void RandomFloat_OverAThousandDraws_StaysInsideTheRequestedRange()
    {
        UniformRandomStream stream = new();
        stream.SetSeed(4242);

        for (int draw = 0; draw < 1000; draw++)
        {
            float value = stream.RandomFloat(-0.5f, 0.5f);

            value.ShouldBeGreaterThanOrEqualTo(-0.5f);
            value.ShouldBeLessThan(0.5f);
        }
    }

    /// <remarks>
    /// **These are the shipped DLL's own numbers, printed by the `vstdlib-random` probe**, which seeds the exported
    /// `RandomSeed` and compares `RandomFloat(-0.5f, 0.5f)` draw for draw against this type. At the time of writing it
    /// reports 1,800 draws over nine seeds, and 3,000 over one, agreeing bit for bit.
    ///
    /// **Held as raw bits, because that is what "bit for bit" means** — a tolerance here would accept a generator that
    /// is merely close, and the whole point is that a hitscan spread rebuilt from a near-miss stream puts every tracer
    /// somewhere plausible and wrong.
    ///
    /// **This test is what the five above could not do.** They pin determinism, range and divergence, and every one of
    /// them passed while the priming loop ran one iteration short — a generator that disagreed with the engine on 1,767
    /// of 1,800 draws. Only the oracle found it, and only these constants keep it found.
    /// </remarks>
    [TestCase(0, -0.08400065f)]
    [TestCase(1, -0.4080351f)]
    [TestCase(2, 0.25641048f)]
    [TestCase(3, 0.02970022f)]
    [TestCase(4, 0.4304365f)]
    public void RandomFloat_SeededAtOne_IsTheShippedLibrarysOwnDraw(int draw, float expected)
    {
        UniformRandomStream stream = new();
        stream.SetSeed(1);

        float drawn = 0f;

        for (int step = 0; step <= draw; step++)
        {
            drawn = stream.RandomFloat(-0.5f, 0.5f);
        }

        BitConverter.SingleToInt32Bits(drawn)
            .ShouldBe(
                BitConverter.SingleToInt32Bits(expected),
                $"draw {draw} on seed 1 must be vstdlib's own bits");
    }
}
