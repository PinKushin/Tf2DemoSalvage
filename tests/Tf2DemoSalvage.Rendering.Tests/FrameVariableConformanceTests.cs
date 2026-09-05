using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The frame a texture shows is a material VARIABLE, not a number the bind computes (B343).
/// </summary>
/// <remarks>
/// **B341 and B342 computed the frame where the texture is bound, and that was a shortcut.** In the
/// engine a texture's frame is `$frame` for the base and `$detailframe` for the detail — ordinary
/// material variables, which is why `BindTexture` takes a frame var index beside the texture var
/// index. `CBaseAnimatedTextureProxy` does not select an image; it writes that variable
/// (`m_AnimatedTextureFrameNumVar->SetIntValue( intFrame )`, `baseanimatedtextureproxy.cpp:135`)
/// and the bind reads it.
///
/// **Routing through the variable is what makes any chain work.** Ten shipped materials compute a
/// frame with `Subtract` and `Clamp` and write `$frame` themselves, with no `AnimatedTexture` in
/// sight — `$frameminusten` clamped to 0..30. A renderer computing the frame from its own clock can
/// never honour those, and B339 recorded that as an INT-path divergence it could not close.
///
/// **This suite pins the variable's arithmetic**; that it reaches the drawn frame is asserted by
/// `AnimatedTextureWiringTests` against a real map.
/// </remarks>
public sealed class FrameVariableConformanceTests
{
    /// <remarks>
    /// **A frame variable is an INTEGER one, so the value is truncated rather than rounded** —
    /// the engine reads it with `GetIntValue()`. A chain computing 12.7 selects frame 12, and a
    /// renderer holding the value as a float and indexing with it would either round or throw.
    /// </remarks>
    [Test]
    public void Frame_AFractionalValue_IsTruncatedRatherThanRounded()
    {
        Frame(12.7f).ShouldBe(12);
        Frame(12.2f).ShouldBe(12);
        Frame(0.9f).ShouldBe(0);
    }

    /// <remarks>
    /// **A negative frame is clamped, not wrapped**, because an index is not a modulo. The ten
    /// materials that compute their own frame do it by SUBTRACTING ten, so a chain reaching a
    /// negative is exactly what `$frameminusten` is for — and C#'s modulo of a negative is
    /// negative, which would read off the front of the frame list.
    /// </remarks>
    [Test]
    public void Frame_ANegativeValue_IsClampedToTheFirstFrame()
    {
        Frame(-3f).ShouldBe(0);
        Frame(-0.5f).ShouldBe(0);
    }

    /// <remarks>
    /// **A variable no proxy wrote is frame zero**, which is what an unanimated material shows and
    /// what the engine's default `$frame` of 0 gives.
    /// </remarks>
    [Test]
    public void Frame_AVariableNoProxyWrote_IsZero()
    {
        WorldRenderer.FrameOfForTesting([], "$frame").ShouldBe(0);
    }

    /// <remarks>
    /// **The RED component is the value**, because a float written into this table is broadcast
    /// across all three — so reading any other component would work by accident and reading a
    /// vector-valued variable's green would be wrong for the one case that matters.
    /// </remarks>
    [Test]
    public void Frame_AVariableWrittenAsATriple_TakesItsFirstComponent()
    {
        WorldRenderer.FrameOfForTesting(
            new Dictionary<string, (float Red, float Green, float Blue)>
            {
                ["$frame"] = (5f, 9f, 9f),
            },
            "$frame").ShouldBe(5);
    }

    /// <summary>The frame index a value produces.</summary>
    private static int Frame(float value) =>
        WorldRenderer.FrameOfForTesting(
            new Dictionary<string, (float Red, float Green, float Blue)>
            {
                ["$frame"] = (value, value, value),
            },
            "$frame");
}
