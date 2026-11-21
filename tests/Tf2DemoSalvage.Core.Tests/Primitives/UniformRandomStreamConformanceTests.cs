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

    /// <remarks>
    /// **The shipped DLL's own answers, printed by `vstdlib-random int`** for seed 0, in this order, which reports 2,100
    /// calls over seven seeds agreeing with the exported `RandomInt` (B733). The last three were added in review: a
    /// difference of 2^31 answers its low end without a draw, so the `(1, 100)` after them is the next draw, 92. `RandomInt` (`0x18000fb70`) works in unsigned
    /// 32 bits: a span of one or less answers its low end without a draw — `(5, 5)`, the reversed `(0, -1)`, and the whole
    /// `int` range, whose span wraps to 0 — and `0..2^30` refuses every draw above `0x7fffffff - 2^31 % span = 2^30`, about
    /// half of them, and draws again.
    /// </remarks>
    [Test]
    public void RandomInt_SeededAtZero_IsTheShippedLibrarysOwnSequence()
    {
        (int Low, int High, int Expected)[] calls =
        [
            (5, 5, 5), (1, 100, 17), (0, -1, 0), (0, 0x40000000, 197493099), (1, 9, 2), (0, 3, 3), (0, 0x40000000, 823564440),
            (0, int.MaxValue - 1, 1404280278), (-7, 7, 0), (int.MinValue, int.MaxValue, int.MinValue), (0, 0x40000000, 823378840),
            (1, 100, 29), (-1, int.MaxValue, -1), (int.MinValue, 0, int.MinValue), (1, 100, 92),
        ];
        UniformRandomStream stream = new();

        stream.SetSeed(0);

        foreach ((int low, int high, int expected) in calls)
        {
            stream.RandomInt(low, high).ShouldBe(expected, $"RandomInt({low}, {high})");
        }
    }

    [Test]
    public void RandomInt_AFirstDrawAboveTheThreshold_IsRefusedAndTheSecondAnswers()
    {
        // `0..2^29`: span 2^29 + 1, and 2^31 % span = 2^29 - 3, so `maxAcceptable = 0x7fffffff - (2^29 - 3) = 0x60000002`.
        // Seed 3's generator numbers are 1928259290 then 1198830848 (the control stream reads them: `RandomInt( 0,
        // 0x7ffffffe )` refuses none, its threshold being the generator's ceiling). The first is above 0x60000002 and is
        // refused — `ja` back to the call — so the answer is the second mod the span: 1198830848 - 2 * 536870913 =
        // 125089022. Taking the first would answer 1928259290 - 3 * 536870913 = 317646551.
        const int Seed = 3;
        UniformRandomStream control = new();
        UniformRandomStream asked = new();

        control.SetSeed(Seed);
        asked.SetSeed(Seed);

        control.RandomInt(0, int.MaxValue - 1).ShouldBe(1928259290, "the condition: the first number is above the threshold");
        control.RandomInt(0, int.MaxValue - 1).ShouldBe(1198830848, "and the second below it");
        asked.RandomInt(0, 0x20000000).ShouldBe(125089022);
    }

    [TestCase(5, 5)]
    [TestCase(0, -1)]
    [TestCase(int.MinValue, int.MaxValue)]
    public void RandomInt_ASpanOfOneOrLess_IsItsLowAndDrawsNothing(int low, int high) => LowWithoutADraw(low, high);

    [TestCase(-1, int.MaxValue)]
    [TestCase(int.MinValue, 0)]
    public void RandomInt_ADifferenceAboveMaxRandomRange_IsItsLowAndDrawsNothing(int low, int high) =>
        // `cmp r8d, 0x7fffffff; ja` (`0x18000fb9a`): a difference of 2^31, whose span 2^31 + 1 is above 1, still answers
        // its low end without a draw.
        LowWithoutADraw(low, high);

    private static void LowWithoutADraw(int low, int high)
    {
        // `cmp ebp, 1; jbe` before any `call GenerateRandomNumber`: the stream is left where it was.
        UniformRandomStream asked = new();
        UniformRandomStream control = new();

        asked.RandomInt(low, high).ShouldBe(low);
        asked.RandomInt(0, int.MaxValue - 1).ShouldBe(control.RandomInt(0, int.MaxValue - 1), "no draw was taken");
    }
}
