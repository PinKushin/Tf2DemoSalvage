namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Valve's arithmetic material proxies, <c>mathproxy.cpp</c> (B337).
/// </summary>
/// <remarks>
/// **Together they outrank everything this project evaluates.** Measured with `vmt-proxy` over the
/// 30,684 shipped materials: `Multiply` on 4,654, `Equals` on 3,870, `LessOrEqual` on 76, `Add` on
/// 69, `Subtract` on 24, `Clamp` on 20, `Divide` on 8 — against `Sine` on 322 and `TextureScroll`
/// on 283, the two that did run.
///
/// **`Equals` is the one that unblocks a chain.** `YellowLevel` writes `$yellow` and two `Equals`
/// proxies copy it into `$color2` and `$selfillumtint` (`soldier_red.vmt`), so the jarate tint
/// reaches nothing without it — running `YellowLevel` alone would be half a mechanism, which is the
/// shape B330 already recorded for `ItemTintColor` and `SelectFirstIfNonZero`.
///
/// **Two divergences are stated rather than hidden, because this layer holds every variable as a
/// triple.** The engine picks a result type per bind — `CFunctionProxy::ComputeResultType`
/// (`functionproxy.cpp:231`): the RESULT variable's own type wins, then src1's, then src2's. So:
///
/// - **The INT path is not reproduced.** `Add` on an integer result computes
///   `GetIntValue() + GetIntValue()` and writes a FLOAT; componentwise arithmetic on floats gives
///   the same answer for every value TF2 actually stores, and would differ only for a variable
///   holding a fraction that the engine would have truncated first.
/// - **`vecSize` is always three here.** The engine reads 2 for a two-component variable, and a
///   third component that should have been left alone is instead written. No shipped material seen
///   so far runs a math proxy on a two-component variable; if one turns up, this is where it breaks.
///
/// Written before the implementation, so these are claims about the engine rather than a
/// description of what got built.
/// </remarks>
public sealed class MathProxyConformanceTests
{
    /// <remarks>
    /// **`Equals` is a copy and nothing more** — `m_pResult->SetVecValue( a )`. It exists because a
    /// proxy can only write one variable, so a value needed in two places is copied.
    /// </remarks>
    [Test]
    public void Equals_ASourceValue_IsCopiedUnchanged()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Equals, (6f, 9f, 2f), (0f, 0f, 0f))
            .ShouldBe((6f, 9f, 2f));
    }

    [Test]
    public void Add_TwoSources_AreSummedComponentwise()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Add, (1f, 2f, 3f), (10f, 20f, 30f))
            .ShouldBe((11f, 22f, 33f));
    }

    /// <remarks>
    /// **Order matters and is src1 − src2**, which is the way round a symmetric test cannot see.
    /// </remarks>
    [Test]
    public void Subtract_TwoSources_TakeTheSecondFromTheFirst()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Subtract, (10f, 20f, 30f), (1f, 2f, 3f))
            .ShouldBe((9f, 18f, 27f));

        MaterialProxies.Apply(MaterialProxies.MathProxy.Subtract, (1f, 2f, 3f), (10f, 20f, 30f))
            .ShouldBe((-9f, -18f, -27f), "and it is not symmetric");
    }

    [Test]
    public void Multiply_TwoSources_AreMultipliedComponentwise()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Multiply, (2f, 3f, 4f), (5f, 6f, 7f))
            .ShouldBe((10f, 18f, 28f));
    }

    [Test]
    public void Divide_TwoSources_DivideTheFirstByTheSecond()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Divide, (10f, 20f, 30f), (2f, 4f, 5f))
            .ShouldBe((5f, 5f, 6f));
    }

    /// <remarks>
    /// **A zero divisor yields the NUMERATOR, not zero and not infinity** — the engine's guard is
    /// explicit:
    ///
    /// <code>
    /// if (m_pSrc2->GetFloatValue() != 0)
    ///     SetFloatResult( m_pSrc1->GetFloatValue() / m_pSrc2->GetFloatValue() );
    /// else
    ///     SetFloatResult( m_pSrc1->GetFloatValue() );
    /// </code>
    ///
    /// (`mathproxy.cpp:229-233`.) Letting it divide would put an infinity into a material variable
    /// and, from there, a NaN into a colour — which draws as black or as nothing at all depending
    /// on the blend, and is the kind of fault that looks like a missing texture.
    /// </remarks>
    [Test]
    public void Divide_ByZero_YieldsTheNumeratorRatherThanInfinity()
    {
        MaterialProxies.Apply(MaterialProxies.MathProxy.Divide, (10f, 20f, 30f), (0f, 0f, 0f))
            .ShouldBe((10f, 20f, 30f));

        // Componentwise, so one zero divisor does not spoil the components beside it.
        MaterialProxies.Apply(MaterialProxies.MathProxy.Divide, (10f, 20f, 30f), (2f, 0f, 5f))
            .ShouldBe((5f, 20f, 6f));
    }

    /// <remarks>
    /// **The bounds are SWAPPED when they arrive the wrong way round**, which the engine does
    /// before clamping anything (`mathproxy.cpp:283-288`). Without it a material stating
    /// `min 1  max 0` clamps everything to a range that cannot contain anything, and the result is
    /// whichever bound the comparisons happen to reach first.
    /// </remarks>
    [Test]
    public void Clamp_BoundsTheWrongWayRound_AreSwappedFirst()
    {
        MaterialProxies.Clamp((0.5f, 2f, -1f), minimum: 1f, maximum: 0f)
            .ShouldBe((0.5f, 1f, 0f), "min and max exchanged, then clamped");
    }

    [Test]
    public void Clamp_ValuesEitherSideOfTheRange_AreBroughtIn()
    {
        MaterialProxies.Clamp((-5f, 0.5f, 5f), minimum: 0f, maximum: 1f)
            .ShouldBe((0f, 0.5f, 1f));
    }
}
