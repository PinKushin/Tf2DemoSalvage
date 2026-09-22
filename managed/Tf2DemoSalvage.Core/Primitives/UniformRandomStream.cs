using System;

namespace Tf2DemoSalvage.Core.Primitives;

/// <summary>Valve's uniform random number generator, <c>CUniformRandomStream</c> (B415).</summary>
/// <remarks>
/// **A hitscan shot is a seed, which is why this exists.** `DT_TEFireBullets` sends an origin, two angles, a weapon id,
/// a mode, a spread, a crit flag and <c>m_iSeed</c> — and no bullet paths at all. `FX_FireBullets` rebuilds every
/// pellet's direction by reseeding this generator per bullet, so a tracer can only be drawn where TF2 drew it if the
/// stream matches bit for bit. Full account, with the call path and the numbers: `docs/findings/57-the-shot-is-a-seed.md`.
///
/// **Every constant below was read out of `vstdlib.dll`'s disassembly, not out of Numerical Recipes.**
/// `public/vstdlib/random.h` declares `m_idum`, `m_iy` and `m_iv[NTAB]` and not one value; `random.cpp` ships closed.
/// The algorithm is `ran1` — a Park–Miller generator behind a Bays–Durham shuffle — and the shipped function's own
/// assert names its source file, `src\vstdlib\random.cpp`.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification =
        "The engine's own type is CUniformRandomStream and D163 keeps Valve's names. CA1711 guards against a name " +
        "implying System.IO.Stream, which this cannot be mistaken for: it has no IO surface at all, and the suffix is " +
        "Valve's sense of a stream of numbers. Renaming it would cost the one thing that ties this file to the " +
        "disassembly it was read from.")]
public sealed class UniformRandomStream
{
    /// <summary>The Park–Miller multiplier — <c>0x41a7</c> in <c>GenerateRandomNumber</c> (<c>0x18000f110</c>).</summary>
    private const int Multiplier = 16807;

    /// <summary>The quotient <c>IM / IA</c> — <c>0x1f31d</c>, the divisor of the Schrage step.</summary>
    private const int Quotient = 127773;

    /// <summary>The modulus — the <c>-0x7fffffff</c> term, and the value added back on the negative branch.</summary>
    private const int Modulus = int.MaxValue;

    /// <summary>The shuffle table's size — the <c>&lt; 0x20</c> guards, and <c>NTAB</c> in the header.</summary>
    private const int TableSize = 32;

    /// <summary>
    /// What the shuffle index divides by — the <c>&gt;&gt; 0x1a</c>, which is <c>1 + (IM - 1) / NTAB</c>.
    /// </summary>
    private const int IndexDivisor = 1 << 26;

    /// <summary>The priming loop's FIRST index — <c>NTAB + 7</c>, counted down to zero inclusive.</summary>
    /// <remarks>
    /// **Forty advances, not thirty-nine**, and the difference is not cosmetic: the decompiled loop starts at
    /// <c>uVar8 = 0x27</c> and runs <c>while (-1 &lt; uVar8)</c>, so index 0 is primed too. Starting one lower gave a
    /// generator that passed every self-consistency test in the suite and disagreed with the shipped DLL on 1,767 of
    /// 1,800 draws — which is what `vstdlib-random` exists to catch.
    /// </remarks>
    private const int FirstPrimed = TableSize + 7;

    /// <summary>
    /// <c>AM</c>, the double at <c>0x180031df8</c>: <c>4.656612875245797E-10</c>, which is <c>1 / int.MaxValue</c>.
    /// </summary>
    private const double Scale = 4.656612875245797E-10;

    /// <summary>The double at <c>0x180031e08</c> that the rounded product is compared against.</summary>
    private const double ClampAbove = 0.99999988d;

    /// <summary>The float at <c>0x180031e00</c>, <c>0x3f7ffffe</c>, substituted when the product exceeds the compare.</summary>
    private const float Clamped = 0.9999999f;

    private readonly int[] _table = new int[TableSize];
    private int _seed;
    private int _last;

    /// <summary>Seeds the stream — <c>SetSeed</c> (<c>0x18000fb10</c>), which is two writes and no draw.</summary>
    /// <param name="seed">The seed; its sign is discarded.</param>
    /// <remarks>
    /// <c>m_iy = 0</c> and <c>m_idum = -|seed|</c>. **The negative is what arms the warm-up** — the generator takes its
    /// priming branch on a non-positive <c>m_idum</c>, so seeding must not produce a number by itself.
    /// </remarks>
    public void SetSeed(int seed)
    {
        _last = 0;
        _seed = -Math.Abs(seed);
    }

    /// <summary>A float in a range — <c>RandomFloat</c> (<c>0x18000f9b0</c>).</summary>
    /// <param name="minimum">The bottom of the range, which a draw can reach.</param>
    /// <param name="maximum">The top, which it cannot, the fraction being clamped below one.</param>
    /// <returns>The value.</returns>
    public float RandomFloat(float minimum, float maximum)
    {
        // **Rounded to float FIRST and widened back for the comparison, which is what the engine does.** Comparing in
        // double throughout would clamp on a different set of inputs — the one place a faithful-looking rewrite drifts.
        float fraction = (float)(GenerateRandomNumber() * Scale);

        if (ClampAbove < fraction)
        {
            fraction = Clamped;
        }

        return ((maximum - minimum) * fraction) + minimum;
    }

    /// <summary>An integer in an inclusive range, through the same stream.</summary>
    /// <param name="minimum">The lowest value.</param>
    /// <param name="maximum">The highest.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// *`CUniformRandomStream::RandomInt`'s own mapping is NOT read* — this takes the generator's number modulo the
    /// span, which is the obvious reading and not an observed one. Nothing in the bullet path uses it; it is here so a
    /// test can watch the raw stream. Flagged in `docs/findings/57` as owed.
    /// </remarks>
    public int RandomInt(int minimum, int maximum)
    {
        long span = (long)maximum - minimum + 1L;

        return (int)(minimum + (GenerateRandomNumber() % span));
    }

    /// <summary>One draw — <c>GenerateRandomNumber</c> (<c>0x18000f110</c>).</summary>
    private int GenerateRandomNumber()
    {
        if (_seed <= 0 || _last == 0)
        {
            Prime();
        }

        _seed = Advance(_seed);

        // `(int)(iy + (iy >> 0x1f & 0x3ffffff)) >> 0x1a` — a signed divide by 2^26, rounding toward zero.
        int index = _last / IndexDivisor;

        // The engine's own guard, which warns and masks rather than trusting the arithmetic:
        // "CUniformRandomStream had an array overrun: tried to write to element %d of 0..31."
        if ((uint)index >= TableSize)
        {
            index &= TableSize - 1;
        }

        _last = _table[index];
        _table[index] = _seed;

        return _last;
    }

    /// <summary>Fills the shuffle table before the first draw is answered.</summary>
    /// <remarks>
    /// <c>NTAB + 7</c> advances, of which the last <c>NTAB</c> are stored — and stored BACKWARDS, which is what the
    /// decompiled loop's descending index does. Filling forwards gives a generator that is a fine `ran1` and not this one.
    /// </remarks>
    private void Prime()
    {
        int running = -_seed;

        if (running < 1)
        {
            running = 1;
        }

        for (int step = FirstPrimed; step >= 0; step--)
        {
            running = Advance(running);

            if (step < TableSize)
            {
                _table[step] = running;
            }
        }

        _seed = running;
        _last = _table[0];
    }

    /// <summary>The Schrage step, as the compiler folded it.</summary>
    /// <remarks>
    /// Textbook `ran1` is <c>idum = IA * (idum - k * IQ) - IR * k</c>. The shipped code is
    /// <c>idum * IA + (idum / IQ) * -IM</c>, which is the same function because <c>IA * IQ + IR == IM</c>
    /// (<c>16807 * 127773 + 2836 == 2147483647</c>). **IR is absorbed, not missing** — reading its absence as a zero
    /// would give a different generator.
    /// </remarks>
    private static int Advance(int value)
    {
        int stepped = (value * Multiplier) + ((value / Quotient) * -Modulus);

        return stepped < 0 ? stepped + Modulus : stepped;
    }
}
