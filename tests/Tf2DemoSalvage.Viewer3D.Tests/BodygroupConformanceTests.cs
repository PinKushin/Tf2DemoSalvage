using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Which body part of a studio model is drawn, against Valve's own arithmetic.
/// </summary>
/// <remarks>
/// **One integer decides several independent choices at once, and that is the whole trick.** A
/// model's <c>m_nBody</c> packs every bodygroup into a single value: each group has a
/// <c>base</c> place value and a <c>nummodels</c> count, so the group's chosen model is a digit of
/// <c>body</c> in a mixed radix. Valve reads it in <c>GetBodygroup</c>
/// (<c>src/game/shared/animation.cpp</c>):
///
/// <code>
/// int iCurrent = (body / pbodypart->base) % pbodypart->nummodels;
/// </code>
///
/// and writes it back in <c>SetBodygroup</c> a few lines above, by subtracting the current digit
/// and adding the new one. <c>WorldRenderer.Shows</c> is the read half, asked once per part.
///
/// **Getting this wrong draws the wrong thing rather than nothing**, which is why it is worth a
/// parity test on a renderer that is otherwise device-bound. A TF2 player model uses bodygroups
/// for the hat slot and for weapon variants, so an off-by-one in the divisor puts the wrong
/// weapon in a hand and a plausible picture on screen. The suite cannot see the picture; it can
/// check the arithmetic that chose it.
///
/// The predictions below are computed from Valve's formula by hand, not read off this
/// implementation — a test whose expectation came from running the code proves only that the code
/// is deterministic.
/// </remarks>
public sealed class BodygroupConformanceTests
{
    /// <summary>Two groups: the first with 2 models at place 1, the second with 3 at place 2.</summary>
    /// <remarks>
    /// Mixed radix, deliberately. Two groups of the same size cannot distinguish a divisor from a
    /// modulus — 2 and 2 give the same answer under either — so the sizes differ and the places
    /// are the running product Valve's compiler assigns.
    /// </remarks>
    private static readonly (int Base, int Count)[] Parts = [(1, 2), (2, 3)];

    [TestCase(0, 0, true)]
    [TestCase(0, 1, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, 1, true)]
    [TestCase(2, 0, true)]
    [TestCase(3, 1, true)]
    public void Shows_TheFirstGroup_SelectsOnTheLowDigit(int body, int model, bool expected)
    {
        // Group 0 is base 1, count 2 — so it is body's lowest digit: (body / 1) % 2, which is
        // body's parity. Bodies 0 and 2 show model 0; 1 and 3 show model 1.
        WorldRenderer.Shows(Parts, part: 0, model: model, body: body).ShouldBe(expected);
    }

    [TestCase(0, 0, true)]
    [TestCase(1, 0, true)]
    [TestCase(2, 1, true)]
    [TestCase(3, 1, true)]
    [TestCase(4, 2, true)]
    [TestCase(5, 2, true)]
    [TestCase(6, 0, true)]
    [TestCase(2, 0, false)]
    public void Shows_TheSecondGroup_SelectsOnTheHigherPlace(int body, int model, bool expected)
    {
        // Group 1 is base 2, count 3 — (body / 2) % 3. Body 6 wraps back to model 0, which is the
        // case a divisor without the modulus gets wrong: 6/2 is 3, and there is no model 3.
        WorldRenderer.Shows(Parts, part: 1, model: model, body: body).ShouldBe(expected);
    }

    [Test]
    public void Shows_AGroupIndexPastTheModel_FallsBackToTheFirstModel()
    {
        // **A part the model does not declare is not an error here.** The renderer walks the
        // mesh's parts, and a mesh can name more than the header describes; drawing the first
        // model is the same thing the engine does with an unset group, and drawing nothing would
        // make the model disappear rather than degrade.
        WorldRenderer.Shows(Parts, part: 5, model: 0, body: 7).ShouldBeTrue();
        WorldRenderer.Shows(Parts, part: 5, model: 1, body: 7).ShouldBeFalse();

        // Negative is the same case from the other side.
        WorldRenderer.Shows(Parts, part: -1, model: 0, body: 0).ShouldBeTrue();
    }

    [Test]
    public void Shows_AGroupWithNoModels_FallsBackToTheFirstModel()
    {
        // A zero count would divide by zero in the modulus and a zero place would divide by zero
        // in the quotient. Valve's own GetBodygroup returns 0 when nummodels <= 1 rather than
        // evaluating the expression, and this is the same guard: with nothing to choose between,
        // model 0 is the answer.
        IReadOnlyList<(int Base, int Count)> degenerate = [(0, 3), (2, 0)];

        WorldRenderer.Shows(degenerate, part: 0, model: 0, body: 9).ShouldBeTrue();
        WorldRenderer.Shows(degenerate, part: 0, model: 1, body: 9).ShouldBeFalse();
        WorldRenderer.Shows(degenerate, part: 1, model: 0, body: 9).ShouldBeTrue();
    }

    [Test]
    public void Shows_EveryBody_SelectsExactlyOneModelPerGroup()
    {
        // **The property the whole scheme rests on**, and the one a wrong divisor breaks silently:
        // for any body value, each group shows exactly one of its models. Two would draw a hat and
        // a bare head through each other; none would drop the part entirely.
        //
        // Stated over the whole range rather than at sampled points, because an error in the place
        // values shows up only at the value where the digits carry.
        for (int body = 0; body < 24; body++)
        {
            for (int part = 0; part < Parts.Length; part++)
            {
                int shown = 0;
                for (int model = 0; model < Parts[part].Count; model++)
                {
                    if (WorldRenderer.Shows(Parts, part, model, body))
                    {
                        shown++;
                    }
                }

                shown.ShouldBe(1, $"body {body}, group {part} showed {shown} models");
            }
        }
    }
}
