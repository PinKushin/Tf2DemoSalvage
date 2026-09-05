namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A proxy naming ONE component of a vector variable — <c>$envmaptint[1]</c> (B339).
/// </summary>
/// <remarks>
/// **B337 recorded this as a divergence and it is reachable in shipped content**, which is the
/// difference between a stated limit and a defect. Measured with `vmt-proxy`: `Equals` writes
/// `$selfillumfresnelminmaxexp[1]` on 64 materials and `[2]` on 64 more, `$temp[1]` on 20;
/// `Subtract` writes `$envmaptint[1]` on 2. Writing all three components where the material named
/// one changes a reflection tint or a self-illumination ramp to a grey of itself.
///
/// **The engine's own two paths** (`functionproxy.cpp:141-160`):
///
/// <code>
/// if (m_pResult->GetType() == MATERIAL_VAR_TYPE_VECTOR)
/// {
///     if ( m_ResultVecComp >= 0 )
///         m_pResult->SetVecComponentValue( result, m_ResultVecComp );
///     else
///         for (int i = 0; i &lt; vecSize; ++i) v[i] = result;   // broadcast
/// }
/// </code>
///
/// So a named component is written ALONE and the rest of the vector is left as it was; an unnamed
/// one broadcasts a single float across every component. The index is parsed off the name by
/// `strtol` after the `[` (`:117-133`), and the same parse runs on the SOURCES
/// (`CFloatInput::Init`, `:38-58`) — so `$foo[2]` as an input reads component two as a scalar.
///
/// Written before the implementation, so it states the engine rather than describing what was
/// built.
/// </remarks>
public sealed class ProxyComponentConformanceTests
{
    [Test]
    public void Reference_APlainName_HasNoComponent()
    {
        MaterialProxies.Reference("$color2").ShouldBe(("$color2", -1));
    }

    /// <remarks>
    /// **The name loses its brackets**, because the engine looks the variable up by the stripped
    /// form: `pResult = pTemp` after `*pArray++ = 0`. A lookup keyed on `$envmaptint[1]` would
    /// match nothing and the proxy would be refused.
    /// </remarks>
    [Test]
    public void Reference_AnIndexedName_SplitsIntoNameAndComponent()
    {
        MaterialProxies.Reference("$envmaptint[1]").ShouldBe(("$envmaptint", 1));
        MaterialProxies.Reference("$selfillumfresnelminmaxexp[2]").ShouldBe(
            ("$selfillumfresnelminmaxexp", 2));
        MaterialProxies.Reference("$temp[0]").ShouldBe(("$temp", 0));
    }

    /// <remarks>
    /// **`strtol` stops at the first non-digit and answers 0 for text**, which is what a malformed
    /// index gets: `$foo[]` and `$foo[x]` both become component 0 rather than an error. Reproduced
    /// rather than tightened, because a material relying on it is relying on component zero.
    /// </remarks>
    [Test]
    public void Reference_AMalformedIndex_IsComponentZeroAsStrtolGivesIt()
    {
        MaterialProxies.Reference("$foo[]").ShouldBe(("$foo", 0));
        MaterialProxies.Reference("$foo[x]").ShouldBe(("$foo", 0));
    }

    /// <remarks>
    /// **A named component is written ALONE.** This is the whole finding: the other two keep the
    /// values they had, and an implementation writing all three turns a tint into a grey.
    /// </remarks>
    [Test]
    public void WriteComponent_OneOfThree_LeavesTheOthersAlone()
    {
        MaterialProxies.WriteComponent((1f, 2f, 3f), component: 1, value: 9f)
            .ShouldBe((1f, 9f, 3f));

        MaterialProxies.WriteComponent((1f, 2f, 3f), component: 0, value: 9f)
            .ShouldBe((9f, 2f, 3f));

        MaterialProxies.WriteComponent((1f, 2f, 3f), component: 2, value: 9f)
            .ShouldBe((1f, 2f, 9f));
    }

    /// <remarks>
    /// **No component named means BROADCAST, not "write the triple"** — the engine's `else` fills
    /// every component with the same float. That is what makes a float-typed result reaching a
    /// vector variable produce a grey rather than a partial write.
    /// </remarks>
    [Test]
    public void WriteComponent_WithNoComponentNamed_BroadcastsAcrossAllThree()
    {
        MaterialProxies.WriteComponent((1f, 2f, 3f), component: -1, value: 9f)
            .ShouldBe((9f, 9f, 9f));
    }

    /// <remarks>
    /// A component past the end is refused rather than wrapping — the engine indexes a `float v[4]`
    /// and a fourth component is legal there, but this layer holds three and writing past them
    /// would be a silent no-op at best.
    /// </remarks>
    [Test]
    public void WriteComponent_PastTheThreeThisLayerHolds_ChangesNothing()
    {
        MaterialProxies.WriteComponent((1f, 2f, 3f), component: 3, value: 9f)
            .ShouldBe((1f, 2f, 3f));
    }

    /// <remarks>
    /// **Reading a named component gives a SCALAR, broadcast** — the operation becomes float-typed,
    /// so both sources are read as floats and the arithmetic runs once rather than three times.
    /// </remarks>
    [Test]
    public void ReadComponent_OneOfThree_IsThatComponentInEveryPlace()
    {
        MaterialProxies.ReadComponent((1f, 2f, 3f), component: 1).ShouldBe((2f, 2f, 2f));
        MaterialProxies.ReadComponent((1f, 2f, 3f), component: -1).ShouldBe((1f, 2f, 3f));
    }
}
